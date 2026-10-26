using System.ComponentModel;
using Lamha.App.Services;
using Lamha.App.ViewModels.Main;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace Lamha.App.Views;

/// <summary>The main window's Clipboard page: search, All / Pinned, pause, and the copies (ViewModels/Main/ClipboardViewModel).</summary>
public sealed partial class ClipboardPage : UserControl
{
    public ClipboardPage(ClipboardViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        // The chosen copy stays in view when the keys move it.
        ViewModel.PropertyChanged += OnViewModelChanged;
    }

    public ClipboardViewModel ViewModel { get; }

    // Its own words (x:Bind reads them once).
    public string OptInTitle { get; } = Localization.Get("Clipboard_OptInTitle");

    public string OptInText { get; } = Localization.Get("Clipboard_OptInText");

    public string EnableLabel { get; } = Localization.Get("Clipboard_Enable");

    public string SearchPlaceholder { get; } = Localization.Get("Clipboard_Search");

    public string SearchLabel { get; } = Localization.Get("Clipboard_SearchLabel");

    public string FilterLabel { get; } = Localization.Get("Clipboard_Filter");

    public string AllLabel { get; } = Localization.Get("Clipboard_All");

    public string PinnedLabel { get; } = Localization.Get("Clipboard_PinnedFilter");

    public string PauseLabel { get; } = Localization.Get("Tray_Pause");

    public string Pause15 { get; } = Localization.Get("Tray_Pause15");

    public string Pause60 { get; } = Localization.Get("Tray_Pause60");

    public string PauseUntil { get; } = Localization.Get("Tray_PauseUntil");

    public string PausedTitle { get; } = Localization.Get("Tray_TipPaused");

    public string PausedText { get; } = Localization.Get("Clipboard_PausedText");

    public string ResumeLabel { get; } = Localization.Get("Clipboard_Resume");

    public string HistoryLabel { get; } = Localization.Get("Clipboard_HistoryLabel");

    public string ShowMoreLabel { get; } = Localization.Get("Clipboard_ShowMore");

    /// <summary>The search box, ready to type (when the page opens).</summary>
    public void FocusSearch() => DispatcherQueue.TryEnqueue(() => SearchBox.Focus(FocusState.Programmatic));

    /// <summary>
    /// The list's keys, from the search box too (Lamha 2's clip-list.js): ↑ ↓ and Page Up / Down choose a copy, Ctrl+P
    /// pins it, Delete deletes it (in the search box only with the caret at the end), Esc empties the search.
    /// </summary>
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var inSearch = ReferenceEquals(e.OriginalSource, SearchBox);
        // A button or a menu with the focus keeps its own keys (Space, Enter).
        if (!inSearch && e.OriginalSource is ButtonBase or TextBox)
        {
            return;
        }

        var ctrl = IsDown(VirtualKey.Control) && !IsDown(VirtualKey.Menu);
        switch (e.Key)
        {
            case VirtualKey.Down or VirtualKey.Up:
                ViewModel.Move(e.Key == VirtualKey.Down ? 1 : -1);
                e.Handled = true;
                break;
            case VirtualKey.PageDown or VirtualKey.PageUp:
                ViewModel.Move(e.Key == VirtualKey.PageDown ? 6 : -6);
                e.Handled = true;
                break;
            case VirtualKey.P when ctrl:
                ViewModel.TogglePinCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Delete when !inSearch || SearchBox.SelectionStart == SearchBox.Text.Length:
                ViewModel.DeleteCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Escape when ViewModel.HasQuery:
                ViewModel.Query = "";
                e.Handled = true;
                break;
        }
    }

    private void OnRowTapped(object sender, TappedRoutedEventArgs e)
    {
        // A tool, pin or delete keeps its own click.
        if (sender is FrameworkElement { DataContext: ClipRow row } && !IsInButton(e.OriginalSource as DependencyObject, (DependencyObject)sender))
        {
            ViewModel.Choose(row);
        }
    }

    /// <summary>The hover tint, as the History rows' (a row isn't a button: it holds buttons).</summary>
    private void OnRowEntered(object sender, PointerRoutedEventArgs e) => Hover(sender, 1);

    private void OnRowExited(object sender, PointerRoutedEventArgs e) => Hover(sender, 0);

    /// <summary>The row's first child is its hover tint.</summary>
    private static void Hover(object row, double opacity)
    {
        if (row is Grid { Children.Count: > 0 } grid)
        {
            grid.Children[0].Opacity = opacity;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClipboardViewModel.Selected) && ViewModel.Selected is { } row && ViewModel.Rows.IndexOf(row) is >= 0 and var at)
        {
            DispatcherQueue.TryEnqueue(() => List.TryGetElement(at)?.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = double.NaN }));
        }
    }

    private static bool IsInButton(DependencyObject? element, DependencyObject row)
    {
        for (var e = element; e is not null && e != row; e = VisualTreeHelper.GetParent(e))
        {
            if (e is ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDown(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
