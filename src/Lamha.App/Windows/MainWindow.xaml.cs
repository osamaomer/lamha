using System.ComponentModel;
using Lamha.App.Services;
using Lamha.App.ViewModels.Main;
using Lamha.App.Views;
using Lamha.Core.Layout;
using Lamha.Windows.Shell;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT;
using ClipboardApi = Lamha.Windows.Clipboard.Clipboard;

namespace Lamha.App.Windows;

/// <summary>
/// Lamha's main window (phase 1's design): one Windows 11 window, Mica behind, its own title bar, the pages on the
/// start side, folding to icons when the window is narrow. Opened from the tray and by starting Lamha again; closing it
/// ends it (made again next time), so nothing of it stays in the tray. Each page is made the first time it's opened.
/// </summary>
public sealed partial class MainWindow : Window, IPageHost
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

    private readonly Brain _brain;
    private readonly string _lookupKeys;
    private TranslatePage? _translate;
    private bool _choosing;

    /// <param name="viewModel">The pages, the one that's open, and the toast.</param>
    /// <param name="brain">Lamha's brain, for the pages.</param>
    /// <param name="lookupKeys">The quick-lookup shortcut ("Ctrl+Alt+L"), for the Translate page's line under its box.</param>
    public MainWindow(MainViewModel viewModel, Brain brain, string lookupKeys)
    {
        ViewModel = viewModel;
        _brain = brain;
        _lookupKeys = lookupKeys;
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
        ViewModel.PropertyChanged += OnViewModelChanged;
        ShowPage();
        // The first focus goes to the Translate page's box, ready to type; on other pages to the pane as if clicked:
        // the arrow keys work in it, without the keyboard's focus rectangle on a window that was opened with the mouse.
        Root.Loaded += (_, _) =>
        {
            if (PageHost.Content is TranslatePage translate)
            {
                translate.FocusBox();
            }
            else
            {
                (FooterList.SelectedItem is null ? PageList : FooterList).Focus(FocusState.Pointer);
            }
        };
        Closed += (_, _) => _translate?.ViewModel.Unload();
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

    /// <summary><c>--sample=text</c> with <c>--page=translate</c>: the text in the box, looked up, for screenshots.</summary>
    public void TypeSample(string text)
    {
        if (PageHost.Content is TranslatePage translate)
        {
            translate.ViewModel.Navigate(text);
        }
    }

    // ----- IPageHost -----

    public void Copy(string text) => ClipboardApi.WriteText(Handle, text, keepOutOfHistory: false);

    public void OpenLink(Uri uri) => _ = global::Windows.System.Launcher.LaunchUriAsync(uri);

    public void Announce(string text)
    {
        if (text.Length > 0 && FrameworkElementAutomationPeer.FromElement(Root) is { } peer)
        {
            peer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, text, "LamhaMain");
        }
    }

    public void Toast(string text, string? action = null, Action? run = null) => ViewModel.ShowToast(text, action, run);

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

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Page))
        {
            ShowPage();
        }
    }

    /// <summary>The open page in the content layer; the pages still to be built show only their title.</summary>
    private void ShowPage()
    {
        switch (ViewModel.Page.Id)
        {
            case "translate":
                _translate ??= new TranslatePage(new TranslateViewModel(_brain, this, _lookupKeys));
                _translate.ViewModel.Refresh();
                PageHost.Content = _translate;
                break;
            default:
                PageHost.Content = null;
                break;
        }
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel.IsFolded = e.NewSize.Width < FoldWidth;
        PaneColumn.Width = new GridLength(ViewModel.IsFolded ? FoldedPaneWidth : PaneWidth);
    }
}
