using Lamha.App.Services;
using Lamha.App.ViewModels.Main;
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
/// Lamha's main window (phase 1's design): one Windows 11 window, Mica behind, its own title bar, the pages on the
/// start side, folding to icons when the window is narrow. Opened from the tray and by starting Lamha again; closing it
/// ends it (made again next time), so nothing of it stays in the tray.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Its size when it opens, at 100 % (the design's).</summary>
    public const int StartWidth = 1000;
    public const int StartHeight = 680;

    /// <summary>The smallest it can be made, at 100 %.</summary>
    private const int MinWidth = 500;
    private const int MinHeight = 400;

    /// <summary>Narrower than this, the pane shows only icons (Windows' navigation folds at about this width too).</summary>
    private const double FoldWidth = 720;

    private const double PaneWidth = 260;
    private const double FoldedPaneWidth = 48;

    private bool _choosing;

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Title = Lamha;
        Root.FlowDirection = Localization.FlowDirection;
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Lamha.ico"));
        Handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        if (Localization.IsArabic)
        {
            Screens.LayOutRightToLeft(Handle);
        }

        PageList.SelectedItem = ViewModel.Page;
        // The first focus goes to the pane as if clicked: the arrow keys work in it, without the keyboard's focus
        // rectangle on a window that was opened with the mouse.
        Root.Loaded += (_, _) => (FooterList.SelectedItem is null ? PageList : FooterList).Focus(FocusState.Pointer);
    }

    public MainViewModel ViewModel { get; }

    public nint Handle { get; }

    public string Lamha { get; } = Localization.Get("Common_Lamha");

    public string Settings { get; } = Localization.Get("Common_Settings");

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

    /// <summary>Opens the page <paramref name="id"/> ("translate", "settings"…).</summary>
    public void Go(string id)
    {
        var page = ViewModel.Find(id);
        if (ViewModel.Footer.Contains(page))
        {
            FooterList.SelectedItem = page;
        }
        else
        {
            PageList.SelectedItem = page;
        }
    }

    /// <summary>Opens at the design's size, in the middle of the screen the mouse is on.</summary>
    public void ShowCentred(PixelRect workArea, double scale)
    {
        var width = Math.Min(Scaled(StartWidth, scale), workArea.Width);
        var height = Math.Min(Scaled(StartHeight, scale), workArea.Height);
        var presenter = AppWindow.Presenter.As<OverlappedPresenter>(); // not `is`: false under NativeAOT (see CardWindow)
        presenter.PreferredMinimumWidth = Scaled(MinWidth, scale);
        presenter.PreferredMinimumHeight = Scaled(MinHeight, scale);
        AppWindow.MoveAndResize(new RectInt32(workArea.X + ((workArea.Width - width) / 2), workArea.Y + ((workArea.Height - height) / 2), width, height));
        AppWindow.Show(activateWindow: true);
        Activate();
        // Started again from the Start menu, Lamha (already running) isn't the program in front, and Windows would
        // open the window behind the others.
        ForegroundWindow.ForceForeground(Handle);
    }

    /// <summary>Brings an open window to the front, restored if it was minimised.</summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter.As<OverlappedPresenter>().State == OverlappedPresenterState.Minimized)
        {
            AppWindow.Presenter.As<OverlappedPresenter>().Restore();
        }

        Activate();
        ForegroundWindow.ForceForeground(Handle);
    }

    private static int Scaled(double dips, double scale) => (int)Math.Round(dips * scale, MidpointRounding.AwayFromZero);

    /// <summary>One page is open across both lists: choosing in one clears the other.</summary>
    private void OnPageChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_choosing || ((ListView)sender).SelectedItem is not NavItem page)
        {
            return;
        }

        _choosing = true;
        (ReferenceEquals(sender, PageList) ? FooterList : PageList).SelectedItem = null;
        _choosing = false;
        ViewModel.Page = page;
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel.IsFolded = e.NewSize.Width < FoldWidth;
        PaneColumn.Width = new GridLength(ViewModel.IsFolded ? FoldedPaneWidth : PaneWidth);
    }
}
