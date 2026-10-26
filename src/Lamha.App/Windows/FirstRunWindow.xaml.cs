using System.ComponentModel;
using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.App.ViewModels.FirstRun;
using Lamha.Core.Dictionary;
using Lamha.Core.Layout;
using Lamha.Windows.Shell;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT;

namespace Lamha.App.Windows;

/// <summary>
/// The first-run window (phase 1's design): 640 × 540 at 100 %, Mica, its own title bar, not resizable, one step at a
/// time. Opened once, at the first start; closing it in any way ends first run.
/// </summary>
public sealed partial class FirstRunWindow : Window
{
    /// <summary>Its size, at 100 % (the design's).</summary>
    public const int DesignWidth = 640;
    public const int DesignHeight = 540;

    public FirstRunWindow(FirstRunViewModel viewModel, bool mica)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Title = ViewModel.Lamha;
        Root.FlowDirection = Localization.FlowDirection;
        SystemBackdrop = mica ? new MicaBackdrop() : null;
        Solid.Visibility = mica ? Visibility.Collapsed : Visibility.Visible;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Lamha.ico"));
        Handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        if (Localization.IsArabic)
        {
            Screens.LayOutRightToLeft(Handle);
        }

        // Only minimise and close, as the design draws it: the steps are laid out for this one size.
        var presenter = AppWindow.Presenter.As<OverlappedPresenter>(); // not `is`: false under NativeAOT (see CardWindow)
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;

        ViewModel.PropertyChanged += OnViewModelChanged;
        Root.ActualThemeChanged += (_, _) => ShowSteps();
        Root.Loaded += (_, _) =>
        {
            ShowSteps();
            FocusStep();
        };
    }

    public FirstRunViewModel ViewModel { get; }

    public nint Handle { get; }

    /// <summary>Whether the window is the one in front (the lookup shortcut then reads its sample sentence).</summary>
    public bool IsInFront => ForegroundWindow.Current == Handle;

    /// <summary>Settings → Appearance → Theme: "light", "dark", or "auto" (Windows'); the title bar's buttons follow.</summary>
    public void UseTheme(string theme)
    {
        Root.RequestedTheme = theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        AppWindow.TitleBar.PreferredTheme = theme switch
        {
            "light" => TitleBarTheme.Light,
            "dark" => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    /// <summary>
    /// Opens in the middle of the screen the mouse is on, or where <paramref name="at"/> was (the window made again in
    /// the language just chosen).
    /// </summary>
    public void Show(PixelRect workArea, double scale, PointInt32? at)
    {
        var width = Math.Min(Scaled(DesignWidth, scale), workArea.Width);
        var height = Math.Min(Scaled(DesignHeight, scale), workArea.Height);
        var position = at ?? new PointInt32(workArea.X + ((workArea.Width - width) / 2), workArea.Y + ((workArea.Height - height) / 2));
        AppWindow.MoveAndResize(new RectInt32(position.X, position.Y, width, height));
        AppWindow.Show(activateWindow: true);
        Activate();
        ForegroundWindow.ForceForeground(Handle);
    }

    public PointInt32 Position => AppWindow.Position;

    /// <summary>Brings the window to the front, restored if it was minimised; the shortcuts read again (Customise shortcuts).</summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter.As<OverlappedPresenter>().State == OverlappedPresenterState.Minimized)
        {
            AppWindow.Presenter.As<OverlappedPresenter>().Restore();
        }

        ViewModel.RefreshKeys();
        Activate();
        ForegroundWindow.ForceForeground(Handle);
    }

    /// <summary>
    /// The word selected in the sample sentence, with the sentence around it, as the lookup shortcut reads it from
    /// another program; null when the window isn't on Try it. An empty text when nothing is selected (the card's hint).
    /// </summary>
    public (string Text, WordContext? Context)? Selection()
    {
        if (!ViewModel.IsTry || SampleText is not { } sample)
        {
            return null;
        }

        var sentence = sample.Text;
        var selected = sample.SelectedText.Trim();
        if (selected.Length == 0)
        {
            return ("", null);
        }

        var start = sample.SelectionStart?.Offset ?? -1;
        if (start < 0 || start + selected.Length > sentence.Length || string.CompareOrdinal(sentence, start, selected, 0, selected.Length) != 0)
        {
            start = sentence.IndexOf(selected, StringComparison.Ordinal);
        }

        return start < 0 ? (selected, null) : (selected, new WordContext(sentence[..start], sentence[(start + selected.Length)..]));
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FirstRunViewModel.Step))
        {
            ShowSteps();
            Scroller.ChangeView(null, 0, null, disableAnimation: true);
            DispatcherQueue.TryEnqueue(FocusStep);
        }
        else if (e.PropertyName == nameof(FirstRunViewModel.Moved))
        {
            DispatcherQueue.TryEnqueue(FocusStep); // Move my data gave way to Next
        }
    }

    /// <summary>
    /// The focus on each step: the chosen language, Move my data, Next (Enter goes on), or Start; on Try it nowhere in
    /// particular, so the mouse can select a word.
    /// </summary>
    private void FocusStep()
    {
        if (ViewModel.IsLanguage)
        {
            (ViewModel.IsArabic ? ArabicChoice : EnglishChoice).Focus(FocusState.Programmatic);
        }
        else if (ViewModel.CanMove)
        {
            MoveButton.Focus(FocusState.Programmatic);
        }
        else if (ViewModel.IsReady)
        {
            StartButton.Focus(FocusState.Programmatic);
        }
        else
        {
            NextButton.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>The steps' row: a number in a 20 px circle and the name, joined by lines that share what's left.</summary>
    private void ShowSteps()
    {
        StepRow.Children.Clear();
        StepRow.ColumnDefinitions.Clear();
        var steps = ViewModel.Steps;
        for (var i = 0; i < steps.Count; i++)
        {
            if (i > 0)
            {
                StepRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 8 });
                var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center, Background = Brush("LamhaLineBrush") };
                Grid.SetColumn(line, StepRow.ColumnDefinitions.Count - 1);
                StepRow.Children.Add(line);
            }

            StepRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var step = Step(steps[i]);
            Grid.SetColumn(step, StepRow.ColumnDefinitions.Count - 1);
            StepRow.Children.Add(step);
        }
    }

    private StackPanel Step(StepItem item)
    {
        UIElement mark = item.IsDone
            ? new LineIcon { Glyph = Glyph.Check, Width = 12, Height = 12, StrokeWidth = 3, Foreground = Brush("LamhaOkBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            : new TextBlock
            {
                Text = item.Number,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = Brush(item.IsCurrent ? "LamhaButtonTextBrush" : "LamhaTextMutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        var circle = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = Brush(item.IsDone ? "LamhaOkSoftBrush" : item.IsCurrent ? "LamhaButtonBrush" : "LamhaChipBrush"),
            Child = mark,
        };
        var label = new TextBlock
        {
            Text = item.Label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = item.IsCurrent ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = Brush(item.IsCurrent ? "LamhaTextBrush" : "LamhaTextFaintBrush"),
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { circle, label } };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(panel, $"{item.Number}. {item.Label}");
        return panel;
    }

    private Brush? Brush(string key) => ThemeLookup.Get(Root, key) as Brush;

    private static int Scaled(double dips, double scale) => (int)Math.Round(dips * scale, MidpointRounding.AwayFromZero);
}
