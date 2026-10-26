using Lamha.App.Services;
using Microsoft.UI.Xaml;

namespace Lamha.App.Controls;

/// <summary>
/// Something that comes and goes with motion (docs/design/system.md, Motion): <c>Reveal.Shown</c> in place of a bound
/// Visibility shows it rising <c>Reveal.Rise</c> px into place (a toast from the foot: 8; an InfoBar from above: -8)
/// and fades it out before it's collapsed; <c>Reveal.OnLoad</c> brings in one that x:Load creates. Through
/// <see cref="Animations"/>, so at the user's level; nothing moves while its window is first laid out.
/// </summary>
public static class Reveal
{
    public static readonly DependencyProperty ShownProperty =
        DependencyProperty.RegisterAttached("Shown", typeof(bool), typeof(Reveal), new PropertyMetadata(false, OnShownChanged));

    public static readonly DependencyProperty OnLoadProperty =
        DependencyProperty.RegisterAttached("OnLoad", typeof(bool), typeof(Reveal), new PropertyMetadata(false, OnOnLoadChanged));

    public static readonly DependencyProperty RiseProperty =
        DependencyProperty.RegisterAttached("Rise", typeof(double), typeof(Reveal), new PropertyMetadata(8d));

    public static bool GetShown(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(ShownProperty);
    }

    public static void SetShown(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ShownProperty, value);
    }

    public static bool GetOnLoad(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(OnLoadProperty);
    }

    public static void SetOnLoad(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(OnLoadProperty, value);
    }

    public static double GetRise(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(RiseProperty);
    }

    public static void SetRise(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(RiseProperty, value);
    }

    private static void OnShownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        var rise = (float)GetRise(element);
        if ((bool)e.NewValue)
        {
            var wasHidden = element.Visibility == Visibility.Collapsed;
            element.Visibility = Visibility.Visible;
            if (wasHidden && element.IsLoaded)
            {
                Animations.Appear(element, rise);
            }

            return;
        }

        if (!element.IsLoaded || element.Visibility == Visibility.Collapsed)
        {
            element.Visibility = Visibility.Collapsed;
            return;
        }

        Animations.Leave(element, rise / 2, () =>
        {
            if (GetShown(element))
            {
                Animations.ShowNow(element); // shown again while it was leaving: it stays
            }
            else
            {
                element.Visibility = Visibility.Collapsed;
            }
        });
    }

    private static void OnOnLoadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && (bool)e.NewValue)
        {
            element.Loaded += (_, _) => Animations.Appear(element, (float)GetRise(element));
        }
    }
}
