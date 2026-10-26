using Lamha.Core.Settings;
using Lamha.Windows.Input;

namespace Lamha.App.Services;

/// <summary>Lamha's global shortcuts, in the tray's order.</summary>
public enum Shortcut
{
    Lookup,
    Write,
    Clipboard,
}

/// <summary>
/// A shortcut as the tray and Settings list it: what it does, its keys, and whether another program held them when
/// Lamha last tried to take them.
/// </summary>
public sealed record ShortcutInfo(Shortcut Kind, Hotkey Keys, string LabelKey, bool Taken);

/// <summary>
/// Lamha's global shortcuts: the quick lookup, the writing tools and the clipboard panel, with the keys from Settings →
/// Keyboard shortcuts (new in Lamha 3; Lamha 2's were fixed) or the app's own. A change applies at once; keys another
/// program holds are refused and the old ones stay. While Lamha is turned off (the tray's Enable Lamha) it holds none
/// of them, so the keys go back to other programs.
/// </summary>
public sealed class ShortcutService
{
    private readonly Hotkeys _hotkeys;
    private readonly SettingsService _settings;
    private readonly Action<Shortcut> _run;
    private readonly Dictionary<Shortcut, Hotkey> _defaults;
    private readonly List<ShortcutInfo> _all = [];

    /// <param name="hotkeys">Windows' global shortcuts, on the tray's message window.</param>
    /// <param name="settings">Where the user's keys are kept, and whether Lamha is on.</param>
    /// <param name="defaults">The app's own keys for each (the beta's Ctrl+Alt+L / W / V).</param>
    /// <param name="run">What a shortcut does: the card in its mode, or the clipboard panel.</param>
    public ShortcutService(Hotkeys hotkeys, SettingsService settings, Dictionary<Shortcut, Hotkey> defaults, Action<Shortcut> run)
    {
        _hotkeys = hotkeys;
        _settings = settings;
        _defaults = defaults;
        _run = run;
        var s = settings.Current;
        IsOn = s.Enabled;
        foreach (var (kind, keys) in defaults)
        {
            var saved = Hotkey.Parse(Saved(s, kind)) ?? keys;
            _all.Add(IsOn ? Register(kind, saved) : new ShortcutInfo(kind, saved, Label(kind), Taken: false));
        }
    }

    /// <summary>The shortcuts, in the tray's order.</summary>
    public IReadOnlyList<ShortcutInfo> All => _all;

    /// <summary>Whether Lamha holds its shortcuts (the tray's Enable Lamha).</summary>
    public bool IsOn { get; private set; }

    /// <summary>The keys for <paramref name="kind"/> ("Ctrl+Alt+L").</summary>
    public Hotkey Keys(Shortcut kind) => _all.First(s => s.Kind == kind).Keys;

    /// <summary>The app's own keys for <paramref name="kind"/>.</summary>
    public Hotkey Default(Shortcut kind) => _defaults[kind];

    /// <summary>
    /// Enable Lamha: on takes the keys again (those another program took meanwhile are marked), off lets go of them.
    /// Saved, so it lasts after a restart.
    /// </summary>
    public void SetOn(bool on)
    {
        if (on == IsOn)
        {
            return;
        }

        IsOn = on;
        _settings.Update(s => s with { Enabled = on });
        for (var i = 0; i < _all.Count; i++)
        {
            var s = _all[i];
            if (on)
            {
                _all[i] = Register(s.Kind, s.Keys);
            }
            else
            {
                _hotkeys.Unregister(s.Keys);
                _all[i] = s with { Taken = false };
            }
        }

        AppLog.Info($"Lamha {(on ? "enabled" : "turned off")}");
    }

    /// <summary>
    /// <paramref name="keys"/> for <paramref name="kind"/>, saved; false (and the old keys kept) when another program
    /// holds them or another of Lamha's shortcuts has them. While Lamha is off they're only tried, then let go.
    /// </summary>
    public bool TryChange(Shortcut kind, Hotkey keys)
    {
        var i = _all.FindIndex(s => s.Kind == kind);
        var old = _all[i];
        if (keys == old.Keys && !old.Taken)
        {
            return true;
        }

        if (_all.Any(s => s.Kind != kind && s.Keys == keys))
        {
            return false;
        }

        _hotkeys.Unregister(old.Keys);
        var next = Register(kind, keys);
        if (next.Taken)
        {
            _all[i] = IsOn ? Register(kind, old.Keys) : old;
            return false;
        }

        if (!IsOn)
        {
            _hotkeys.Unregister(keys);
        }

        _all[i] = next;
        var text = keys == _defaults[kind] ? "" : keys.ToString();
        _settings.Update(s => kind switch
        {
            Shortcut.Write => s with { WriteKeys = text },
            Shortcut.Clipboard => s with { ClipboardKeys = text },
            _ => s with { LookupKeys = text },
        });
        return true;
    }

    private static string Saved(LamhaSettings s, Shortcut kind) => kind switch
    {
        Shortcut.Write => s.WriteKeys,
        Shortcut.Clipboard => s.ClipboardKeys,
        _ => s.LookupKeys,
    };

    private static string Label(Shortcut kind) => kind switch
    {
        Shortcut.Write => "Tray_ShortcutWrite",
        Shortcut.Clipboard => "Settings_HkClip",
        _ => "Tray_ShortcutLookup",
    };

    private ShortcutInfo Register(Shortcut kind, Hotkey keys)
    {
        var registered = _hotkeys.Register(keys, () => _run(kind));
        AppLog.Info($"hotkey {keys}: {(registered ? "registered" : "taken by another program")}");
        return new ShortcutInfo(kind, keys, Label(kind), Taken: !registered);
    }
}
