using Lamha.App.Services;
using Lamha.App.ViewModels.Mini;
using Lamha.Core.Layout;
using Lamha.Windows.Shell;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;

namespace Lamha.App.Windows;

/// <summary>
/// The mini translator's window (<see cref="MiniViewModel"/>): Windows' compact overlay, which keeps it on top of every
/// other window, as Video's picture in picture and Calculator's Keep on top. Its own title bar with Back to Lamha
/// before Windows' Close; it opens where it was last, or at the top end of the screen.
/// </summary>
public sealed partial class MiniWindow : Window
{
    /// <summary>Its size when it first opens, at 100 % (the design's 340 wide).</summary>
    public const int StartWidth = 340;
    public const int StartHeight = 180;

    /// <summary>Where it's remembered (its own place, apart from the main window's).</summary>
    public const string PlaceKey = "mini.place";

    /// <summary>Away from the screen's edges when it opens there for the first time.</summary>
    private const int EdgeGap = 24;

    private WindowPlace? _place;

    public MiniWindow(MiniViewModel viewModel, bool mica)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Title = Lamha;
        Root.FlowDirection = Localization.FlowDirection;
        SystemBackdrop = mica ? new MicaBackdrop() : null;
        Solid.Visibility = mica ? Visibility.Collapsed : Visibility.Visible;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragArea);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Lamha.ico"));
        Handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        if (Localization.IsArabic)
        {
            Screens.LayOutRightToLeft(Handle);
        }

        AppWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay);
        AppWindow.Changed += OnChanged;
        ViewModel.Announced += Announce;
        Root.Loaded += (_, _) =>
        {
            FitCaption();
            Box.Focus(FocusState.Programmatic);
            Box.Select(Box.Text.Length, 0);
        };
        Closed += (_, _) => ViewModel.Unload();
    }

    public MiniViewModel ViewModel { get; }

    public nint Handle { get; }

    /// <summary>Its place on the screen (as it was when it closed), to remember for next time.</summary>
    public WindowPlace? Place => _place;

    /// <summary>Back to Lamha: the main window's Translate page, with what's in the box.</summary>
    public event Action<string>? BackRequested;

    public string Lamha { get; } = Localization.Get("Common_Lamha");

    /// <summary>"Back to Lamha"</summary>
    public string BackLabel { get; } = Localization.Get("Mini_Back");

    /// <summary>Settings → Appearance → Theme: "light", "dark", or "auto" (Windows'); Windows' Close follows.</summary>
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

    /// <summary>
    /// Opens where it was last (<paramref name="was"/>, when that place is still on a screen), or at the top end of the
    /// screen the mouse is on, at the design's size.
    /// </summary>
    public void ShowOn(PixelRect workArea, double scale, WindowPlace? was)
    {
        if (was?.On(Screens.WorkAreaAt(new PixelPoint(was.X + (was.Width / 2), was.Y))) is { } place)
        {
            AppWindow.MoveAndResize(new RectInt32(place.X, place.Y, place.Width, place.Height));
        }
        else
        {
            var width = Scaled(StartWidth, scale);
            var height = Scaled(StartHeight, scale);
            var gap = Scaled(EdgeGap, scale);
            var x = Localization.IsArabic ? workArea.X + gap : workArea.X + workArea.Width - width - gap;
            AppWindow.MoveAndResize(new RectInt32(x, workArea.Y + gap, width, height));
        }

        Remember();
        AppWindow.Show(activateWindow: true);
        Activate();
        ForegroundWindow.ForceForeground(Handle);
    }

    private static int Scaled(double dips, double scale) => (int)Math.Round(dips * scale, MidpointRounding.AwayFromZero);

    private void Remember() =>
        _place = new WindowPlace(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height, false);

    /// <summary>Moved or resized: remembered; and the room kept for Windows' Close follows the screen's scale.</summary>
    private void OnChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPositionChange || args.DidSizeChange)
        {
            Remember();
            FitCaption();
        }
    }

    /// <summary>Windows' Close sits over the title bar's end: Back to Lamha stops before it.</summary>
    private void FitCaption()
    {
        if (Root.XamlRoot is { RasterizationScale: > 0 } root)
        {
            var inset = Math.Max(AppWindow.TitleBar.LeftInset, AppWindow.TitleBar.RightInset);
            CaptionInset.Width = new GridLength(inset / root.RasterizationScale);
        }
    }

    /// <summary>Enter looks up at once; Esc empties the box (it stays open: it's for keeping on top).</summary>
    private void OnBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            ViewModel.LookUpNow();
        }
        else if (e.Key == VirtualKey.Escape && ViewModel.HasQuery)
        {
            e.Handled = true;
            ViewModel.Query = "";
        }
    }

    private void OnBack(object sender, RoutedEventArgs e) => BackRequested?.Invoke(ViewModel.Query.Trim());

    private void Announce(string text)
    {
        if (text.Length > 0 && FrameworkElementAutomationPeer.FromElement(Root) is { } peer)
        {
            peer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, text, "LamhaMini");
        }
    }
}
