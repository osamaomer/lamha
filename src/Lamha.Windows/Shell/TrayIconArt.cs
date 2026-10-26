namespace Lamha.Windows.Shell;

/// <summary>
/// The tray icon's two marks, drawn on its pixels (32-bit BGRA, straight alpha, top row first), from the tray design
/// (2026-10-25): a dot when words are waiting for review, and faded while Lamha is turned off.
/// </summary>
public static class TrayIconArt
{
    /// <summary>The dot's amber, the design's #f59e0b (blue, green, red).</summary>
    private static readonly (byte B, byte G, byte R) DotColour = (0x0B, 0x9E, 0xF5);

    /// <summary>Grey at 55%: the design's <c>grayscale(1)</c> and <c>opacity: .55</c>.</summary>
    public static void Fade(Span<byte> bgra)
    {
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            // CSS's grayscale(1): the Rec. 709 weights.
            var grey = (byte)Math.Round((0.0722 * bgra[i]) + (0.7152 * bgra[i + 1]) + (0.2126 * bgra[i + 2]));
            bgra[i] = bgra[i + 1] = bgra[i + 2] = grey;
            bgra[i + 3] = (byte)Math.Round(bgra[i + 3] * 0.55);
        }
    }

    /// <summary>
    /// The dot in the top right corner, 7/16 of the icon across (the design's 7 px on 16), with a clear gap around it
    /// (1.5 px on 16) so it stands apart from the icon on any taskbar: the design's ring in the taskbar's colour.
    /// </summary>
    /// <param name="bgra">A square icon's pixels.</param>
    /// <param name="size">Its width and height in pixels.</param>
    public static void Dot(Span<byte> bgra, int size)
    {
        var radius = size * 7 / 32.0;
        var gap = size * 1.5 / 16;
        var (cx, cy) = (size - radius, radius);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distance = Math.Sqrt(Math.Pow(x + 0.5 - cx, 2) + Math.Pow(y + 0.5 - cy, 2));
                var cut = Math.Clamp(radius + gap + 0.5 - distance, 0, 1);
                if (cut == 0)
                {
                    continue;
                }

                // The gap clears what's there; the dot is laid over what's left, both smoothed at their edges.
                var cover = Math.Clamp(radius + 0.5 - distance, 0, 1);
                var i = ((y * size) + x) * 4;
                var under = bgra[i + 3] / 255.0 * (1 - cut);
                var alpha = cover + (under * (1 - cover));
                bgra[i] = Blend(DotColour.B, bgra[i]);
                bgra[i + 1] = Blend(DotColour.G, bgra[i + 1]);
                bgra[i + 2] = Blend(DotColour.R, bgra[i + 2]);
                bgra[i + 3] = (byte)Math.Round(alpha * 255);

                byte Blend(byte dot, byte old) =>
                    alpha == 0 ? (byte)0 : (byte)Math.Round(((dot * cover) + (old * under * (1 - cover))) / alpha);
            }
        }
    }
}
