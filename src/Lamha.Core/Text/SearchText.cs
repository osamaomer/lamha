using System.Text;

namespace Lamha.Core.Text;

/// <summary>
/// Text made comparable for searching, where Arabic spelling variants, marks, digits and case don't matter: clipboard
/// history's search. Lamha 2's shared/arabic-normalize.js (<c>LamhaArabic</c>).
/// </summary>
public static class SearchText
{
    /// <summary>
    /// A form of <paramref name="text"/> where variants compare equal: مَصْرِف → مصرف, أحمد → احمد, مسؤول → مسوول,
    /// ٢٠٢٤ → 2024, Invoice → invoice, whitespace collapsed to single spaces. Lamha 2's <c>normalizeForSearch()</c>.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var sb = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsWhiteSpace(c))
            {
                space = sb.Length > 0;
                continue;
            }

            if (IsMark(c))
            {
                continue;
            }

            if (space)
            {
                sb.Append(' ');
                space = false;
            }

            AppendFolded(sb, c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Where the query's words occur in the original <paramref name="text"/>, for highlighting: sorted, merged
    /// [start, end) ranges. It matches as search does (مصرف highlights all of مَصْرِف, marks included). Lamha 2's
    /// <c>matchRanges()</c>.
    /// </summary>
    public static IReadOnlyList<(int Start, int End)> MatchRanges(string? text, string? query)
    {
        text ??= "";
        var tokens = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return [];
        }

        // The normalized text, and for each of its characters the index of the original character it came from.
        var norm = new StringBuilder(text.Length);
        var from = new List<int>(text.Length + 1);
        for (var i = 0; i < text.Length;)
        {
            var length = char.IsSurrogatePair(text, i) ? 2 : 1;
            var piece = text.AsSpan(i, length);
            var n = piece.Length == 1 && char.IsWhiteSpace(piece[0]) ? " " : Normalize(piece.ToString());
            norm.Append(n);
            for (var k = 0; k < n.Length; k++)
            {
                from.Add(i);
            }

            i += length;
        }

        from.Add(text.Length);
        var all = norm.ToString();
        var ranges = new List<(int Start, int End)>();
        foreach (var t in tokens)
        {
            for (var s = all.IndexOf(t, StringComparison.Ordinal); s != -1; s = all.IndexOf(t, s + 1, StringComparison.Ordinal))
            {
                ranges.Add((from[s], from[s + t.Length]));
            }
        }

        ranges.Sort();
        var merged = new List<(int Start, int End)>();
        foreach (var r in ranges)
        {
            if (merged.Count > 0 && r.Start <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, r.End));
            }
            else
            {
                merged.Add(r);
            }
        }

        return merged;
    }

    /// <summary>
    /// "ar" | "en" | "mixed" | "other", from the share of Arabic letters (U+0621–U+064A) among Arabic and Latin letters
    /// in the first 2,000 characters; fewer than 2 letters (numbers, emoji, symbols) is "other". Lamha 2's
    /// <c>detectLang()</c>.
    /// </summary>
    public static string DetectLanguage(string? text)
    {
        var head = (text ?? "").AsSpan(0, Math.Min((text ?? "").Length, 2000));
        int arabic = 0, latin = 0;
        foreach (var c in head)
        {
            if (c is >= 'ء' and <= 'ي')
            {
                arabic++;
            }
            else if (char.IsAsciiLetter(c))
            {
                latin++;
            }
        }

        if (arabic + latin < 2)
        {
            return "other";
        }

        var share = (double)arabic / (arabic + latin);
        return share >= 0.8 ? "ar" : share <= 0.2 ? "en" : "mixed";
    }

    /// <summary>Tashkeel, superscript alef and tatweel.</summary>
    private static bool IsMark(char c) => c is (>= 'ً' and <= 'ٟ') or 'ٰ' or 'ـ';

    private static void AppendFolded(StringBuilder sb, char c)
    {
        switch (c)
        {
            case 'أ' or 'إ' or 'آ' or 'ٱ':
                sb.Append('ا');
                break;
            case 'ى' or 'ئ':
                sb.Append('ي');
                break;
            case 'ة':
                sb.Append('ه');
                break;
            case 'ؤ':
                sb.Append('و');
                break;
            case >= '٠' and <= '٩':
                sb.Append((char)('0' + (c - '٠')));
                break;
            case >= '۰' and <= '۹':
                sb.Append((char)('0' + (c - '۰')));
                break;
            default:
                sb.Append(char.ToLowerInvariant(c));
                break;
        }
    }
}
