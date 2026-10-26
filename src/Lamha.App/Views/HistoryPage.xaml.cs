using Lamha.App.Services;
using Lamha.App.ViewModels.Main;
using Lamha.Core.Appearance;
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
        _ = new ListMotion(DayList, ViewModel, nameof(HistoryViewModel.Days), items: Lines);
    }

    /// <summary>What comes in one after another: each day's name, then its words.</summary>
    private IEnumerable<UIElement> Lines()
    {
        foreach (var day in ListMotion.Realized(DayList, Motion.StaggerMax))
        {
            if (day is not Panel { Children: [var name, ItemsRepeater words] })
            {
                continue;
            }

            yield return name;
            foreach (var word in ListMotion.Realized(words, Motion.StaggerMax))
            {
                yield return word;
            }
        }
    }

    public HistoryViewModel ViewModel { get; }

    // Its own words (x:Bind reads them once).
    public string Placeholder { get; } = Localization.Get("History_Search");

    public string ClearLabel { get; } = Localization.Get("Common_Clear");

    public string TranslateWordLabel { get; } = Localization.Get("History_TranslateWord");

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
}
