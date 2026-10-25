using Lamha.Core.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Lamha.App.Controls;

/// <summary>
/// Proofreading's corrected text with each mistake struck through and its fix highlighted (Lamha 2's
/// <c>diffNodes()</c>: <c>del</c> in the danger colour, <c>ins</c> in the "ok" colour on its tint, semibold).
/// </summary>
public sealed partial class DiffText : UserControl
{
    public static readonly DependencyProperty PartsProperty =
        DependencyProperty.Register(nameof(Parts), typeof(object), typeof(DiffText), new PropertyMetadata(null, (d, _) => ((DiffText)d).Draw()));

    public static readonly DependencyProperty FallbackProperty =
        DependencyProperty.Register(nameof(Fallback), typeof(string), typeof(DiffText), new PropertyMetadata("", (d, _) => ((DiffText)d).Draw()));

    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };

    public DiffText()
    {
        IsTabStop = false;
        Content = _text;
        ActualThemeChanged += (_, _) => Draw(); // the colours are the theme's
    }

    /// <summary>The <see cref="DiffPart"/> list; null shows <see cref="Fallback"/> alone (texts too long to compare).</summary>
    public object? Parts
    {
        get => GetValue(PartsProperty);
        set => SetValue(PartsProperty, value);
    }

    public string Fallback
    {
        get => (string)GetValue(FallbackProperty);
        set => SetValue(FallbackProperty, value);
    }

    /// <summary>The text's style (size, line height); its colours come from the theme's tokens.</summary>
    public Style? TextStyle
    {
        get => _text.Style;
        set => _text.Style = value;
    }

    private void Draw()
    {
        _text.Inlines.Clear();
        _text.TextHighlighters.Clear();
        if (Parts is not IReadOnlyList<DiffPart> parts)
        {
            _text.Inlines.Add(new Run { Text = Fallback });
            return;
        }

        var removed = Brush("LamhaDangerBrush");
        var added = Brush("LamhaOkBrush");
        var highlight = new TextHighlighter { Background = Brush("LamhaOkSoftBrush") };
        var at = 0;
        foreach (var part in parts)
        {
            // Spaces that were removed don't show; removed and added words keep the spaces around them.
            if (part.Kind != DiffKind.Same && part.Text.Trim().Length == 0)
            {
                if (part.Kind == DiffKind.Removed)
                {
                    continue;
                }

                _text.Inlines.Add(new Run { Text = part.Text });
                at += part.Text.Length;
                continue;
            }

            var run = new Run { Text = part.Text };
            if (part.Kind == DiffKind.Removed)
            {
                run.Foreground = removed;
                run.TextDecorations = global::Windows.UI.Text.TextDecorations.Strikethrough;
            }
            else if (part.Kind == DiffKind.Inserted)
            {
                run.Foreground = added;
                run.FontWeight = FontWeights.SemiBold;
                highlight.Ranges.Add(new TextRange { StartIndex = at, Length = part.Text.Length });
            }

            _text.Inlines.Add(run);
            at += part.Text.Length;
        }

        _text.TextHighlighters.Add(highlight);
    }

    private Brush? Brush(string key) => ThemeLookup.Get(this, key) as Brush;
}
