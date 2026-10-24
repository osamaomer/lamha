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
