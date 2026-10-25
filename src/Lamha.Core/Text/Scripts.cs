using System.Globalization;
using System.Text;

namespace Lamha.Core.Text;

/// <summary>Which writing system text is in.</summary>
public static class Scripts
{
    /// <summary>
    /// A character from the Arabic blocks (Arabic, its supplement, extended-A and the presentation forms): Lamha 2's
    /// <c>ARABIC_RE</c>.
    /// </summary>
    public static bool IsArabicChar(char c) =>
        c is (>= '؀' and <= 'ۿ') or (>= 'ݐ' and <= 'ݿ') or (>= 'ࢠ' and <= 'ࣿ')
            or (>= 'ﭐ' and <= '﷿') or (>= 'ﹰ' and <= '﻿');

    public static bool HasArabic(string text) => text.Any(IsArabicChar);

    /// <summary>Three Latin letters in a row: English (or another Latin-script language) in the text.</summary>
    public static bool HasLatinWord(string text)
    {
        var run = 0;
        foreach (var c in text)
        {
            run = char.IsAsciiLetter(c) ? run + 1 : 0;
            if (run >= 3)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>More Arabic characters than Latin letters: an Arabic draft (Lamha 2's <c>isArabicText</c>).</summary>
    public static bool IsArabicText(string text) => text.Count(IsArabicChar) > text.Count(char.IsAsciiLetter);

    /// <summary>
    /// Letters that have no business in a translation: not Latin (names, links), not Arabic script when the target is
    /// written in it, and not in the source either (a Greek name may stay Greek). Lamha 2's <c>strayLetters</c>: Gemini
    /// Flash-Lite once put Korean "캐시" and Hebrew letters into Arabic sentences; small local models do it more.
    /// </summary>
    public static bool StrayLetters(string source, string translation, string targetLang)
    {
        var arabicTarget = targetLang is "ar" or "fa" or "ur" or "ps" or "sd" or "ku";
        foreach (var rune in translation.EnumerateRunes())
        {
            if (!Rune.IsLetter(rune) || IsLatin(rune) || (arabicTarget && IsArabicScript(rune))
                || source.Contains(rune.ToString(), StringComparison.Ordinal))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>A letter of the Latin script (Unicode's Script=Latin, by its blocks).</summary>
    public static bool IsLatin(Rune rune)
    {
        var v = rune.Value;
        return v switch
        {
            <= 0x7F => Rune.IsLetter(rune),
            0xAA or 0xBA => true,
            >= 0xC0 and <= 0x24F => v is not (0xD7 or 0xF7),
            >= 0x250 and <= 0x2AF => true, // IPA
            >= 0x2B0 and <= 0x2B8 or >= 0x2E0 and <= 0x2E4 => true,
            >= 0x1D00 and <= 0x1D25 or >= 0x1D2C and <= 0x1D5C or >= 0x1D62 and <= 0x1D65 or >= 0x1D6B and <= 0x1D77 or >= 0x1D79 and <= 0x1DBE => true,
            >= 0x1E00 and <= 0x1EFF => true,
            0x2071 or 0x207F or (>= 0x2090 and <= 0x209C) or 0x212A or 0x212B or 0x2132 or 0x214E => true,
            >= 0x2160 and <= 0x2188 => true,
            >= 0x2C60 and <= 0x2C7F or >= 0xA722 and <= 0xA7FF or >= 0xAB30 and <= 0xAB5A or >= 0xAB5C and <= 0xAB64 => true,
            >= 0xFB00 and <= 0xFB06 => true,
            >= 0xFF21 and <= 0xFF3A or >= 0xFF41 and <= 0xFF5A => true,
            _ => false,
        };
    }

    /// <summary>A letter of the Arabic script (Unicode's Script=Arabic, by its blocks).</summary>
    public static bool IsArabicScript(Rune rune)
    {
        var v = rune.Value;
        return v is (>= 0x600 and <= 0x6FF) or (>= 0x750 and <= 0x77F) or (>= 0x870 and <= 0x8FF)
            or (>= 0xFB50 and <= 0xFDFF) or (>= 0xFE70 and <= 0xFEFF) or (>= 0x10E60 and <= 0x10E7F)
            or (>= 0x1EE00 and <= 0x1EEFF);
    }

    /// <summary>Letters and digits only, in small letters: "Gemini!" and "gemini" are the same word.</summary>
    public static string LettersAndDigits(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.ToLowerInvariant().EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsLetter(rune) || category is UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber)
            {
                sb.Append(rune.ToString());
            }
        }

        return sb.ToString();
    }
}
