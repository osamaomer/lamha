using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Dwm;
using global::Windows.Win32.Graphics.Gdi;
using global::Windows.Win32.UI.HiDpi;
using global::Windows.Win32.UI.WindowsAndMessaging;
using Lamha.Core.Layout;

namespace Lamha.Windows.Shell;

/// <summary>The mouse, the screens and their scaling, in physical pixels.</summary>
public static unsafe class Screens
{
    public static PixelPoint Cursor
    {
        get
        {
            PInvoke.GetCursorPos(out var p);
            return new PixelPoint(p.X, p.Y);
        }
    }

    /// <summary>The work area (the screen without the taskbar) of the monitor at <paramref name="point"/>.</summary>
    public static PixelRect WorkAreaAt(PixelPoint point)
    {
        var monitor = PInvoke.MonitorFromPoint(new System.Drawing.Point(point.X, point.Y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        PInvoke.GetMonitorInfo(monitor, &info);
        var r = info.rcWork;
        return new PixelRect(r.left, r.top, r.right - r.left, r.bottom - r.top);
    }

    /// <summary>The scale of the monitor at <paramref name="point"/>: 1.0 at 100 %, 1.5 at 150 %.</summary>
    public static double ScaleAt(PixelPoint point)
    {
        var monitor = PInvoke.MonitorFromPoint(new System.Drawing.Point(point.X, point.Y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        return PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpi, out _).Succeeded ? dpi / 96.0 : 1.0;
    }

    /// <summary>Windows 11's rounded corners for a window without a title bar.</summary>
    public static void RoundCorners(nint hwnd)
    {
        var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        PInvoke.DwmSetWindowAttribute((HWND)hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &preference, sizeof(DWM_WINDOW_CORNER_PREFERENCE));
    }

    /// <summary>
    /// Lays a window out right to left (Arabic): Windows then puts its minimise, maximise and close buttons on the left,
    /// as in Windows' own Arabic programs.
    /// </summary>
    public static void LayOutRightToLeft(nint hwnd)
    {
        var window = (HWND)hwnd;
        var style = PInvoke.GetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, style | (int)WINDOW_EX_STYLE.WS_EX_LAYOUTRTL);
        PInvoke.SetWindowPos(window, HWND.Null, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Moves a window without a title bar with the mouse, from a press on what serves as its title bar (a pinned card's
    /// bar): Windows' own move, as if the press were on a title bar. Call it while the left button is down.
    /// </summary>
    public static void StartMove(nint hwnd)
    {
        PInvoke.ReleaseCapture();
        PInvoke.SendMessage((HWND)hwnd, PInvoke.WM_NCLBUTTONDOWN, PInvoke.HTCAPTION, 0);
    }
}
