using global::Windows.Win32;
using global::Windows.Win32.UI.Input.KeyboardAndMouse;
using Lamha.Windows.Shell;

namespace Lamha.Windows.Input;

/// <summary>Modifier keys of a global shortcut.</summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>A global shortcut, e.g. Ctrl+Alt+L.</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, VirtualKey Key)
{
    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join('+', parts);
    }
}

/// <summary>Global shortcuts, received by the <see cref="MessageWindow"/> on the UI thread.</summary>
public sealed class Hotkeys : IDisposable
{
    private readonly MessageWindow _window;
    private readonly Dictionary<int, Action> _actions = [];
    private int _nextId = 1;

    public Hotkeys(MessageWindow window)
    {
        _window = window;
        _window.HotkeyPressed += OnPressed;
    }

    /// <summary>Registers <paramref name="hotkey"/>; false when another program already uses it.</summary>
    public bool Register(Hotkey hotkey, Action action)
    {
        var id = _nextId++;
        // MOD_NOREPEAT: holding the keys down doesn't fire again and again.
        var modifiers = (HOT_KEY_MODIFIERS)hotkey.Modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        if (!PInvoke.RegisterHotKey(_window.Handle, id, modifiers, (uint)hotkey.Key))
        {
            return false;
        }

        _actions[id] = action;
        return true;
    }

    public void Dispose()
    {
        _window.HotkeyPressed -= OnPressed;
        foreach (var id in _actions.Keys)
        {
            PInvoke.UnregisterHotKey(_window.Handle, id);
        }

        _actions.Clear();
    }

    private void OnPressed(int id)
    {
        if (_actions.TryGetValue(id, out var action))
        {
            action();
        }
    }
}
