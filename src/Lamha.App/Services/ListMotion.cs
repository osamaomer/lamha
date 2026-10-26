using System.ComponentModel;
using Lamha.Core.Appearance;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lamha.App.Services;

/// <summary>
/// A list's motion, as Lamha 2's clip-list.js has it (full only, both from shared/motion.js with their numbers):
/// when the list comes into view its first items come in one after another (<c>stagger()</c>); when it changes after
/// that, its rows glide to their new places (<c>flip()</c>): a copy pinned to the top and the others making room, a row
/// deleted and the others closing the gap, Undo bringing it back. Rows are matched by <c>key</c>, a new one fades in,
/// at most 12 move. It watches the view model's property that holds the list (replaced whole when it changes).
/// </summary>
public sealed class ListMotion
{
    /// <summary>Lamha 2's limits: the rows looked at, and the rows that move.</summary>
    private const int Looked = 40;
    private const int Moved = 12;

    /// <summary>A row that's new to the list fades in this long (Lamha 2's 160 ms).</summary>
    private const int NewMilliseconds = 160;

    private readonly ItemsRepeater _list;
    private readonly Func<object, object>? _key;
    private readonly Func<IEnumerable<UIElement>> _items;
    private readonly string _property;
    private readonly int _each;
    private readonly int _milliseconds;
    private Dictionary<object, double>? _before;
    private bool _entering;

    /// <param name="list">The list.</param>
    /// <param name="viewModel">What holds it, in <paramref name="property"/>.</param>
    /// <param name="property">The property that's replaced when the list changes.</param>
    /// <param name="key">What makes a row the same row after the change; without one, the list only comes in.</param>
    /// <param name="items">What comes in one after another, when it isn't the list's rows (History's days and words).</param>
    /// <param name="each">How far apart they come in (Lamha 2's clipboard list: 15).</param>
    /// <param name="milliseconds">How long each takes (Lamha 2's clipboard list: 180).</param>
    public ListMotion(ItemsRepeater list, INotifyPropertyChanged viewModel, string property, Func<object, object>? key = null,
        Func<IEnumerable<UIElement>>? items = null, int each = Motion.StaggerEachMilliseconds, int milliseconds = Motion.StaggerMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _list = list;
        _key = key;
        _property = property;
        _each = each;
        _milliseconds = milliseconds;
        _items = items ?? (() => Rows());
        list.Loaded += (_, _) => Entering();
        viewModel.PropertyChanged += OnChanged;
        if (key is not null && viewModel is INotifyPropertyChanging changing)
        {
            changing.PropertyChanging += OnChanging;
        }
    }

    /// <summary>The list comes into view (its page opened, its panel shown): it comes in once it's laid out.</summary>
    public void Entering()
    {
        _entering = true;
        _list.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ComeIn);
    }

    private void OnChanging(object? sender, PropertyChangingEventArgs e)
    {
        if (e.PropertyName == _property && !_entering)
        {
            Before();
        }
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != _property)
        {
            return;
        }

        if (_entering)
        {
            _list.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ComeIn);
        }
        else
        {
            After();
        }
    }

    /// <summary>The first items come in, if they're there yet (the list may still be read).</summary>
    private void ComeIn()
    {
        if (!_entering)
        {
            return;
        }

        _list.UpdateLayout();
        var items = _items().ToList();
        if (items.Count > 0)
        {
            _entering = false;
            Animations.Stagger(items, _each, _milliseconds);
        }
    }

    /// <summary>Before the list changes: where its rows are.</summary>
    private void Before()
    {
        _before = null;
        if (Animations.Level != MotionLevel.Full || _key is null)
        {
            return;
        }

        var before = new Dictionary<object, double>();
        foreach (var row in Rows())
        {
            if (row.DataContext is { } item)
            {
                before[_key(item)] = row.ActualOffset.Y;
            }
        }

        _before = before;
    }

    /// <summary>After it changed: once laid out, each row glides from where it was.</summary>
    private void After()
    {
        if (_before is not { } before || _key is not { } key)
        {
            return;
        }

        _before = null;
        _list.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _list.UpdateLayout();
            var moved = 0;
            foreach (var row in Rows())
            {
                if (moved >= Moved)
                {
                    break;
                }

                if (row.DataContext is not { } item)
                {
                    continue;
                }

                if (!before.TryGetValue(key(item), out var was))
                {
                    Animations.Enter(row, Animations.Level, NewMilliseconds, from: 0, rise: 0, scale: 1, Motion.EnterEasing);
                    moved++;
                    continue;
                }

                var by = was - row.ActualOffset.Y;
                if (Math.Abs(by) >= 1)
                {
                    Animations.Glide(row, (float)by);
                    moved++;
                }
            }
        });
    }

    /// <summary>The rows on screen (a list scrolled down has none realized at its top).</summary>
    private List<FrameworkElement> Rows() => Realized(_list, Looked);

    /// <summary>Up to <paramref name="most"/> of <paramref name="list"/>'s realized items, in order.</summary>
    public static List<FrameworkElement> Realized(ItemsRepeater list, int most)
    {
        ArgumentNullException.ThrowIfNull(list);
        var rows = new List<FrameworkElement>();
        var count = list.ItemsSourceView?.Count ?? 0;
        for (var i = 0; i < count && rows.Count < most; i++)
        {
            if (list.TryGetElement(i) is FrameworkElement row)
            {
                rows.Add(row);
            }
            else if (rows.Count > 0)
            {
                break; // past the rows on screen
            }
        }

        return rows;
    }
}
