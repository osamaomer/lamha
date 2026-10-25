using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Services;
using Lamha.App.ViewModels.Main;
using Lamha.Core.Settings;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.FirstRun;

/// <summary>First run's steps, in order. Lamha 2's data (between the language and Try it) comes with the importer, in phase 6.</summary>
public enum FirstRunStep
{
    Language,
    Try,
    Writing,
    Ready,
}

/// <summary>A step in the row at the top: its number (or a check once done) and its name.</summary>
public sealed record StepItem(string Number, string Label, bool IsDone, bool IsCurrent);

/// <summary>What the first-run window asks of the app.</summary>
public interface IFirstRunHost
{
    /// <summary>Step 1's choice: the interface language changes at once, so the rest is in it.</summary>
    void ChangeLanguage(string language);

    /// <summary>Customise shortcuts: Settings → Keyboard shortcuts in the main window.</summary>
    void OpenShortcuts();

    /// <summary>Start: the last step's two switches applied, and the window closes.</summary>
    void Finish(bool startWithWindows, bool clipboard);
}

/// <summary>
/// First run (phase 1's design, docs/design/first-run.html): the language, a first lookup by doing it, the writing
/// tools (optional), and ready, one screen each. Nothing is chosen for the user: no AI until they pick one, and
/// clipboard history stays off unless they turn it on (Lamha 2's rule).
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    private static readonly FirstRunStep[] Order = [FirstRunStep.Language, FirstRunStep.Try, FirstRunStep.Writing, FirstRunStep.Ready];

    private readonly Brain _brain;
    private readonly ShortcutService _shortcuts;
    private readonly IFirstRunHost _host;
    private AiProviderKind _providerBefore;

    /// <param name="brain">Lamha's brain: the settings, and the writing tools' setup.</param>
    /// <param name="shortcuts">The shortcuts, named as they are now (Customise shortcuts may change them).</param>
    /// <param name="host">The app: the language, Settings, and Start.</param>
    /// <param name="step">The step to open at: the first, or the language step again after the language changed.</param>
    /// <param name="startWithWindows">Whether Lamha starts with Windows now; first run turns it on unless the user says no.</param>
    public FirstRunViewModel(Brain brain, ShortcutService shortcuts, IFirstRunHost host, FirstRunStep step, bool startWithWindows)
    {
        _brain = brain;
        _shortcuts = shortcuts;
        _host = host;
        Writing = new WritingSetupViewModel(brain, () => { });
        IsArabic = Localization.IsArabic;
        IsEnglish = !Localization.IsArabic;
        StartWithWindows = startWithWindows;
        ClipboardOn = brain.Settings.Current.ClipboardEnabled;
        RefreshKeys();
        Step = step;
        Enter(step);
    }

    public WritingSetupViewModel Writing { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLanguage), nameof(IsTry), nameof(IsWriting), nameof(IsReady), nameof(Steps))]
    public partial FirstRunStep Step { get; private set; }

    public bool IsLanguage => Step == FirstRunStep.Language;

    public bool IsTry => Step == FirstRunStep.Try;

    public bool IsWriting => Step == FirstRunStep.Writing;

    public bool IsReady => Step == FirstRunStep.Ready;

    /// <summary>The row at the top: every step, the ones before this one checked.</summary>
    public List<StepItem> Steps
    {
        get
        {
            var at = Array.IndexOf(Order, Step);
            return [.. Order.Select((step, i) => new StepItem(Localization.Number(i + 1), StepName(step), i < at, i == at))];
        }
    }

    // ----- 1. The language -----

    [ObservableProperty]
    public partial bool IsArabic { get; set; }

    [ObservableProperty]
    public partial bool IsEnglish { get; set; }

    partial void OnIsArabicChanged(bool value) => Choose(value, "ar");

    partial void OnIsEnglishChanged(bool value) => Choose(value, "en");

    // ----- 2. Try it -----

    /// <summary>The lookup shortcut opened the card on the sample sentence: the instruction gives way to "That's it".</summary>
    [ObservableProperty]
    public partial bool Tried { get; set; }

    /// <summary>The three shortcuts as they are now, drawn as key caps.</summary>
    [ObservableProperty]
    public partial List<KeyPart> LookupKeys { get; private set; } = [];

    [ObservableProperty]
    public partial List<KeyPart> WriteKeys { get; private set; } = [];

    [ObservableProperty]
    public partial List<KeyPart> ClipboardKeys { get; private set; } = [];

    /// <summary>The shortcuts again, after Customise shortcuts (the window comes back to the front).</summary>
    public void RefreshKeys()
    {
        LookupKeys = KeyPart.Of(_shortcuts.Keys(Shortcut.Lookup));
        WriteKeys = KeyPart.Of(_shortcuts.Keys(Shortcut.Write));
        ClipboardKeys = KeyPart.Of(_shortcuts.Keys(Shortcut.Clipboard));
    }

    // ----- 4. Ready -----

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial bool ClipboardOn { get; set; }

    // ----- moving between steps -----

    [RelayCommand]
    private void Next()
    {
        var i = Array.IndexOf(Order, Step);
        if (i < Order.Length - 1)
        {
            Go(Order[i + 1]);
        }
    }

    [RelayCommand]
    private void Back()
    {
        var i = Array.IndexOf(Order, Step);
        if (i > 0)
        {
            Go(Order[i - 1]);
        }
    }

    /// <summary>Later, on the writing tools: no AI until the user chooses, so the provider goes back to what it was.</summary>
    [RelayCommand]
    private void Later()
    {
        if (_brain.Settings.Current.AiProvider != _providerBefore)
        {
            _brain.Settings.Update(s => s with { AiProvider = _providerBefore });
        }

        Go(FirstRunStep.Ready);
    }

    [RelayCommand]
    private void Start() => _host.Finish(StartWithWindows, ClipboardOn);

    [RelayCommand]
    private void Customize() => _host.OpenShortcuts();

    private void Go(FirstRunStep step)
    {
        Step = step;
        Enter(step);
    }

    private void Enter(FirstRunStep step)
    {
        if (step == FirstRunStep.Writing)
        {
            _providerBefore = _brain.Settings.Current.AiProvider;
            Writing.Load(_brain.Settings.Current);
        }
    }

    private void Choose(bool chosen, string language)
    {
        if (chosen && language != Localization.Language)
        {
            _host.ChangeLanguage(language);
        }
    }

    private static string StepName(FirstRunStep step) => Localization.Get(step switch
    {
        FirstRunStep.Language => "FirstRun_StepLanguage",
        FirstRunStep.Try => "FirstRun_StepTry",
        FirstRunStep.Writing => "Settings_WritingTools",
        _ => "FirstRun_StepReady",
    });

    // ----- the words (x:Bind reads them once) -----

    public string Lamha { get; } = Localization.Get("Common_Lamha");

    public string Welcome { get; } = Localization.Get("FirstRun_Welcome");

    public string Hero { get; } = Localization.Get("FirstRun_Hero");

    public string UiLang { get; } = Localization.Get("Settings_UiLang");

    public string UiLangHint { get; } = Localization.Get("Settings_UiLangHint");

    /// <summary>Each language in its own name, as Windows lists them.</summary>
    public string Arabic { get; } = Localization.Get("FirstRun_Arabic");

    public string English { get; } = Localization.Get("FirstRun_English");

    public string TryTitle { get; } = Localization.Get("FirstRun_TryTitle");

    public string TryText { get; } = Localization.Get("FirstRun_TryText");

    public string TryOk { get; } = Localization.Get("FirstRun_TryOk");

    public string Sample { get; } = Localization.Get("FirstRun_Sample");

    public string HkWrite { get; } = Localization.Get("Settings_HkWrite");

    public string HkClip { get; } = Localization.Get("Settings_HkClip");

    public string CustomizeLabel { get; } = Localization.Get("FirstRun_Customize");

    public string WritingTitle { get; } = Localization.Get("Settings_WritingTools");

    public string AiText { get; } = Localization.Get("FirstRun_AiText");

    public string ReadyTitle { get; } = Localization.Get("FirstRun_ReadyTitle");

    public string ReadyText { get; } = Localization.Get("FirstRun_ReadyText");

    public string Login { get; } = Localization.Get("Tray_StartWithWindows");

    public string LoginHint { get; } = Localization.Get("Settings_LoginHint");

    public string Clip { get; } = Localization.Get("Clipboard_Enable");

    public string ClipHint { get; } = Localization.Get("Clipboard_OptInText");

    public string NextLabel { get; } = Localization.Get("FirstRun_Next");

    public string BackLabel { get; } = Localization.Get("FirstRun_Back");

    public string LaterLabel { get; } = Localization.Get("FirstRun_Later");

    public string StartLabel { get; } = Localization.Get("FirstRun_Start");
}
