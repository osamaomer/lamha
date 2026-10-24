using System.Text;

namespace Lamha.Core.Text;

/// <summary>Arabic spelling variants made equal for lookups.</summary>
public static class ArabicText
{
    /// <summary>
    /// Without marks (tashkeel, superscript alef, tatweel), with أ إ آ ٱ → ا, ى → ي, ة → ه, and single spaces. The
    /// offline dictionary's key: local-dict.js <c>normAr</c>.
    /// </summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (c is (>= 'ً' and <= 'ٰ') or 'ـ')
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                space = sb.Length > 0;
                continue;
            }

            if (space)
            {
                sb.Append(' ');
                space = false;
            }

            sb.Append(c switch
            {
                'إ' or 'أ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                _ => c,
            });
        }

        return sb.ToString();
    }
}
