using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.Core.Translation;
using Lamha.Core.Wikipedia;
using Microsoft.UI.Xaml;

namespace Lamha.App.ViewModels.Card;

/// <summary>What an answer on the card can ask of the card.</summary>
public interface IAnswerActions
{
    /// <summary>Looks up a meaning, a synonym or "did you mean" as a word of its own (Back returns).</summary>
    void Navigate(string word);

    void OpenLink(Uri uri);

    /// <summary>The answer's text as a sentence's writing tools (the footer's "Writing tools").</summary>
    void OpenWritingTools();

    /// <summary>Asks the AI to translate the text again (the footer's "Better translation").</summary>
    void BetterTranslation();
}

/// <summary>Something in a line of meanings: a part of speech, a meaning to look up, a sense's hint, or "+3".</summary>
public abstract record MeaningItem;

/// <summary>"verb", at the start of a part of speech's meanings.</summary>
public sealed record PosItem(string Label) : MeaningItem;

/// <summary>A meaning, a synonym: a chip that opens it as a word of its own.</summary>
public sealed record ChipItem(string Text, string Tooltip, FlowDirection Flow, bool Small, IRelayCommand Open) : MeaningItem;

/// <summary>The offline dictionary's sense a line of meanings is for ("financial institution").</summary>
public sealed record HintItem(string Text) : MeaningItem;

/// <summary>"+3" or "Other meanings (2)": shows the rest.</summary>
public sealed record MoreItem(string Label, string Tooltip, IRelayCommand Show) : MeaningItem;

/// <summary>A line of meanings; the one closest to the sentence is tinted.</summary>
public sealed record MeaningLine(ObservableCollection<MeaningItem> Items, bool Best = false);

/// <summary>A definition: its Arabic words, its gloss (and its translation, which may arrive later), example and synonyms.</summary>
public sealed partial class DefinitionItem : ObservableObject
{
    public required string Number { get; init; }

    /// <summary>The offline dictionary's Arabic words for this sense ("يسدّد · يدفع").</summary>
    public string ArabicWords { get; init; } = "";

    public required string Gloss { get; init; }

    /// <summary>The gloss is in a right-to-left language (an Arabic word explained in Arabic).</summary>
    public bool GlossIsRightToLeft { get; init; }

    public string Example { get; init; } = "";

    public List<ChipItem> Synonyms { get; init; } = [];

    public bool Best { get; init; }

    public string BestTooltip => Best ? Localization.Get("Card_BestSense") : "";

    /// <summary>The gloss translated (Google's own, or filled in after the offline dictionary's answer shows).</summary>
    [ObservableProperty]
    public partial string Translated { get; set; } = "";

    /// <summary>Its translation is on the way: a shimmering line until it comes.</summary>
    [ObservableProperty]
    public partial bool Pending { get; set; }
}

/// <summary>A part of speech's definitions.</summary>
public sealed record DefinitionGroupItem(string Pos, List<DefinitionItem> Entries);

/// <summary>The Wikipedia summary, under the answer when it arrives.</summary>
public sealed record WikiItem(string Extract, FlowDirection Flow, Uri? Url, Uri? Thumb, IRelayCommand Read);

/// <summary>A link at the foot of the card: Google Translate, Cambridge, Better translation, Writing tools.</summary>
public sealed record LinkItem(Glyph Glyph, string Label, string Tooltip, IRelayCommand Open);

/// <summary>
/// A lookup's answer as the card shows it: Lamha 2's <c>render(d)</c> in content.js, part by part, from Core's
/// <see cref="LookupResult"/>. Built once per answer; the review mark, glosses and Wikipedia arrive later.
/// </summary>
public sealed partial class WordAnswer : ObservableObject
{
    /// <summary>Meanings shown per part of speech before "+n" (Lamha 2's 7), parts of speech shown (5), and senses (4).</summary>
    private const int ChipsShown = 7;
    private const int PartsShown = 5;
    private const int SensesShown = 4;

    /// <summary>Glosses translated after the answer shows, at most (Lamha 2's 14).</summary>
    private const int GlossesFilled = 14;

    private readonly IAnswerActions _actions;

    /// <param name="result">The lookup's answer.</param>
    /// <param name="actions">The card, which follows words and opens links.</param>
    /// <param name="translator">The AI translator's name when one is set up (the footer's "Better translation").</param>
    /// <param name="writingTools">The writing tools are set up (the footer's "Writing tools" for a text).</param>
    /// <param name="translateGlosses">The offline dictionary's definitions get translated after the answer shows.</param>
    public WordAnswer(LookupResult result, IAnswerActions actions, string? translator, bool writingTools, bool translateGlosses)
    {
        ArgumentNullException.ThrowIfNull(result);
        Result = result;
        _actions = actions;
        var d = result;
        var target = d.Explain ? d.Other : d.Target;
        TargetFlow = Localization.FlowOf(d.Target);
        SourceFlow = Localization.FlowOf(d.Source);

        // The head: the word, how it sounds, and what it's a form of; or the text as selected.
        IsWord = d.IsWord;
        Query = d.Query;
        Phonetic = d.SourceTranslit.Length > 0 ? $"/{d.SourceTranslit}/" : "";
        if (d.Inflected.Length > 0)
        {
            FormOfText = d.Inflected + Localization.Get("Card_FormOf") + d.Query;
        }
        else if (d.FormOf.Length > 0)
        {
            FormOfText = Localization.Get("Card_FormOfPlain");
            FormOfWord = d.FormOf;
        }

        CanReview = d.IsWord && d.Source == "en" && d.Translation.Length > 0;
        Spell = d.Spell;

        // The hero: the meaning in this sentence when known (explained words: the definition that fits).
        var context = d.Context is { Word.Length: > 0 } c ? c : null;
        var main = context?.Word ?? d.Translation;
        HeroText = main;
        HeroIsLong = main.Length > 40;
        HeroFlow = context is { Untranslated: true } ? Localization.FlowOf(Scripts(main)) : Localization.FlowOf(d.Target);
        HeroLabel = d.AiGuess ? Localization.Get("Card_AiGuess")
            : d.Explain ? string.Join(" · ", new[] { PosName(d.HeroPos), d.ContextSense ? Localization.Get("Card_InContext") : "" }.Where(s => s.Length > 0))
            : context is not null || d.ContextSense ? Localization.Get(context is { Untranslated: true } ? "Card_InContextName" : "Card_InContext")
            : "";
        if (main.Length == 0)
        {
            HeroNone = d.BestGloss.Length > 0 ? Localization.Get("Card_NoArSense")
                : d.Explain ? Localization.Format(d.ExplainMissing ? "Card_NoExplainAI" : "Card_NoDef", Localization.LanguageName(d.Source))
                : Localization.Get("Card_NoArDirect");
            HeroNoneGloss = d.BestGloss;
        }

        HeroExample = d.Explain && main.Length > 0 && d.HeroExample.Length > 0 ? $"“{d.HeroExample}”" : "";
        Roots = d.Explain
            ? string.Join(" · ", new[] { d.Root.Length > 0 ? Localization.Format("Card_Root", d.Root) : "", d.Plural.Length > 0 ? Localization.Format("Card_Plural", d.Plural) : "" }.Where(s => s.Length > 0))
            : "";
        Translit = context is null && d.IsWord && d.Translit.Length > 0 && TargetFlow == FlowDirection.RightToLeft ? d.Translit : "";
        if (context is not null && (context.Before.Length > 0 || context.After.Length > 0))
        {
            ContextBefore = context.Before;
            ContextWord = context.Word;
            ContextAfter = context.After;
        }

        ExplainMissingNote = !d.Explain && d.ExplainMissing ? Localization.Format("Card_NoExplainAI", Localization.LanguageName(d.Source)) : "";

        foreach (var group in d.Dict.Take(PartsShown))
        {
            AddMeaningLines(group);
        }

        // Definitions: in the explained view, not the one the hero already shows; in the word's own direction.
        var number = 0;
        var defsRightToLeft = d.Explain ? Localization.IsRightToLeft(target) : Localization.IsRightToLeft(d.Source);
        var fill = translateGlosses && d.From == ResultSource.Local && Localization.IsRightToLeft(d.Target);
        Definitions = [.. d.Definitions
            .Select(g => (g.Pos, Entries: d.Explain && main.Length > 0 ? g.Entries.Where(e => e.Gloss != main).ToList() : [.. g.Entries]))
            .Where(g => g.Entries.Count > 0)
            .Select(g => new DefinitionGroupItem(PosName(g.Pos), [.. g.Entries.Select(e =>
            {
                number++;
                return new DefinitionItem
                {
                    Number = Localization.Number(number),
                    ArabicWords = string.Join(" · ", e.Arabic),
                    Gloss = e.Gloss,
                    GlossIsRightToLeft = defsRightToLeft,
                    Translated = e.GlossTranslated,
                    Pending = e.GlossTranslated.Length == 0 && fill && number <= GlossesFilled,
                    Example = e.Example.Length > 0 ? $"“{e.Example}”" : "",
                    Synonyms = [.. e.Synonyms.Select(s => Chip(s, Localization.Format("Card_LookUpX", s), defsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight))],
                    Best = e.Best,
                };
            })]))];
        Examples = Definitions.Count == 0 ? [.. d.Examples] : [];

        Milestone = d.Milestone > 0 ? Localization.Format("Card_Milestone", d.Milestone) : d.Goal > 0 ? Localization.Format("Card_GoalDone", d.Goal) : "";
        Links = Footer(d, translator, writingTools);
    }

    public LookupResult Result { get; }

    public bool IsWord { get; }

    public bool IsText => !IsWord;

    public string Query { get; }

    /// <summary>The source language's direction: the headword's, the sentence's.</summary>
    public FlowDirection SourceFlow { get; }

    public FlowDirection TargetFlow { get; }

    public string Phonetic { get; }

    /// <summary>"maisons → a form of maison", or "A form of " before <see cref="FormOfWord"/>.</summary>
    public string FormOfText { get; } = "";

    /// <summary>The word this one is a form of, which opens as a word of its own.</summary>
    public string FormOfWord { get; } = "";

    /// <summary>An English word with a meaning: it can go into the review deck (the bookmark).</summary>
    public bool CanReview { get; }

    [ObservableProperty]
    public partial bool InReview { get; set; }

    public string ReviewTooltip => Localization.Get(InReview ? "Card_InReview" : "Card_AddReview");

    /// <summary>Google's "Did you mean".</summary>
    public string Spell { get; }

    public string HeroLabel { get; }

    public string HeroText { get; }

    public bool HeroIsLong { get; }

    public FlowDirection HeroFlow { get; }

    /// <summary>Why there's no meaning to show (no direct Arabic meaning, no definition in that language).</summary>
    public string HeroNone { get; } = "";

    /// <summary>The fitting sense's definition, when it has no Arabic word.</summary>
    public string HeroNoneGloss { get; } = "";

    public string HeroExample { get; }

    /// <summary>Arabic explained in Arabic: "Root: ك ت ب · Plural: كتب".</summary>
    public string Roots { get; }

    public string Translit { get; }

    /// <summary>The sentence's translation around the word, which is marked.</summary>
    public string ContextBefore { get; } = "";

    public string ContextWord { get; } = "";

    public string ContextAfter { get; } = "";

    public bool HasContext => ContextWord.Length > 0;

    public string ExplainMissingNote { get; }

    public ObservableCollection<MeaningLine> OtherMeanings { get; } = [];

    public List<DefinitionGroupItem> Definitions { get; }

    public List<string> Examples { get; }

    [ObservableProperty]
    public partial WikiItem? Wikipedia { get; set; }

    /// <summary>"🎉 That's word number 50…" or today's goal reached.</summary>
    public string Milestone { get; }

    public List<LinkItem> Links { get; }

    /// <summary>What Narrator says when the answer arrives: the word and its meaning, or the translation (Lamha 2's <c>spoken()</c>).</summary>
    public string Spoken => IsWord ? $"{Query}: {HeroText}" : HeroText;

    /// <summary>The definitions whose translation is still to come.</summary>
    public IReadOnlyList<DefinitionItem> PendingGlosses => [.. Definitions.SelectMany(g => g.Entries).Where(e => e.Pending)];

    partial void OnInReviewChanged(bool value) => OnPropertyChanged(nameof(ReviewTooltip));

    [RelayCommand]
    private void OpenFormOf() => _actions.Navigate(FormOfWord);

    [RelayCommand]
    private void OpenSpell() => _actions.Navigate(Spell);

    /// <summary>"ar" for text with more Arabic than Latin, for a name kept as it was in the translation.</summary>
    private static string Scripts(string text) => Lamha.Core.Text.Scripts.IsArabicText(text) ? "ar" : "en";

    /// <summary>A part of speech in the interface language: Lamha's code, or Google's own label.</summary>
    private static string PosName(PartOfSpeech pos) =>
        pos.Code.Length > 0 ? Localization.Get("Pos_" + pos.Code) : pos.Label;

    private ChipItem Chip(string word, string tooltip, FlowDirection flow) =>
        new(word, tooltip, flow, flow == FlowDirection.LeftToRight, new RelayCommand(() => _actions.Navigate(word)));

    /// <summary>
    /// A part of speech's meanings: one line of chips (Google's, "+n" for the rest), or the offline dictionary's lines
    /// per sense, the one that fits the sentence tinted (Lamha 2's <c>senseGroups()</c>).
    /// </summary>
    private void AddMeaningLines(DictGroup group)
    {
        var flow = Localization.FlowOf(Result.Target);
        var backJoin = Localization.IsRightToLeft(Result.Source) ? "، " : ", ";
        ChipItem Term(DictTerm t) => Chip(
            t.Word,
            t.Back.Count > 0 ? string.Join(backJoin, t.Back) : t.Hint.Length > 0 ? t.Hint : Localization.Format("Card_LookUpX", t.Word),
            flow);

        var pos = new PosItem(PosName(group.Pos));
        if (!group.Terms.Any(t => t.Hint.Length > 0))
        {
            var items = new ObservableCollection<MeaningItem> { pos };
            foreach (var t in group.Terms.Take(ChipsShown))
            {
                items.Add(Term(t));
            }

            var rest = group.Terms.Skip(ChipsShown).ToList();
            if (rest.Count > 0)
            {
                MoreItem? more = null;
                more = new MoreItem($"+{Localization.Number(rest.Count)}", Localization.Get("Card_ShowMore"), new RelayCommand(() =>
                {
                    items.Remove(more!);
                    foreach (var t in rest)
                    {
                        items.Add(Term(t));
                    }
                }));
                items.Add(more);
            }

            OtherMeanings.Add(new MeaningLine(items));
            return;
        }

        // The offline dictionary: the part of speech on its own line, then a line per sense.
        OtherMeanings.Add(new MeaningLine([pos]));
        var senses = group.Terms.GroupBy(t => t.Hint).Select(g =>
        {
            var line = new ObservableCollection<MeaningItem>(g.Select(Term));
            if (g.Key.Length > 0)
            {
                line.Add(new HintItem(g.Key));
            }

            return new MeaningLine(line, g.Any(t => t.Best));
        }).ToList();
        foreach (var line in senses.Take(SensesShown))
        {
            OtherMeanings.Add(line);
        }

        if (senses.Count > SensesShown)
        {
            // "Other meanings (2)" opens the rest in its place.
            MeaningLine? more = null;
            more = new MeaningLine([new MoreItem(
                Localization.Format("Card_MoreMeanings", senses.Count - SensesShown),
                Localization.Get("Card_ShowMore"),
                new RelayCommand(() =>
                {
                    var at = OtherMeanings.IndexOf(more!);
                    OtherMeanings.RemoveAt(at);
                    foreach (var line in senses.Skip(SensesShown).Reverse())
                    {
                        OtherMeanings.Insert(at, line);
                    }
                }))]);
            OtherMeanings.Add(more);
        }
    }

    /// <summary>Lamha 2's <c>footer()</c>: Google Translate, Cambridge for English words, Better translation, Writing tools.</summary>
    private List<LinkItem> Footer(LookupResult d, string? translator, bool writingTools)
    {
        var q = Uri.EscapeDataString(d.Query);
        var links = new List<LinkItem>
        {
            Link(Glyph.External, Localization.Get("Card_Google"), "", $"https://translate.google.com/?sl={Uri.EscapeDataString(d.Source.Length > 0 ? d.Source : "auto")}&tl={Uri.EscapeDataString(d.Target)}&text={q}&op=translate"),
        };
        if (d.IsWord && d.Source == "en" && (d.Target == "ar" || d.Explain))
        {
            var book = d.Explain ? "english" : "english-arabic";
            links.Add(Link(Glyph.External, Localization.Get("Card_Cambridge"), "", $"https://dictionary.cambridge.org/dictionary/{book}/{Uri.EscapeDataString(d.Query.ToLowerInvariant().Replace(' ', '-'))}"));
        }

        if (translator is not null && !d.IsWord && d.From != ResultSource.Ai)
        {
            // Google's answer: the AI can do idioms and slang better.
            links.Add(new LinkItem(Glyph.Sparkle, Localization.Get("Card_BetterTr"), Localization.Format("Card_BetterTrTitle", translator), new RelayCommand(_actions.BetterTranslation)));
        }

        if (writingTools && !d.IsWord)
        {
            links.Add(new LinkItem(Glyph.Sparkle, Localization.Get("Card_WritingTools"), "", new RelayCommand(_actions.OpenWritingTools)));
        }

        return links;
    }

    private LinkItem Link(Glyph glyph, string label, string tooltip, string url) =>
        new(glyph, label, tooltip, new RelayCommand(() => _actions.OpenLink(new Uri(url))));

    /// <summary>The Wikipedia summary as the card shows it (Lamha 2's <c>wikiSection()</c>, online only in 3.0).</summary>
    public void ShowWikipedia(WikiSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        Uri.TryCreate(summary.Url, UriKind.Absolute, out var url);
        Uri.TryCreate(summary.Thumb, UriKind.Absolute, out var thumb);
        Wikipedia = new WikiItem(summary.Extract, Localization.FlowOf(summary.Lang), url, thumb, new RelayCommand(() =>
        {
            if (url is not null)
            {
                _actions.OpenLink(url);
            }
        }));
    }
}
