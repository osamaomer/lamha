using System.Globalization;

namespace Lamha.Core.Appearance;

/// <summary>A colour without transparency.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb White = new(255, 255, 255);
    public static readonly Rgb Black = new(0, 0, 0);

    /// <summary>"#rrggbb".</summary>
    public string Hex => string.Create(CultureInfo.InvariantCulture, $"#{R:x2}{G:x2}{B:x2}");

    /// <summary>"RRGGBB", "RRGGBBAA" or with a "#": the colour, or null when it isn't one.</summary>
    public static Rgb? Parse(string? text)
    {
        var s = (text ?? "").TrimStart('#');
        if (s.Length is not (6 or 8) || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
        {
            return null;
        }

        if (s.Length == 8)
        {
            v >>= 8; // the alpha is ignored
        }

        return new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    /// <summary>WCAG's contrast ratio between two colours (1 to 21).</summary>
    public static double Contrast(Rgb a, Rgb b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    /// <summary>This colour at <paramref name="alpha"/> over <paramref name="under"/>.</summary>
    public Rgb Over(double alpha, Rgb under) => new(
        Round((R * alpha) + (under.R * (1 - alpha))),
        Round((G * alpha) + (under.G * (1 - alpha))),
        Round((B * alpha) + (under.B * (1 - alpha))));

    /// <summary>This colour moved <paramref name="t"/> (0 to 1) of the way toward <paramref name="to"/>.</summary>
    public Rgb Mix(Rgb to, double t) => new(
        Round(R + ((to.R - R) * t)), Round(G + ((to.G - G) * t)), Round(B + ((to.B - B) * t)));

    private static double Luminance(Rgb c)
    {
        static double Channel(byte v)
        {
            var x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    /// <summary>JavaScript's Math.round (halves up), as Lamha 2 computed these.</summary>
    private static byte Round(double v) => (byte)Math.Clamp(Math.Floor(v + 0.5), 0, 255);
}

/// <summary>One theme's accent colours.</summary>
/// <param name="Accent">Links, labels, icons: readable on the theme's cards and on its own tint.</param>
/// <param name="SoftAlpha">The tint behind selected things: <see cref="Accent"/> at this opacity.</param>
/// <param name="Button">
/// Filled buttons, selected chips, a switch when on: under white text in light, and in dark the accent itself under
/// <see cref="AccentPalette.DarkButtonText"/>, as Windows draws its accent buttons (decision 0020).
/// </param>
public sealed record ThemeAccent(Rgb Accent, double SoftAlpha, Rgb Button);

/// <summary>
/// Lamha's colours in Windows' accent colour (Settings → Appearance → "Windows' accent colour"). Windows lets people
/// pick any colour, light yellow included, so it isn't used as it is: each theme gets a version that keeps text
/// readable (WCAG 4.5:1). Lamha 2's <c>accentPalette()</c> in desktop/app-rules.js, with its numbers.
/// </summary>
public static class AccentPalette
{
    /// <summary>The cards' surface: Lamha 2's --surface in ui.css and the card's --bg.</summary>
    public static readonly Rgb LightSurface = Rgb.White;

    public static readonly Rgb DarkSurface = new(44, 44, 46);

    /// <summary>The text on a dark theme's filled button (Tokens.xaml's LamhaButtonTextBrush in dark).</summary>
    public static readonly Rgb DarkButtonText = new(0x0B, 0x0B, 0x12);

    /// <summary>Both themes' colours from Windows' accent ("RRGGBB" or "RRGGBBAA"), or null when it isn't a colour.</summary>
    public static (ThemeAccent Light, ThemeAccent Dark)? From(string? windowsAccent) =>
        Rgb.Parse(windowsAccent) is { } rgb ? From(rgb) : null;

    public static (ThemeAccent Light, ThemeAccent Dark) From(Rgb accent) =>
        (Theme(accent, LightSurface, Rgb.Black), Theme(accent, DarkSurface, Rgb.White));

    private static ThemeAccent Theme(Rgb rgb, Rgb surface, Rgb toward)
    {
        var dark = toward == Rgb.White;
        var alpha = dark ? 0.12 : 0.09; // --accent-soft's, as in ui.css
        var accent = Readable(rgb, surface, toward, alpha);
        // Dark: Windows' way, the light accent under dark text (it reads at 4.5:1 on the dark cards, so far more on the text).
        return new ThemeAccent(accent, alpha, dark ? accent : Readable(rgb, Rgb.White, Rgb.Black, 0));
    }

    /// <summary>
    /// <paramref name="rgb"/> moved toward <paramref name="to"/> (black or white) just enough to reach 4.5:1 against
    /// <paramref name="against"/> and, with a <paramref name="tint"/>, also on its own tint of that opacity (selected
    /// chips: the accent on its soft colour).
    /// </summary>
    private static Rgb Readable(Rgb rgb, Rgb against, Rgb to, double tint)
    {
        bool Ok(Rgb c) => Rgb.Contrast(c, against) >= 4.5 && (tint == 0 || Rgb.Contrast(c, c.Over(tint, against)) >= 4.5);

        // The same steps as Lamha 2's loop (t += 0.04 up to 1.0001), so the results match it exactly.
        for (var t = 0.0; t <= 1.0001; t += 0.04)
        {
            var c = rgb.Mix(to, t);
            if (Ok(c))
            {
                return c;
            }
        }

        return to;
    }
}
