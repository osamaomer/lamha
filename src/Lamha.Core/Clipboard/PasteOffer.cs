using System.Text;
using Lamha.Core.Settings;
using Lamha.Core.Text;

namespace Lamha.Core.Clipboard;

/// <summary>
/// Paste and translate (docs/design/system.md, the study's features): the Translate page's empty box offers what's on
/// the clipboard in one click. The copy is read only when clipboard history's own rules would read it (a program's
/// "don't keep this" marks, the excluded programs), whether history is on or not, and a card number isn't offered.
/// </summary>
public static class PasteOffer
{
    /// <summary>Longer copies aren't offered (characters): the box is for a word, a sentence or a paragraph.</summary>
    public const int MaxText = 5000;

    /// <summary>The chip shows the copy's start, up to this many characters (the chip trims it further to its width).</summary>
    public const int PreviewLength = 80;

    /// <summary>Whether the copy may be read at all, before its text is.</summary>
    public static bool MayRead(ClipFormats formats, LamhaSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return ClipCapture.BeforeReading(formats, settings with { ClipboardEnabled = true }, paused: false) == ClipSkip.None;
    }

    /// <summary>The text to offer from the copy that was read (trimmed), or null when there's nothing to offer.</summary>
    public static string? Offer(string? text)
    {
        var trimmed = (text ?? "").Trim();
        return trimmed.Length == 0 || trimmed.Length > MaxText || ClipPrivacy.IsCardNumber(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// The chip's quote: the copy on one line, its spaces collapsed, cut at <see cref="PreviewLength"/>, in English
    /// quotes (“…”) or, for Arabic, Arabic ones («…»).
    /// </summary>
    public static string Preview(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var line = new StringBuilder(Math.Min(text.Length, PreviewLength + 1));
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (line.Length > 0 && line[^1] != ' ')
                {
                    line.Append(' ');
                }
            }
            else
            {
                line.Append(c);
            }

            if (line.Length > PreviewLength)
            {
                break;
            }
        }

        var shown = line.ToString().TrimEnd();
        if (shown.Length > PreviewLength)
        {
            shown = shown[..PreviewLength].TrimEnd() + "…";
        }

        return Scripts.IsArabicText(shown) ? $"«{shown}»" : $"“{shown}”";
    }
}
