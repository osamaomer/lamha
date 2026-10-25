using Lamha.App.Services;
using Lamha.App.ViewModels.Card;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;

namespace Lamha.App.Views;

/// <summary>
/// A lookup's answer (<see cref="WordAnswer"/>), as the card shows it: in the card, and on the main window's Translate
/// page. Its host gives it edges and the links at the foot.
/// </summary>
public sealed partial class AnswerView : UserControl
{
    public static readonly DependencyProperty AnswerProperty =
        DependencyProperty.Register(nameof(Answer), typeof(WordAnswer), typeof(AnswerView), new PropertyMetadata(null, (d, _) => ((AnswerView)d).Bindings.Update()));

    public static readonly DependencyProperty ShowsTextProperty =
        DependencyProperty.Register(nameof(ShowsText), typeof(bool), typeof(AnswerView), new PropertyMetadata(true, (d, _) => ((AnswerView)d).Bindings.Update()));

    public AnswerView()
    {
        InitializeComponent();
    }

    public WordAnswer? Answer
    {
        get => (WordAnswer?)GetValue(AnswerProperty);
        set => SetValue(AnswerProperty, value);
    }

    /// <summary>
    /// A text's answer starts with the text, as selected (the card). False where the text is already in sight, in the
    /// Translate page's box.
    /// </summary>
    public bool ShowsText
    {
        get => (bool)GetValue(ShowsTextProperty);
        set => SetValue(ShowsTextProperty, value);
    }

    /// <summary>The interface's direction, for lines inside a box that runs right to left in both languages.</summary>
    public FlowDirection InterfaceFlow { get; } = Localization.FlowDirection;

    // Its own words (x:Bind reads them once).
    public string ShowAll { get; } = Localization.Get("Card_ShowAll");

    public string DidYouMean { get; } = Localization.Get("Card_DidYouMean");

    public string DidYouMeanEnd { get; } = Localization.Get("Card_DidYouMeanEnd");

    public string OtherMeaningsHeading { get; } = Localization.Get("Card_OtherMeanings");

    public string DefinitionHeading { get; } = Localization.Get("Card_Definition");

    public string ExamplesHeading { get; } = Localization.Get("Card_Examples");

    public string WikipediaHeading { get; } = Localization.Get("Card_Wikipedia");

    public string ReadWiki { get; } = Localization.Get("Card_ReadWiki");

    public Visibility TextShown(bool isText) => isText && ShowsText ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The meaning's box is 12 px under the headword or the text; first in the answer when the text isn't shown.</summary>
    public Thickness HeroMargin(bool isText) => new(0, isText && !ShowsText ? 0 : 12, 0, 0);

    /// <summary>The selected text, clamped to a few lines, shows all of it on a click (Lamha 2's <c>expandable()</c>).</summary>
    private void OnSourceTapped(object sender, TappedRoutedEventArgs e) => SourceText.MaxLines = SourceText.MaxLines == 0 ? 3 : 0;

    private void OnSpellClick(Hyperlink sender, HyperlinkClickEventArgs args) => Answer?.OpenSpellCommand.Execute(null);
}
