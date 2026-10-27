using Lamha.Core.Translation;
using Microsoft.UI.Xaml;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.Main;

/// <summary>A row of side by side: a sentence beside its translation, each in its own direction; a paragraph's last one has room after it.</summary>
public sealed record SideRowItem(string Source, string Translation, FlowDirection SourceFlow, FlowDirection TranslationFlow, Thickness Margin)
{
    /// <summary>What Narrator reads for the row: the sentence, then its translation.</summary>
    public string Spoken => $"{Source} {Translation}";
}

/// <summary>
/// A paragraph's answer as two columns (Core's <see cref="SideBySide"/>): the text at the start, its translation beside
/// it, each column headed by its language with Listen (and Copy for the translation).
/// </summary>
public sealed class SideBySideAnswer
{
    /// <summary>The room after a paragraph's last sentence (Windows' 12).</summary>
    private const double ParagraphGap = 12;

    private SideBySideAnswer(LookupResult result, IReadOnlyList<SideRow> rows)
    {
        SourceLang = result.Source;
        TranslationLang = result.Target;
        SourceName = Localization.LanguageName(result.Source);
        TranslationName = Localization.LanguageName(result.Target);
        SourceFlow = Localization.FlowOf(result.Source);
        TranslationFlow = Localization.FlowOf(result.Target);
        SourceText = result.Query;
        TranslationText = result.Translation;
        Rows = [.. rows.Select(r => new SideRowItem(r.Source, r.Translation, SourceFlow, TranslationFlow, new Thickness(0, 0, 0, r.EndsParagraph ? ParagraphGap : 0)))];
    }

    /// <summary>Side by side for <paramref name="result"/>, or null when it isn't a paragraph.</summary>
    public static SideBySideAnswer? For(LookupResult result) =>
        SideBySide.Rows(result) is { Count: > 0 } rows ? new SideBySideAnswer(result, rows) : null;

    public List<SideRowItem> Rows { get; }

    public string SourceLang { get; }

    public string TranslationLang { get; }

    /// <summary>"English", "العربية": each column's heading, in the interface's language.</summary>
    public string SourceName { get; }

    public string TranslationName { get; }

    public FlowDirection SourceFlow { get; }

    public FlowDirection TranslationFlow { get; }

    /// <summary>The whole text and the whole translation, for Listen.</summary>
    public string SourceText { get; }

    public string TranslationText { get; }
}
