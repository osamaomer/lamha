using System.Text.RegularExpressions;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.System.Threading;

namespace Lamha.Windows.Shell;

/// <summary>The window in front (the one the user is working in) and how to bring a window back to the front.</summary>
public static partial class ForegroundWindow
{
    /// <summary>The window in front now, as a handle number (0 when there's none).</summary>
    public static nint Current => PInvoke.GetForegroundWindow();

    public static bool IsWindow(nint hwnd) => hwnd != 0 && PInvoke.IsWindow((HWND)hwnd);

    /// <summary>The window's class name, e.g. "Notepad" or "ConsoleWindowClass"; "" when unknown.</summary>
    public static unsafe string ClassName(nint hwnd)
    {
        if (hwnd == 0)
        {
            return "";
        }

        var buffer = stackalloc char[256];
        var length = PInvoke.GetClassName((HWND)hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : "";
    }

    /// <summary>The file name of the program that owns the window, lower case ("notepad.exe"); "" when unknown.</summary>
    public static unsafe string ProcessName(nint hwnd)
    {
        uint id = 0;
        if (hwnd == 0 || PInvoke.GetWindowThreadProcessId((HWND)hwnd, &id) == 0 || id == 0)
        {
            return "";
        }

        var process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, id);
        if (process.IsNull)
        {
            return ""; // a program running as administrator, or one that just ended
        }

        try
        {
            var buffer = stackalloc char[1024];
            var size = 1024u;
            if (!PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, &size))
            {
                return "";
            }

            var path = new ReadOnlySpan<char>(buffer, (int)size);
            return path[(path.LastIndexOf('\\') + 1)..].ToString().ToLowerInvariant();
        }
        finally
        {
            PInvoke.CloseHandle(process);
        }
    }

    /// <summary>Terminals: Ctrl+C would interrupt the program running there instead of copying.</summary>
    public static bool IsTerminal(nint hwnd) => IsTerminalClass(ClassName(hwnd));

    internal static bool IsTerminalClass(string className) => TerminalClass().IsMatch(className);

    /// <summary>
    /// Brings <paramref name="hwnd"/> to the front even when Windows' focus-stealing rules would refuse, by attaching
    /// to the current foreground thread's input for a moment. True when it's in front afterwards.
    /// </summary>
    public static unsafe bool ForceForeground(nint hwnd)
    {
        if (!IsWindow(hwnd))
        {
            return false;
        }

        var target = (HWND)hwnd;
        if (PInvoke.GetForegroundWindow() == target)
        {
            return true;
        }

        if (PInvoke.SetForegroundWindow(target) && PInvoke.GetForegroundWindow() == target)
        {
            return true;
        }

        var foreground = PInvoke.GetForegroundWindow();
        var me = PInvoke.GetCurrentThreadId();
        var other = foreground == HWND.Null ? 0 : PInvoke.GetWindowThreadProcessId(foreground, null);
        var attached = other != 0 && other != me && PInvoke.AttachThreadInput(me, other, true);
        PInvoke.BringWindowToTop(target);
        PInvoke.SetForegroundWindow(target);
        if (attached)
        {
            PInvoke.AttachThreadInput(me, other, false);
        }

        return PInvoke.GetForegroundWindow() == target;
    }

    /// <summary>Brings <paramref name="hwnd"/> to the front; false if Windows or the program refused within ~500 ms.</summary>
    public static async Task<bool> FocusAsync(nint hwnd)
    {
        ForceForeground(hwnd);
        for (var i = 0; i < 25 && Current != hwnd; i++)
        {
            await Task.Delay(20).ConfigureAwait(true);
        }

        return Current == hwnd;
    }

    [GeneratedRegex("^(ConsoleWindowClass|CASCADIA_HOSTING_WINDOW_CLASS|mintty|VirtualConsoleClass|PuTTY)$")]
    private static partial Regex TerminalClass();
}
