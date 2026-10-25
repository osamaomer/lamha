using System.Text.RegularExpressions;
using Lamha.Core.Review;
using Lamha.Core.Settings;
using Lamha.Core.Translation;
using Lamha.Core.Writing;

namespace Lamha.Core.Clipboard;

/// <summary>Lamha's tools on a copy in clipboard history.</summary>
public enum ClipAction
{
    Translate,

    /// <summary>"Write it in English": Arabic (or mixed) text as natural English.</summary>
    English,
    Proofread,
    Summary,

    /// <summary>Look the copy up as a word or phrase (on the Translate page or the card).</summary>
    Lookup,

    /// <summary>The single English word into the review list, or out of it.</summary>
    Review,

    /// <summary>The quick panel only: paste without formatting.</summary>
    Plain,
}

/// <summary>
/// A tool's answer: the text (translation, English, summary; the corrected text for Proofread) with its issues, the
/// summary's language, whether it came from the copy's cache; for Review, the word, whether it's now in the list, and
/// its meaning.
/// </summary>
public sealed record ClipActionResult(ClipAction Action, string Text)
{
    public IReadOnlyList<Issue> Issues { get; init; } = [];

    /// <summary>The summary's language, "ar" or "en".</summary>
    public string Lang { get; init; } = "";

    public bool Cached { get; init; }

    public string Word { get; init; } = "";

    public bool InReview { get; init; }
}

/// <summary>
/// Lamha's tools on a copy: translate, write it in English, proofread, summarize, add to review (Lamha 2's
/// desktop/clipboard-actions.js). Each goes through the same services as everywhere else, so the user's settings apply
/// (the dictionary mode, the AI provider, the mistake journal); results are kept with the copy, so a second look costs
/// nothing, and "Redo" (<c>fresh</c>) asks again.
/// </summary>
public sealed partial class ClipActions(ClipboardHistory history, LookupService lookup, WritingService writing, ReviewService review, SettingsService settings)
{
    /// <summary>The copy isn't a single English word (Review).</summary>
    public const string NotAWord = "not_a_word";

    /// <summary>The tool answered nothing, or the word has no translation to learn.</summary>
    public const string EmptyResult = "empty_result";
    public const string NoTranslation = "no_translation";

    /// <summary>
    /// The tools that fit a copy, by its language and length, in Lamha 2's order (<c>available()</c> in
    /// clip-actions.js); Plain only where there's a program to paste into (the quick panel).
    /// </summary>
    public static IReadOnlyList<ClipAction> Available(string text, string lang, int length, bool panel)
    {
        ArgumentNullException.ThrowIfNull(text);
        var t = text.Trim();
        var words = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var all = new (ClipAction Action, bool Fits)[]
        {
            (ClipAction.Translate, lang is "en" or "mixed" or "other"),
            (ClipAction.English, lang is "ar" or "mixed"),
            (ClipAction.Proofread, lang == "en"),
            (ClipAction.Summary, length > 400),
            (ClipAction.Lookup, lang == "en" && words is >= 1 and <= 4),
            (ClipAction.Review, ReviewWord().IsMatch(t)),
            (ClipAction.Plain, panel),
        };
        return [.. all.Where(a => a.Fits).Select(a => a.Action)];
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the copy <paramref name="id"/>; <paramref name="fresh"/> (Redo) skips the cache
    /// and refreshes it; <paramref name="lang"/> is the summary's language ("ar" unless "en"). Errors are
    /// <see cref="LamhaException"/>s with Lamha's codes (the lookup's and the writing tools', and the ones here).
    /// </summary>
    public Task<ClipActionResult> RunAsync(string id, ClipAction action, bool fresh = false, string? lang = null, CancellationToken cancel = default)
    {
        var clip = history.Get(id) ?? throw new LamhaException(ClipboardHistory.NotFound);
        return action switch
        {
            ClipAction.Translate => TranslateAsync(clip, fresh, cancel),
            ClipAction.English or ClipAction.Proofread or ClipAction.Summary => WriteAsync(clip, action, fresh, lang, cancel),
            ClipAction.Review => ReviewAsync(clip, cancel),
            _ => throw new LamhaException("unknown_action"),
        };
    }

    private async Task<ClipActionResult> TranslateAsync(Clip clip, bool fresh, CancellationToken cancel)
    {
        var target = settings.Current.TargetLang;
        if (!fresh && clip.Cache.Translation.TryGetValue(target, out var hit))
        {
            return new ClipActionResult(ClipAction.Translate, hit) { Cached = true };
        }

        // The lookup the Translate box does: the dictionary mode applies ("Offline only" sends nothing).
        var result = await lookup.LookupAsync(clip.Text, cancel: cancel).ConfigureAwait(false);
        if (result.Translation.Length == 0)
        {
            throw new LamhaException(EmptyResult);
        }

        history.SetTranslation(clip.Id, target, result.Translation);
        return new ClipActionResult(ClipAction.Translate, result.Translation);
    }

    private async Task<ClipActionResult> WriteAsync(Clip clip, ClipAction action, bool fresh, string? lang, CancellationToken cancel)
    {
        lang = lang == "en" ? "en" : "ar"; // the summary's; the other tools have one output language
        var cache = clip.Cache;
        if (!fresh)
        {
            switch (action)
            {
                case ClipAction.English when cache.English is { } english:
                    return new ClipActionResult(action, english) { Cached = true };
                case ClipAction.Summary when cache.Summary.TryGetValue(lang, out var summary):
                    return new ClipActionResult(action, summary) { Lang = lang, Cached = true };
                case ClipAction.Proofread when cache.Proofread is { } proof:
                    return new ClipActionResult(action, proof.Corrected) { Issues = proof.Issues, Cached = true };
            }
        }

        // Fresh: a new answer, and proofreading isn't counted in the mistake journal twice.
        var tool = action switch
        {
            ClipAction.English => WritingTool.ToEnglish,
            ClipAction.Proofread => WritingTool.Proofread,
            _ => WritingTool.Summarize,
        };
        var options = new WritingOptions { Language = action == ClipAction.Summary ? lang : "", Fresh = fresh };
        var result = await writing.RunAsync(tool, clip.Text, options, cancel).ConfigureAwait(false);
        switch (action)
        {
            case ClipAction.English:
                history.UpdateCache(clip.Id, c => c with { English = result.Text });
                return new ClipActionResult(action, result.Text);
            case ClipAction.Proofread:
                history.UpdateCache(clip.Id, c => c with { Proofread = new ClipProofread(result.Text, result.Issues) });
                return new ClipActionResult(action, result.Text) { Issues = result.Issues };
            default:
                history.UpdateCache(clip.Id, c => c with { Summary = new Dictionary<string, string>(c.Summary) { [lang] = result.Text } });
                return new ClipActionResult(action, result.Text) { Lang = lang };
        }
    }

    /// <summary>
    /// Add to review: the word into the list, or out if it's there (the card's bookmark). Whether it was there is read
    /// before the lookup, which adds English words by itself when "Add words automatically" is on; such a card, with
    /// no progress yet, is replaced by one with the dictionary's example (a copy has no sentence around it).
    /// </summary>
    private async Task<ClipActionResult> ReviewAsync(Clip clip, CancellationToken cancel)
    {
        var word = clip.Text.Trim();
        if (!ReviewWord().IsMatch(word))
        {
            throw new LamhaException(NotAWord);
        }

        var had = await review.HasAsync(word).ConfigureAwait(false);
        var result = await lookup.LookupAsync(word, cancel: cancel).ConfigureAwait(false);
        var answer = new ClipActionResult(ClipAction.Review, "") { Word = result.Query, Text = result.Translation };
        if (had)
        {
            await review.RemoveAsync(Card.KeyOf(result.Query)).ConfigureAwait(false);
            return answer with { InReview = false };
        }

        if (await review.HasAsync(result.Query).ConfigureAwait(false))
        {
            await review.RemoveAsync(Card.KeyOf(result.Query)).ConfigureAwait(false);
        }

        var draft = CardFrom.Lookup(result, word, null);
        var definition = result.Definitions.SelectMany(d => d.Entries).FirstOrDefault();
        var example = definition?.Example is { Length: > 0 } e ? e : result.Examples.FirstOrDefault() ?? "";
        draft = draft with { Example = result.Explain && example.Length == 0 ? result.HeroExample : example };
        if (!await review.AddAsync(draft).ConfigureAwait(false))
        {
            throw new LamhaException(NoTranslation);
        }

        return answer with { InReview = true };
    }

    /// <summary>One English word (Lamha 2's <c>REVIEW_WORD</c>).</summary>
    [GeneratedRegex("^[A-Za-z][A-Za-z'-]{1,40}$")]
    private static partial Regex ReviewWord();
}
