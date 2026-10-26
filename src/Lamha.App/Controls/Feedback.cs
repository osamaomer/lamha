using Lamha.App.Services;
using Lamha.Core.Appearance;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Lamha.App.Controls;

/// <summary>
/// A button that confirms itself (docs/design/system.md, Motion: "an action confirms itself"): with
/// <c>Feedback.Copy</c>, its Copy icon turns into a ✓ for 1.5 s when pressed, then back; with <c>Feedback.Pop</c>, a
/// toggle's icon (the star) pops when it's turned on. Only when the user presses it, never when a list is shown.
/// <c>Feedback.Dip</c> makes a pressed button dip to 98 %, <c>Feedback.Fade</c> fades a template's hover fill.
/// </summary>
public static class Feedback
{
    /// <summary>How long the ✓ stays (Windows' Accept, as the design system has it).</summary>
    private static readonly TimeSpan CheckTime = TimeSpan.FromMilliseconds(1500);

    public static readonly DependencyProperty CopyProperty =
        DependencyProperty.RegisterAttached("Copy", typeof(bool), typeof(Feedback), new PropertyMetadata(false, OnCopyChanged));

    public static readonly DependencyProperty PopProperty =
        DependencyProperty.RegisterAttached("Pop", typeof(bool), typeof(Feedback), new PropertyMetadata(false, OnPopChanged));

    /// <summary>Pressed, it dips to 98 % and comes back (Settings' cards, icon buttons).</summary>
    public static readonly DependencyProperty DipProperty =
        DependencyProperty.RegisterAttached("Dip", typeof(bool), typeof(Feedback), new PropertyMetadata(false, OnDipChanged));

    /// <summary>A hover fill whose opacity fades (Windows' 83 ms) instead of switching: Lamha's own templates.</summary>
    public static readonly DependencyProperty FadeProperty =
        DependencyProperty.RegisterAttached("Fade", typeof(bool), typeof(Feedback), new PropertyMetadata(false, OnFadeChanged));

    /// <summary>Counts a button's presses, so the ✓ of an earlier one doesn't put the icon back too soon.</summary>
    private static readonly DependencyProperty PressesProperty =
        DependencyProperty.RegisterAttached("Presses", typeof(int), typeof(Feedback), new PropertyMetadata(0));

    public static bool GetCopy(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(CopyProperty);
    }

    public static void SetCopy(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CopyProperty, value);
    }

    public static bool GetPop(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(PopProperty);
    }

    public static void SetPop(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PopProperty, value);
    }

    public static bool GetDip(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(DipProperty);
    }

    public static void SetDip(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(DipProperty, value);
    }

    public static bool GetFade(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(FadeProperty);
    }

    public static void SetFade(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FadeProperty, value);
    }

    private static void OnDipChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && (bool)e.NewValue)
        {
            // Handled too: a button handles its own presses.
            element.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => Animations.Dip(element, down: true)), handledEventsToo: true);
            var up = new PointerEventHandler((_, _) => Animations.Dip(element, down: false));
            element.AddHandler(UIElement.PointerReleasedEvent, up, handledEventsToo: true);
            element.AddHandler(UIElement.PointerCaptureLostEvent, up, handledEventsToo: true);
            element.AddHandler(UIElement.PointerCanceledEvent, up, handledEventsToo: true);
        }
    }

    /// <summary>The fade is set when the element loads, at the level then (none at off).</summary>
    private static void OnFadeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && (bool)e.NewValue)
        {
            element.Loaded += (_, _) => element.OpacityTransition = Animations.Level == MotionLevel.Off ? null
                : new ScalarTransition { Duration = Motion.Time(Animations.Level, Motion.ExitMilliseconds) };
        }
    }

    private static void OnCopyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ButtonBase button && (bool)e.NewValue)
        {
            button.Click += OnCopyClick;
        }
    }

    private static void OnPopChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToggleButton button && (bool)e.NewValue)
        {
            button.Click += OnPopClick;
        }
    }

    private static async void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ButtonBase button || Icon(button) is not { } icon)
        {
            return;
        }

        var press = (int)button.GetValue(PressesProperty) + 1;
        button.SetValue(PressesProperty, press);
        icon.Glyph = Glyph.Check;
        Animations.Pop(icon);
        await Task.Delay(CheckTime).ConfigureAwait(true);
        if ((int)button.GetValue(PressesProperty) == press)
        {
            icon.Glyph = Glyph.Copy;
        }
    }

    /// <summary>A toggle's state has changed by the time it's clicked: it pops only when it's now on.</summary>
    private static void OnPopClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { IsChecked: true } button && Icon(button) is { } icon)
        {
            Animations.Pop(icon);
        }
    }

    /// <summary>The button's icon: its content, or the first icon inside it (an icon beside a label).</summary>
    private static FluentIcon? Icon(DependencyObject parent)
    {
        if (parent is FluentIcon icon)
        {
            return icon;
        }

        if (parent is ContentControl { Content: DependencyObject content } && Icon(content) is { } inContent)
        {
            return inContent;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            if (Icon(VisualTreeHelper.GetChild(parent, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
