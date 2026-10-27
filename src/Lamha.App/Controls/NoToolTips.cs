using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lamha.App.Controls;

/// <summary>
/// No tooltips anywhere under an element: the card (the user's call, 2026-10-27: they got in the way). The card's
/// tooltips come from many places (x:Uid strings, bindings, item templates, views it shares with the main window), and
/// WinUI lays a tooltip out outside the card, where the card's styles don't reach; so after each layout what the
/// element holds is looked through once and its tooltips taken off. Names for screen readers stay.
/// </summary>
public static class NoToolTips
{
    public static readonly DependencyProperty IsOnProperty =
        DependencyProperty.RegisterAttached("IsOn", typeof(bool), typeof(NoToolTips), new PropertyMetadata(false, OnIsOnChanged));

    /// <summary>The elements with a look-through already queued.</summary>
    private static readonly HashSet<FrameworkElement> Queued = [];

    public static bool GetIsOn(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsOnProperty);
    }

    public static void SetIsOn(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsOnProperty, value);
    }

    private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement root && (bool)e.NewValue)
        {
            // Raised after any layout in the window: new content, an answer arriving, a binding setting a tooltip again.
            root.LayoutUpdated += (_, _) => Queue(root);
        }
    }

    /// <summary>One look-through after a burst of layouts (the card's height animating lays out every frame).</summary>
    private static void Queue(FrameworkElement root)
    {
        if (Queued.Add(root))
        {
            root.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                Queued.Remove(root);
                TakeOff(root);
            });
        }
    }

    private static void TakeOff(DependencyObject element)
    {
        if (ToolTipService.GetToolTip(element) is not null)
        {
            ToolTipService.SetToolTip(element, null);
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            TakeOff(VisualTreeHelper.GetChild(element, i));
        }
    }
}
