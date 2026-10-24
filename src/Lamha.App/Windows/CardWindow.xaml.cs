using Lamha.App.Services;
using Lamha.App.ViewModels;
using Lamha.Core.Layout;
using Lamha.Windows.Shell;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Lamha.App.Windows;

/// <summary>
/// The floating card over other programs: no title bar, rounded, acrylic, always on top, not in Alt+Tab or the
/// taskbar. It takes the focus when it opens (so Esc and typing work at once); Replace gives it back to the program
/// the text came from before pasting.
/// </summary>
public sealed partial class CardWindow : Window
{
    /// <summary>The card's size at 100 % scale, in effective pixels.</summary>
    public const int CardWidth = 420;
    public const int CardHeight = 300;

    public CardWindow(CardViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Root.FlowDirection = Localization.FlowDirection;
        Title = Localization.Get("Common_Lamha");
        SystemBackdrop = new DesktopAcrylicBackdrop();

        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Lamha.ico"));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }

        Handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        Screens.RoundCorners(Handle);
        Activated += OnActivated;
    }

    public CardViewModel ViewModel { get; }

    public nint Handle { get; }

    public string ResultLabel { get; } = Localization.Get("Proto_Result");

    /// <summary>The card lost the focus (the user clicked elsewhere).</summary>
    public event Action? Deactivated;

    /// <summary>Shows the card at <paramref name="bounds"/> (physical pixels) and brings it to the front.</summary>
    public void ShowAt(PixelRect bounds)
    {
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        AppWindow.Show(activateWindow: true);
        Activate();
        ForegroundWindow.ForceForeground(Handle);
    }

    public void Hide() => AppWindow.Hide();

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            Deactivated?.Invoke();
        }
    }

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ViewModel.CloseCommand.Execute(null);
    }
}
