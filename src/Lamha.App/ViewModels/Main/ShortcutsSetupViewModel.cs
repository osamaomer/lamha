using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.Windows.Input;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.Main;

/// <summary>A shortcut in Settings: what it does, its keys, and Change (then the keys pressed next).</summary>
public sealed partial class ShortcutRow(Shortcut kind, string label, IRelayCommand<ShortcutRow> change) : ObservableObject
{
    public Shortcut Kind { get; } = kind;

    public string Label { get; } = label;

    /// <summary>"Ctrl", "Alt", "L": shown as keys.</summary>
    [ObservableProperty]
    public partial List<KeyPart> Keys { get; set; } = [];

    /// <summary>Waiting for the new keys: "Press the new keys, or Esc to cancel".</summary>
    [ObservableProperty]
    public partial bool IsCapturing { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial StatusKind StatusKind { get; set; }

    public string ChangeLabel { get; } = Localization.Get("Settings_HkChange");

    /// <summary>What Narrator reads for Change: "Change: Look up and translate in any program, Ctrl+Alt+L".</summary>
    public string ChangeName => $"{ChangeLabel}: {Label}, {string.Concat(Keys.Select(k => k.Text))}";

    public IRelayCommand<ShortcutRow> Change { get; } = change;

    partial void OnKeysChanged(List<KeyPart> value) => OnPropertyChanged(nameof(ChangeName));
}

/// <summary>
/// Settings → Keyboard shortcuts (phase 1's design: each one changeable; new in Lamha 3, Lamha 2's were fixed). The
/// quick lookup, the writing tools and the clipboard panel; Esc closes the card.
/// </summary>
public sealed partial class ShortcutsSetupViewModel : ObservableObject
{
    private readonly ShortcutService _shortcuts;

    public ShortcutsSetupViewModel(ShortcutService shortcuts)
    {
        _shortcuts = shortcuts;
        var change = new RelayCommand<ShortcutRow>(Start);
        Rows = [.. shortcuts.All.Select(s => new ShortcutRow(s.Kind, Localization.Get(s.LabelKey), change))];
        Load();
    }

    public List<ShortcutRow> Rows { get; }

    /// <summary>Esc, which closes the card: shown, not changeable.</summary>
    public List<KeyPart> CloseKeys { get; } = [new("Esc", true)];

    /// <summary>The row waiting for keys, or null.</summary>
    public ShortcutRow? Capturing { get; private set; }

    /// <summary>The section opens: the keys as they are now.</summary>
    public void Load()
    {
        Cancel();
        foreach (var row in Rows)
        {
            var info = _shortcuts.All.First(s => s.Kind == row.Kind);
            row.Keys = KeyPart.Of(info.Keys);
            row.Status = info.Taken ? Localization.Format("Note_HotkeyTakenBody", info.Keys.ToString()) : "";
            row.StatusKind = StatusKind.Bad;
        }
    }

    /// <summary>
    /// A key pressed while waiting: Esc cancels; modifiers alone wait for the rest. Returns whether the page should
    /// take the key (so it doesn't also reach a control).
    /// </summary>
    public bool Press(HotkeyModifiers modifiers, VirtualKey key)
    {
        if (Capturing is not { } row)
        {
            return false;
        }

        if (key == (VirtualKey)0x1B && modifiers == HotkeyModifiers.None)
        {
            Cancel();
            return true;
        }

        if (key is VirtualKey.Shift or VirtualKey.Control or VirtualKey.Menu or VirtualKey.LeftWin or VirtualKey.RightWin)
        {
            return true;
        }

        var hotkey = new Hotkey(modifiers, key);
        if (!hotkey.IsValid)
        {
            row.Status = Localization.Get("Settings_HkInvalid");
            row.StatusKind = StatusKind.Bad;
            return true;
        }

        Capturing = null;
        row.IsCapturing = false;
        if (_shortcuts.TryChange(row.Kind, hotkey))
        {
            row.Keys = KeyPart.Of(hotkey);
            row.Status = "";
        }
        else
        {
            row.Status = Localization.Format("Note_HotkeyTakenBody", hotkey.ToString());
            row.StatusKind = StatusKind.Bad;
        }

        return true;
    }

    /// <summary>Change: the next keys pressed become the shortcut (another row's waiting stops).</summary>
    private void Start(ShortcutRow? row)
    {
        Cancel();
        if (row is null)
        {
            return;
        }

        Capturing = row;
        row.IsCapturing = true;
        row.Status = Localization.Get("Settings_HkPress");
        row.StatusKind = StatusKind.Neutral;
    }

    private void Cancel()
    {
        if (Capturing is { } row)
        {
            row.IsCapturing = false;
            row.Status = "";
        }

        Capturing = null;
    }
}
