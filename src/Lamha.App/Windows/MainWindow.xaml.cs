using System.ComponentModel;
using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.App.ViewModels.Main;
using Lamha.App.Views;
using Lamha.Core.Layout;
using Lamha.Core.Search;
using Lamha.Windows.Shell;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT;
using ClipboardApi = Lamha.Windows.Clipboard.Clipboard;

namespace Lamha.App.Windows;

/// <summary>
/// Lamha's main window, as Windows' Settings (docs/design/system.md; decision 0020): Mica behind, Windows' title bar
/// with Back, Windows' navigation pane (open, or hidden behind ☰ when narrow), and the page. Back goes through the
/// pages and Settings' categories as they were opened. Opened from the tray and by starting Lamha again; closing it
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

    private readonly Brain _brain;
    private readonly string _lookupKeys;
    private readonly ShortcutService _shortcuts;
    private readonly ClipboardService _clipboard;

    /// <summary>The pane's rows, by page.</summary>
    private readonly Dictionary<string, NavigationViewItem> _items = [];

    /// <summary>Where Back goes: the pages and Settings' categories, as they were left.</summary>
    private readonly Stack<(string Page, string Section)> _back = new();
    private TranslatePage? _translate;
    private WritePage? _write;
    private HistoryPage? _history;
    private ReviewPage? _review;
    private ClipboardPage? _clipboardPage;
    private SettingsPage? _settings;
    private string _section = "";

    /// <summary>What's showing: the page, and Settings' category ("" for its home).</summary>
    private (string Page, string Section) _here = ("", "");

    /// <summary>The page is changing because of Back: it isn't added to where Back goes, and it slides in from the start.</summary>
    private bool _goingBack;

    /// <summary>Where the window is when neither maximised nor minimised: saved when it closes.</summary>
    private WindowPlace? _normal;

    /// <param name="viewModel">The pages, the one that's open, and the toast.</param>
    /// <param name="brain">Lamha's brain, for the pages.</param>
    /// <param name="shortcuts">The shortcuts: the quick lookup's keys under the Translate page's box, and Settings' changes.</param>
    /// <param name="clipboard">Clipboard history's switch and pause, for the Clipboard page.</param>
    public MainWindow(MainViewModel viewModel, Brain brain, ShortcutService shortcuts, ClipboardService clipboard)
    {
        ViewModel = viewModel;
        _brain = brain;
        _shortcuts = shortcuts;
        _lookupKeys = shortcuts.Keys(Shortcut.Lookup).ToString();
        _clipboard = clipboard;
        InitializeComponent();
        Animations.Refresh(brain.Settings.Current); // the setting may have changed since the last window
        Title = Lamha;
        Root.FlowDirection = Localization.FlowDirection;
        UseMica(brain.Settings.Current.WindowMica);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall; // Windows' buttons as tall as the title bar
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Lamha.ico"));
        Handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        if (Localization.IsArabic)
        {
            Screens.LayOutRightToLeft(Handle);
        }

        foreach (var page in ViewModel.Pages)
        {
            Nav.MenuItems.Add(Item(page));
        }

        foreach (var page in ViewModel.Footer)
        {
            Nav.FooterMenuItems.Add(Item(page));
        }

        Nav.SelectedItem = _items[ViewModel.Page.Id];
        ViewModel.PropertyChanged += OnViewModelChanged;
        Root.PointerPressed += OnPointerPressed;
        ShowPage();
        // The first focus goes to the Translate page's box, ready to type (Write's draft, Review's card likewise); on
        // other pages to the pane's row, as if clicked.
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
                _items[ViewModel.Page.Id].Focus(FocusState.Pointer);
            }
        };
        Closed += (_, _) =>
        {
            SavePlace();
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

    /// <summary>The updates, for Settings → Lamha; set before the window shows a page.</summary>
    public UpdateService? Updates { get; set; }

    /// <summary>Settings → Delete my data: deletes everything and quits (the app's).</summary>
    public Action? DeleteData { get; set; }

    // Queued: it closes this window, which shouldn't happen inside the window's own call.
    public void DeleteDataAndQuit() => DispatcherQueue.TryEnqueue(() => DeleteData?.Invoke());

    public nint Handle { get; }

    public string Lamha { get; } = Localization.Get("Common_Lamha");

    public string SearchPlaceholder { get; } = Localization.Get("Search_Placeholder");

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
    /// The accent changed: Windows' controls read their accent again only when their theme is applied, so the theme is
    /// applied once the other way and back (both before the next frame, so nothing flickers).
    /// </summary>
    public void RefreshAccent(string theme)
    {
        Root.RequestedTheme = Root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        UseTheme(theme);
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
        if (page.Id == "settings" && section.Length > 0)
        {
            if (PageHost.Content is SettingsPage settings)
            {
                settings.ViewModel.Open(section);
            }
            else
            {
                // Settings opens at the category when the page is shown.
                _section = section;
            }
        }

        Nav.SelectedItem = _items[page.Id];
    }

    /// <summary>
    /// Opens where it was when it last closed (maximised again if it was), as Windows' own apps do; the first time, or
    /// when that screen is gone, at the design's size in the middle of the screen the mouse is on.
    /// </summary>
    public void ShowCentred(PixelRect workArea, double scale)
    {
        var presenter = AppWindow.Presenter.As<OverlappedPresenter>(); // not `is`: false under NativeAOT (see CardWindow)
        presenter.PreferredMinimumWidth = Scaled(MinWidth, scale);
        presenter.PreferredMinimumHeight = Scaled(MinHeight, scale);
        var was = WindowPlace.Parse(_brain.State.Get(WindowPlace.Key)) is { } saved
            ? saved.On(Screens.WorkAreaAt(new PixelPoint(saved.X + (saved.Width / 2), saved.Y)))
            : null;
        if (was is not null)
        {
            AppWindow.MoveAndResize(new RectInt32(was.X, was.Y, was.Width, was.Height));
        }
        else
        {
            var width = Math.Min(Scaled(StartWidth, scale), workArea.Width);
            var height = Math.Min(Scaled(StartHeight, scale), workArea.Height);
            AppWindow.MoveAndResize(new RectInt32(workArea.X + ((workArea.Width - width) / 2), workArea.Y + ((workArea.Height - height) / 2), width, height));
        }

        _normal = new WindowPlace(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height, false);
        if (was?.Maximized == true)
        {
            presenter.Maximize();
        }

        AppWindow.Changed += OnPlaceChanged;
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

    /// <summary>
    /// <c>--sample=text</c> with <c>--page=translate</c>: the text in the box, looked up, for screenshots;
    /// <c>--sample=search:text</c> with any page: the text in the title bar's search, its suggestions open.
    /// </summary>
    public void TypeSample(string text)
    {
        if (text.StartsWith("search:", StringComparison.Ordinal))
        {
            void Type()
            {
                Search.Visibility = Visibility.Visible;
                Search.Text = text["search:".Length..];
                Search.ItemsSource = Suggestions(Search.Text);
                Search.Focus(FocusState.Programmatic);
                Search.IsSuggestionListOpen = true;
            }

            if (Root.IsLoaded)
            {
                Type();
            }
            else
            {
                Root.Loaded += (_, _) => DispatcherQueue.TryEnqueue(Type);
            }

            return;
        }

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

    public void Celebrate(string text)
    {
        ViewModel.ShowToast(text);
        // Once the toast is laid out, so the letters start from its middle.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Controls.Burst.From(ToastBox, BurstLayer));
    }

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

    /// <summary>Moved or resized while neither maximised nor minimised: that's where it opens next time.</summary>
    private void OnPlaceChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if ((args.DidPositionChange || args.DidSizeChange) && sender.Presenter.As<OverlappedPresenter>().State == OverlappedPresenterState.Restored)
        {
            _normal = new WindowPlace(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height, false);
        }
    }

    /// <summary>Closing: where it was (its normal place, and whether it was maximised), for next time.</summary>
    private void SavePlace()
    {
        if (_normal is { } normal)
        {
            var maximized = AppWindow.Presenter.As<OverlappedPresenter>().State == OverlappedPresenterState.Maximized;
            _brain.State.Set(WindowPlace.Key, (normal with { Maximized = maximized }).ToString());
        }
    }

    /// <summary>
    /// A row of the pane: the page's coloured icon and its name (and Review's count), as Windows' Settings draws its
    /// pages. The icon is in the row's content, not its Icon, which takes only Windows' icon types.
    /// </summary>
    private NavigationViewItem Item(NavItem page)
    {
        var badgeText = new TextBlock { Style = (Style)Application.Current.Resources["LamhaNavBadgeTextStyle"], Text = page.Badge };
        var badge = new Border { Style = (Style)Application.Current.Resources["LamhaNavBadgeStyle"], Child = badgeText };
        badge.Visibility = page.Badge.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        page.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NavItem.Badge))
            {
                badgeText.Text = page.Badge;
                badge.Visibility = page.Badge.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        };

        var row = new Grid { ColumnSpacing = (double)Application.Current.Resources["LamhaNavIconGap"], Margin = (Thickness)Application.Current.Resources["LamhaNavRowMargin"] };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new PaneIcon { Page = page.Id, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = page.Label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(name, 1);
        Grid.SetColumn(badge, 2);
        row.Children.Add(icon);
        row.Children.Add(name);
        row.Children.Add(badge);

        var item = new NavigationViewItem { Content = row, Tag = page.Id, Margin = (Thickness)Application.Current.Resources["LamhaNavItemMargin"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, page.Label);
        _items[page.Id] = item;
        return item;
    }

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

    private void OnNavChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string id } && id != ViewModel.Page.Id)
        {
            ViewModel.Page = ViewModel.Find(id);
        }
    }

    /// <summary>Narrow, the pane hides and ☰ appears in the title bar, and the search becomes a 🔍 button (Windows' Settings).</summary>
    private void OnDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        var narrow = args.DisplayMode == NavigationViewDisplayMode.Minimal;
        AppTitleBar.IsPaneToggleButtonVisible = narrow;
        SearchButton.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
        Search.Visibility = narrow && Search.FocusState == FocusState.Unfocused ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Narrow: the 🔍 button opens the box, ready to type.</summary>
    private void OnSearchButton(object sender, RoutedEventArgs e)
    {
        Search.Visibility = Visibility.Visible;
        SearchButton.Visibility = Visibility.Collapsed;
        Search.Focus(FocusState.Programmatic);
    }

    /// <summary>Narrow, an empty box left goes back to its 🔍 button.</summary>
    private void OnSearchLostFocus(object sender, RoutedEventArgs e)
    {
        if (Nav.DisplayMode == NavigationViewDisplayMode.Minimal && Search.Text.Length == 0)
        {
            Search.Visibility = Visibility.Collapsed;
            SearchButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>The suggestions as the user types: settings, words looked up, copies (when clipboard history is on), and "Translate …".</summary>
    private void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        sender.ItemsSource = Suggestions(sender.Text);
    }

    /// <summary>The search's suggestions for <paramref name="query"/>: copies only when clipboard history is on.</summary>
    private List<QuickHit> Suggestions(string query)
    {
        var copies = _clipboard.IsOn && query.Trim().Length > 0
            ? _brain.ClipboardHistory.List(query.Trim(), limit: QuickSearch.PerKind).Items.Select(c => (c.Id, c.Text))
            : [];
        return [.. QuickSearch.Find(query, SettingsCategories.Entries(), _brain.History.Recent(), copies)];
    }

    private void OnSearchChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        // Keep what was typed in the box while ↑ ↓ move through the suggestions (Settings' search does the same).
    }

    /// <summary>A suggestion chosen (or Enter): its setting, its word's answer, the Clipboard page, or the text translated.</summary>
    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var hit = args.ChosenSuggestion as QuickHit ?? new QuickHit(QuickKind.Translate, args.QueryText.Trim(), "", args.QueryText.Trim());
        if (hit.Target.Length == 0)
        {
            return;
        }

        sender.Text = "";
        sender.ItemsSource = null;
        switch (hit.Kind)
        {
            case QuickKind.Setting:
                Go("settings", hit.Target);
                break;
            case QuickKind.Copy:
                Go("clipboard");
                break;
            default:
                Translate(hit.Target);
                break;
        }
    }

    private void OnPaneToggleRequested(TitleBar sender, object args) => Nav.IsPaneOpen = !Nav.IsPaneOpen;

    private void OnBackRequested(TitleBar sender, object args) => GoBack();

    private void OnBackKey(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = _back.Count > 0;
        GoBack();
    }

    /// <summary>The mouse's back button goes back too, as in Windows' Settings.</summary>
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(Root).Properties.IsXButton1Pressed)
        {
            e.Handled = _back.Count > 0;
            GoBack();
        }
    }

    /// <summary>To the page (and Settings' category) before this one.</summary>
    private void GoBack()
    {
        if (!_back.TryPop(out var to))
        {
            return;
        }

        _goingBack = true;
        try
        {
            if (to.Page == _here.Page && to.Page == "settings")
            {
                _settings?.ViewModel.Open(to.Section);
            }
            else
            {
                Go(to.Page, to.Section);
            }
        }
        finally
        {
            _goingBack = false;
            AppTitleBar.IsBackButtonEnabled = _back.Count > 0;
        }
    }

    /// <summary>Somewhere new: where we were goes on Back's list (not when going back), and the title bar's Back follows.</summary>
    private void Arrive((string Page, string Section) to)
    {
        if (to == _here)
        {
            return;
        }

        if (!_goingBack && _here.Page.Length > 0)
        {
            _back.Push(_here);
        }

        _here = to;
        AppTitleBar.IsBackButtonEnabled = _back.Count > 0;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Page))
        {
            if (!ReferenceEquals(Nav.SelectedItem, _items[ViewModel.Page.Id]))
            {
                Nav.SelectedItem = _items[ViewModel.Page.Id];
            }

            ShowPage();
        }
    }

    /// <summary>Settings opened a category or its home: Back can return, and the category slides in as Windows' Settings.</summary>
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsViewModel.Section) || _settings is not { } settings || !ReferenceEquals(PageHost.Content, settings))
        {
            return;
        }

        var section = settings.ViewModel.Section;
        var move = _goingBack || section.Length == 0 ? PageMove.Back : PageMove.DrillIn;
        Arrive(("settings", section));
        Animations.Page(PageHost, move);
    }

    /// <summary>The open page in the content layer.</summary>
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
                if (_settings is null)
                {
                    _settings = new SettingsPage(new SettingsViewModel(_brain, this, _shortcuts, _clipboard, Startup, Updates));
                    _settings.ViewModel.PropertyChanged += OnSettingsChanged;
                }

                // The category opens before the page shows, so it comes in with the page rather than sliding in after.
                _settings.ViewModel.Refresh();
                _settings.ViewModel.Open(_section);
                _section = "";
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

        var first = _here.Page.Length == 0;
        Arrive((ViewModel.Page.Id, ViewModel.Page.Id == "settings" ? _settings?.ViewModel.Section ?? "" : ""));
        if (!first)
        {
            Animations.Page(PageFrame, _goingBack ? PageMove.Back : PageMove.Entrance);
        }
    }
}
