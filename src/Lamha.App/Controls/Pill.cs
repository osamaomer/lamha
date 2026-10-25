using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lamha.App.Controls;

/// <summary>
/// A pill: round ends, whatever the element's height (Arabic text makes a chip taller). Lamha 2's CSS said
/// <c>border-radius: 999px</c>, which a browser scales down evenly into a pill; WinUI clamps each corner to half the
/// width across and half the height down on its own, so 999 drew an ellipse. The radius is half the height instead.
/// </summary>
public static class Pill
{
    public static readonly DependencyProperty IsPillProperty =
        DependencyProperty.RegisterAttached("IsPill", typeof(bool), typeof(Pill), new PropertyMetadata(false, OnIsPillChanged));

    public static bool GetIsPill(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsPillProperty);
    }

    public static void SetIsPill(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsPillProperty, value);
    }

    private static void OnIsPillChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.SizeChanged -= OnSizeChanged;
        if ((bool)e.NewValue)
        {
            element.SizeChanged += OnSizeChanged;
            Round(element, element.ActualHeight);
        }
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => Round((FrameworkElement)sender, e.NewSize.Height);

    private static void Round(FrameworkElement element, double height)
    {
        var radius = new CornerRadius(height / 2);
        switch (element)
        {
            case Control control:
                control.CornerRadius = radius;
                break;
            case Border border:
                border.CornerRadius = radius;
                break;
        }
    }
}
