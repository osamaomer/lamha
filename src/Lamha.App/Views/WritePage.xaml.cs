using System.ComponentModel;
using Lamha.App.Services;
using Lamha.App.ViewModels.Main;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Lamha.App.Views;

/// <summary>The main window's Write page: the draft, its tools and their result, and the mistake journal.</summary>
public sealed partial class WritePage : UserControl
{
    /// <summary>The draft's text is being replaced by Lamha's: the caret goes after it once it's there.</summary>
    private bool _caretToEnd;

    public WritePage(WriteViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        // Text set from code leaves Windows' caret before its first letter: after it, ready to go on writing.
        viewModel.DraftReplaced += () =>
        {
            if (DraftBox.Text == ViewModel.Draft)
            {
                DraftBox.Select(DraftBox.Text.Length, 0);
            }
            else
            {
                _caretToEnd = true;
            }

            FocusDraft();
        };
        DraftBox.TextChanged += (_, _) =>
        {
            if (_caretToEnd && DraftBox.Text == ViewModel.Draft)
            {
                _caretToEnd = false;
                DraftBox.Select(DraftBox.Text.Length, 0);
            }
        };
        viewModel.JournalShown += () => JournalBox.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true, VerticalAlignmentRatio = 0 });
        viewModel.Journal.PropertyChanged += OnJournalChanged;
        AlignBars();
    }

    public WriteViewModel ViewModel { get; }

    // Its own words (x:Bind reads them once).
    public string Placeholder { get; } = Localization.Get("Write_Placeholder");

    public string ToolsName { get; } = Localization.Get("Card_WritingTools");

    public string UseIt { get; } = Localization.Get("Write_UseIt");

    public string UseItTitle { get; } = Localization.Get("Write_UseItTitle");

    public string CopyLabel { get; } = Localization.Get("Common_Copy");

    public string NoMistakes { get; } = Localization.Get("Card_NoMistakes");

    public string TopMistake { get; } = Localization.Get("Write_TopMistake");

    public string JournalTitle { get; } = Localization.Get("Journal_Title");

    public string RuleLabel { get; } = Localization.Get("Journal_Rule");

    public string SaveLabel { get; } = Localization.Get("Journal_Save");

    public string SaveHint { get; } = Localization.Get("Journal_SaveHint");

    public string ClearTitle { get; } = Localization.Get("Journal_Clear");

    public string StartOver { get; } = Localization.Get("Journal_StartOver");

    public string ClearLabel { get; } = Localization.Get("Common_Clear");

    public string SetupTitle { get; } = Localization.Get("Write_SetupTitle");

    public string SetupText { get; } = Localization.Get("Write_SetupText");

    public string SetUpLabel { get; } = Localization.Get("Write_SetUp");

    public string RewriteLabel { get; } = Localization.Get("Write_Rewrite");

    /// <summary>The main tool's shortcut, on the button and in its tooltip (the study's W3).</summary>
    public string RunKeys { get; } = "Ctrl+Enter";

    /// <summary>The caret in the draft, ready to write; once the page is shown, when it has only just been put in the window.</summary>
    public void FocusDraft()
    {
        if (IsLoaded)
        {
            DraftBox.Focus(FocusState.Programmatic);
            return;
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            DraftBox.Focus(FocusState.Programmatic);
        }

        Loaded += OnLoaded;
    }

    /// <summary>Ctrl+Enter: "In English" for an Arabic draft, Proofread for an English one (Lamha 2's).</summary>
    private void OnRunKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ViewModel.RunDefault();
    }

    /// <summary>Rewrite ▾: the draft's other tools, as they are now (they change with its language).</summary>
    private void OnRewriteOpening(object? sender, object e)
    {
        RewriteMenu.Items.Clear();
        foreach (var tool in ViewModel.RewriteTools)
        {
            RewriteMenu.Items.Add(new MenuFlyoutItem { Text = tool.Label, Command = tool.Pick, IsEnabled = tool.IsEnabled });
        }
    }

    /// <summary>
    /// Set up: Settings → Writing tools' own setup in a dialog, so the draft stays where it is (the study's W1); the
    /// tools wake up when it closes with a provider ready.
    /// </summary>
    private async void OnSetUp(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            FlowDirection = Localization.FlowDirection,
            Title = Localization.Get("Settings_WritingTools"),
            Content = new ScrollViewer { Content = new WritingSetupView { Setup = ViewModel.NewSetup() } },
            CloseButtonText = Localization.Get("Common_Close"),
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
        ViewModel.Refresh();
    }

    private void OnJournalChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(JournalViewModel.Bars))
        {
            AlignBars();
        }
    }

    /// <summary>
    /// Every bar starts where the longest kind's name ends (Lamha 2's <c>max-content</c> column), so the bars line up:
    /// the names are measured as the rows draw them.
    /// </summary>
    private void AlignBars()
    {
        var bars = ViewModel.Journal.Bars;
        if (bars.Count == 0)
        {
            return;
        }

        var probe = new TextBlock { Style = (Style)Application.Current.Resources["LamhaSecondaryTextStyle"], TextWrapping = TextWrapping.NoWrap };
        double widest = 0;
        foreach (var bar in bars)
        {
            probe.Text = bar.Label;
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            widest = Math.Max(widest, probe.DesiredSize.Width);
        }

        var width = new GridLength(Math.Ceiling(widest));
        foreach (var bar in bars)
        {
            bar.LabelWidth = width;
        }
    }
}
