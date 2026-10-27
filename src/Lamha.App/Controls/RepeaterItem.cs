using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lamha.App.Controls;

/// <summary>
/// The item behind a row of an <see cref="ItemsRepeater"/>. The repeater hands an x:Bind template its item through the
/// template's bindings only, never as DataContext (a row's DataContext is null), so a row's handler asks the repeater.
/// </summary>
public static class RepeaterItem
{
    /// <summary>The item shown by the row that holds <paramref name="element"/> (the row itself, or anything in it).</summary>
    public static object? Of(ItemsRepeater list, DependencyObject? element)
    {
        ArgumentNullException.ThrowIfNull(list);
        for (var e = element; e is not null; e = VisualTreeHelper.GetParent(e))
        {
            if (e is UIElement row && ReferenceEquals(VisualTreeHelper.GetParent(e), list))
            {
                var index = list.GetElementIndex(row);
                return index >= 0 && list.ItemsSourceView is { } items && index < items.Count ? items.GetAt(index) : null;
            }
        }

        return null;
    }
}
