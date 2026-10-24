using global::Windows.Win32;
using global::Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Lamha.Windows.Input;

/// <summary>The virtual keys Lamha uses.</summary>
public enum VirtualKey : ushort
{
    Shift = 0x10,
    Control = 0x11,
    Menu = 0x12,
    LeftWin = 0x5B,
    RightWin = 0x5C,
    A = 0x41,
    C = 0x43,
    L = 0x4C,
    V = 0x56,
    W = 0x57,
}

/// <summary>Reads and presses keys in whatever program has the keyboard focus.</summary>
public static class Keyboard
{
    private static readonly VirtualKey[] Modifiers =
        [VirtualKey.Menu, VirtualKey.Shift, VirtualKey.Control, VirtualKey.LeftWin, VirtualKey.RightWin];

    public static bool IsDown(VirtualKey key) => (PInvoke.GetAsyncKeyState((int)key) & 0x8000) != 0;

    /// <summary>
    /// Waits until the user has let go of the shortcut's keys (Alt, Shift, Ctrl, Win), then releases any still held.
    /// Otherwise the other program would see Ctrl+Alt+Shift+C instead of Ctrl+C.
    /// </summary>
    public static async Task ReleaseModifiersAsync(TimeSpan timeout)
    {
        var until = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Modifiers.Any(IsDown) && Environment.TickCount64 < until)
        {
            await Task.Delay(15).ConfigureAwait(true);
        }

        foreach (var key in Modifiers.Where(IsDown))
        {
            Send([KeyInput(key, up: true)]);
        }
    }

    /// <summary>Presses Ctrl+<paramref name="key"/> in the program that has the keyboard focus.</summary>
    public static void CtrlChord(VirtualKey key) => Send(
    [
        KeyInput(VirtualKey.Control, up: false),
        KeyInput(key, up: false),
        KeyInput(key, up: true),
        KeyInput(VirtualKey.Control, up: true),
    ]);

    private static INPUT KeyInput(VirtualKey key, bool up) => new()
    {
        type = INPUT_TYPE.INPUT_KEYBOARD,
        Anonymous = new INPUT._Anonymous_e__Union
        {
            ki = new KEYBDINPUT
            {
                wVk = (VIRTUAL_KEY)key,
                dwFlags = up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0,
            },
        },
    };

    private static unsafe void Send(ReadOnlySpan<INPUT> inputs)
    {
        fixed (INPUT* p = inputs)
        {
            PInvoke.SendInput((uint)inputs.Length, p, sizeof(INPUT));
        }
    }
}
