using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Dwm;
using global::Windows.Win32.Graphics.Gdi;
using global::Windows.Win32.UI.HiDpi;
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
}
