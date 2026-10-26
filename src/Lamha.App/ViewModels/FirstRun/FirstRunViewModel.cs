using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.App.ViewModels.Main;
using Lamha.Core;
using Lamha.Core.Import;
using Lamha.Core.Settings;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.FirstRun;

/// <summary>First run's steps, in order. Lamha 2's data only when it's found.</summary>
public enum FirstRunStep
{
    Language,
    Lamha2,
    Try,
    Writing,
    Ready,
}

/// <summary>A step in the row at the top: its number (or a check once done) and its name.</summary>
public sealed record StepItem(string Number, string Label, bool IsDone, bool IsCurrent);

/// <summary>A row of the Lamha 2 step: what moves, how many (nothing for the settings), and whether it's the first (no line above).</summary>
public sealed record MoveItem(Glyph Glyph, string Label, string Count, bool IsFirst);

/// <summary>What the first-run window asks of the app.</summary>
public interface IFirstRunHost
{
    /// <summary>Step 1's choice: the interface language changes at once, so the rest is in it.</summary>
    void ChangeLanguage(string language);

    /// <summary>Customise shortcuts: Settings → Keyboard shortcuts in the main window.</summary>
    void OpenShortcuts();

    /// <summary>Start: the last step's two switches applied, and the window closes.</summary>
    void Finish(bool startWithWindows, bool clipboard);

    /// <summary>Lamha 2's data moved: remembered for the window made again in another language, and its theme applied.</summary>
    void Moved(Lamha2Summary summary);
}

/// <summary>
/// First run (phase 1's design, docs/design/first-run.html): the language, Lamha 2's data when it's found, a first
/// lookup by doing it, the writing tools (optional), and ready, one screen each. Nothing is chosen for the user: no AI
/// until they pick one, and clipboard history stays off unless they turn it on (Lamha 2's rule). Lamha 2's settings,
/// once moved, fill the later steps: the writing tools are skipped when they're set up.
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    private readonly FirstRunStep[] _order;
    private readonly Brain _brain;
    private readonly ShortcutService _shortcuts;
    private readonly IFirstRunHost _host;
    private readonly Lamha2Data? _lamha2;
    private AiProviderKind _providerBefore;

    /// <param name="brain">Lamha's brain: the settings, and the writing tools' setup.</param>
    /// <param name="shortcuts">The shortcuts, named as they are now (Customise shortcuts may change them).</param>
    /// <param name="host">The app: the language, Settings, and Start.</param>
    /// <param name="step">The step to open at: the first, or the language step again after the language changed.</param>
    /// <param name="startWithWindows">Whether Lamha starts with Windows now; first run turns it on unless the user says no.</param>
    /// <param name="lamha2">Lamha 2's data, when it was found: its step comes after the language.</param>
    /// <param name="moved">What moved, when it already has (the window made again in another language).</param>
    public FirstRunViewModel(Brain brain, ShortcutService shortcuts, IFirstRunHost host, FirstRunStep step, bool startWithWindows, Lamha2Data? lamha2 = null, Lamha2Summary? moved = null)
    {
        _brain = brain;
        _shortcuts = shortcuts;
        _host = host;
        _lamha2 = lamha2;
        _order = lamha2 is null
            ? [FirstRunStep.Language, FirstRunStep.Try, FirstRunStep.Writing, FirstRunStep.Ready]
            : [FirstRunStep.Language, FirstRunStep.Lamha2, FirstRunStep.Try, FirstRunStep.Writing, FirstRunStep.Ready];
        step = _order.Contains(step) ? step : FirstRunStep.Language;
        Lamha2Items = lamha2 is null ? [] : Items(Lamha2Import.Count(lamha2));
        if (moved is not null)
        {
            ShowMoved(moved);
        }

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
    [NotifyPropertyChangedFor(nameof(IsLanguage), nameof(IsLamha2), nameof(IsTry), nameof(IsWriting), nameof(IsReady), nameof(Steps), nameof(CanMove), nameof(ShowsNext))]
    public partial FirstRunStep Step { get; private set; }

    public bool IsLanguage => Step == FirstRunStep.Language;

    public bool IsLamha2 => Step == FirstRunStep.Lamha2;

    public bool IsTry => Step == FirstRunStep.Try;

    public bool IsWriting => Step == FirstRunStep.Writing;

    public bool IsReady => Step == FirstRunStep.Ready;

    /// <summary>The row at the top: every step, the ones before this one checked.</summary>
    public List<StepItem> Steps
    {
        get
        {
            var at = Array.IndexOf(_order, Step);
            return [.. _order.Select((step, i) => new StepItem(Localization.Number(i + 1), StepName(step), i < at, i == at))];
        }
    }

    // ----- 1. The language -----

    [ObservableProperty]
    public partial bool IsArabic { get; set; }

    [ObservableProperty]
    public partial bool IsEnglish { get; set; }

    partial void OnIsArabicChanged(bool value) => Choose(value, "ar");

    partial void OnIsEnglishChanged(bool value) => Choose(value, "en");

    // ----- Lamha 2's data -----

    /// <summary>What moves (then what moved): the deck, the history, the journal, the settings and keys, clipboard history.</summary>
    public List<MoveItem> Lamha2Items { get; }

    /// <summary>Lamha 2's data has moved: the step says so, and names what Lamha 3 doesn't have.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Lamha2Title), nameof(CanMove), nameof(ShowsNext))]
    public partial bool Moved { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMove))]
    public partial bool IsMoving { get; private set; }

    /// <summary>The move failed: nothing in Lamha 2's folder changed, and it can be tried again.</summary>
    [ObservableProperty]
    public partial bool MoveFailed { get; private set; }

    /// <summary>What Lamha 2 had that Lamha 3 doesn't, of what this user used; empty when nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDropped))]
    public partial List<string> Dropped { get; private set; } = [];

    public bool HasDropped => Dropped.Count > 0;

    /// <summary>A key couldn't be read here: it's asked for again.</summary>
    [ObservableProperty]
    public partial bool KeysLost { get; private set; }

    [ObservableProperty]
    public partial bool ClipsLost { get; private set; }

    public string Lamha2Title => Localization.Get(Moved ? "FirstRun_MovedTitle" : "FirstRun_Lamha2Title");

    /// <summary>Move my data and Start fresh: on the step, until it has moved.</summary>
    public bool CanMove => IsLamha2 && !Moved;

    /// <summary>Next: everywhere but Ready, and on the Lamha 2 step only once it has moved.</summary>
    public bool ShowsNext => !IsReady && !(IsLamha2 && !Moved);

    [RelayCommand]
    private async Task MoveAsync()
    {
        if (_lamha2 is null || IsMoving || Moved)
        {
            return;
        }

        IsMoving = true;
        MoveFailed = false;
        try
        {
            var summary = await _brain.MoveFromLamha2Async(_lamha2);
            var c = summary.Counts;
            AppLog.Info($"first run: Lamha 2's data moved ({c.Cards} cards, {c.History} words, {c.Mistakes} mistakes, {c.Clips} copies; " +
                $"keys: {(summary.Keys.Count > 0 ? string.Join(", ", summary.Keys) : "none")}{(summary.KeysLost ? ", some unreadable" : "")})");
            ShowMoved(summary);
            ClipboardOn = _brain.Settings.Current.ClipboardEnabled;
            _host.Moved(summary);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Whatever it was, Lamha 2's folder is unchanged and the step offers to try again or start fresh.
            AppLog.Error("first run: moving Lamha 2's data", ex);
            MoveFailed = true;
        }
        finally
        {
            IsMoving = false;
        }
    }

    /// <summary>Start fresh: Lamha 2's data stays where it is, untouched.</summary>
    [RelayCommand]
    private void Fresh()
    {
        AppLog.Info("first run: started fresh (Lamha 2's data left)");
        Go(FirstRunStep.Try);
    }

    private void ShowMoved(Lamha2Summary summary)
    {
        Dropped = [.. summary.Dropped.Select(d => Localization.Get(d == Lamha2Dropped.Firefox ? "FirstRun_DroppedFirefox" : "FirstRun_DroppedWikipedia"))];
        KeysLost = summary.KeysLost;
        ClipsLost = summary.ClipsLost;
        Moved = true;
    }

    /// <summary>The step's rows: what there is (a part with nothing in it isn't listed), and always the settings.</summary>
    private static List<MoveItem> Items(Lamha2Counts counts)
    {
        var items = new List<(Glyph Glyph, string Label, string Count)>();
        if (counts.Cards > 0)
        {
            items.Add((Glyph.Review, "FirstRun_Lamha2Cards", Counted("Review_Words", counts.Cards)));
        }

        if (counts.History > 0)
        {
            items.Add((Glyph.History, "FirstRun_Lamha2History", Counted("Review_Words", counts.History)));
        }

        if (counts.Mistakes > 0)
        {
            items.Add((Glyph.Book, "FirstRun_Lamha2Journal", Counted("FirstRun_Mistakes", counts.Mistakes)));
        }

        items.Add((Glyph.Settings, "FirstRun_Lamha2Settings", ""));
        if (counts.Clips > 0)
        {
            items.Add((Glyph.Clipboard, "FirstRun_Lamha2Clipboard", Counted("Settings_CbItems", counts.Clips)));
        }

        return [.. items.Select((item, i) => new MoveItem(item.Glyph, Localization.Get(item.Label), item.Count, i == 0))];
    }

    private static string Counted(string key, int n) => n switch
    {
        1 => Localization.Get(key + "One"),
        2 => Localization.Get(key + "Two"),
        >= 3 and <= 10 => Localization.Format(key + "Few", Localization.Number(n)),
        _ => Localization.Format(key + "Many", Localization.Number(n)),
    };

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
        var i = Array.IndexOf(_order, Step) + 1;
        if (i < _order.Length && Skipped(_order[i]))
        {
            i++;
        }

        if (i < _order.Length)
        {
            Go(_order[i]);
        }
    }

    [RelayCommand]
    private void Back()
    {
        var i = Array.IndexOf(_order, Step) - 1;
        if (i > 0 && Skipped(_order[i]))
        {
            i--;
        }

        if (i >= 0)
        {
            Go(_order[i]);
        }
    }

    /// <summary>The writing tools, once Lamha 2's settings have set them up.</summary>
    private bool Skipped(FirstRunStep step)
    {
        if (step != FirstRunStep.Writing || !Moved)
        {
            return false;
        }

        try
        {
            _brain.Ai.Config();
            return true;
        }
        catch (LamhaException)
        {
            return false; // no key or no model: the step offers the choice
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
        FirstRunStep.Lamha2 => "FirstRun_StepLamha2",
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

    public string Lamha2Text { get; } = Localization.Get("FirstRun_Lamha2Text");

    public string FolderStays { get; } = Localization.Get("FirstRun_FolderStays");

    public string NotInLamha3 { get; } = Localization.Get("FirstRun_NotInLamha3");

    public string KeysLostText { get; } = Localization.Get("FirstRun_KeysLost");

    public string ClipsLostText { get; } = Localization.Get("FirstRun_ClipsLost");

    public string MoveFailedText { get; } = Localization.Get("FirstRun_MoveFailed");

    public string MoveLabel { get; } = Localization.Get("FirstRun_Move");

    public string FreshLabel { get; } = Localization.Get("FirstRun_Fresh");

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
