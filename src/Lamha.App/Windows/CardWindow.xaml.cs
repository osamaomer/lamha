using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.App.ViewModels.Card;
using Lamha.Core.Appearance;
using Lamha.Core.Layout;
using Lamha.Windows.Shell;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT;

namespace Lamha.App.Windows;

/// <summary>
/// The floating card over other programs: no title bar, Windows' 8 px corners, edge and shadow, Acrylic (or solid),
/// always on top, not in Alt+Tab or the taskbar. Its height follows the card up to 540 px, then the middle scrolls
/// (phase 1's design). It takes the focus when it opens (so Esc and typing work at once); Replace gives it back to the
/// program the text came from before pasting. The hint ("Select some text first") shows without taking the focus.
/// It moves as the design system has it: in from 96 % (167 ms), out in 83 ms, its height following the answer (250 ms).
/// </summary>
public sealed partial class CardWindow : Window
{
    /// <summary>The card's tallest, at 100 % (Lamha 2's limit); the hint's size.</summary>
    public const int MaxHeight = 540;
    public const int HintWidth = 240;
    public const int HintHeight = 44;

    /// <summary>The card grows in from this (docs/design/system.md, Motion).</summary>
    private const float EnterScale = 0.96f;

    private PixelRect _workArea;
    private bool _above;
    private bool _shown;

    /// <summary>Counts the shows and hides, so a card that fades out doesn't hide the one shown meanwhile.</summary>
    private int _turn;

    /// <summary>When it was shown: a height found while it comes in is taken at once, not animated.</summary>
    private long _shownAt;

    // The window's size going from one height to the next (Fit).
    private PixelRect _from;
    private PixelRect _to;
    private long _resizeStart;
    private TimeSpan _resizeTime;
    private bool _resizing;

    public CardWindow(CardViewModel viewModel, bool solid)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Card.ViewModel = viewModel;
        Title = Localization.Get("Common_Lamha");
        if (solid)
        {
            Card.ShowSolid();
        }
        else
        {
            SystemBackdrop = new CardBackdrop();
        }

        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Lamha.ico"));
        // As<T>, not `AppWindow.Presenter is OverlappedPresenter`: published with NativeAOT that test is false (C#/WinRT
        // returns the base type), and the card had a title bar, a resizable edge and no "on top".
        var presenter = AppWindow.Presenter.As<OverlappedPresenter>();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);

        Handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        Screens.RoundCorners(Handle);
        Activated += OnActivated;
        Card.ContentResized += Fit;
        Card.MoveRequested += () =>
        {
            Screens.StartMove(Handle);
            _above = false; // where it was dropped is where it grows from now on
        };
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CardViewModel.Width))
            {
                Fit();
            }
        };
    }

    public CardViewModel ViewModel { get; }

    public nint Handle { get; }

    /// <summary>The card lost the focus (the user clicked elsewhere).</summary>
    public event Action? Deactivated;

    /// <summary>Shows the card next to <paramref name="cursor"/>; its height then follows its content.</summary>
    public void ShowAt(PixelPoint cursor, PixelRect workArea, double scale)
    {
        _workArea = workArea;
        var hint = ViewModel.IsHint;
        var width = Scaled(hint ? HintWidth : ViewModel.Width, scale);
        var height = Scaled(hint ? HintHeight : InitialHeight(), scale);
        var bounds = CardPlacement.Place(cursor, workArea, width, height, toast: hint).Window;
        _above = bounds.Y < cursor.Y;
        Card.ScrollToTop();
        StopResize();
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        _shown = true;
        _turn++;
        _shownAt = Environment.TickCount64;
        // It grows from the side the selection is on.
        Animations.Appear(Card, rise: 0, EnterScale, new System.Numerics.Vector2(0.5f, _above ? 1 : 0));
        if (hint)
        {
            AppWindow.Show(activateWindow: false);
            return;
        }

        AppWindow.Show(activateWindow: true);
        Activate();
        ForegroundWindow.ForceForeground(Handle);
        Fit();
    }

    /// <summary>
    /// The card goes: it fades out first unless <paramref name="fade"/> is false (Replace, which gives the focus back at
    /// once); then <paramref name="hidden"/> runs, unless the card was shown again meanwhile.
    /// </summary>
    public void Hide(bool fade = true, Action? hidden = null)
    {
        _shown = false;
        StopResize();
        var turn = ++_turn;
        void Gone()
        {
            if (turn == _turn)
            {
                AppWindow.Hide();
                hidden?.Invoke();
            }
        }

        if (fade && AppWindow.IsVisible)
        {
            Animations.Leave(Card, drop: 0, Gone);
        }
        else
        {
            Gone();
        }
    }

    public void Announce(string text) => Card.Announce(text);

    /// <summary>Settings → Appearance → Theme: "light", "dark", or "auto" (Windows').</summary>
    public void UseTheme(string theme) => Card.RequestedTheme = theme switch
    {
        "light" => ElementTheme.Light,
        "dark" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    private static int Scaled(double dips, double scale) => (int)Math.Round(dips * scale, MidpointRounding.AwayFromZero);

    /// <summary>Before the card has laid itself out: about the waiting card's height (Lamha 2's skeleton).</summary>
    private int InitialHeight() => ViewModel.IsWriting ? 220 : ViewModel.WaitingIsWord ? 260 : 200;

    /// <summary>
    /// The window to the card's height, up to 540 px; past that the body scrolls. A card under the mouse grows down,
    /// one above it grows up (CardPlacement.Resize); a wider one (the writing tools) stays centred where it was.
    /// </summary>
    private void Fit()
    {
        if (!_shown || ViewModel.IsHint || Card.XamlRoot is null)
        {
            return;
        }

        var content = Card.ContentHeight;
        if (content <= 0)
        {
            return;
        }

        // The window's edge takes a little of its size: the card gets the client area.
        var scale = Card.XamlRoot.RasterizationScale;
        var height = Scaled(Math.Min(content, MaxHeight), scale) + (AppWindow.Size.Height - AppWindow.ClientSize.Height);
        var width = Scaled(ViewModel.Width, scale) + (AppWindow.Size.Width - AppWindow.ClientSize.Width);
        var current = new PixelRect(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        if (current.Height == height && current.Width == width)
        {
            return;
        }

        var x = Math.Clamp(current.X - ((width - current.Width) / 2), _workArea.X, Math.Max(_workArea.X, _workArea.Right - width));
        var resized = CardPlacement.Resize(current with { X = x, Width = width }, height, _workArea, _above);
        if (_resizing && resized == _to)
        {
            return; // already on its way there
        }

        var time = Motion.Time(Animations.Level, Motion.ResizeMilliseconds);
        if (time == TimeSpan.Zero || Environment.TickCount64 - _shownAt < Motion.EnterMilliseconds)
        {
            StopResize();
            AppWindow.MoveAndResize(new RectInt32(resized.X, resized.Y, resized.Width, resized.Height));
            return;
        }

        _from = current;
        _to = resized;
        _resizeStart = Environment.TickCount64;
        _resizeTime = time;
        if (!_resizing)
        {
            _resizing = true;
            CompositionTarget.Rendering += OnResizeFrame;
        }
    }

    /// <summary>A frame of the window growing or shrinking to its new height, on Windows' curve.</summary>
    private void OnResizeFrame(object? sender, object e)
    {
        var progress = (Environment.TickCount64 - _resizeStart) / _resizeTime.TotalMilliseconds;
        var at = Motion.Ease(Motion.EnterEasing, progress);
        int Step(int from, int to) => from + (int)Math.Round((to - from) * at, MidpointRounding.AwayFromZero);
        AppWindow.MoveAndResize(new RectInt32(Step(_from.X, _to.X), Step(_from.Y, _to.Y), Step(_from.Width, _to.Width), Step(_from.Height, _to.Height)));
        if (progress >= 1)
        {
            StopResize();
        }
    }

    private void StopResize()
    {
        if (_resizing)
        {
            _resizing = false;
            CompositionTarget.Rendering -= OnResizeFrame;
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            Deactivated?.Invoke();
        }
    }
}
