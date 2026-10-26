using Lamha.App.Services;
using Lamha.App.ViewModels.Main;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Lamha.App.Views;

/// <summary>The main window's Translate page: the box, the answer, Today and the recent lookups.</summary>
public sealed partial class TranslatePage : UserControl
{
    /// <summary>The box's text is being replaced by Lamha's: the caret goes after it once it's there.</summary>
    private bool _caretToEnd;

    public TranslatePage(TranslateViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        // Text set from code leaves Windows' caret before its first letter: after it, ready to go on typing.
        viewModel.QueryReplaced += () =>
        {
            if (QueryBox.Text == ViewModel.Query)
            {
                QueryBox.Select(QueryBox.Text.Length, 0);
            }
            else
            {
                _caretToEnd = true;
            }
        };
        QueryBox.TextChanged += (_, _) =>
        {
            if (_caretToEnd && QueryBox.Text == ViewModel.Query)
            {
                _caretToEnd = false;
                QueryBox.Select(QueryBox.Text.Length, 0);
            }
        };
        if (viewModel.ShowsLookupTip)
        {
            Loaded += OnLoadedOpenTip;
        }
    }

    public TranslateViewModel ViewModel { get; }

    public string LookupTipTitle { get; } = Localization.Get("Translate_TipTitle");

    /// <summary>The first time the page shows: the hint, once laid out (it points at the line); seen once it's shown.</summary>
    private void OnLoadedOpenTip(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedOpenTip;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            LookupTip.IsOpen = true;
            ViewModel.LookupTipSeen();
        });
    }

    private void OnLookupTipClosed(TeachingTip sender, TeachingTipClosedEventArgs args) => FocusBox();

    // Its own words (x:Bind reads them once).
    public string Placeholder { get; } = Localization.Get("Translate_Placeholder");

    public string QuickLookup { get; } = Localization.Get("Translate_QuickLookup");

    public string TodayLabel { get; } = Localization.Get("Today_Label");

    public string StreakTitle { get; } = Localization.Get("Today_StreakTitle");

    public string LookUpLabel { get; } = Localization.Get("Today_LookUp");

    public string RecentHeading { get; } = Localization.Get("Translate_Recent");

    public string ClearLabel { get; } = Localization.Get("Common_Clear");

    /// <summary>The caret in the box, ready to type; once the page is shown, when it has only just been put in the window.</summary>
    public void FocusBox()
    {
        if (IsLoaded)
        {
            QueryBox.Focus(FocusState.Programmatic);
            return;
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            QueryBox.Focus(FocusState.Programmatic);
        }

        Loaded += OnLoaded;
    }

    /// <summary>Enter looks up at once; Shift+Enter starts a new line (Lamha 2's box).</summary>
    private void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && !InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down))
        {
            e.Handled = true;
            ViewModel.LookUpNow();
        }
    }

    /// <summary>The ×: the box empties, and keeps the focus for the next word.</summary>
    private void OnClear(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearCommand.Execute(null);
        FocusBox();
    }
}
