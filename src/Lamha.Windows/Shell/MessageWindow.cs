using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.UI.WindowsAndMessaging;

namespace Lamha.Windows.Shell;

/// <summary>
/// A hidden window, never shown: it receives the global hotkeys, the tray icon's clicks, clipboard changes and
/// Explorer's restarts. It's a top-level window rather than a message-only one because only top-level windows get
/// broadcasts (TaskbarCreated, the message that says the tray icon must be added again). Create it on the UI thread:
/// its messages arrive through that thread's message loop.
/// </summary>
public sealed unsafe class MessageWindow : IDisposable
{
    private const string ClassName = "Lamha.MessageWindow";

    /// <summary>The tray icon's callback message.</summary>
    internal const uint TrayCallback = PInvoke.WM_APP + 1;

    /// <summary>Guards the window class and the handle → window map: tests make windows on several threads.</summary>
    private static readonly Lock Gate = new();
    private static readonly Dictionary<nint, MessageWindow> Windows = [];
    private static ushort s_classAtom;

    private readonly uint _taskbarCreated;
    private bool _disposed;

    public MessageWindow()
    {
        var instance = PInvoke.GetModuleHandle((char*)null);
        fixed (char* name = ClassName)
        {
            lock (Gate)
            {
                Register(instance, name);
            }

            // A tool window that can't be activated: never in Alt+Tab or the taskbar, even if something showed it.
            Handle = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE, name, name, WINDOW_STYLE.WS_POPUP,
                0, 0, 0, 0, default, default, instance, null);
        }

        if (Handle == HWND.Null)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateWindowEx");
        }

        lock (Gate)
        {
            Windows[Handle] = this;
        }

        fixed (char* msg = "TaskbarCreated")
        {
            _taskbarCreated = PInvoke.RegisterWindowMessage(msg);
        }
    }

    internal HWND Handle { get; }

    /// <summary>A registered hotkey was pressed (its id).</summary>
    public event Action<int>? HotkeyPressed;

    /// <summary>The tray icon's callback: the mouse or keyboard event, and the screen point it happened at.</summary>
    internal event Action<uint, int, int>? TrayMessage;

    /// <summary>Explorer restarted: tray icons must be added again.</summary>
    internal event Action? TaskbarCreated;

    /// <summary>A menu item was chosen (its id).</summary>
    internal event Action<int>? Command;

    /// <summary>Something was put on the clipboard (once <see cref="Clipboard.ClipboardMonitor"/> listens).</summary>
    internal event Action? ClipboardUpdated;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (Gate)
        {
            Windows.Remove(Handle);
        }

        PInvoke.DestroyWindow(Handle);
    }

    /// <summary>Registers the window class once per process (call it under <see cref="Gate"/>).</summary>
    private static void Register(HINSTANCE instance, char* name)
    {
        if (s_classAtom != 0)
        {
            return;
        }

        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)sizeof(WNDCLASSEXW),
            lpfnWndProc = &WndProc,
            hInstance = instance,
            lpszClassName = name,
        };
        s_classAtom = PInvoke.RegisterClassEx(&wc);
        if (s_classAtom == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "RegisterClassEx");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        MessageWindow? window;
        lock (Gate)
        {
            Windows.TryGetValue(hwnd, out window);
        }

        if (window is not null)
        {
            try
            {
                if (window.OnMessage(msg, wParam, lParam))
                {
                    return (LRESULT)0;
                }
            }
            catch (Exception ex)
            {
                // An exception must never cross back into Windows: it would end the process without a trace.
                Environment.FailFast("Unhandled exception in a window message handler", ex);
            }
        }

        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private bool OnMessage(uint msg, WPARAM wParam, LPARAM lParam)
    {
        switch (msg)
        {
            case PInvoke.WM_HOTKEY:
                HotkeyPressed?.Invoke((int)wParam.Value);
                return true;
            case TrayCallback:
                // NOTIFYICON_VERSION_4: the low word of lParam is the event, wParam holds the point.
                var point = (int)wParam.Value;
                TrayMessage?.Invoke((uint)(lParam.Value & 0xFFFF), (short)(point & 0xFFFF), (short)((point >> 16) & 0xFFFF));
                return true;
            case PInvoke.WM_COMMAND:
                Command?.Invoke((int)(wParam.Value & 0xFFFF));
                return true;
            case PInvoke.WM_CLIPBOARDUPDATE:
                ClipboardUpdated?.Invoke();
                return true;
            default:
                if (msg == _taskbarCreated && msg != 0)
                {
                    TaskbarCreated?.Invoke();
                    return true;
                }

                return false;
        }
    }
}
