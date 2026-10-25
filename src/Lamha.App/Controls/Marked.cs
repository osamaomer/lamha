using Lamha.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Lamha.App.Controls;

/// <summary>
/// Marks what a search matched in a TextBlock's text (Lamha 2's clip-list.js <c>highlight()</c>), with the search's own
/// Arabic-aware rules (Core's <c>SearchText.MatchRanges</c>): with or without hamza and harakat, digits and case aside.
/// </summary>
public static class Marked
{
    public static readonly DependencyProperty QueryProperty =
        DependencyProperty.RegisterAttached("Query", typeof(string), typeof(Marked), new PropertyMetadata("", OnQueryChanged));

    /// <summary>The TextBlock's text is watched once a query is set: a row shown again for another copy is marked afresh.</summary>
    private static readonly DependencyProperty WatchProperty =
        DependencyProperty.RegisterAttached("Watch", typeof(long), typeof(Marked), new PropertyMetadata(0L));

    public static string GetQuery(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string)element.GetValue(QueryProperty);
    }

    public static void SetQuery(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(QueryProperty, value);
    }

    private static void OnQueryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock text)
        {
            return;
        }

        if ((long)text.GetValue(WatchProperty) == 0)
        {
            text.SetValue(WatchProperty, text.RegisterPropertyChangedCallback(TextBlock.TextProperty, (sender, _) => Mark((TextBlock)sender)));
        }

        Mark(text);
    }

    private static void Mark(TextBlock text)
    {
        text.TextHighlighters.Clear();
        var ranges = SearchText.MatchRanges(text.Text, GetQuery(text));
        if (ranges.Count == 0)
        {
            return;
        }

        var mark = new TextHighlighter
        {
            Background = ThemeLookup.Get(text, "LamhaMarkBrush") as Brush,
            Foreground = ThemeLookup.Get(text, "LamhaMarkTextBrush") as Brush,
        };
        foreach (var (start, end) in ranges)
        {
            mark.Ranges.Add(new TextRange { StartIndex = start, Length = end - start });
        }

        text.TextHighlighters.Add(mark);
    }
}
