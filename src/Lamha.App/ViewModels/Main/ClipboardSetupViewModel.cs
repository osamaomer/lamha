using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Services;
using Lamha.Core.Clipboard;
using Lamha.Core.Settings;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.Main;

/// <summary>An excluded program in the list: its file name, and Remove ("Remove keepassxc.exe" for Narrator).</summary>
public sealed record ExcludedApp(string Exe, string RemoveName, IRelayCommand Remove, bool IsFirst);

/// <summary>A program seen in the history and not excluded: "+ Notepad", its copies counted in the tooltip.</summary>
public sealed record SeenApp(string Label, string Tooltip, IRelayCommand Add);

/// <summary>
/// Settings → Clipboard: Lamha 2's desktop/renderer/clipboard/settings.js. Clipboard history on or off (turning it off
/// asks whether to delete it too), how much it keeps and for how long, bank cards, the programs it never records, and
/// clearing it. Lamha's tray only pauses it.
/// </summary>
public sealed partial class ClipboardSetupViewModel : ObservableObject
{
    /// <summary>Lamha 2's choices: copies kept, and days an unpinned copy stays (0: for ever).</summary>
    private static readonly int[] Limits = [100, 250, 500, 1000];
    private static readonly int[] Expiries = [1, 7, 30, 90, 0];

    private readonly Brain _brain;
    private readonly ClipboardService _clipboard;
    private readonly IPageHost _host;
    private bool _loading;

    public ClipboardSetupViewModel(Brain brain, ClipboardService clipboard, IPageHost host)
    {
        _brain = brain;
        _clipboard = clipboard;
        _host = host;
        LimitChoices = [.. Limits.Select(n => Localization.Number(n))];
        ExpiryChoices = [Localization.Get("Settings_Day1"), Localization.Get("Settings_Days7"), Localization.Get("Settings_Days30"),
            Localization.Get("Settings_Days90"), Localization.Get("Settings_Never")];
    }

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    public List<string> LimitChoices { get; }

    [ObservableProperty]
    public partial int LimitIndex { get; set; }

    public List<string> ExpiryChoices { get; }

    [ObservableProperty]
    public partial int ExpiryIndex { get; set; }

    [ObservableProperty]
    public partial bool SkipCards { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoApps))]
    public partial List<ExcludedApp> Excluded { get; private set; } = [];

    /// <summary>"No excluded programs."</summary>
    public bool HasNoApps => Excluded.Count == 0;

    /// <summary>The program's name being typed ("notepad.exe").</summary>
    [ObservableProperty]
    public partial string AppInput { get; set; } = "";

    /// <summary>"Type the program's name as Task Manager shows it…" after a name that can't be one.</summary>
    [ObservableProperty]
    public partial string AppError { get; private set; } = "";

    /// <summary>Programs in the history that aren't excluded, the 12 with most copies: one click excludes them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSeen))]
    public partial List<SeenApp> Seen { get; private set; } = [];

    public bool HasSeen => Seen.Count > 0;

    /// <summary>The section opens, or the settings changed elsewhere (the Clipboard page's Turn on).</summary>
    public void Load(LamhaSettings s)
    {
        _loading = true;
        try
        {
            IsOn = s.ClipboardEnabled;
            LimitIndex = Math.Max(0, Array.IndexOf(Limits, s.ClipboardMaxItems));
            ExpiryIndex = Math.Max(0, Array.IndexOf(Expiries, s.ClipboardExpiryDays));
            SkipCards = s.ClipboardSkipCards;
        }
        finally
        {
            _loading = false;
        }

        Excluded = [.. s.ClipboardExcludedApps.Select((exe, i) => new ExcludedApp(exe, Localization.Format("Settings_CbRemoveApp", exe), new RelayCommand(() => Include(exe)), i == 0))];
        LoadSeen(s);
    }

    // ----- saving -----

    /// <summary>On at once; off asks Lamha 2's question first: delete the history too, keep it, or (Esc) stay on.</summary>
    partial void OnIsOnChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        if (value)
        {
            _clipboard.SetOn(true);
            return;
        }

        _ = TurnOffAsync();
    }

    private async Task TurnOffAsync()
    {
        var answer = await _host.AskAsync(Localization.Get("Settings_CbOffQ"), Localization.Get("Common_Delete"),
            Localization.Get("Settings_CbOffQText"), Localization.Get("Settings_CbKeepIt")).ConfigureAwait(true);
        if (answer == Answer.Cancel)
        {
            _loading = true;
            IsOn = true; // nothing changes
            _loading = false;
            return;
        }

        _clipboard.SetOn(false);
        if (answer == Answer.Confirm)
        {
            _brain.ClipboardHistory.Clear();
            AppLog.Info("settings: clipboard history turned off and deleted");
        }

        LoadSeen(_brain.Settings.Current);
    }

    partial void OnLimitIndexChanged(int value) => Save(s => value >= 0 ? s with { ClipboardMaxItems = Limits[value] } : s);

    partial void OnExpiryIndexChanged(int value) => Save(s => value >= 0 ? s with { ClipboardExpiryDays = Expiries[value] } : s);

    partial void OnSkipCardsChanged(bool value) => Save(s => s with { ClipboardSkipCards = value });

    partial void OnAppInputChanged(string value) => AppError = "";

    private void Save(Func<LamhaSettings, LamhaSettings> change)
    {
        if (!_loading)
        {
            _brain.Settings.Update(change);
        }
    }

    // ----- excluded programs -----

    /// <summary>Add (or Enter in the field): the program typed, by its file name.</summary>
    [RelayCommand]
    private Task AddTypedAsync() => ExcludeAsync(AppInput);

    /// <summary>
    /// Never records <paramref name="input"/> again; if the history has copies from it, Lamha 2 asks whether to delete
    /// them.
    /// </summary>
    private async Task ExcludeAsync(string input)
    {
        var exe = ClipPrivacy.ExeName(input);
        if (exe.Length == 0)
        {
            AppError = Localization.Get("Settings_CbBadApp");
            return;
        }

        _brain.Settings.Update(s => s.ClipboardExcludedApps.Contains(exe) ? s : s with { ClipboardExcludedApps = [.. s.ClipboardExcludedApps, exe] });
        AppInput = "";
        Load(_brain.Settings.Current);
        var count = _brain.ClipboardHistory.Apps().FirstOrDefault(a => a.App == exe).Count;
        if (count > 0 && await _host.AskAsync(Localization.Format("Settings_CbDeleteAppQ", AppName(exe)), Localization.Get("Common_Delete"),
                Counted("Settings_CbAppItems", count), Localization.Get("Settings_CbKeepThem")).ConfigureAwait(true) == Answer.Confirm)
        {
            _brain.ClipboardHistory.RemoveApp(exe);
            LoadSeen(_brain.Settings.Current);
        }
    }

    private void Include(string exe)
    {
        _brain.Settings.Update(s => s with { ClipboardExcludedApps = [.. s.ClipboardExcludedApps.Where(a => a != exe)] });
        Load(_brain.Settings.Current);
    }

    /// <summary>Lamha 2's <c>renderSeen()</c>: programs in the history, not Lamha's own copies nor unknown ones.</summary>
    private void LoadSeen(LamhaSettings s)
    {
        try
        {
            Seen = [.. _brain.ClipboardHistory.Apps()
                .Where(a => a.App is not ("lamha" or "unknown") && !s.ClipboardExcludedApps.Contains(a.App))
                .Take(12)
                .Select(a => new SeenApp("+ " + AppName(a.App), Counted("Settings_CbItems", a.Count), new AsyncRelayCommand(() => ExcludeAsync(a.App))))];
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            AppLog.Error("clipboard programs", e);
            Seen = [];
        }
    }

    // ----- clearing -----

    [RelayCommand]
    private async Task ClearUnpinnedAsync()
    {
        if (await _host.AskAsync(Localization.Get("Settings_CbClearUnpinnedQ"), Localization.Get("Common_ClearShort"),
                Localization.Get("Settings_CbClearUnpinnedText")).ConfigureAwait(true) == Answer.Confirm)
        {
            _brain.ClipboardHistory.Clear(keepPinned: true);
            LoadSeen(_brain.Settings.Current);
        }
    }

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        if (await _host.AskAsync(Localization.Get("Settings_CbClearAllQ"), Localization.Get("Settings_CbClearAll"),
                Localization.Get("Settings_CbClearAllText")).ConfigureAwait(true) == Answer.Confirm)
        {
            _brain.ClipboardHistory.Clear();
            LoadSeen(_brain.Settings.Current);
        }
    }

    /// <summary>"Notepad" for notepad.exe (Lamha 2's <c>appName</c>, as the Clipboard page shows programs).</summary>
    private static string AppName(string exe) => exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe[..^4] : exe;

    /// <summary>Lamha 2's <c>items</c>: one, two, 3 to 10 and the rest each have their own words in Arabic.</summary>
    private static string Counted(string key, int n) => n switch
    {
        1 => Localization.Get(key + "One"),
        2 => Localization.Get(key + "Two"),
        >= 3 and <= 10 => Localization.Format(key + "Few", n),
        _ => Localization.Format(key + "Many", n),
    };
}
