using System.Globalization;
using System.Text;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.System.Memory;

namespace Lamha.Windows.Clipboard;

/// <summary>
/// The Windows clipboard, through Win32: plain text, every format at once (to put the user's clipboard back exactly as
/// it was), and the flags that keep Lamha's own copies out of Windows' clipboard history (Win+V).
/// </summary>
public static unsafe class Clipboard
{
    private const uint CfUnicodeText = 13;

    /// <summary>A number that changes whenever anything puts something on the clipboard.</summary>
    public static uint SequenceNumber => PInvoke.GetClipboardSequenceNumber();

    /// <summary>The clipboard holds text.</summary>
    public static bool HasText => PInvoke.IsClipboardFormatAvailable(CfUnicodeText);

    /// <summary>The clipboard's text, or null when it holds none or is busy.</summary>
    public static string? ReadText(nint owner)
    {
        if (!PInvoke.IsClipboardFormatAvailable(CfUnicodeText) || !Open(owner))
        {
            return null;
        }

        try
        {
            var handle = (HGLOBAL)(nint)PInvoke.GetClipboardData(CfUnicodeText).Value;
            if (handle == HGLOBAL.Null)
            {
                return null;
            }

            var p = (char*)PInvoke.GlobalLock(handle);
            if (p == null)
            {
                return null;
            }

            try
            {
                var max = (int)(PInvoke.GlobalSize(handle) / sizeof(char));
                var length = new ReadOnlySpan<char>(p, max).IndexOf('\0');
                return new string(p, 0, length < 0 ? max : length);
            }
            finally
            {
                PInvoke.GlobalUnlock(handle);
            }
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    /// <summary>
    /// Puts <paramref name="text"/> on the clipboard. With <paramref name="keepOutOfHistory"/>, it's marked so that
    /// Windows' clipboard history and cloud clipboard skip it: Lamha's own copy for a paste, not something the user
    /// copied. False when the clipboard was busy.
    /// </summary>
    public static bool WriteText(nint owner, string text, bool keepOutOfHistory)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!Open(owner))
        {
            return false;
        }

        try
        {
            PInvoke.EmptyClipboard();
            var bytes = new byte[(text.Length + 1) * sizeof(char)];
            fixed (char* source = text)
            {
                new ReadOnlySpan<byte>(source, text.Length * sizeof(char)).CopyTo(bytes);
            }

            var ok = Set(CfUnicodeText, bytes);
            if (keepOutOfHistory)
            {
                MarkPrivate([]);
            }

            return ok;
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    /// <summary>
    /// Puts a copy from clipboard history back on the clipboard: its text, and its formatting as "HTML Format" when it
    /// has some (Lamha 2's <c>writeClip</c>), kept out of Windows' clipboard history (it's already in it). False when the
    /// clipboard was busy.
    /// </summary>
    public static bool WriteClip(nint owner, string text, string? html)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!Open(owner))
        {
            return false;
        }

        try
        {
            PInvoke.EmptyClipboard();
            var bytes = new byte[(text.Length + 1) * sizeof(char)];
            fixed (char* source = text)
            {
                new ReadOnlySpan<byte>(source, text.Length * sizeof(char)).CopyTo(bytes);
            }

            var ok = Set(CfUnicodeText, bytes);
            if (html is { Length: > 0 } && Register("HTML Format") is var format and not 0)
            {
                Set(format, CfHtml(html));
            }

            MarkPrivate([]);
            return ok;
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    /// <summary>
    /// <paramref name="html"/> as Windows' "HTML Format": a header giving the byte offsets of the document and of the
    /// fragment, which is the whole body (or the whole text when it has no body), in UTF-8 and ending in a zero.
    /// </summary>
    internal static byte[] CfHtml(string html)
    {
        const string Header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        var headerLength = string.Format(CultureInfo.InvariantCulture, Header, 0, 0, 0, 0).Length;

        // A copy keeps the document between StartHTML and EndHTML (ReadHtml); a fragment of it is marked again here.
        var body = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase) is var b and >= 0 && html.IndexOf('>', b) is var open and >= 0 ? open + 1 : -1;
        var bodyEnd = body >= 0 ? html.LastIndexOf("</body", StringComparison.OrdinalIgnoreCase) : -1;
        string document;
        int fragmentStart, fragmentEnd;
        if (body >= 0 && bodyEnd >= body)
        {
            document = html.Insert(bodyEnd, "<!--EndFragment-->").Insert(body, "<!--StartFragment-->");
            fragmentStart = Encoding.UTF8.GetByteCount(document.AsSpan(0, body + "<!--StartFragment-->".Length));
            fragmentEnd = Encoding.UTF8.GetByteCount(document.AsSpan(0, bodyEnd + "<!--StartFragment-->".Length));
        }
        else
        {
            const string Open = "<html><body><!--StartFragment-->";
            document = Open + html + "<!--EndFragment--></body></html>";
            fragmentStart = Encoding.UTF8.GetByteCount(Open);
            fragmentEnd = fragmentStart + Encoding.UTF8.GetByteCount(html);
        }

        var length = Encoding.UTF8.GetByteCount(document);
        var header = string.Format(CultureInfo.InvariantCulture, Header, headerLength, headerLength + length, headerLength + fragmentStart, headerLength + fragmentEnd);
        var bytes = new byte[headerLength + length + 1];
        Encoding.ASCII.GetBytes(header, bytes);
        Encoding.UTF8.GetBytes(document, bytes.AsSpan(headerLength));
        return bytes;
    }

    /// <summary>The clipboard holds the registered format <paramref name="name"/> ("HTML Format").</summary>
    public static bool HasFormat(string name)
    {
        var format = Register(name);
        return format != 0 && PInvoke.IsClipboardFormatAvailable(format);
    }

    /// <summary>The first four bytes of format <paramref name="name"/> as a number, or null when it's missing or unreadable.</summary>
    public static uint? ReadDword(nint owner, string name)
    {
        var data = Read(owner, Register(name), 4);
        return data is { Length: >= 4 } ? BitConverter.ToUInt32(data, 0) : null;
    }

    /// <summary>
    /// The copied formatting: the HTML document of the "HTML Format" (CF_HTML) between its StartHTML and EndHTML, or
    /// null. At most <paramref name="maxBytes"/> are read.
    /// </summary>
    public static string? ReadHtml(nint owner, int maxBytes)
    {
        var data = Read(owner, Register("HTML Format"), maxBytes + 1024);
        if (data is null)
        {
            return null;
        }

        var length = Array.IndexOf(data, (byte)0) is var z and >= 0 ? z : data.Length;
        var header = Encoding.ASCII.GetString(data, 0, Math.Min(length, 512));
        var start = Offset(header, "StartHTML:");
        var end = Offset(header, "EndHTML:");
        if (start < 0 || end <= start || end > length)
        {
            // Some programs write -1 or nothing: everything after the header's lines is the HTML.
            start = header.IndexOf('<', StringComparison.Ordinal);
            end = length;
            if (start < 0)
            {
                return null;
            }
        }

        return Encoding.UTF8.GetString(data, start, end - start);
    }

    /// <summary>Everything on the clipboard now, to put back later with <see cref="Restore"/>.</summary>
    public static ClipboardSnapshot Save(nint owner) => ClipboardSnapshot.Take(owner);

    /// <summary>Puts a snapshot back, kept out of clipboard history (the user's content is already in it).</summary>
    public static bool Restore(nint owner, ClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Open(owner))
        {
            return false;
        }

        try
        {
            PInvoke.EmptyClipboard();
            var formats = new HashSet<uint>();
            foreach (var (format, data) in snapshot.Formats)
            {
                if (Set(format, data))
                {
                    formats.Add(format);
                }
            }

            MarkPrivate(formats);
            return true;
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    /// <summary>Up to <paramref name="max"/> bytes of <paramref name="format"/>; null when it's missing, busy or bigger.</summary>
    private static byte[]? Read(nint owner, uint format, int max)
    {
        if (format == 0 || !PInvoke.IsClipboardFormatAvailable(format) || !Open(owner))
        {
            return null;
        }

        try
        {
            var handle = (HGLOBAL)(nint)PInvoke.GetClipboardData(format).Value;
            if (handle == HGLOBAL.Null)
            {
                return null;
            }

            var size = (long)PInvoke.GlobalSize(handle);
            if (size > max)
            {
                return null;
            }

            var p = PInvoke.GlobalLock(handle);
            if (p == null)
            {
                return null;
            }

            try
            {
                return new ReadOnlySpan<byte>(p, (int)size).ToArray();
            }
            finally
            {
                PInvoke.GlobalUnlock(handle);
            }
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    /// <summary>The number after <paramref name="key"/> in a CF_HTML header ("StartHTML:0000000105"), or -1.</summary>
    private static int Offset(string header, string key)
    {
        var at = header.IndexOf(key, StringComparison.Ordinal);
        if (at < 0)
        {
            return -1;
        }

        var digits = header.AsSpan(at + key.Length);
        var n = 0;
        while (n < digits.Length && char.IsAsciiDigit(digits[n]))
        {
            n++;
        }

        return int.TryParse(digits[..n], CultureInfo.InvariantCulture, out var value) ? value : -1;
    }

    /// <summary>Opens the clipboard, retrying while another program holds it (4 × 25 ms: it's rare and short).</summary>
    internal static bool Open(nint owner)
    {
        for (var i = 0; i < 4; i++)
        {
            if (PInvoke.OpenClipboard((HWND)owner))
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return false;
    }

    /// <summary>Copies <paramref name="data"/> into a new global block and hands it to the open clipboard.</summary>
    internal static bool Set(uint format, ReadOnlySpan<byte> data)
    {
        var handle = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, (nuint)Math.Max(1, data.Length));
        if (handle == HGLOBAL.Null)
        {
            return false;
        }

        var p = PInvoke.GlobalLock(handle);
        if (p == null)
        {
            PInvoke.GlobalFree(handle);
            return false;
        }

        data.CopyTo(new Span<byte>(p, data.Length));
        PInvoke.GlobalUnlock(handle);
        if (PInvoke.SetClipboardData(format, (HANDLE)(nint)handle).IsNull)
        {
            PInvoke.GlobalFree(handle); // the clipboard didn't take it, so it's still ours to free
            return false;
        }

        return true;
    }

    /// <summary>Windows' documented flags for content that shouldn't go into clipboard history or the cloud.</summary>
    private static void MarkPrivate(HashSet<uint> already)
    {
        Span<byte> zero = stackalloc byte[4];
        foreach (var name in (ReadOnlySpan<string>)["CanIncludeInClipboardHistory", "CanUploadToCloudClipboard"])
        {
            var format = Register(name);
            if (format != 0 && !already.Contains(format))
            {
                Set(format, zero);
            }
        }

        var exclude = Register("ExcludeClipboardContentFromMonitorProcessing");
        if (exclude != 0 && !already.Contains(exclude))
        {
            Set(exclude, zero);
        }
    }

    internal static uint Register(string name)
    {
        fixed (char* p = name)
        {
            return PInvoke.RegisterClipboardFormat(p);
        }
    }
}
