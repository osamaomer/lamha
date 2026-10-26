using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.Core;
using Lamha.Core.Ai;
using Lamha.Core.Money;
using Lamha.Core.Settings;
using Lamha.Core.Updates;
using Lamha.Windows.Shell;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.Main;

/// <summary>A category on Settings' home: its icon, its name, and what's in it ("Interface language · Translation language…").</summary>
public sealed record SettingsTile(string Id, Glyph Glyph, string Title, string Summary, IRelayCommand Open)
{
    /// <summary>What Narrator reads for the tile.</summary>
    public string Spoken => $"{Title}: {Summary}";
}

/// <summary>
/// A language to explain in its own language (Settings → The lookup card): Lamha 2's <c>renderExplainLangs()</c>. The
/// translation language is always explained, so its chip is on and can't be turned off.
/// </summary>
public sealed partial class ExplainChip(string code, Action<ExplainChip> changed) : ObservableObject
{
    private bool _loading;

    public string Code { get; } = code;

    public string Label { get; } = Localization.LanguageName(code);

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>Not the translation language, which is always explained.</summary>
    [ObservableProperty]
    public partial bool CanChange { get; private set; } = true;

    /// <summary>"Always: this is your translation language", on the one that can't be turned off.</summary>
    [ObservableProperty]
    public partial string? Tooltip { get; private set; }

    /// <summary>Shows what the settings say, without saving it back.</summary>
    public void Load(bool on, bool always)
    {
        _loading = true;
        IsOn = on || always;
        CanChange = !always;
        Tooltip = always ? Localization.Get("Settings_ExplainAlways") : null;
        _loading = false;
    }

    partial void OnIsOnChanged(bool value)
    {
        if (!_loading)
        {
            changed(this);
        }
    }
}

/// <summary>
/// The main window's Settings page (phase 1's design, signed off 2026-10-25): a home of categories in Lamha 2's order,
/// each opening its own page of settings cards, with a breadcrumb back. Lamha 2's options page (options.js) and the
/// desktop app's additions to it (desktop/renderer/clipboard/settings.js). Each change is saved at once, as Lamha 2's.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>Lamha 2's choices (options.html): new words per day, the day's goal (0: no goal).</summary>
    private static readonly int[] PerDay = [5, 10, 20, 30];
    private static readonly int[] Goals = [0, 5, 10, 20, 30];
    private static readonly string[] Themes = ["auto", "light", "dark"];
    private static readonly string[] Motions = ["auto", "full", "subtle", "off"];
    private static readonly string[] InterfaceLanguages = ["auto", "ar", "en"];

    /// <summary>Who translates: the writing tools' AI, or one of its own (Lamha 2's order, then the two new ones).</summary>
    private static readonly AiProviderKind?[] Translators = [null, AiProviderKind.Ollama, AiProviderKind.Gemini, AiProviderKind.Claude, AiProviderKind.OpenAi, AiProviderKind.Custom];

    /// <summary>Lamha 2's <c>EXPLAIN_LANGS</c>.</summary>
    private static readonly string[] ExplainLanguages = ["en", "ar", "fr", "tr", "ur", "fa", "es", "de"];

    private readonly Brain _brain;
    private readonly IPageHost _host;
    private readonly StartWithWindows? _startup;
    private readonly UpdateService? _updates;
    private readonly ShortcutService _shortcutService;
    private bool _loading;
    private bool _attached;
    private int _trVersion;

    /// <param name="brain">Lamha's brain: the settings, the keys and the AI, the review list for Delete all cards.</param>
    /// <param name="host">The main window.</param>
    /// <param name="shortcuts">The shortcuts: Keyboard shortcuts changes them, Writing tools' first line names one.</param>
    /// <param name="clipboard">Clipboard history's switch, for Settings → Clipboard.</param>
    /// <param name="startup">Start with Windows, for Settings → Lamha (none in tests' windows).</param>
    /// <param name="updates">The updates, for Settings → Lamha (none in tests' windows).</param>
    public SettingsViewModel(Brain brain, IPageHost host, ShortcutService shortcuts, ClipboardService clipboard, StartWithWindows? startup = null, UpdateService? updates = null)
    {
        _brain = brain;
        _host = host;
        _startup = startup;
        _updates = updates;
        _shortcutService = shortcuts;
        Writing = new WritingSetupViewModel(brain, () => _ = LoadTranslatorAsync());
        Shortcuts = new ShortcutsSetupViewModel(shortcuts);
        Clipboard = new ClipboardSetupViewModel(brain, clipboard, host);
        Privacy = new PrivacySetupViewModel(brain, host);
        Offline = new OfflineSetupViewModel(brain);
        Tiles = [.. SettingsCategories.All.Select(Tile)];

        var settings = brain.Settings.Current;
        ExplainChips = [.. ExplainLanguages.Select(code => new ExplainChip(code, OnExplainChanged))];
        UiLangChoices = [Localization.Get("Settings_Automatic"), Localization.Get("Lang_ar"), Localization.Get("Lang_en")];
        TargetLangChoices = [.. Localization.NamedLanguages.Select(Localization.LanguageName)];
        TranslatorChoices = [Localization.Get("Settings_TrSameAsWriting"), .. Translators.Skip(1).Select(p => AiNames.Of(p!.Value, settings))];
        PerDayChoices = [.. PerDay.Select(n => Localization.Number(n))];
        GoalChoices = [Localization.Get("Settings_NoGoal"), .. Goals.Skip(1).Select(n => Localization.Number(n))];
        ThemeChoices = [Localization.Get("Settings_Automatic"), Localization.Get("Settings_ThemeLight"), Localization.Get("Settings_ThemeDark")];
        MotionChoices = [Localization.Get("Settings_MotionAuto"), Localization.Get("Settings_MotionFull"), Localization.Get("Settings_MotionSubtle"), Localization.Get("Settings_MotionOff")];
        CardBgChoices = [Localization.Get("Settings_Translucent"), Localization.Get("Settings_Solid")];
        var region = RegionCurrency();
        var regionName = Localization.Get($"Currency_{region}_One");
        CurrencyChoices = [Localization.Format("Settings_CurrencyAuto", regionName.StartsWith("Currency_", StringComparison.Ordinal) ? region : regionName),
            .. Prices.Named.Select(CurrencyName)];
        Load(settings);
    }

    public List<SettingsTile> Tiles { get; }

    // ----- where we are: the home, or a category -----

    /// <summary>The category open ("languages", "card"…), or "" for the home.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHome), nameof(SectionTitle), nameof(IsLanguages), nameof(IsCard), nameof(IsWriting), nameof(IsTranslation), nameof(IsClipboard), nameof(IsPrivacy), nameof(IsAbout), nameof(IsKeys),
        nameof(IsReview), nameof(IsOffline), nameof(IsAppearance))]
    public partial string Section { get; private set; } = "";

    public bool IsHome => Section.Length == 0;

    /// <summary>The category's name, after "Settings ›" in the breadcrumb.</summary>
    public string SectionTitle => Tiles.FirstOrDefault(t => t.Id == Section)?.Title ?? "";

    public bool IsLanguages => Section == "languages";

    public bool IsCard => Section == "card";

    public bool IsWriting => Section == "writing";

    public bool IsTranslation => Section == "translation";

    public bool IsClipboard => Section == "clipboard";

    public ClipboardSetupViewModel Clipboard { get; }

    public bool IsPrivacy => Section == "privacy";

    public PrivacySetupViewModel Privacy { get; }

    // ----- Lamha -----

    public bool IsAbout => Section == "about";

    /// <summary>"Lamha · version 3.0.0-beta.2".</summary>
    public string Version { get; } = Localization.Format("Settings_Version", UpdateService.Shown(UpdateService.Current));

    public bool HasUpdates => _updates is not null;

    /// <summary>Lamha 2's line under Check for an update: checking, downloading, ready, up to date, couldn't (its <c>STATUS</c>).</summary>
    public string UpStatus => _updates?.State is not { } state ? "" : state.Status switch
    {
        UpdateStatus.Checking => Localization.Get("Settings_UpChecking"),
        UpdateStatus.Downloading => Localization.Format("Settings_UpDownloading", UpdateService.Shown(state.Version ?? "")),
        UpdateStatus.Ready => Localization.Format("Settings_UpReady", UpdateService.Shown(state.Version ?? "")),
        UpdateStatus.Latest => Localization.Get("Settings_UpLatest"),
        UpdateStatus.Error => Localization.Get("Settings_UpError"),
        UpdateStatus.Dev => Localization.Get("Note_UpDevBody"),
        _ => "",
    };

    /// <summary>The design's state: green when up to date or ready, red when the check failed.</summary>
    public StatusKind UpStatusKind => _updates?.State.Status switch
    {
        UpdateStatus.Latest or UpdateStatus.Ready => StatusKind.Ok,
        UpdateStatus.Error => StatusKind.Bad,
        _ => StatusKind.Neutral,
    };

    /// <summary>Check now, or Restart now once a version is ready (Lamha 2's).</summary>
    public string UpButton => _updates?.State.Status == UpdateStatus.Ready ? Localization.Get("Note_UpRestartNow") : Localization.Get("Settings_UpCheckNow");

    /// <summary>Automatic updates (Lamha 2's <c>updatesAuto</c>).</summary>
    [ObservableProperty]
    public partial bool UpdatesAuto { get; set; }

    /// <summary>Lamha starts at sign-in (Windows' Run key, and its Startup switch).</summary>
    [ObservableProperty]
    public partial bool StartsWithWindows { get; set; }

    public bool CanStartWithWindows => _startup is not null;

    public bool IsReview => Section == "review";

    public bool IsOffline => Section == "offline";

    /// <summary>Dictionaries to download (the language packs).</summary>
    public OfflineSetupViewModel Offline { get; }

    public bool IsAppearance => Section == "appearance";

    // ----- Languages -----

    public List<string> UiLangChoices { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRestartPending))]
    public partial int UiLangIndex { get; set; }

    /// <summary>The interface language chosen isn't the one Lamha started in: it applies at the next start (Lamha 2's words).</summary>
    public bool IsRestartPending => _brain.Localization.Language != Localization.Language;

    public List<string> TargetLangChoices { get; }

    [ObservableProperty]
    public partial int TargetLangIndex { get; set; }

    [ObservableProperty]
    public partial bool ReverseForArabic { get; set; }

    /// <summary>"Automatic (Saudi riyal, from Windows' region)", then the currencies Lamha has names for.</summary>
    public List<string> CurrencyChoices { get; }

    [ObservableProperty]
    public partial int CurrencyIndex { get; set; }

    // ----- Writing tools -----

    /// <summary>"Select any text in any program and press Ctrl+Alt+W to proofread it…", with the keys as they are now.</summary>
    [ObservableProperty]
    public partial string AiIntro { get; private set; } = "";

    /// <summary>"A history of what you copy in any program. Open it with Ctrl+Alt+V…", with the keys as they are now.</summary>
    [ObservableProperty]
    public partial string ClipboardIntro { get; private set; } = "";

    // ----- Keyboard shortcuts -----

    public bool IsKeys => Section == "keys";

    public ShortcutsSetupViewModel Shortcuts { get; }

    public WritingSetupViewModel Writing { get; }

    // ----- The lookup card -----

    [ObservableProperty]
    public partial bool UseContext { get; set; }

    public List<ExplainChip> ExplainChips { get; }

    [ObservableProperty]
    public partial bool TranslateDefinitions { get; set; }

    [ObservableProperty]
    public partial bool ShowWikipedia { get; set; }

    [ObservableProperty]
    public partial bool AutoSpeak { get; set; }

    // ----- Translation -----

    [ObservableProperty]
    public partial bool TrAuto { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesAi))]
    public partial bool TrGoogle { get; set; }

    [ObservableProperty]
    public partial bool TrAi { get; set; }

    public List<string> TranslatorChoices { get; }

    [ObservableProperty]
    public partial int TranslatorIndex { get; set; }

    /// <summary>The AI translates sometimes (not "Google only"): who, with which model, and whether it's set up.</summary>
    public bool UsesAi => !TrGoogle;

    /// <summary>The translating provider's models (Lamha 2's <c>fillTrModels()</c>), the one it translates with by default first.</summary>
    [ObservableProperty]
    public partial List<ModelChoice> TrModelChoices { get; private set; } = [];

    [ObservableProperty]
    public partial int TrModelIndex { get; set; } = -1;

    [ObservableProperty]
    public partial string TrModelHint { get; private set; } = "";

    /// <summary>"Sentences are translated with Claude", or that it isn't set up yet.</summary>
    [ObservableProperty]
    public partial string TrStatus { get; private set; } = "";

    [ObservableProperty]
    public partial StatusKind TrStatusKind { get; private set; }

    // ----- Word review -----

    [ObservableProperty]
    public partial bool CardsAuto { get; set; }

    public List<string> PerDayChoices { get; }

    [ObservableProperty]
    public partial int PerDayIndex { get; set; }

    public List<string> GoalChoices { get; }

    [ObservableProperty]
    public partial int GoalIndex { get; set; }

    // ----- Without internet -----

    [ObservableProperty]
    public partial bool DictLocal { get; set; }

    [ObservableProperty]
    public partial bool DictOffline { get; set; }

    [ObservableProperty]
    public partial bool DictOnline { get; set; }

    // ----- Appearance -----

    public List<string> ThemeChoices { get; }

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    public List<string> MotionChoices { get; }

    [ObservableProperty]
    public partial int MotionIndex { get; set; }

    /// <summary>
    /// Under Animations' preview, what the level does: "Everything, plus playful touches", after "Automatic is using:
    /// Full —" when it's automatic (Lamha 2's <c>initMotionSetting()</c> note).
    /// </summary>
    [ObservableProperty]
    public partial string MotionNote { get; private set; } = "";

    /// <summary>The preview plays: the level was changed, or Preview pressed (Lamha 2's <c>demo()</c>).</summary>
    public event Action? MotionPreview;

    public List<string> CardBgChoices { get; }

    [ObservableProperty]
    public partial int CardBgIndex { get; set; }

    [ObservableProperty]
    public partial bool AccentWindows { get; set; }

    [ObservableProperty]
    public partial bool WindowMica { get; set; }

    /// <summary>The page shows: at its home, with the settings as they are now.</summary>
    public void Refresh()
    {
        Section = "";
        Load(_brain.Settings.Current);
    }

    /// <summary>
    /// A category straight away: an error's Settings button ("writing", "translation", "offline"), or the home for one
    /// that doesn't exist.
    /// </summary>
    public void Open(string section) => Section = Tiles.Any(t => t.Id == section) ? section : "";

    /// <summary>
    /// The page is on screen: changes made elsewhere (the card's language switch, the tray) show here. Detach when it
    /// goes, so a closed window isn't kept alive by the settings' event.
    /// </summary>
    public void Attach()
    {
        if (!_attached)
        {
            _attached = true;
            _brain.Settings.Changed += OnSettingsChanged;
            if (IsOffline)
            {
                Offline.Attach();
            }

            if (_updates is not null)
            {
                _updates.Changed += OnUpdateChanged;
                OnUpdateChanged(); // what happened while the page was away
            }
        }
    }

    public void Detach()
    {
        _attached = false;
        _brain.Settings.Changed -= OnSettingsChanged;
        Offline.Detach();
        if (_updates is not null)
        {
            _updates.Changed -= OnUpdateChanged;
        }
    }

    // ----- commands -----

    /// <summary>The breadcrumb's "Settings", Alt+←: back to the home.</summary>
    [RelayCommand]
    private void GoHome() => Section = "";

    /// <summary>Delete all cards, after Lamha 2's question: the words and the progress.</summary>
    [RelayCommand]
    private async Task DeleteCardsAsync()
    {
        if (await _host.AskAsync(Localization.Get("Settings_DeleteCardsQ"), Localization.Get("Common_Delete")).ConfigureAwait(true) != Answer.Confirm)
        {
            return;
        }

        try
        {
            await _brain.Review.ClearAsync().ConfigureAwait(true);
            _host.ReviewChanged();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            AppLog.Error("delete cards", e);
        }
    }

    // ----- saving -----

    partial void OnUiLangIndexChanged(int value) => Save(s => s with { UiLang = Pick(InterfaceLanguages, value, s.UiLang) });

    partial void OnTargetLangIndexChanged(int value)
    {
        Save(s => s with { TargetLang = Pick(Localization.NamedLanguages, value, s.TargetLang) });
        LoadExplain(_brain.Settings.Current); // the translation language's chip is always on
    }

    partial void OnReverseForArabicChanged(bool value) => Save(s => s with { ReverseForArabic = value });

    partial void OnUseContextChanged(bool value) => Save(s => s with { UseContext = value });

    partial void OnTranslateDefinitionsChanged(bool value) => Save(s => s with { TranslateDefinitions = value });

    partial void OnShowWikipediaChanged(bool value) => Save(s => s with { ShowWikipedia = value });

    partial void OnAutoSpeakChanged(bool value) => Save(s => s with { AutoSpeak = value });

    partial void OnTrAutoChanged(bool value) => Choose(value, s => s with { TrService = TranslateWith.Auto });

    partial void OnTrGoogleChanged(bool value) => Choose(value, s => s with { TrService = TranslateWith.Google });

    partial void OnTrAiChanged(bool value) => Choose(value, s => s with { TrService = TranslateWith.Ai });

    partial void OnTranslatorIndexChanged(int value)
    {
        Save(s => s with { TrProvider = Pick(Translators, value, s.TrProvider) });
        if (!_loading)
        {
            _ = LoadTranslatorAsync();
        }
    }

    /// <summary>The translating provider's model: Lamha 2's <c>trModels</c>, one per provider ("" = its default).</summary>
    partial void OnTrModelIndexChanged(int value)
    {
        if (_loading || value < 0 || value >= TrModelChoices.Count)
        {
            return;
        }

        var id = AiModels.Id(Translator(_brain.Settings.Current));
        var model = TrModelChoices[value].Id;
        Save(s => s with { TrModels = new Dictionary<string, string>(s.TrModels) { [id] = model } });
        UpdateTrStatus();
    }

    partial void OnStartsWithWindowsChanged(bool value)
    {
        if (!_loading && _startup is not null)
        {
            _startup.Set(value);
            AppLog.Info($"start with Windows: {(value ? "on" : "off")}");
        }
    }

    /// <summary>Check now, or Restart now when a version is ready; greyed while a check or a download runs.</summary>
    [RelayCommand(CanExecute = nameof(CanCheckUpdates))]
    private void CheckUpdates()
    {
        if (_updates?.State.Status == UpdateStatus.Ready)
        {
            _updates.Restart();
        }
        else
        {
            _updates?.Check();
        }
    }

    private bool CanCheckUpdates() => _updates?.State.Status is not (UpdateStatus.Checking or UpdateStatus.Downloading);

    private void OnUpdateChanged()
    {
        OnPropertyChanged(nameof(UpStatus));
        OnPropertyChanged(nameof(UpStatusKind));
        OnPropertyChanged(nameof(UpButton));
        CheckUpdatesCommand.NotifyCanExecuteChanged();
    }

    partial void OnUpdatesAutoChanged(bool value) => Save(s => s with { UpdatesAuto = value });

    /// <summary>All releases on GitHub (Lamha 2's link under Updates).</summary>
    [RelayCommand]
    private void OpenReleases() => _host.OpenLink(new Uri("https://github.com/osamaomer/lamha/releases"));

    partial void OnCurrencyIndexChanged(int value) => Save(s => s with { Currency = value <= 0 ? "" : Prices.Named[value - 1] });

    /// <summary>The section opens: Writing tools reads the keys and the chosen provider's models; Translation its model.</summary>
    partial void OnSectionChanged(string value)
    {
        // The packs' list follows downloads only while it's on screen.
        if (value == "offline" && _attached)
        {
            Offline.Attach();
        }
        else
        {
            Offline.Detach();
        }

        if (value == "writing")
        {
            AiIntro = Localization.Format("Settings_AiIntro", _shortcutService.Keys(Shortcut.Write).ToString());
            Writing.Load(_brain.Settings.Current);
        }
        else if (value == "keys")
        {
            Shortcuts.Load();
        }
        else if (value == "translation")
        {
            _ = LoadTranslatorAsync();
        }
        else if (value == "clipboard")
        {
            ClipboardIntro = Localization.Format("Settings_CbIntro", _shortcutService.Keys(Shortcut.Clipboard).ToString());
            Clipboard.Load(_brain.Settings.Current);
        }
        else if (value == "privacy")
        {
            Privacy.Load(_brain.Settings.Current);
        }
        else if (value == "about" && _startup is not null)
        {
            // Read each time: Windows' Startup settings or the tray may have changed it.
            _loading = true;
            StartsWithWindows = _startup.IsOn;
            _loading = false;
        }
    }

    partial void OnCardsAutoChanged(bool value) => Save(s => s with { CardsAuto = value });

    partial void OnPerDayIndexChanged(int value) => Save(s => s with { CardsNewPerDay = Pick(PerDay, value, s.CardsNewPerDay) });

    partial void OnGoalIndexChanged(int value) => Save(s => s with { DailyGoal = Pick(Goals, value, s.DailyGoal) });

    partial void OnDictLocalChanged(bool value) => Choose(value, s => s with { DictSource = DictSource.Local });

    partial void OnDictOfflineChanged(bool value) => Choose(value, s => s with { DictSource = DictSource.Offline });

    partial void OnDictOnlineChanged(bool value) => Choose(value, s => s with { DictSource = DictSource.Online });

    partial void OnThemeIndexChanged(int value) => Save(s => s with { Theme = Pick(Themes, value, s.Theme) });

    partial void OnMotionIndexChanged(int value)
    {
        Save(s => s with { Motion = Pick(Motions, value, s.Motion) });
        if (!_loading)
        {
            Animations.Refresh(_brain.Settings.Current); // at once, so the preview shows the new level
            ShowMotionNote();
            MotionPreview?.Invoke();
        }
    }

    [RelayCommand]
    private void PreviewMotion() => MotionPreview?.Invoke();

    private void ShowMotionNote()
    {
        var level = Animations.Level switch
        {
            Core.Appearance.MotionLevel.Full => "Full",
            Core.Appearance.MotionLevel.Subtle => "Subtle",
            _ => "Off",
        };
        var note = Localization.Get("Settings_MotionNote" + level);
        MotionNote = Pick(Motions, MotionIndex, "auto") == "auto"
            ? Localization.Format("Settings_MotionAutoIs", Localization.Get("Settings_Motion" + level)) + " — " + note
            : note;
    }

    partial void OnCardBgIndexChanged(int value) => Save(s => s with { CardSolid = value == 1 });

    partial void OnAccentWindowsChanged(bool value) => Save(s => s with { AccentWindows = value });

    partial void OnWindowMicaChanged(bool value) => Save(s => s with { WindowMica = value });

    /// <summary>English is the <c>enDict</c> setting, the others <c>explainLangs</c> (Lamha 2's).</summary>
    private void OnExplainChanged(ExplainChip chip) => Save(s => chip.Code == "en"
        ? s with { EnDict = chip.IsOn }
        : s with { ExplainLangs = [.. s.ExplainLangs.Where(l => l != chip.Code), .. chip.IsOn ? [chip.Code] : Array.Empty<string>()] });

    private void Save(Func<LamhaSettings, LamhaSettings> change)
    {
        if (_loading)
        {
            return;
        }

        _loading = true; // Changed comes back here while attached: what's shown is already what was saved
        try
        {
            _brain.Settings.Update(change);
        }
        finally
        {
            _loading = false;
        }

        OnPropertyChanged(nameof(IsRestartPending));
    }

    /// <summary>A radio button: the one chosen saves; the one that lost the choice says nothing.</summary>
    private void Choose(bool chosen, Func<LamhaSettings, LamhaSettings> change)
    {
        if (chosen)
        {
            Save(change);
        }
    }

    private void OnSettingsChanged(object? sender, LamhaSettings settings)
    {
        if (!_loading)
        {
            Load(settings);
        }
    }

    /// <summary>Shows <paramref name="s"/>, without saving it back.</summary>
    private void Load(LamhaSettings s)
    {
        _loading = true;
        try
        {
            UiLangIndex = Index(InterfaceLanguages, s.UiLang);
            TargetLangIndex = Index(Localization.NamedLanguages, s.TargetLang);
            ReverseForArabic = s.ReverseForArabic;
            UseContext = s.UseContext;
            TranslateDefinitions = s.TranslateDefinitions;
            ShowWikipedia = s.ShowWikipedia;
            AutoSpeak = s.AutoSpeak;
            TrAuto = s.TrService == TranslateWith.Auto;
            TrGoogle = s.TrService == TranslateWith.Google;
            TrAi = s.TrService == TranslateWith.Ai;
            TranslatorIndex = Index(Translators, s.TrProvider);
            CardsAuto = s.CardsAuto;
            PerDayIndex = Index(PerDay, s.CardsNewPerDay);
            GoalIndex = Index(Goals, s.DailyGoal);
            DictLocal = s.DictSource == DictSource.Local;
            DictOffline = s.DictSource == DictSource.Offline;
            DictOnline = s.DictSource == DictSource.Online;
            ThemeIndex = Index(Themes, s.Theme);
            MotionIndex = Index(Motions, s.Motion);
            ShowMotionNote();
            CardBgIndex = s.CardSolid ? 1 : 0;
            AccentWindows = s.AccentWindows;
            WindowMica = s.WindowMica;
            UpdatesAuto = s.UpdatesAuto;
            CurrencyIndex = Prices.Named.ToList().IndexOf(s.Currency) + 1; // 0: automatic, also for a code Lamha has no name for
            LoadExplain(s);
        }
        finally
        {
            _loading = false;
        }

        OnPropertyChanged(nameof(IsRestartPending));
        if (IsTranslation)
        {
            _ = LoadTranslatorAsync();
        }
        else if (IsClipboard)
        {
            Clipboard.Load(s);
        }
    }

    // ----- Translation's model (Lamha 2's fillTrModels() and its status line) -----

    private static AiProviderKind Translator(LamhaSettings s) => s.TrProvider ?? s.AiProvider;

    /// <summary>
    /// The translating provider's models: Claude's and Gemini's, the quickest first (what translates by default); for
    /// Ollama, OpenAI and another server "Same as the writing tools", then the models it lists.
    /// </summary>
    private async Task LoadTranslatorAsync()
    {
        var version = ++_trVersion;
        var s = _brain.Settings.Current;
        var provider = Translator(s);
        var chosen = s.TrModels.TryGetValue(AiModels.Id(provider), out var m) ? m : "";
        List<ModelChoice> choices;
        switch (provider)
        {
            case AiProviderKind.Claude:
                choices = [new(AiModels.ClaudeTranslation, Localization.Get("Settings_Haiku")), new("claude-sonnet-5-5", Localization.Get("Settings_Sonnet")),
                    new("claude-opus-5-5", Localization.Get("Settings_Opus"))];
                TrModelHint = Localization.Get("Settings_TrModelHintClaude");
                break;
            case AiProviderKind.Gemini:
                choices = [new(AiModels.GeminiTranslation, Localization.Get("Settings_GemLite")), new("gemini-3.8-flash", Localization.Get("Settings_GemFlash"))];
                TrModelHint = Localization.Get("Settings_TrModelHintGemini");
                break;
            default:
                var writing = provider switch
                {
                    AiProviderKind.Ollama => s.OllamaModel,
                    AiProviderKind.OpenAi => s.OpenAiModel,
                    _ => s.CustomModel,
                };
                choices = [new("", Localization.Format("Settings_TrOllamaSame", writing.Length > 0 ? writing : "—"))];
                TrModelHint = provider == AiProviderKind.Ollama ? Localization.Get("Settings_TrModelHintOllama") : "";
                ShowTrModels(choices, chosen);
                choices = [.. choices, .. await ListedModelsAsync(provider).ConfigureAwait(true)];
                if (version != _trVersion)
                {
                    return;
                }

                if (chosen.Length > 0 && !choices.Any(c => c.Id == chosen))
                {
                    choices.Add(new(chosen, chosen)); // installed on another PC, or the server is off: keep it (Lamha 2's)
                }

                break;
        }

        ShowTrModels(choices, chosen);
        UpdateTrStatus();
    }

    private void ShowTrModels(List<ModelChoice> choices, string chosen)
    {
        _loading = true;
        try
        {
            TrModelChoices = choices;
            TrModelIndex = Math.Max(0, choices.FindIndex(c => c.Id == chosen));
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>The models Ollama, OpenAI or the server lists; none when it can't be reached (the dropdown keeps "same as").</summary>
    private async Task<List<ModelChoice>> ListedModelsAsync(AiProviderKind provider)
    {
        try
        {
            var config = provider switch
            {
                AiProviderKind.Ollama => new AiConfig(provider, "", BaseUrl: OllamaProvider.BaseUrl(_brain.Settings.Current.OllamaUrl)),
                AiProviderKind.OpenAi when _brain.Secrets.Get("openai") is { Length: > 0 } key => new AiConfig(provider, "", key),
                AiProviderKind.Custom when CustomServer.Normalize(_brain.Settings.Current.CustomUrl) is { } url => new AiConfig(provider, "", _brain.Secrets.Get("custom") ?? "", url),
                _ => null,
            };
            return config is null ? [] : [.. (await _brain.Ai.Provider(provider).ListModelsAsync(config).ConfigureAwait(true)).Select(m => new ModelChoice(m.Name, m.Name))];
        }
        catch (LamhaException)
        {
            return [];
        }
    }

    /// <summary>"Sentences are translated with Claude", or Lamha 2's "Claude isn't set up yet…".</summary>
    private void UpdateTrStatus()
    {
        var s = _brain.Settings.Current;
        var name = AiNames.Of(Translator(s), s);
        var ready = _brain.Ai.TranslatorConfig() is not null;
        TrStatus = Localization.Format(ready ? "Settings_TrUses" : "Settings_TrNotReady", name);
        TrStatusKind = ready ? StatusKind.Ok : StatusKind.Bad;
    }

    // ----- the currency -----

    /// <summary>Windows' region's currency ("SAR"), as Core's <c>CurrencyService.Mine</c> reads it.</summary>
    private static string RegionCurrency()
    {
        try
        {
            return System.Globalization.RegionInfo.CurrentRegion.ISOCurrencySymbol;
        }
        catch (ArgumentException)
        {
            return "USD";
        }
    }

    /// <summary>"Saudi riyal (SAR)", or the code alone for one Lamha has no name for.</summary>
    private static string CurrencyName(string code)
    {
        var name = Localization.Get($"Currency_{code}_One");
        // A right-to-left mark after the brackets keeps "(SAR)" at the end of an Arabic name.
        return name == $"Currency_{code}_One" ? code : $"{name} ({code}){(Localization.IsArabic ? "‏" : "")}";
    }

    private void LoadExplain(LamhaSettings s)
    {
        foreach (var chip in ExplainChips)
        {
            chip.Load(chip.Code == "en" ? s.EnDict : s.ExplainLangs.Contains(chip.Code), always: chip.Code == s.TargetLang);
        }
    }

    private SettingsTile Tile(SettingsCategory category) =>
        new(category.Id, category.Glyph, Localization.Get(category.Title), SettingsCategories.Summary(category), new RelayCommand(() => Section = category.Id));

    /// <summary>A setting's place in its choices; the first (its default's place in Lamha 2's lists) for one that isn't there.</summary>
    private static int Index<T>(IReadOnlyList<T> choices, T value)
    {
        for (var i = 0; i < choices.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(choices[i], value))
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>The choice at <paramref name="index"/>; <paramref name="current"/> when nothing is chosen (-1).</summary>
    private static T Pick<T>(IReadOnlyList<T> choices, int index, T current) => index >= 0 && index < choices.Count ? choices[index] : current;
}
