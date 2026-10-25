using System.ComponentModel;
using Lamha.App.Services;
using Lamha.App.ViewModels.Card;
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
    private readonly ShortcutService _shortcuts;
    private TranslatePage? _translate;
    private WritePage? _write;
    private HistoryPage? _history;
    private ReviewPage? _review;
    private ClipboardPage? _clipboardPage;
    private SettingsPage? _settings;
    private readonly ClipboardService _clipboard;
    private bool _choosing;

    /// <param name="viewModel">The pages, the one that's open, and the toast.</param>
    /// <param name="brain">Lamha's brain, for the pages.</param>
    /// <param name="shortcuts">The shortcuts: the quick lookup's keys under the Translate page's box, and Settings' changes.</param>
    /// <param name="clipboard">Clipboard history's switch and pause, for the Clipboard page.</param>
    public MainWindow(MainViewModel viewModel, Brain brain, ShortcutService shortcuts, ClipboardService clipboard)
    {
        ViewModel = viewModel;
        _brain = brain;
        _shortcuts = shortcuts;
        _lookupKeys = shortcuts.Keys(CardMode.Lookup).ToString();
        _clipboard = clipboard;
        InitializeComponent();
        Animations.Refresh(brain.Settings.Current); // the setting may have changed since the last window
        Title = Lamha;
        Root.FlowDirection = Localization.FlowDirection;
        UseMica(brain.Settings.Current.WindowMica);
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
        // The first focus goes to the Translate page's box, ready to type (Write's draft, Review's card likewise); on
        // other pages to the pane as if clicked:
        // the arrow keys work in it, without the keyboard's focus rectangle on a window that was opened with the mouse.
        Root.Loaded += (_, _) =>
        {
            if (PageHost.Content is TranslatePage translate)
            {
                translate.FocusBox();
            }
            else if (PageHost.Content is WritePage write)
            {
                write.FocusDraft();
            }
            else if (PageHost.Content is ReviewPage review)
            {
                review.FocusCard(); // so Space shows the meaning at once, not the page under the pane's focus
            }
            else if (PageHost.Content is ClipboardPage clipboard && clipboard.ViewModel.IsOn)
            {
                clipboard.FocusSearch(); // typing searches, and ↑ ↓ choose a copy (Lamha 2's)
            }
            else
            {
                (FooterList.SelectedItem is null ? PageList : FooterList).Focus(FocusState.Pointer);
            }
        };
        Closed += (_, _) =>
        {
            _translate?.ViewModel.Unload();
            _write?.ViewModel.Unload();
            if (ReferenceEquals(PageHost.Content, _clipboardPage))
            {
                _clipboardPage?.ViewModel.Detach();
            }

            _settings?.ViewModel.Detach();
        };
    }

    public MainViewModel ViewModel { get; }

    /// <summary>Start with Windows, for Settings → Lamha; set before the window shows a page.</summary>
    public StartWithWindows? Startup { get; set; }

    /// <summary>Settings → Delete my data: deletes everything and quits (the app's).</summary>
    public Action? DeleteData { get; set; }

    // Queued: it closes this window, which shouldn't happen inside the window's own call.
    public void DeleteDataAndQuit() => DispatcherQueue.TryEnqueue(() => DeleteData?.Invoke());

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

    /// <summary>Settings → Appearance → the wallpaper behind the window: Mica, or Lamha's page colour.</summary>
    public void UseMica(bool on)
    {
        SystemBackdrop = on ? SystemBackdrop as MicaBackdrop ?? new MicaBackdrop() : null;
        Solid.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Opens the page <paramref name="id"/> ("translate", "settings"…); Settings at its category <paramref name="section"/>.</summary>
    public void Go(string id, string section = "")
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

        if (page.Id == "settings" && section.Length > 0)
        {
            _settings?.ViewModel.Open(section);
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

    public void Translate(string text)
    {
        Go("translate");
        if (_translate is { } translate)
        {
            translate.ViewModel.Navigate(text);
            translate.FocusBox();
        }
    }

    public void Write(string text)
    {
        Go("write");
        _write?.ViewModel.Use(text);
    }

    public void ReviewChanged() => _ = CountReviewAsync();

    public void OpenSettings(string section) => Go("settings", section);

    public async Task<string?> PickSaveFileAsync(string name)
    {
        var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(AppWindow.Id) { SuggestedFileName = name, DefaultFileExtension = ".json" };
        picker.FileTypeChoices.Add("JSON", [".json"]);
        return (await picker.PickSaveFileAsync())?.Path;
    }

    public async Task<string?> PickOpenFileAsync()
    {
        var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id);
        picker.FileTypeFilter.Add(".json");
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public void OpenFolder(string folder) => _ = global::Windows.System.Launcher.LaunchFolderPathAsync(folder);

    public async Task<Answer> AskAsync(string question, string confirm, string text = "", string? other = null)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            RequestedTheme = Root.ActualTheme,
            FlowDirection = Localization.FlowDirection,
            Title = question,
            Content = text.Length > 0 ? new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, TextReadingOrder = TextReadingOrder.UseFlowDirection } : null,
            PrimaryButtonText = confirm,
            PrimaryButtonStyle = (Style)Application.Current.Resources["LamhaDialogDangerButtonStyle"],
            SecondaryButtonText = other ?? "",
            CloseButtonText = Localization.Get("Common_Cancel"),
            // Nothing in Windows' accent: Cancel is still what Esc does, and the red choice stands out.
            DefaultButton = ContentDialogButton.None,
        };
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => Answer.Confirm,
            ContentDialogResult.Secondary => Answer.Other,
            _ => Answer.Cancel,
        };
    }

    private static int Scaled(double dips, double scale) => (int)Math.Round(dips * scale, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The count on Review: when the window opens and each time another page opens (a word bookmarked on Translate or
    /// History, or added from the Clipboard page); the Review page keeps it up to date as words are answered.
    /// </summary>
    private async Task CountReviewAsync()
    {
        try
        {
            var queue = await _brain.Review.QueueAsync().ConfigureAwait(true);
            ViewModel.SetReviewCount(queue.Due + queue.New);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            AppLog.Error("review count", e);
        }
    }

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
        _ = CountReviewAsync();

        // Leaving the Clipboard page: what was copied, decrypted for the list, goes from memory.
        if (PageHost.Content is ClipboardPage leaving && ViewModel.Page.Id != "clipboard")
        {
            leaving.ViewModel.Detach();
        }

        if (PageHost.Content is SettingsPage settings && ViewModel.Page.Id != "settings")
        {
            settings.ViewModel.Detach();
        }

        switch (ViewModel.Page.Id)
        {
            case "settings":
                _settings ??= new SettingsPage(new SettingsViewModel(_brain, this, _shortcuts, _clipboard, Startup));
                _settings.ViewModel.Refresh();
                _settings.ViewModel.Attach();
                PageHost.Content = _settings;
                break;
            case "clipboard":
                _clipboardPage ??= new ClipboardPage(new ClipboardViewModel(_brain, _clipboard, this));
                if (!ReferenceEquals(PageHost.Content, _clipboardPage))
                {
                    _clipboardPage.ViewModel.Attach();
                }

                PageHost.Content = _clipboardPage;
                break;
            case "translate":
                _translate ??= new TranslatePage(new TranslateViewModel(_brain, this, _lookupKeys));
                _translate.ViewModel.Refresh();
                PageHost.Content = _translate;
                break;
            case "write":
                _write ??= new WritePage(new WriteViewModel(_brain, this));
                _write.ViewModel.Refresh();
                PageHost.Content = _write;
                break;
            case "history":
                _history ??= new HistoryPage(new HistoryViewModel(_brain, this));
                _history.ViewModel.Refresh();
                PageHost.Content = _history;
                break;
            case "review":
                if (_review is null)
                {
                    _review = new ReviewPage(new ReviewViewModel(_brain, this));
                    _review.ViewModel.CountChanged += ViewModel.SetReviewCount;
                }

                _review.ViewModel.Refresh();
                PageHost.Content = _review;
                _review.FocusCard();
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
