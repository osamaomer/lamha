using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.App.ViewModels.Card;
using Lamha.Core;
using Lamha.Core.Clipboard;
using Lamha.Core.Text;
using Lamha.Core.Writing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.Main;

/// <summary>What a copy's tools show: nothing yet, working, a text, a proofread, "no mistakes", Review's line, an error.</summary>
public enum ClipResultView
{
    None,
    Working,
    Text,
    Proofread,
    NoMistakes,
    Done,
    Error,
}

/// <summary>
/// One of Lamha's tools on the chosen copy: a button on the Clipboard page; in the clipboard panel's menu also its
/// number (1–9 runs it), its icon and what it does in a few words (Lamha 2's clip-actions.js <c>HINTS</c>).
/// </summary>
public sealed record ClipTool(ClipAction Action, string Label, IRelayCommand Run)
{
    public string Number { get; init; } = "";

    public Glyph Glyph { get; init; }

    public string Hint { get; init; } = "";
}

/// <summary>
/// A copy in the list: its text (two lines), its name, an English copy's translation, the program and when; the chosen
/// one shows Lamha's tools, pin and delete, and a tool's result (Lamha 2's clip-list.js row and clip-actions.js).
/// </summary>
public sealed partial class ClipRow : ObservableObject
{
    public ClipRow(ClipPreview clip, IReadOnlyList<ClipTool> tools, ClipboardViewModel owner)
    {
        Id = clip.Id;
        Tools = [.. tools];
        Owner = owner;
        Update(clip);
    }

    public string Id { get; }

    /// <summary>The page, whose commands act on the chosen copy (pin, delete, copy, redo).</summary>
    public ClipboardViewModel Owner { get; }

    /// <summary>The clipboard panel's number for the copy, 1–9 (Ctrl+1…9 pastes it); "" past the ninth and on the page.</summary>
    [ObservableProperty]
    public partial string Number { get; set; } = "";

    /// <summary>What's searched for, marked in the copy's text and name.</summary>
    [ObservableProperty]
    public partial string Query { get; set; } = "";

    /// <summary>The list's first copy: no line above it.</summary>
    [ObservableProperty]
    public partial bool IsFirst { get; set; }

    /// <summary>The text as the copy shows it, and for the tools.</summary>
    public string FullText { get; private set; } = "";

    /// <summary>"ar", "en", "mixed" or "other" (Core's <c>ClipPreview.Lang</c>).</summary>
    public string Lang { get; private set; } = "";

    [ObservableProperty]
    public partial string Text { get; private set; } = "";

    [ObservableProperty]
    public partial FlowDirection TextFlow { get; private set; }

    [ObservableProperty]
    public partial string Label { get; private set; } = "";

    /// <summary>A name runs the way its own language does, as the text does (Lamha 2's <c>dir="auto"</c>).</summary>
    [ObservableProperty]
    public partial FlowDirection LabelFlow { get; private set; }

    /// <summary>The line under the text: an English copy's translation, or an Arabic copy's English.</summary>
    [ObservableProperty]
    public partial string Translation { get; private set; } = "";

    [ObservableProperty]
    public partial FlowDirection TranslationFlow { get; private set; }

    /// <summary>"Notepad · 5 minutes ago".</summary>
    [ObservableProperty]
    public partial string Meta { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinTooltip))]
    public partial bool Pinned { get; private set; }

    public string PinTooltip => Localization.Get(Pinned ? "Clipboard_UnpinKey" : "Clipboard_PinKey");

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The tools that fit the copy: made again when Replace changed its language.</summary>
    [ObservableProperty]
    public partial List<ClipTool> Tools { get; set; }

    // The row's own words (a row's template sees only the row).
    public string PinnedName { get; } = Localization.Get("Clipboard_Pinned");

    public string DeleteTooltip { get; } = Localization.Get("Clipboard_DeleteKey");

    public string SummaryLangName { get; } = Localization.Get("Clipboard_SummaryLang");

    public string SummaryLangColon { get; } = Localization.Get("Clipboard_SummaryLangColon");

    public string CopyLabel { get; } = Localization.Get("Common_Copy");

    public string RedoLabel { get; } = Localization.Get("Clipboard_Redo");

    public string ReplaceLabel { get; } = Localization.Get("Card_Replace");

    public string ReplaceTooltip { get; } = Localization.Get("Clipboard_ReplaceTitle");

    public string NewResult { get; } = Localization.Get("Clipboard_NewResult");

    public string CachedNote { get; } = Localization.Get("Clipboard_Cached");

    public string NoMistakes { get; } = Localization.Get("Card_NoMistakes");

    // ----- a tool's result -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsWorking), nameof(IsResult), nameof(IsProofread), nameof(IsNoMistakes), nameof(IsDone), nameof(IsError), nameof(CanReplace))]
    public partial ClipResultView View { get; set; }

    /// <summary>No tool has answered yet: the clipboard panel shows its menu of tools.</summary>
    public bool IsIdle => View == ClipResultView.None;

    public bool IsWorking => View == ClipResultView.Working;

    /// <summary>A text to copy: a translation, the English, a summary, or the corrected text.</summary>
    public bool IsResult => View is ClipResultView.Text or ClipResultView.Proofread;

    public bool IsProofread => View == ClipResultView.Proofread;

    public bool IsNoMistakes => View == ClipResultView.NoMistakes;

    public bool IsDone => View == ClipResultView.Done;

    public bool IsError => View == ClipResultView.Error;

    /// <summary>The result can take the copy's place: Proofread's corrections, or its English.</summary>
    public bool CanReplace => IsResult && ClipActions.CanReplace(Action);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReplace))]
    public partial ClipAction Action { get; set; }

    [ObservableProperty]
    public partial string Result { get; set; } = "";

    [ObservableProperty]
    public partial FlowDirection ResultFlow { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<DiffPart>? Diff { get; set; }

    [ObservableProperty]
    public partial List<IssueItem> Issues { get; set; } = [];

    [ObservableProperty]
    public partial string CorrectionsTitle { get; set; } = "";

    /// <summary>"Saved result — Redo asks for a new one".</summary>
    [ObservableProperty]
    public partial bool Cached { get; set; }

    /// <summary>The summary's language switch shows (العربية | English), and which is on.</summary>
    [ObservableProperty]
    public partial bool IsSummary { get; set; }

    [ObservableProperty]
    public partial bool SummaryInEnglish { get; set; }

    /// <summary>Review's line ("Added “bank” to review 🔖") and the word's meaning under it.</summary>
    [ObservableProperty]
    public partial string DoneText { get; set; } = "";

    [ObservableProperty]
    public partial string DoneMeaning { get; set; } = "";

    [ObservableProperty]
    public partial ErrorInfo? Error { get; set; }

    /// <summary>A fresh look at the copy (a new copy of the same text moved it up, a pin, a name).</summary>
    public void Update(ClipPreview clip)
    {
        FullText = clip.Text;
        Lang = clip.Lang;
        Text = clip.Text.TrimStart()[..Math.Min(clip.Text.TrimStart().Length, 300)];
        TextFlow = clip.Lang == "ar" || (clip.Lang == "mixed" && Scripts.IsArabicText(clip.Text)) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Label = clip.Label;
        LabelFlow = Scripts.IsArabicText(clip.Label) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Translation = clip.Translation;
        TranslationFlow = Scripts.IsArabicText(clip.Translation) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Pinned = clip.Pinned;
        Meta = $"{AppName(clip.SourceApp)} · {Ago(Math.Max(clip.LastCopiedAt, clip.LastUsedAt))}";
    }

    /// <summary>"Notepad" for notepad.exe; Lamha's own copies, and a program Windows didn't name (Lamha 2's <c>appName</c>).</summary>
    private static string AppName(string app) => app switch
    {
        "lamha" => Localization.Get("Clipboard_AppLamha"),
        "unknown" => Localization.Get("Clipboard_AppUnknown"),
        _ => app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? app[..^4] : app,
    };

    /// <summary>"قبل ٥ دقائق", "5 minutes ago", Yesterday, then the date (Lamha 2's <c>relTime</c>).</summary>
    private static string Ago(long at)
    {
        var seconds = Math.Max(0, (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - at) / 1000);
        if (seconds < 60)
        {
            return Localization.Get("Clipboard_Now");
        }

        var minutes = seconds / 60;
        if (minutes < 60)
        {
            return Counted("Clipboard_MinAgo", minutes);
        }

        var hours = minutes / 60;
        if (hours < 24)
        {
            return Counted("Clipboard_HourAgo", hours);
        }

        var days = hours / 24;
        return days == 1 ? Localization.Get("Clipboard_Yesterday")
            : days < 30 ? Counted("Clipboard_DayAgo", days)
            : Localization.FullDate(DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(at).LocalDateTime));
    }

    /// <summary>Lamha 2's <c>arCount</c>: one, two, 3 to 10, and the rest each have their own words in Arabic.</summary>
    private static string Counted(string key, long n) => n switch
    {
        1 => Localization.Get(key + "One"),
        2 => Localization.Get(key + "Two"),
        >= 3 and <= 10 => Localization.Format(key + "Few", Localization.Number(n)),
        _ => Localization.Format(key + "Many", Localization.Number(n)),
    };
}

/// <summary>
/// The main window's Clipboard page (phase 1's design): search, All / Pinned, pause; the history, newest first with
/// pinned copies on top; and on the chosen copy, Lamha's tools that fit it, pin and delete (Lamha 2's clipboard tab,
/// clip-list.js and clip-actions.js). Off until the user turns clipboard history on; a banner while it's paused. The
/// clipboard panel's list is the same, in its panel mode: the copies numbered 1–9, and Paste as plain text among the
/// tools (Lamha 2's <c>mode: "panel"</c>).
/// </summary>
public sealed partial class ClipboardViewModel : ObservableObject
{
    /// <summary>Copies shown at a time (Lamha 2's <c>PAGE</c>).</summary>
    private const int Page = 100;

    /// <summary>The summary's language, kept for next time (Lamha 2's <c>clipboardSummaryLang</c>).</summary>
    private const string SummaryLangKey = "clipboardSummaryLang";

    private readonly Brain _brain;
    private readonly ClipboardService _clipboard;
    private readonly IClipHost _host;
    private readonly bool _panel;
    private readonly DispatcherQueue _queue;
    private readonly Dictionary<string, ClipRow> _rows = [];
    private int _shown = Page;
    private bool _attached;
    private bool _reloading;
    private bool _reloadAgain;
    private CancellationTokenSource? _run;

    /// <summary>After a delete, the copy that took its place is chosen.</summary>
    private int _chooseAt = -1;

    /// <summary>A fresh list starts on the newest copy, below the pinned ones (Lamha 2's).</summary>
    private bool _toNewest = true;

    /// <param name="brain">Lamha's brain: clipboard history and the tools.</param>
    /// <param name="clipboard">Clipboard history's switch and pause.</param>
    /// <param name="host">The main window, or the clipboard panel.</param>
    /// <param name="panel">The clipboard panel's list: numbered copies, and Paste as plain text.</param>
    public ClipboardViewModel(Brain brain, ClipboardService clipboard, IClipHost host, bool panel = false)
    {
        _brain = brain;
        _clipboard = clipboard;
        _host = host;
        _panel = panel;
        _queue = DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>The clipboard panel's Paste as plain text, on this copy.</summary>
    public event Action<ClipRow>? PlainRequested;

    /// <summary>
    /// A copy's tools or result are about to go (another copy chosen, a result redrawn, the list read again): the page
    /// takes the keyboard focus off a button that's going, or WinUI moves it to the search box and scrolls up to it.
    /// </summary>
    public event Action? ButtonsLeaving;

    /// <summary>Clipboard history is on (the setting); off, the page offers to turn it on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOff))]
    public partial bool IsOn { get; private set; }

    public bool IsOff => !IsOn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    public partial bool IsPaused { get; private set; }

    public bool CanPause => IsOn && !IsPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuery))]
    public partial string Query { get; set; } = "";

    public bool HasQuery => Query.Length > 0;

    [ObservableProperty]
    public partial bool PinnedOnly { get; set; }

    [ObservableProperty]
    public partial List<ClipRow> Rows { get; private set; } = [];

    [ObservableProperty]
    public partial bool HasMore { get; private set; }

    /// <summary>"Nothing here yet", "No results", "Nothing pinned yet": "" while there are rows.</summary>
    [ObservableProperty]
    public partial string Empty { get; private set; } = "";

    [ObservableProperty]
    public partial ClipRow? Selected { get; private set; }

    /// <summary>The page shows: on or off, paused or not, and the list read again; it follows new copies while open.</summary>
    public void Attach()
    {
        _attached = true;
        _brain.ClipboardHistory.Changed += OnHistoryChanged;
        _clipboard.PauseChanged += OnPauseChanged;
        _brain.Settings.Changed += OnSettingsChanged;
        OnPauseChanged();
        IsOn = _clipboard.IsOn;
        Reload();
    }

    /// <summary>
    /// The page goes (another page, or the window closes): no more following, and the decrypted copies kept for
    /// searching are let go, so nothing of what the user copied stays in memory (CLAUDE.md's privacy rules).
    /// </summary>
    public void Detach()
    {
        _attached = false;
        _brain.ClipboardHistory.Changed -= OnHistoryChanged;
        _clipboard.PauseChanged -= OnPauseChanged;
        _brain.Settings.Changed -= OnSettingsChanged;
        _run?.Cancel();
        _rows.Clear();
        Rows = [];
        Selected = null;
        _brain.ClipboardHistory.Unload();
    }

    /// <summary>
    /// The clipboard panel opens afresh (Lamha 2's <c>reset()</c>): no search, All, the first page, and the newest
    /// copy chosen. Before <see cref="Attach"/>, which reads the list.
    /// </summary>
    public void Reset()
    {
        Query = "";
        PinnedOnly = false;
        _shown = Page;
        _chooseAt = -1;
        _toNewest = true;
    }

    /// <summary>↑ / ↓ (and Page Up / Down by six) choose another copy.</summary>
    public void Move(int by)
    {
        if (Rows.Count == 0)
        {
            return;
        }

        var at = Selected is null ? -1 : Rows.IndexOf(Selected);
        Choose(Rows[Math.Clamp(at + by, 0, Rows.Count - 1)]);
    }

    /// <summary>The copy's tools show again, without a result; a tool still working is let go.</summary>
    public void Dismiss(ClipRow row)
    {
        ButtonsLeaving?.Invoke();
        _run?.Cancel();
        row.View = ClipResultView.None;
    }

    /// <summary>A click on a copy: it's chosen, with its tools.</summary>
    public void Choose(ClipRow row)
    {
        if (Selected == row)
        {
            return;
        }

        ButtonsLeaving?.Invoke();
        _run?.Cancel();
        if (Selected is { } old)
        {
            old.IsSelected = false;
            old.View = ClipResultView.None;
        }

        Selected = row;
        row.IsSelected = true;
    }

    // ----- commands -----

    [RelayCommand]
    private void TurnOn() => _clipboard.SetOn(true);

    [RelayCommand]
    private void Pause(string minutes) => _clipboard.Pause(minutes == "0" ? null : TimeSpan.FromMinutes(int.Parse(minutes, CultureInfo.InvariantCulture)));

    [RelayCommand]
    private void Resume() => _clipboard.Resume();

    /// <summary>All, or Pinned only.</summary>
    [RelayCommand]
    private void Filter(string pinned) => PinnedOnly = pinned == "true";

    [RelayCommand]
    private void ShowMore()
    {
        _shown += Page;
        Reload();
    }

    /// <summary>Ctrl+P, or the pin on the chosen copy.</summary>
    [RelayCommand]
    private void TogglePin()
    {
        if (Selected is not { } row)
        {
            return;
        }

        try
        {
            _brain.ClipboardHistory.SetPinned(row.Id, !row.Pinned);
            _host.Toast(Localization.Get(row.Pinned ? "Clipboard_Unpinned" : "Clipboard_PinnedToast"));
        }
        catch (LamhaException e)
        {
            _host.Toast(Localization.Get(e.Code == ClipboardHistory.PinLimit ? "Clipboard_PinLimit" : "Clipboard_PinFailed"));
        }
    }

    /// <summary>Delete: the copy goes, with Undo for a few seconds (Lamha 2's).</summary>
    [RelayCommand]
    private void Delete()
    {
        if (Selected is not { } row)
        {
            return;
        }

        var next = Rows.IndexOf(row);
        if (!_brain.ClipboardHistory.Remove(row.Id))
        {
            return;
        }

        _rows.Remove(row.Id);
        Selected = null;
        _chooseAt = next;
        _host.Toast(Localization.Get("Clipboard_Deleted"), Localization.Get("Common_Undo"), () =>
        {
            try
            {
                _brain.ClipboardHistory.Restore(row.Id);
            }
            catch (LamhaException)
            {
                // Gone for good meanwhile (cleared, expired): nothing to bring back.
            }
        });
    }

    [RelayCommand]
    private void CopyResult()
    {
        if (Selected is { Result.Length: > 0 } row)
        {
            _host.Copy(row.Result);
            _host.Toast(Localization.Get("Clipboard_Copied"));
        }
    }

    /// <summary>
    /// Replace: Proofread's corrections, or the English, take the chosen copy's place, and go on the clipboard for the
    /// next paste (not recorded again: the copy is there already). Undo, for a few seconds, puts both back.
    /// </summary>
    [RelayCommand]
    private void ReplaceResult()
    {
        if (Selected is not { CanReplace: true } row)
        {
            return;
        }

        ClipReplaced replaced;
        try
        {
            replaced = _brain.ClipActions.Replace(row.Id, row.Action);
        }
        catch (LamhaException)
        {
            _host.Toast(Localization.Get("Note_ReplaceFailed"));
            return;
        }

        Dismiss(row);
        CopyUnrecorded(replaced.Text);
        _host.Toast(Localization.Get("Clipboard_Replaced"), Localization.Get("Common_Undo"), () =>
        {
            if (_brain.ClipboardHistory.UndoReplace(replaced))
            {
                CopyUnrecorded(replaced.Before.Text);
            }
        });
    }

    /// <summary>Redo: asks again instead of the copy's saved result.</summary>
    [RelayCommand]
    private void Redo()
    {
        if (Selected is { } row)
        {
            _ = RunAsync(row, row.Action, fresh: true, row.IsSummary ? (row.SummaryInEnglish ? "en" : "ar") : null);
        }
    }

    /// <summary>The summary in the other language (each is kept), remembered for next time.</summary>
    [RelayCommand]
    private void SummaryIn(string lang)
    {
        _brain.State.Set(SummaryLangKey, lang);
        if (Selected is { } row && row.SummaryInEnglish != (lang == "en"))
        {
            _ = RunAsync(row, ClipAction.Summary, fresh: false, lang);
        }
    }

    [RelayCommand]
    private void RunErrorAction()
    {
        if (Selected is { Error.Action: ErrorAction.Retry } row)
        {
            _ = RunAsync(row, row.Action, fresh: false, row.IsSummary ? (row.SummaryInEnglish ? "en" : "ar") : null);
        }
        else if (Selected is { Error: { Action: ErrorAction.Settings } error })
        {
            _host.OpenSettings(error.SettingsSection);
        }
    }

    partial void OnQueryChanged(string value)
    {
        _shown = Page;
        _toNewest = value.Length == 0;
        _chooseAt = value.Length == 0 ? -1 : 0; // what's typed chooses its first match (Lamha 2's sel = 0)
        Reload();
    }

    partial void OnPinnedOnlyChanged(bool value)
    {
        _shown = Page;
        _toNewest = true;
        Reload();
    }

    // ----- the list -----

    private void OnHistoryChanged(object? sender, EventArgs e) => _queue.TryEnqueue(Reload);

    private void OnPauseChanged() => _queue.TryEnqueue(() => IsPaused = _clipboard.PausedUntil is not null);

    private void OnSettingsChanged(object? sender, Lamha.Core.Settings.LamhaSettings s) => _queue.TryEnqueue(() =>
    {
        if (IsOn != s.ClipboardEnabled)
        {
            IsOn = s.ClipboardEnabled;
            Reload();
        }
    });

    /// <summary>Reads the page of copies again; changes while it reads collapse into one more read (Lamha 2's).</summary>
    private void Reload()
    {
        if (!_attached)
        {
            return; // read when the page or the panel shows
        }

        if (_reloading)
        {
            _reloadAgain = true;
            return;
        }

        _reloading = true;
        try
        {
            do
            {
                _reloadAgain = false;
                Read();
            }
            while (_reloadAgain);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            AppLog.Error("clipboard page", e);
        }
        finally
        {
            _reloading = false;
        }
    }

    private void Read()
    {
        if (!IsOn)
        {
            Rows = [];
            Selected = null;
            Empty = "";
            HasMore = false;
            return;
        }

        var page = _brain.ClipboardHistory.List(Query.Trim(), PinnedOnly, _shown, 0, _brain.Settings.Current.TargetLang);
        var rows = new List<ClipRow>(page.Items.Count);
        foreach (var clip in page.Items)
        {
            // The same row object for the same copy, so a tool's result stays while new copies come in.
            if (_rows.TryGetValue(clip.Id, out var row))
            {
                if (row.Lang != clip.Lang)
                {
                    var same = row;
                    row.Tools = ToolsFor(clip, () => same); // replaced by its English: an English copy's tools now
                }

                row.Update(clip);
            }
            else
            {
                row = _rows[clip.Id] = NewRow(clip);
            }

            rows.Add(row);
        }

        foreach (var gone in _rows.Keys.Except(rows.Select(r => r.Id)).ToList())
        {
            _rows.Remove(gone);
        }

        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].IsFirst = i == 0;
            rows[i].Query = Query.Trim();
            rows[i].Number = _panel && i < 9 ? (i + 1).ToString(CultureInfo.InvariantCulture) : "";
        }

        // The same copies in the same order (a result kept, a pin, a Replace) keep the list as it is: each row changed in
        // place. A new list redraws every row, and the focus with them.
        if (!rows.Select(r => r.Id).SequenceEqual(Rows.Select(r => r.Id), StringComparer.Ordinal))
        {
            ButtonsLeaving?.Invoke();
            Rows = rows;
        }

        HasMore = rows.Count < page.Total;
        Empty = rows.Count > 0 ? "" : Localization.Get(Query.Trim().Length > 0 ? "Clipboard_NoResults" : PinnedOnly ? "Clipboard_NoPinned" : "Clipboard_Empty");

        var keep = Selected is { } s && rows.Contains(s) ? s : null;
        if (_chooseAt >= 0 && rows.Count > 0)
        {
            keep = rows[Math.Min(_chooseAt, rows.Count - 1)];
        }
        else if (_toNewest || keep is null)
        {
            // Pinned copies sit on top, but the list starts on the newest copy (Lamha 2: Enter then pastes what was just copied).
            keep = keep is not null && !_toNewest ? keep : rows.FirstOrDefault(r => !r.Pinned) ?? rows.FirstOrDefault();
        }

        _chooseAt = -1;
        _toNewest = false;
        if (keep is null)
        {
            Selected = null;
        }
        else
        {
            Choose(keep);
        }
    }

    private ClipRow NewRow(ClipPreview clip)
    {
        ClipRow? row = null;
        row = new ClipRow(clip, ToolsFor(clip, () => row!), this);
        return row;
    }

    /// <summary>The tools that fit the copy, each running on its row (asked for when it runs: the row is made after its tools).</summary>
    private List<ClipTool> ToolsFor(ClipPreview clip, Func<ClipRow> row) => ClipActions.Available(clip.Text, clip.Lang, clip.Length, _panel)
        .Select((a, i) => new ClipTool(a, Localization.Get("Clipboard_" + a), new RelayCommand(() => _ = RunAsync(row(), a, fresh: false, null)))
        {
            Number = _panel && i < 9 ? Localization.Number(i + 1) : "",
            Glyph = ToolGlyph(a),
            Hint = _panel ? ToolHint(a) : "",
        })
        .ToList();

    /// <summary>
    /// Replace's text (and Undo's) on the clipboard, as the user's Copy is, but not recorded again in clipboard history,
    /// where the copy itself has it.
    /// </summary>
    private void CopyUnrecorded(string text) => _ = _clipboard.Monitor.SuppressAsync(() =>
    {
        _host.Copy(text);
        return Task.FromResult(true);
    });

    /// <summary>Each tool's icon (Lamha 2's clip-list.js <c>ICONS</c>).</summary>
    private static Glyph ToolGlyph(ClipAction action) => action switch
    {
        ClipAction.Translate => Glyph.Translate,
        ClipAction.English => Glyph.Sparkle,
        ClipAction.Proofread => Glyph.Check,
        ClipAction.Summary => Glyph.List,
        ClipAction.Lookup => Glyph.Search,
        ClipAction.Review => Glyph.Bookmark,
        _ => Glyph.Text,
    };

    /// <summary>What a tool does, in a few words: "Into Arabic", "In the dictionary" (Lamha 2's <c>HINTS</c>, <c>hintFor()</c>).</summary>
    private string ToolHint(ClipAction action) => action == ClipAction.Translate
        ? Localization.Format("Panel_HintTranslate", Localization.LanguageName(_brain.Settings.Current.TargetLang))
        : Localization.Get("Panel_Hint" + action);

    /// <summary>
    /// A tool on the copy: Look up opens it on Translate (the card, from the panel); Paste as plain text is the panel's;
    /// the others answer under the tools (Lamha 2's <c>run()</c> in clip-actions.js). Another tool or another copy
    /// chosen meanwhile wins.
    /// </summary>
    private async Task RunAsync(ClipRow row, ClipAction action, bool fresh, string? lang)
    {
        if (action == ClipAction.Lookup)
        {
            _host.Translate(row.FullText.Trim());
            return;
        }

        if (action == ClipAction.Plain)
        {
            PlainRequested?.Invoke(row);
            return;
        }

        if (action == ClipAction.Summary && lang is null)
        {
            lang = _brain.State.Get(SummaryLangKey) is "en" or "ar" ? _brain.State.Get(SummaryLangKey) : Localization.Language;
        }

        ButtonsLeaving?.Invoke();
        _run?.Cancel();
        var run = _run = new CancellationTokenSource();
        row.Action = action;
        row.View = ClipResultView.Working;
        try
        {
            var result = await _brain.ClipActions.RunAsync(row.Id, action, fresh, lang, run.Token).ConfigureAwait(true);
            if (!run.IsCancellationRequested && Selected == row)
            {
                Show(row, result);
            }
        }
        catch (LamhaException e) when (!run.IsCancellationRequested && Selected == row)
        {
            ShowError(row, action, e);
        }
        catch (LamhaException)
        {
            // Another copy was chosen meanwhile: its tools are what shows now.
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is not OutOfMemoryException && !run.IsCancellationRequested)
        {
            AppLog.Error("clipboard tool", e);
            row.Error = ErrorText.Unexpected;
            row.View = ClipResultView.Error;
        }
    }

    private void Show(ClipRow row, ClipActionResult result)
    {
        row.Cached = result.Cached;
        row.IsSummary = result.Action == ClipAction.Summary;
        row.SummaryInEnglish = result.Lang == "en";
        if (result.Action == ClipAction.Review)
        {
            row.DoneText = Localization.Format(result.InReview ? "Clipboard_AddedReview" : "Clipboard_RemovedReview", result.Word);
            row.DoneMeaning = result.Text;
            row.View = ClipResultView.Done;
            _host.Announce(row.DoneText);
            _host.ReviewChanged();
            return;
        }

        if (result.Action == ClipAction.Proofread)
        {
            var original = row.FullText.Trim();
            var corrected = result.Text.Trim();
            if (result.Issues.Count == 0 || corrected.Length == 0 || corrected == original)
            {
                row.View = ClipResultView.NoMistakes;
                _host.Announce(Localization.Get("Card_NoMistakes"));
                return;
            }

            row.Result = corrected;
            row.ResultFlow = FlowDirection.LeftToRight;
            row.Diff = WordDiff.Compare(original, corrected);
            row.CorrectionsTitle = Localization.Format("Card_Corrections", result.Issues.Count);
            row.Issues = [.. result.Issues.Select(i => new IssueItem(
                i.Original,
                i.Fix,
                i.Category == MistakeCategories.Other || !MistakeCategories.IsKnown(i.Category) ? "" : Localization.Category(i.Category),
                i.Why,
                Scripts.IsArabicText(i.Why) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight))];
            row.View = ClipResultView.Proofread;
            _host.Announce(corrected);
            return;
        }

        row.Result = result.Text;
        row.ResultFlow = Scripts.IsArabicText(result.Text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        row.Diff = null;
        row.View = ClipResultView.Text;
        _host.Announce(result.Text);
    }

    /// <summary>The error in Lamha's words: the writing tools' for the AI's tools, the lookup's for Translate and Review.</summary>
    private void ShowError(ClipRow row, ClipAction action, LamhaException error)
    {
        var s = _brain.Settings.Current;
        row.Error = error.Code is ClipActions.EmptyResult or ClipActions.NoTranslation
            ? new ErrorInfo(Localization.Get("Clipboard_ErrNoTranslation"), Localization.Get("Clipboard_ErrNoTranslationHint"), ErrorAction.None)
            : action is ClipAction.English or ClipAction.Proofread or ClipAction.Summary
                ? ErrorText.ForAi(error, s.AiProvider, s)
                : ErrorText.ForLookup(error, s.TrProvider ?? s.AiProvider, s, !_brain.Network.IsOffline);
        row.View = ClipResultView.Error;
        _host.Announce(row.Error.Title);
    }
}
