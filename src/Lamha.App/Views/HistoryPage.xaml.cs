using Lamha.App.Services;
using Lamha.App.ViewModels.Main;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Lamha.App.Views;

/// <summary>The main window's History page: the search, Clear, and every word by day.</summary>
public sealed partial class HistoryPage : UserControl
{
    public HistoryPage(HistoryViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public HistoryViewModel ViewModel { get; }

    // Its own words (x:Bind reads them once).
    public string Placeholder { get; } = Localization.Get("History_Search");

    public string ClearLabel { get; } = Localization.Get("Common_Clear");

    public string EmptyTitle { get; } = Localization.Get("History_Empty");

    public string EmptyHint { get; } = Localization.Get("History_EmptyHint");

    public string NoResults { get; } = Localization.Get("History_NoResults");

    /// <summary>Esc empties the search, and the whole history comes back.</summary>
    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && ViewModel.HasQuery)
        {
            e.Handled = true;
            ViewModel.Query = "";
        }
    }

    /// <summary>The field's accent ring while the search has the focus.</summary>
    private void OnSearchFocus(object sender, RoutedEventArgs e) =>
        FieldRing.Visibility = SearchBox.FocusState == FocusState.Unfocused ? Visibility.Collapsed : Visibility.Visible;
}
