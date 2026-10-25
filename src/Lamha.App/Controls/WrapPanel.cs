using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Lamha.App.Controls;

/// <summary>
/// Lays its children out in lines, wrapping to the next when one is full (CSS's flex-wrap, which Lamha 2's chips use).
/// Right to left in Arabic, like any panel: the window's FlowDirection mirrors it.
/// </summary>
public sealed partial class WrapPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(0.0, Relayout));

    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(nameof(VerticalSpacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(0.0, Relayout));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double x = 0, y = 0, line = 0, width = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > availableSize.Width)
            {
                y += line + VerticalSpacing;
                x = line = 0;
            }

            x += size.Width + HorizontalSpacing;
            line = Math.Max(line, size.Height);
            width = Math.Max(width, x - HorizontalSpacing);
        }

        return new Size(width, Children.Count == 0 ? 0 : y + line);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, line = 0;
        var row = new List<UIElement>();
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > finalSize.Width)
            {
                Place(row, y, line);
                y += line + VerticalSpacing;
                x = line = 0;
                row.Clear();
            }

            row.Add(child);
            x += size.Width + HorizontalSpacing;
            line = Math.Max(line, size.Height);
        }

        Place(row, y, line);
        return finalSize;
    }

    private static void Relayout(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((WrapPanel)d).InvalidateMeasure();

    /// <summary>One line, its items centred on the line's height (a chip next to a smaller label).</summary>
    private void Place(List<UIElement> row, double y, double line)
    {
        double x = 0;
        foreach (var child in row)
        {
            var size = child.DesiredSize;
            child.Arrange(new Rect(x, y + ((line - size.Height) / 2), size.Width, size.Height));
            x += size.Width + HorizontalSpacing;
        }
    }
}
