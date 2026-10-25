using global::Windows.Win32;
using global::Windows.Win32.Foundation;

namespace Lamha.Windows.Clipboard;

/// <summary>
/// A copy of every memory-based format on the clipboard, so Lamha can borrow the clipboard (Ctrl+C, Ctrl+V) and put the
/// user's content back. Formats held as GDI objects (bitmaps, metafiles, palettes) are skipped: Windows makes the
/// bitmap again from the DIB copy, which is kept. A snapshot over <see cref="MaxBytes"/> keeps nothing, so a huge
/// copied picture doesn't cost that much memory again; the clipboard is then left with Lamha's text.
/// </summary>
public sealed unsafe class ClipboardSnapshot
{
    /// <summary>The most a snapshot holds (64 MB).</summary>
    public const long MaxBytes = 64L * 1024 * 1024;

    private static readonly HashSet<uint> GdiFormats =
    [
        2,    // CF_BITMAP
        3,    // CF_METAFILEPICT
        9,    // CF_PALETTE
        14,   // CF_ENHMETAFILE
        0x80, // CF_OWNERDISPLAY
        0x82, // CF_DSPBITMAP
        0x83, // CF_DSPMETAFILEPICT
        0x8E, // CF_DSPENHMETAFILE
    ];

    private ClipboardSnapshot(List<(uint Format, byte[] Data)> formats, bool complete)
    {
        Formats = formats;
        IsComplete = complete;
    }

    public IReadOnlyList<(uint Format, byte[] Data)> Formats { get; }

    /// <summary>False when the clipboard couldn't be read, or was too big to keep.</summary>
    public bool IsComplete { get; }

    public bool IsEmpty => Formats.Count == 0;

    internal static ClipboardSnapshot Take(nint owner)
    {
        if (!Clipboard.Open(owner))
        {
            return new ClipboardSnapshot([], complete: false);
        }

        try
        {
            var formats = new List<(uint, byte[])>();
            long total = 0;
            for (var format = PInvoke.EnumClipboardFormats(0); format != 0; format = PInvoke.EnumClipboardFormats(format))
            {
                // GDI objects, and private formats: their handles mean nothing once the clipboard is emptied.
                if (GdiFormats.Contains(format) || format is >= 0x200 and <= 0x3FF)
                {
                    continue;
                }

                var handle = (HGLOBAL)(nint)PInvoke.GetClipboardData(format).Value;
                if (handle == HGLOBAL.Null)
                {
                    continue;
                }

                var size = (long)PInvoke.GlobalSize(handle);
                total += size;
                if (total > MaxBytes)
                {
                    return new ClipboardSnapshot([], complete: false);
                }

                var p = PInvoke.GlobalLock(handle);
                if (p == null)
                {
                    continue;
                }

                try
                {
                    formats.Add((format, new ReadOnlySpan<byte>(p, (int)size).ToArray()));
                }
                finally
                {
                    PInvoke.GlobalUnlock(handle);
                }
            }

            return new ClipboardSnapshot(formats, complete: true);
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }
}
