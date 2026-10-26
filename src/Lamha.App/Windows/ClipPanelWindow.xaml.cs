using System.ComponentModel;
using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.App.ViewModels.Main;
using Lamha.App.ViewModels.Panel;
using Lamha.App.Views;
using Lamha.Core.Layout;
using Lamha.Windows.Shell;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using WinRT;

namespace Lamha.App.Windows;

/// <summary>
/// The clipboard panel's window: the card's kind (no title bar, Windows' 8 px corners, edge and shadow, Acrylic or
/// solid as the card's setting says, always on top, not in Alt+Tab or the taskbar), Lamha 2's 380 × 460, under the
/// mouse. It takes the focus so typing searches at once; a paste gives it back to the program first.
/// </summary>
public sealed partial class ClipPanelWindow : Window
{
    /// <summary>Lamha 2's panel, at 100 %.</summary>
    public const int PanelWidth = 380;
    public const int PanelHeight = 460;

    /// <summary>How far the panel rises as it opens (as a toast does: Controls/Reveal).</summary>
    private const float PanelRise = 8;

    private readonly ListMotion _listMotion;
    private bool _focusOnActivate;

    public ClipPanelWindow(ClipPanelViewModel viewModel, bool solid)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Title = Localization.Get("Panel_WindowTitle");
        Root.FlowDirection = Localization.FlowDirection;
        if (solid)
        {
            SolidBackground.Visibility = Visibility.Visible;
        }
        else
        {
            SystemBackdrop = new CardBackdrop();
        }

        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Lamha.ico"));
        var presenter = AppWindow.Presenter.As<OverlappedPresenter>(); // not `is`: false under NativeAOT (see CardWindow)
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);

        Handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        Screens.RoundCorners(Handle);
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                Deactivated?.Invoke();
            }
            else if (_focusOnActivate)
            {
                // Only an active window can give an element the focus: the search box gets it here, not in ShowAt.
                _focusOnActivate = false;
                FocusStart();
            }
        };
        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.List.PropertyChanged += OnListChanged;
        _listMotion = new ListMotion(List, ViewModel.List, nameof(ClipboardViewModel.Rows), ClipboardPage.Key,
            each: ClipboardPage.ClipStaggerEach, milliseconds: ClipboardPage.ClipStaggerMilliseconds);
    }

    public ClipPanelViewModel ViewModel { get; }

    public nint Handle { get; }

    /// <summary>The panel lost the focus (the user clicked elsewhere).</summary>
    public event Action? Deactivated;

    // Its own words (x:Bind reads them once).
    public string Heading { get; } = Localization.Get("Tray_Clipboard");

    public string Hint { get; } = Localization.Get("Panel_Hint");

    public string OptInTitle { get; } = Localization.Get("Clipboard_OptInTitle");

    public string OptInText { get; } = Localization.Get("Clipboard_OptInText");

    public string EnableLabel { get; } = Localization.Get("Clipboard_Enable");

    public string CancelLabel { get; } = Localization.Get("Common_Cancel");

    public string PausedTitle { get; } = Localization.Get("Tray_TipPaused");

    public string PausedText { get; } = Localization.Get("Clipboard_PausedText");

    public string ResumeLabel { get; } = Localization.Get("Clipboard_Resume");

    public string SearchPlaceholder { get; } = Localization.Get("Clipboard_Search");

    public string SearchLabel { get; } = Localization.Get("Clipboard_SearchLabel");

    public string FilterLabel { get; } = Localization.Get("Clipboard_Filter");

    public string AllLabel { get; } = Localization.Get("Clipboard_All");

    public string PinnedLabel { get; } = Localization.Get("Clipboard_PinnedFilter");

    public string HistoryLabel { get; } = Localization.Get("Clipboard_HistoryLabel");

    public string ShowMoreLabel { get; } = Localization.Get("Clipboard_ShowMore");

    public string ToolsTitle { get; } = Localization.Get("Panel_ToolsTitle");

    public string ToolsLabel { get; } = Localization.Get("Clipboard_ToolsLabel");

    public string BackToList { get; } = Localization.Get("Panel_BackToList");

    public string PasteLabel { get; } = Localization.Get("Panel_Paste");

    public string CopyLabel { get; } = Localization.Get("Common_Copy");

    public string RedoLabel { get; } = Localization.Get("Clipboard_Redo");

    public string NewResult { get; } = Localization.Get("Clipboard_NewResult");

    public string CachedNote { get; } = Localization.Get("Clipboard_Cached");

    public string NoMistakes { get; } = Localization.Get("Card_NoMistakes");

    public string SummaryLangName { get; } = Localization.Get("Clipboard_SummaryLang");

    public string SummaryLangColon { get; } = Localization.Get("Clipboard_SummaryLangColon");

    public string KeyPaste { get; } = Localization.Get("Panel_KeyPaste");

    public string KeyPlain { get; } = Localization.Get("Panel_KeyPlain");

    public string KeyTools { get; } = Localization.Get("Panel_KeyTools");

    public string KeyClose { get; } = Localization.Get("Panel_KeyClose");

    public string KeyRun { get; } = Localization.Get("Panel_KeyRun");

    public string KeyShortcut { get; } = Localization.Get("Panel_KeyShortcut");

    public string KeyBack { get; } = Localization.Get("Panel_KeyBack");

    public string KeyToolsBack { get; } = Localization.Get("Panel_KeyToolsBack");

    /// <summary>
    /// Shows the panel under the mouse (above it when there's no room below), in front, with the search box ready
    /// (or Turn on, while clipboard history is off).
    /// </summary>
    public void ShowAt(PixelPoint cursor, PixelRect workArea, double scale)
    {
        var bounds = CardPlacement.PlacePanel(cursor, workArea, Scaled(PanelWidth, scale), Scaled(PanelHeight, scale));
        ListScroller.ChangeView(null, 0, null, disableAnimation: true);
        _listMotion.Entering();
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        _focusOnActivate = true;
        // It rises from the taskbar's side, as Windows' clipboard (Win+V) does.
        Animations.Appear(Root, PanelRise);
        AppWindow.Show(activateWindow: true);
        Activate();
        ForegroundWindow.ForceForeground(Handle);

        // The window's edge takes a little of its size: the panel gets the whole 380 × 460 inside it.
        var edgeX = AppWindow.Size.Width - AppWindow.ClientSize.Width;
        var edgeY = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        if (edgeX > 0 || edgeY > 0)
        {
            var grown = CardPlacement.PlacePanel(cursor, workArea, bounds.Width + edgeX, bounds.Height + edgeY);
            AppWindow.MoveAndResize(new RectInt32(grown.X, grown.Y, grown.Width, grown.Height));
        }

        FocusStart(); // already active (a kept window shown again): Activated may not come
    }

    public void Hide() => AppWindow.Hide();

    public void Announce(string text)
    {
        if (text.Length > 0 && FrameworkElementAutomationPeer.FromElement(Root) is { } peer)
        {
            peer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, text, "LamhaPanel");
        }
    }

    /// <summary>Settings → Appearance → Theme: "light", "dark", or "auto" (Windows').</summary>
    public void UseTheme(string theme) => Root.RequestedTheme = theme switch
    {
        "light" => ElementTheme.Light,
        "dark" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    private static int Scaled(double dips, double scale) => (int)Math.Round(dips * scale, MidpointRounding.AwayFromZero);

    private static bool IsDown(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    /// <summary>
    /// The search box, or Turn on while clipboard history is off (Lamha 2's <c>focusButton()</c>). A new window's
    /// content loads just after it first activates: then once it has.
    /// </summary>
    private void FocusStart() => DispatcherQueue.TryEnqueue(() =>
    {
        Control target = ViewModel.List.IsOn ? SearchBox : EnableButton;
        if (target.IsLoaded)
        {
            target.Focus(FocusState.Programmatic);
            return;
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            target.Loaded -= OnLoaded;
            target.Focus(FocusState.Programmatic);
        }

        target.Loaded += OnLoaded;
    });

    /// <summary>
    /// The panel's keys (Lamha 2's clip-list.js and clip-actions.js, panel mode), before the search box sees them.
    /// The list: ↑ ↓ and Page Up / Down choose, Enter pastes (Shift: as plain text), Ctrl+1…9 pastes that copy, Ctrl+P
    /// pins, Delete deletes (in the search box only with the caret at the end), Tab (or → with nothing searched) opens
    /// the tools, Esc closes. The tools: 1…9 runs one, ↑ ↓ choose, Esc goes back.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = IsDown(VirtualKey.Control) && !IsDown(VirtualKey.Menu);
        var shift = IsDown(VirtualKey.Shift);
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            if (!ViewModel.IsTools)
            {
                ViewModel.CloseCommand.Execute(null);
            }
            else if (ViewModel.Back())
            {
                SearchBox.Focus(FocusState.Programmatic);
            }

            return;
        }

        if (!ViewModel.List.IsOn)
        {
            return;
        }

        if (ViewModel.IsTools)
        {
            OnToolsKey(e);
            return;
        }

        var inSearch = ReferenceEquals(e.OriginalSource, SearchBox);
        // A button with the focus (pin, delete, Resume) keeps its own keys.
        if (!inSearch && e.OriginalSource is ButtonBase)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Down or VirtualKey.Up:
                ViewModel.List.Move(e.Key == VirtualKey.Down ? 1 : -1);
                e.Handled = true;
                break;
            case VirtualKey.PageDown or VirtualKey.PageUp:
                ViewModel.List.Move(e.Key == VirtualKey.PageDown ? 6 : -6);
                e.Handled = true;
                break;
            case VirtualKey.Enter when !ctrl && ViewModel.List.Selected is { } row:
                ViewModel.Paste(row, plain: shift);
                e.Handled = true;
                break;
            case VirtualKey.P when ctrl:
                ViewModel.List.TogglePinCommand.Execute(null);
                e.Handled = true;
                break;
            case >= VirtualKey.Number1 and <= VirtualKey.Number9 when ctrl:
                ViewModel.PasteAt(e.Key - VirtualKey.Number1, plain: shift);
                e.Handled = true;
                break;
            case VirtualKey.Delete when !inSearch || SearchBox.SelectionStart == SearchBox.Text.Length:
                ViewModel.List.DeleteCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Tab when !shift:
            case VirtualKey.Right when !ViewModel.List.HasQuery:
                if (ViewModel.List.Selected is not null)
                {
                    ViewModel.OpenTools();
                    FocusTool(0);
                }

                e.Handled = true;
                break;
        }
    }

    private void OnToolsKey(KeyRoutedEventArgs e)
    {
        if (ViewModel.ToolsRow is not { IsIdle: true } row)
        {
            return; // a result: its buttons have the keys (Enter pastes)
        }

        var digit = e.Key switch
        {
            >= VirtualKey.Number1 and <= VirtualKey.Number9 => e.Key - VirtualKey.Number1,
            >= VirtualKey.NumberPad1 and <= VirtualKey.NumberPad9 => e.Key - VirtualKey.NumberPad1,
            _ => -1,
        };
        if (digit >= 0 && !IsDown(VirtualKey.Control) && !IsDown(VirtualKey.Menu))
        {
            ViewModel.RunTool(digit);
            e.Handled = true;
            return;
        }

        if (e.Key is VirtualKey.Down or VirtualKey.Up && row.Tools.Count > 0)
        {
            var at = FocusedTool();
            var next = ((at < 0 ? 0 : at + (e.Key == VirtualKey.Down ? 1 : -1)) + row.Tools.Count) % row.Tools.Count;
            FocusTool(next);
            e.Handled = true;
        }
    }

    /// <summary>The tool with the focus, or -1.</summary>
    private int FocusedTool()
    {
        var focused = FocusManager.GetFocusedElement(Root.XamlRoot) as UIElement;
        return focused is null ? -1 : ToolList.GetElementIndex(focused);
    }

    private void FocusTool(int index) => DispatcherQueue.TryEnqueue(() =>
    {
        Root.UpdateLayout(); // the tools were just shown: their buttons are made in this layout pass
        if (ToolList.TryGetElement(index) is Control tool)
        {
            tool.Focus(FocusState.Keyboard);
        }
        else
        {
            Root.Focus(FocusState.Programmatic);
        }
    });

    /// <summary>The keyboard's choice among the tools is tinted, as the chosen copy is.</summary>
    private void OnToolFocus(object sender, RoutedEventArgs e)
    {
        if (sender is Button tool)
        {
            tool.Background = tool.FocusState != FocusState.Unfocused && ThemeLookup.Get(Root, "LamhaAccentSoftBrush") is Brush tint
                ? tint
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }

    /// <summary>The mouse moves the keyboard's choice, so one tool is lit at a time (Lamha 2's).</summary>
    private void OnToolPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button tool)
        {
            tool.Focus(FocusState.Pointer);
        }
    }

    /// <summary>A click on a copy pastes it (a pin or delete keeps its own click).</summary>
    private void OnRowTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClipRow row } && !IsInButton(e.OriginalSource as DependencyObject, (DependencyObject)sender))
        {
            ViewModel.List.Choose(row);
            ViewModel.Paste(row, plain: false);
        }
    }

    /// <summary>The mouse chooses the copy it moves over (Lamha 2's <c>mousemove</c>), not one that scrolls under it.</summary>
    private void OnRowPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClipRow row } && ViewModel.List.Selected != row)
        {
            ViewModel.List.Choose(row);
        }
    }

    /// <summary>The field's accent ring while the search has the focus (the Clipboard page's).</summary>
    private void OnSearchFocus(object sender, RoutedEventArgs e) =>
        FieldRing.Visibility = SearchBox.FocusState == FocusState.Unfocused ? Visibility.Collapsed : Visibility.Visible;

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ClipPanelViewModel.ToolsRow))
        {
            return;
        }

        if (ViewModel.ToolsRow is { } row)
        {
            row.PropertyChanged += OnToolsRowChanged;
        }
    }

    /// <summary>A result takes the keys: Enter pastes it (or Redo, Try again); back to the tools, the one used.</summary>
    private void OnToolsRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ClipRow row || e.PropertyName != nameof(ClipRow.View))
        {
            return;
        }

        if (row != ViewModel.ToolsRow)
        {
            row.PropertyChanged -= OnToolsRowChanged;
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            Control? target = row.View switch
            {
                ClipResultView.Text or ClipResultView.Proofread => PasteButton,
                ClipResultView.NoMistakes => NoMistakesRedo,
                ClipResultView.Error when ErrorButton.Visibility == Visibility.Visible => ErrorButton,
                _ => null,
            };
            if (target is not null)
            {
                target.Focus(FocusState.Keyboard);
            }
            else if (row.View == ClipResultView.None)
            {
                FocusTool(Math.Max(0, row.Tools.FindIndex(t => t.Action == row.Action)));
            }
            else
            {
                Root.Focus(FocusState.Programmatic); // working: Esc still goes back
            }
        });
    }

    /// <summary>The chosen copy stays in view when the keys move it.</summary>
    private void OnListChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClipboardViewModel.Selected) && ViewModel.List.Selected is { } row && ViewModel.List.Rows.IndexOf(row) is >= 0 and var at)
        {
            DispatcherQueue.TryEnqueue(() => List.TryGetElement(at)?.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = double.NaN }));
        }
        else if (e.PropertyName == nameof(ClipboardViewModel.IsOn) && ViewModel.List.IsOn)
        {
            FocusStart(); // Turn on: the list, ready to search
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
}
