using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.App.ViewModels.Card;
using Lamha.Core.Layout;
using Lamha.Windows.Shell;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT;

namespace Lamha.App.Windows;

/// <summary>
/// The floating card over other programs: no title bar, Windows' 8 px corners, edge and shadow, Acrylic (or solid),
/// always on top, not in Alt+Tab or the taskbar. Its height follows the card up to 540 px, then the middle scrolls
/// (phase 1's design). It takes the focus when it opens (so Esc and typing work at once); Replace gives it back to the
/// program the text came from before pasting. The hint ("Select some text first") shows without taking the focus.
/// </summary>
public sealed partial class CardWindow : Window
{
    /// <summary>The card's tallest, at 100 % (Lamha 2's limit); the hint's size.</summary>
    public const int MaxHeight = 540;
    public const int HintWidth = 240;
    public const int HintHeight = 44;

    private PixelRect _workArea;
    private bool _above;
    private bool _shown;

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
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        _shown = true;
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

    public void Hide()
    {
        _shown = false;
        AppWindow.Hide();
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
        AppWindow.MoveAndResize(new RectInt32(resized.X, resized.Y, resized.Width, resized.Height));
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            Deactivated?.Invoke();
        }
    }
}
