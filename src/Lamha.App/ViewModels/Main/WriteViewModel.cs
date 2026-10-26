using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Services;
using Lamha.App.ViewModels.Card;
using Lamha.Core;
using Lamha.Core.Text;
using Lamha.Core.Writing;
using Microsoft.UI.Xaml;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.Main;

/// <summary>A writing tool under the draft: its name, whether it's the one shown, and whether it can run yet.</summary>
public sealed partial class DraftTool(WritingTool tool, string label, IRelayCommand pick) : ObservableObject
{
    public WritingTool Tool { get; } = tool;

    public string Label { get; } = label;

    public IRelayCommand Pick { get; } = pick;

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>There's a draft, and the writing tools are set up.</summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; }
}

/// <summary>
/// The main window's Write page (phase 1's design): Lamha 2's Write tab (popup.js's compose: <c>renderTools()</c>,
/// <c>runWrite()</c>, <c>renderWeak()</c>), the draft kept between visits, and the mistake journal under it, moved
/// from Lamha 2's Settings.
/// </summary>
public sealed partial class WriteViewModel : ObservableObject
{
    /// <summary>Where the draft is kept (Lamha 2's <c>draft</c>), so it's there next time.</summary>
    private const string DraftKey = "draft";

    /// <summary>The draft is saved after this pause in typing (Lamha 2's <c>draftTimer</c>).</summary>
    private static readonly TimeSpan SavePause = TimeSpan.FromMilliseconds(300);

    /// <summary>An English draft gets the editing tools; an Arabic one gets "In English" (Lamha 2's <c>WRITE_TOOLS</c>).</summary>
    private static readonly WritingTool[] EnglishTools =
        [WritingTool.Proofread, WritingTool.Improve, WritingTool.Formal, WritingTool.Friendly, WritingTool.Concise, WritingTool.Expand];

    private readonly Brain _brain;
    private readonly IPageHost _host;
    private CancellationTokenSource? _run;
    private CancellationTokenSource? _save;
    private WritingTool _tool = WritingTool.Proofread;
    private LamhaException? _notReady;
    private bool _arabic;

    /// <param name="brain">Lamha's brain: the writing tools, the journal, the saved draft.</param>
    /// <param name="host">The main window.</param>
    public WriteViewModel(Brain brain, IPageHost host)
    {
        _brain = brain;
        _host = host;
        Journal = new JournalViewModel(brain, host);
        Journal.Replaced += RefreshWeak;
        Tools = MakeTools(arabic: false);
        Draft = brain.State.Get(DraftKey) ?? "";
        _save?.Cancel(); // read, not typed: nothing to save
        _save = null;
    }

    // ----- the draft and its tools -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDraft))]
    public partial string Draft { get; set; } = "";

    public bool HasDraft => Draft.Trim().Length > 0;

    /// <summary>
    /// The tools for the draft's language: the first is the main button (Proofread, or "In English" for Arabic: what
    /// Ctrl+Enter runs), the rest are under Rewrite ▾ (the study's W2, the user's answer).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MainTool), nameof(RewriteTools), nameof(HasRewrite))]
    public partial List<DraftTool> Tools { get; private set; } = [];

    public DraftTool MainTool => Tools[0];

    public List<DraftTool> RewriteTools => [.. Tools.Skip(1)];

    public bool HasRewrite => Tools.Count > 1;

    /// <summary>
    /// No AI provider is set up yet: an InfoBar offers the setup right on the page (the study's W1), and the tools wait.
    /// </summary>
    [ObservableProperty]
    public partial bool NeedsSetup { get; private set; }

    /// <summary>A setup of the writing tools for the dialog Write opens (Settings → Writing tools' own).</summary>
    public WritingSetupViewModel NewSetup() => new(_brain, () => { });

    /// <summary>The draft's text was put there by Lamha (a result, a text from Translate), not typed.</summary>
    public event Action? DraftReplaced;

    // ----- under the tools -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorking), nameof(IsResult), nameof(IsProofread), nameof(IsNoMistakes), nameof(IsError), nameof(HasOutput))]
    public partial WritingView View { get; private set; }

    /// <summary>Something under the tools for the card that holds it: working, a result, or no mistakes.</summary>
    public bool HasOutput => IsWorking || IsResult || IsNoMistakes;

    public bool IsWorking => View == WritingView.Working;

    /// <summary>A text to use: a tool's, or the corrected one (with its actions under it).</summary>
    public bool IsResult => View is WritingView.Text or WritingView.Proofread;

    public bool IsProofread => View == WritingView.Proofread;

    public bool IsNoMistakes => View == WritingView.NoMistakes;

    public bool IsError => View == WritingView.Error;

    [ObservableProperty]
    public partial string Result { get; private set; } = "";

    [ObservableProperty]
    public partial FlowDirection ResultFlow { get; private set; }

    /// <summary>Proofreading: each mistake struck through and its fix (null: too long to compare, the text alone).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<DiffPart>? Diff { get; private set; }

    [ObservableProperty]
    public partial List<IssueItem> Issues { get; private set; } = [];

    [ObservableProperty]
    public partial string CorrectionsTitle { get; private set; } = "";

    [ObservableProperty]
    public partial ErrorInfo? Error { get; private set; }

    // ----- your most frequent mistake -----

    /// <summary>The kind's name ("Verb tenses"), or "" when there's none yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWeak))]
    public partial string WeakName { get; private set; } = "";

    /// <summary>" (7) — learn the rule →".</summary>
    [ObservableProperty]
    public partial string WeakLink { get; private set; } = "";

    public bool HasWeak => WeakName.Length > 0;

    public JournalViewModel Journal { get; }

    /// <summary>"Learn the rule": the journal is open at the kind, and the page should bring it into view.</summary>
    public event Action? JournalShown;

    /// <summary>The page shows: whether the writing tools are set up now, and the journal, are read again.</summary>
    public void Refresh()
    {
        _notReady = NotReady();
        NeedsSetup = _notReady is not null;
        if (NeedsSetup)
        {
            // Lamha 2's showSetup(): how to set one up as soon as the page opens, a first step rather than an error.
            Cancel();
            View = WritingView.None;
        }
        else if (View == WritingView.Error && Error?.Action != ErrorAction.Retry)
        {
            View = WritingView.None; // set up since: the tools wake up
        }

        UpdateTools();
        Journal.Refresh();
        RefreshWeak();
    }

    /// <summary>Ctrl+Enter in the draft: "In English" for Arabic, Proofread for English (Lamha 2's).</summary>
    public void RunDefault() => _ = RunAsync(_arabic ? WritingTool.ToEnglish : WritingTool.Proofread, fresh: false);

    /// <summary>A text from Translate's "Writing tools": in the draft, ready for a tool.</summary>
    public void Use(string text)
    {
        if (text.Trim().Length == 0)
        {
            return;
        }

        Cancel();
        Draft = text.Trim();
        if (_notReady is null)
        {
            View = WritingView.None;
        }

        DraftReplaced?.Invoke();
    }

    /// <summary>The window closed: a tool still running isn't wanted, and the draft is kept as it is.</summary>
    public void Unload()
    {
        Cancel();
        if (_save is not null)
        {
            _save.Cancel();
            _save = null;
            SaveDraft();
        }
    }

    // ----- commands -----

    /// <summary>"Replace": the result into the draft, to keep working on it (Lamha 2's <c>write.useIt</c>).</summary>
    [RelayCommand]
    private void UseResult()
    {
        Draft = Result;
        Mark(null);
        View = WritingView.None;
        DraftReplaced?.Invoke();
    }

    [RelayCommand]
    private void Copy()
    {
        _host.Copy(Result);
        _host.Toast(Localization.Get("Card_Copied"));
    }

    /// <summary>Asks again, not from the cache (Lamha 2's ↻).</summary>
    [RelayCommand]
    private void Again() => _ = RunAsync(_tool, fresh: true);

    /// <summary>The error's button: Try again, or Settings at the category that fixes it (Writing tools).</summary>
    [RelayCommand]
    private void RunErrorAction()
    {
        if (Error?.Action == ErrorAction.Retry)
        {
            _ = RunAsync(_tool, fresh: false);
        }
        else if (Error is { Action: ErrorAction.Settings } error)
        {
            _host.OpenSettings(error.SettingsSection);
        }
    }

    [RelayCommand]
    private void LearnRule()
    {
        if (_brain.Writing.MostFrequent() is { } top)
        {
            Journal.Open(top.Category);
            JournalShown?.Invoke();
        }
    }

    // ----- running a tool -----

    partial void OnDraftChanged(string value)
    {
        var arabic = Scripts.IsArabicText(value);
        if (arabic != _arabic)
        {
            _arabic = arabic;
            Tools = MakeTools(arabic);
        }

        UpdateTools();
        _save?.Cancel();
        _ = SaveAfterPauseAsync(_save = new CancellationTokenSource());
    }

    private async Task SaveAfterPauseAsync(CancellationTokenSource save)
    {
        try
        {
            await Task.Delay(SavePause, save.Token).ConfigureAwait(true);
            _save = null;
            SaveDraft();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SaveDraft() => _brain.State.Set(DraftKey, Draft.Length == 0 ? null : Draft);

    private List<DraftTool> MakeTools(bool arabic)
    {
        WritingTool[] tools = arabic ? [WritingTool.ToEnglish] : EnglishTools;
        return [.. tools.Select(t => new DraftTool(t, Label(t), new RelayCommand(() => _ = RunAsync(t, fresh: false))))];
    }

    /// <summary>The short names Lamha 2's Write tab used ("Improve", "In English"), the card's for the rest.</summary>
    private static string Label(WritingTool tool) => tool switch
    {
        WritingTool.Improve => Localization.Get("Tool_ImproveShort"),
        WritingTool.ToEnglish => Localization.Get("Tool_ToEnglishShort"),
        _ => Localization.Get("Tool_" + tool),
    };

    private void UpdateTools()
    {
        var enabled = HasDraft && _notReady is null;
        foreach (var tool in Tools)
        {
            tool.IsEnabled = enabled;
        }
    }

    private void Mark(WritingTool? tool)
    {
        foreach (var chip in Tools)
        {
            chip.IsOn = chip.Tool == tool;
        }
    }

    private void Cancel()
    {
        _run?.Cancel();
        _run = null;
    }

    private async Task RunAsync(WritingTool tool, bool fresh)
    {
        var text = Draft.Trim();
        if (text.Length == 0)
        {
            return;
        }

        _tool = tool;
        if (_notReady is not null)
        {
            return; // the InfoBar above the draft says what to do
        }

        Cancel();
        var run = _run = new CancellationTokenSource();
        Mark(tool);
        View = WritingView.Working;
        try
        {
            var result = await _brain.Writing.RunAsync(tool, text, new WritingOptions { Fresh = fresh }, run.Token).ConfigureAwait(true);
            if (run.IsCancellationRequested)
            {
                return;
            }

            if (tool == WritingTool.Proofread)
            {
                ShowProofread(result, text);
                Journal.Refresh();
                RefreshWeak();
            }
            else
            {
                ShowText(result.Text.Trim());
            }
        }
        catch (LamhaException e) when (!run.IsCancellationRequested)
        {
            ShowError(e);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is not OutOfMemoryException && !run.IsCancellationRequested)
        {
            // A fault of Lamha's own: said, and logged by its type, rather than working for ever.
            AppLog.Error("write", e);
            Error = ErrorText.Unexpected;
            View = WritingView.Error;
            _host.Announce(Error.Title);
        }
    }

    private void ShowText(string text)
    {
        Result = text;
        ResultFlow = Scripts.IsArabicText(text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Diff = null;
        View = WritingView.Text;
        _host.Announce(text);
    }

    /// <summary>The corrections against the draft as it was sent (Lamha 2: none, or the same text, is "No mistakes").</summary>
    private void ShowProofread(WritingResult result, string text)
    {
        var corrected = result.Text.Trim();
        if (result.Issues.Count == 0 || corrected.Length == 0 || corrected == text)
        {
            View = WritingView.NoMistakes;
            _host.Announce(Localization.Get("Card_NoMistakes"));
            return;
        }

        Result = corrected;
        ResultFlow = FlowDirection.LeftToRight;
        Diff = WordDiff.Compare(text, corrected);
        CorrectionsTitle = Localization.Format("Card_Corrections", result.Issues.Count);
        Issues = [.. result.Issues.Select(i => new IssueItem(
            i.Original,
            i.Fix,
            i.Category == MistakeCategories.Other || !MistakeCategories.IsKnown(i.Category) ? "" : Localization.Category(i.Category),
            i.Why,
            Scripts.IsArabicText(i.Why) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight))];
        View = WritingView.Proofread;
        _host.Announce(corrected);
    }

    private void ShowError(LamhaException error)
    {
        var settings = _brain.Settings.Current;
        Error = ErrorText.ForAi(error, settings.AiProvider, settings);
        View = WritingView.Error;
        _host.Announce(Error.Title);
    }

    private void RefreshWeak()
    {
        var top = _brain.Writing.MostFrequent();
        WeakName = top is { } t ? Localization.Category(t.Category) : "";
        WeakLink = top is { } n ? Localization.Format("Write_LearnRule", Localization.Number(n.Count)) : "";
    }

    /// <summary>Why the writing tools can't run yet ("ai_no_key"), or null when they're set up.</summary>
    private LamhaException? NotReady()
    {
        try
        {
            _brain.Ai.Config();
            return null;
        }
        catch (LamhaException e)
        {
            return e;
        }
    }
}
