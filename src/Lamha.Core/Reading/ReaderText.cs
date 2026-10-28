using System.Globalization;
using Lamha.Core.Dictionary;

namespace Lamha.Core.Reading;

/// <summary>
/// The reading page's text: the word a click lands on, and the sentence around it, which the card reads the word in
/// ("let" in "Netherfield Park is let at last" is rented, not allowed).
/// </summary>
public static class ReaderText
{
    /// <summary>
    /// The word at <paramref name="index"/> in <paramref name="text"/>: its letters and digits, with their marks (Arabic's
    /// vowels) and the apostrophes and hyphens inside it ("don't", "well-known"); null on a space or a mark of punctuation.
    /// </summary>
    public static (int Start, int Length)? WordAt(string text, int index)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (index < 0 || index >= text.Length || !IsLetter(text[index]))
        {
            // A click just past a word's last letter (the gap after it) still means that word.
            if (index > 0 && index <= text.Length && IsLetter(text[index - 1]))
            {
                index--;
            }
            else
            {
                return null;
            }
        }

        var start = index;
        while (start > 0 && (IsLetter(text[start - 1]) || (IsJoiner(text[start - 1]) && start > 1 && IsLetter(text[start - 2]))))
        {
            start--;
        }

        var end = index + 1;
        while (end < text.Length && (IsLetter(text[end]) || (IsJoiner(text[end]) && end + 1 < text.Length && IsLetter(text[end + 1]))))
        {
            end++;
        }

        return (start, end - start);
    }

    /// <summary>
    /// The sentence around a word, as the card takes it: the text before the word back to the sentence's start, and after
    /// it to the sentence's end ("." "!" "?" "…" and their Arabic marks, or a line break), each at most 300 characters.
    /// </summary>
    public static WordContext ContextAround(string text, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        const int Most = 300;
        var from = start;
        for (var i = start - 1; i >= 0 && start - i <= Most; i--)
        {
            var end = SentenceEnd(text, i);
            if (end >= 0 && end <= start)
            {
                from = text[i] == '\n' ? i + 1 : end;
                break;
            }

            from = i;
        }

        var to = start + length;
        while (to < text.Length && to - (start + length) < Most)
        {
            var end = SentenceEnd(text, to);
            if (end >= 0)
            {
                to = text[to] == '\n' ? to : end;
                break;
            }

            to++;
        }

        return new WordContext(text[from..start].TrimStart(), text[(start + length)..to].TrimEnd());
    }

    /// <summary>
    /// Where the sentence ends when one ends at <paramref name="i"/>, or -1: at a line break; or at an end mark (with the
    /// closing quotes after it) followed by a space or the text's end, unless it's a title's full stop ("Mr. Darcy").
    /// </summary>
    private static int SentenceEnd(string text, int i)
    {
        if (text[i] == '\n')
        {
            return i;
        }

        if (!IsEnd(text[i]) || IsTitle(text, i))
        {
            return -1;
        }

        var end = i + 1;
        while (end < text.Length && IsEnd(text[end]))
        {
            end++;
        }

        while (end < text.Length && text[end] is '"' or '”' or '’' or '»' or ')')
        {
            end++;
        }

        return end == text.Length || char.IsWhiteSpace(text[end]) ? end : -1;
    }

    private static bool IsLetter(char c) =>
        char.IsLetterOrDigit(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;

    private static bool IsJoiner(char c) => c is '\'' or '’' or '-' or '‑';

    private static bool IsEnd(char c) => c is '.' or '!' or '?' or '…' or '؟' or '。';

    // "Mr." "Mrs." "Dr." "St.": a title's full stop isn't a sentence's.
    private static bool IsTitle(string text, int dot)
    {
        var from = dot;
        while (from > 0 && char.IsLetter(text[from - 1]))
        {
            from--;
        }

        return text[dot] == '.' && text[from..dot] is "Mr" or "Mrs" or "Ms" or "Dr" or "St" or "Sr" or "Jr" or "Mme" or "Mlle" or "Capt" or "Col" or "Gen" or "Rev";
    }
}
