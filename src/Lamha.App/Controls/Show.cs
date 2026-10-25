using Lamha.App.Services;
using Lamha.App.ViewModels.Card;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Lamha.App.Controls;

/// <summary>Small functions for x:Bind: whether to show something, and values derived from the view models.</summary>
public static class Show
{
    public static Visibility If(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility IfNot(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static bool Not(bool value) => !value;

    public static Visibility IfBoth(bool first, bool second) => If(first && second);

    /// <summary>Seen or not, keeping its room either way (CSS's <c>visibility: hidden</c>): the text beside it doesn't reflow.</summary>
    public static double Opaque(bool shown) => shown ? 1 : 0;

    /// <summary>What <see cref="Opaque"/> hides is hidden from Narrator too.</summary>
    public static AccessibilityView Reachable(bool shown) => shown ? AccessibilityView.Content : AccessibilityView.Raw;

    public static Visibility IfText(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility IfSet(object? value) => value is null ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility IfAny(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>A line between the rows of a list, none above its first (Settings' lists).</summary>
    public static Thickness LineAbove(bool first) => first ? new Thickness(0) : new Thickness(0, 1, 0, 0);

    /// <summary>A bar's filled part and the rest, as star widths of a grid (Review's progress, from 0 to 1).</summary>
    public static GridLength Filled(double part) => new(Math.Clamp(part, 0, 1), GridUnitType.Star);

    public static GridLength Unfilled(double part) => new(1 - Math.Clamp(part, 0, 1), GridUnitType.Star);

    /// <summary>A tooltip only when there's something to say (an empty one still pops up).</summary>
    public static string? Tooltip(string? text) => string.IsNullOrEmpty(text) ? null : text;

    public static FlowDirection Flow(bool rightToLeft) => rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>The answer dims while the language switch waits for the other view.</summary>
    public static double Dim(bool busy) => busy ? 0.5 : 1;

    /// <summary>The bar's icon for where the answer came from.</summary>
    public static Glyph BadgeGlyph(AnswerBadge badge) => badge == AnswerBadge.Ai ? Glyph.Sparkle : Glyph.Book;

    /// <summary>A small picture from the internet (Wikipedia's), decoded at the size it's shown so it holds little memory.</summary>
    public static ImageSource? Picture(Uri? uri, int pixels) =>
        uri is null ? null : new BitmapImage(uri) { DecodePixelWidth = pixels, DecodePixelType = DecodePixelType.Logical };

    // ----- text that runs the other way from the interface -----

    /// <summary>
    /// Lamha 2's <c>.card[dir="ltr"] .def [dir="rtl"]</c>: in the English card, a meaning's Arabic lines start on the left
    /// like the rest; in the Arabic card every line starts where its own language starts. A TextBlock's alignment turns
    /// with its own FlowDirection, so an Arabic line in the English card is aligned to its end.
    /// </summary>
    public static TextAlignment StartAligned(FlowDirection lineFlow) =>
        lineFlow == FlowDirection.RightToLeft && !Localization.IsArabic ? TextAlignment.Right : TextAlignment.Left;

    public static TextAlignment FlowAlignment(bool rightToLeft) => StartAligned(Flow(rightToLeft));

    public static TextAlignment LatinAlignment => StartAligned(FlowDirection.LeftToRight);

    public static TextAlignment ArabicAlignment => StartAligned(FlowDirection.RightToLeft);

    /// <summary>
    /// A line that starts where the interface starts, whatever its own language: a meaning next to its word in a row
    /// (an English meaning in the Arabic window stays on the right, by its word).
    /// </summary>
    public static TextAlignment InterfaceAligned(FlowDirection lineFlow) =>
        lineFlow == Localization.FlowDirection ? TextAlignment.Left : TextAlignment.Right;

    /// <summary>
    /// A text box's text lines up as its language reads (English on the left in the Arabic window); an empty box starts
    /// where the interface starts, where Windows would otherwise put the caret on the left.
    /// </summary>
    public static TextAlignment BoxAlignment(bool hasText) => hasText ? TextAlignment.DetectFromContent : TextAlignment.Left;

    // ----- the card's sizes and styles -----

    /// <summary>Chips in English are a size smaller (Lamha 2's <c>.chip.en</c>, 13 px).</summary>
    public static double ChipSize(bool small) => small ? 13 : 14;

    /// <summary>The meaning in context: big and bold, or, for a sentence's translation, a reading size.</summary>
    public static Style HeroStyle(bool isLong) => AppStyle(isLong ? "LamhaHeroLongTextStyle" : "LamhaHeroTextStyle");

    /// <summary>A gloss in Arabic reads at 14.5 px; one in English is secondary text.</summary>
    public static Style GlossStyle(bool rightToLeft) => AppStyle(rightToLeft ? "LamhaReadingTextStyle" : "LamhaSecondaryTextStyle");

    /// <summary>Copy is the filled button when there's no Replace next to it.</summary>
    public static Style CopyStyle(bool canReplace) => AppStyle(canReplace ? "LamhaQuietButtonStyle" : "LamhaButtonStyle");

    /// <summary>The tinted box (the best sense) reaches 8 px past the text on both sides, so the text stays in line.</summary>
    public static Thickness TintedMargin(bool best) => best ? new Thickness(-8, 0, -8, 4) : new Thickness(0, 0, 0, 4);

    public static Thickness TintedPadding(bool best) => best ? new Thickness(8) : new Thickness(0);

    /// <summary>Definitions are 12 px apart; the tinted one reaches past the text like a sense.</summary>
    public static Thickness DefinitionMargin(bool best) => best ? new Thickness(-8, 0, -8, 12) : new Thickness(0, 0, 0, 12);

    /// <summary>The writing tools' output is 16 px under the tools; Write new has no tools above it.</summary>
    public static Thickness WritingOutMargin(bool compose) => compose ? new Thickness(0) : new Thickness(0, 16, 0, 0);

    // ----- errors -----

    public static Visibility ErrorButton(ErrorInfo? error) => If(error is { Action: not ErrorAction.None });

    public static Glyph ErrorGlyph(ErrorInfo? error) => error?.Action == ErrorAction.Settings ? Glyph.Settings : Glyph.Retry;

    public static string ErrorLabel(ErrorInfo? error) =>
        error?.Action == ErrorAction.Settings ? Localization.Get("Common_Settings") : Localization.Get("Common_Retry");

    private static Style AppStyle(string key) => (Style)Application.Current.Resources[key];
}
