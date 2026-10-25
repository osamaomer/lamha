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
    /// <summary>
    /// A shortcut the user can choose (Settings → Keyboard shortcuts): Ctrl, Alt or Win with a letter, a digit or F1–F24.
    /// Shift alone would take a capital letter from every program.
    /// </summary>
    public bool IsValid =>
        (Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0 && KeyName(Key) is not null;

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyName(Key) ?? ((ushort)Key).ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        return string.Join('+', parts);
    }

    /// <summary>"Ctrl+Alt+L" (as <see cref="ToString"/> writes it) back into a shortcut; null for anything else.</summary>
    public static Hotkey? Parse(string? text)
    {
        var parts = (text ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            HotkeyModifiers? modifier = part.ToUpperInvariant() switch
            {
                "CTRL" => HotkeyModifiers.Control,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" => HotkeyModifiers.Win,
                _ => null,
            };
            if (modifier is null)
            {
                return null;
            }

            modifiers |= modifier.Value;
        }

        return KeyOf(parts[^1]) is { } key && new Hotkey(modifiers, key) is { IsValid: true } hotkey ? hotkey : null;
    }

    /// <summary>A key's name in a shortcut: "L", "7", "F5"; null for the keys a shortcut doesn't use.</summary>
    public static string? KeyName(VirtualKey key) => (ushort)key switch
    {
        >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 => ((char)(ushort)key).ToString(),
        >= 0x70 and <= 0x87 => "F" + ((ushort)key - 0x6F).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => null,
    };

    private static VirtualKey? KeyOf(string name)
    {
        var n = name.ToUpperInvariant();
        if (n.Length == 1 && n[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return (VirtualKey)n[0];
        }

        return n.Length > 1 && n[0] == 'F' && int.TryParse(n[1..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var f) && f is >= 1 and <= 24
            ? (VirtualKey)(0x6F + f)
            : null;
    }
}

/// <summary>Global shortcuts, received by the <see cref="MessageWindow"/> on the UI thread.</summary>
public sealed class Hotkeys : IDisposable
{
    private readonly MessageWindow _window;
    private readonly Dictionary<int, Action> _actions = [];
    private readonly Dictionary<Hotkey, int> _ids = [];
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
        _ids[hotkey] = id;
        return true;
    }

    /// <summary>Lets go of <paramref name="hotkey"/> (another one replaces it); nothing if it wasn't registered.</summary>
    public void Unregister(Hotkey hotkey)
    {
        if (_ids.Remove(hotkey, out var id))
        {
            PInvoke.UnregisterHotKey(_window.Handle, id);
            _actions.Remove(id);
        }
    }

    public void Dispose()
    {
        _window.HotkeyPressed -= OnPressed;
        foreach (var id in _actions.Keys)
        {
            PInvoke.UnregisterHotKey(_window.Handle, id);
        }

        _actions.Clear();
        _ids.Clear();
    }

    private void OnPressed(int id)
    {
        if (_actions.TryGetValue(id, out var action))
        {
            action();
        }
    }
}
