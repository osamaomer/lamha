using Lamha.App.ViewModels.Card;
using Lamha.Core.Settings;
using Lamha.Windows.Input;

namespace Lamha.App.Services;

/// <summary>A shortcut as the tray and Settings list it: what it does, its keys, and whether Lamha got them.</summary>
public sealed record ShortcutInfo(CardMode Mode, Hotkey Keys, string LabelKey, bool Registered);

/// <summary>
/// Lamha's global shortcuts: the quick lookup and the writing tools, with the keys from Settings → Keyboard shortcuts
/// (new in Lamha 3; Lamha 2's were fixed) or the app's own. A change applies at once; keys another program holds are
/// refused and the old ones stay.
/// </summary>
public sealed class ShortcutService
{
    private readonly Hotkeys _hotkeys;
    private readonly SettingsService _settings;
    private readonly Action<CardMode> _run;
    private readonly Dictionary<CardMode, Hotkey> _defaults;
    private readonly List<ShortcutInfo> _all = [];

    /// <param name="hotkeys">Windows' global shortcuts, on the tray's message window.</param>
    /// <param name="settings">Where the user's keys are kept.</param>
    /// <param name="defaults">The app's own keys for each (the beta's Ctrl+Alt+L / W).</param>
    /// <param name="run">What a shortcut does: the card, in its mode.</param>
    public ShortcutService(Hotkeys hotkeys, SettingsService settings, Dictionary<CardMode, Hotkey> defaults, Action<CardMode> run)
    {
        _hotkeys = hotkeys;
        _settings = settings;
        _defaults = defaults;
        _run = run;
        var s = settings.Current;
        foreach (var (mode, keys) in defaults)
        {
            _all.Add(Register(mode, Hotkey.Parse(mode == CardMode.Write ? s.WriteKeys : s.LookupKeys) ?? keys));
        }
    }

    /// <summary>The shortcuts, in the tray's order.</summary>
    public IReadOnlyList<ShortcutInfo> All => _all;

    /// <summary>The keys for <paramref name="mode"/> ("Ctrl+Alt+L").</summary>
    public Hotkey Keys(CardMode mode) => _all.First(s => s.Mode == mode).Keys;

    /// <summary>The app's own keys for <paramref name="mode"/>.</summary>
    public Hotkey Default(CardMode mode) => _defaults[mode];

    /// <summary>
    /// <paramref name="keys"/> for <paramref name="mode"/>, saved; false (and the old keys kept) when another program
    /// holds them or Lamha's other shortcut has them.
    /// </summary>
    public bool TryChange(CardMode mode, Hotkey keys)
    {
        var i = _all.FindIndex(s => s.Mode == mode);
        var old = _all[i];
        if (keys == old.Keys && old.Registered)
        {
            return true;
        }

        if (_all.Any(s => s.Mode != mode && s.Keys == keys))
        {
            return false;
        }

        _hotkeys.Unregister(old.Keys);
        var next = Register(mode, keys);
        if (!next.Registered)
        {
            _all[i] = Register(mode, old.Keys);
            return false;
        }

        _all[i] = next;
        var text = keys == _defaults[mode] ? "" : keys.ToString();
        _settings.Update(s => mode == CardMode.Write ? s with { WriteKeys = text } : s with { LookupKeys = text });
        return true;
    }

    private ShortcutInfo Register(CardMode mode, Hotkey keys)
    {
        var registered = _hotkeys.Register(keys, () => _run(mode));
        AppLog.Info($"hotkey {keys}: {(registered ? "registered" : "taken by another program")}");
        return new ShortcutInfo(mode, keys, mode == CardMode.Write ? "Tray_ShortcutWrite" : "Tray_ShortcutLookup", registered);
    }
}
