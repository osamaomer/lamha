using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.ViewModels.Main;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.Panel;

/// <summary>What the clipboard panel asks of the app: to paste, and to close.</summary>
public interface IClipPanelHost
{
    /// <summary>
    /// The copy into the program that was in front, with its formatting unless <paramref name="plain"/>; the panel
    /// closes first. Without a program to paste into (opened from the tray) it's only copied.
    /// </summary>
    void Paste(ClipRow row, bool plain);

    /// <summary>A tool's result into that program, after the user saw it (Lamha 2's <c>pasteResult</c>).</summary>
    void PasteResult(ClipRow row, string text);

    void Close();
}

/// <summary>
/// The clipboard quick panel (phase 1's design, Lamha 2's panel.js): clipboard history's list in its panel mode, where
/// Enter pastes; Tab opens Lamha's tools on the chosen copy, and a tool's result shows before it's pasted. Off and
/// paused as the list shows them.
/// </summary>
public sealed partial class ClipPanelViewModel : ObservableObject
{
    private static readonly TimeSpan ToastTime = TimeSpan.FromMilliseconds(1600);
    private static readonly TimeSpan ToastActionTime = TimeSpan.FromSeconds(5);

    private readonly IClipPanelHost _host;
    private CancellationTokenSource? _toast;
    private Action? _toastRun;

    /// <param name="list">Clipboard history's list, in its panel mode.</param>
    /// <param name="host">The panel's service: pasting and closing.</param>
    public ClipPanelViewModel(ClipboardViewModel list, IClipPanelHost host)
    {
        List = list;
        _host = host;
        List.PlainRequested += row => _host.Paste(row, plain: true);
    }

    public ClipboardViewModel List { get; }

    /// <summary>The copy whose tools are open (Tab), or null for the list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTools), nameof(IsList), nameof(ToolsText), nameof(ToolsTextFlow), nameof(ToolsMeta), nameof(ToolsRange))]
    public partial ClipRow? ToolsRow { get; private set; }

    /// <summary>"1–5" ("١–٥"), the tools' numbers at the foot.</summary>
    public string ToolsRange => $"{Localization.Number(1)}–{Localization.Number(Math.Clamp(ToolsRow?.Tools.Count ?? 1, 1, 9))}";

    public bool IsTools => ToolsRow is not null;

    /// <summary>The list: shown while clipboard history is on and no copy's tools are open.</summary>
    public bool IsList => ToolsRow is null;

    /// <summary>The copy at the top of its tools: its name, or its text (Lamha 2's <c>clip.label || clip.text</c>).</summary>
    public string ToolsText => ToolsRow is not { } row ? "" : row.Label.Length > 0 ? row.Label : row.FullText.Trim()[..Math.Min(row.FullText.Trim().Length, 400)];

    public Microsoft.UI.Xaml.FlowDirection ToolsTextFlow => ToolsRow is not { } row ? default : row.Label.Length > 0 ? row.LabelFlow : row.TextFlow;

    /// <summary>"Word · 5 minutes ago · English".</summary>
    public string ToolsMeta => ToolsRow is not { } row ? "" : row.Lang is "ar" or "en" ? $"{row.Meta} · {Localization.LanguageName(row.Lang)}" : row.Meta;

    [ObservableProperty]
    public partial string Toast { get; private set; } = "";

    /// <summary>The toast's button ("Undo"), or "".</summary>
    [ObservableProperty]
    public partial string ToastAction { get; private set; } = "";

    /// <summary>Whether the toast shows; its words stay while it fades out (Controls/Reveal).</summary>
    [ObservableProperty]
    public partial bool IsToastShown { get; private set; }

    /// <summary>The panel opens: a fresh list, read now (Lamha 2's <c>show()</c> and <c>reset()</c>).</summary>
    public void Open()
    {
        ToolsRow = null;
        HideToast();
        List.Reset();
        List.Attach();
    }

    /// <summary>The panel closes: what was decrypted for the list goes from memory.</summary>
    public void Leave()
    {
        ToolsRow = null;
        _toast?.Cancel();
        HideToast();
        List.Detach();
    }

    /// <summary>Shows <paramref name="text"/> at the panel's foot, with <paramref name="action"/> to <paramref name="run"/> if given.</summary>
    public async void ShowToast(string text, string? action = null, Action? run = null)
    {
        _toast?.Cancel();
        var mine = _toast = new CancellationTokenSource();
        Toast = text;
        ToastAction = run is null ? "" : action ?? "";
        _toastRun = run;
        IsToastShown = true;
        try
        {
            await Task.Delay(run is null ? ToastTime : ToastActionTime, mine.Token).ConfigureAwait(true);
            HideToast();
        }
        catch (OperationCanceledException)
        {
            // A newer toast took its place.
        }
    }

    /// <summary>Enter (Shift+Enter: as plain text), or a click on a copy.</summary>
    public void Paste(ClipRow row, bool plain) => _host.Paste(row, plain);

    /// <summary>Ctrl+1…9: the copy with that number.</summary>
    public void PasteAt(int index, bool plain)
    {
        if (index < List.Rows.Count)
        {
            List.Choose(List.Rows[index]);
            _host.Paste(List.Rows[index], plain);
        }
    }

    /// <summary>Tab (or → with nothing searched): the chosen copy's tools.</summary>
    public void OpenTools()
    {
        if (List.Selected is { } row)
        {
            List.Dismiss(row);
            ToolsRow = row;
        }
    }

    /// <summary>1…9 in the tools: that tool.</summary>
    public void RunTool(int index)
    {
        if (ToolsRow is { } row && row.IsIdle && index < row.Tools.Count)
        {
            row.Tools[index].Run.Execute(null);
        }
    }

    /// <summary>
    /// Esc in the tools: from a result back to the tools (a tool still working is let go), from the tools back to the
    /// list. True when it went back to the list.
    /// </summary>
    public bool Back()
    {
        if (ToolsRow is not { } row)
        {
            return true;
        }

        if (!row.IsIdle)
        {
            List.Dismiss(row);
            return false;
        }

        ToolsRow = null;
        return true;
    }

    /// <summary>The tools' Back to the list.</summary>
    [RelayCommand]
    private void BackToList()
    {
        if (ToolsRow is { } row)
        {
            List.Dismiss(row);
        }

        ToolsRow = null;
    }

    [RelayCommand]
    private void PasteResult()
    {
        if (ToolsRow is { IsResult: true, Result.Length: > 0 } row)
        {
            _host.PasteResult(row, row.Result);
        }
    }

    [RelayCommand]
    private void Close() => _host.Close();

    [RelayCommand]
    private void RunToastAction()
    {
        var run = _toastRun;
        _toast?.Cancel();
        HideToast();
        run?.Invoke();
    }

    private void HideToast()
    {
        IsToastShown = false;
        _toastRun = null;
    }
}
