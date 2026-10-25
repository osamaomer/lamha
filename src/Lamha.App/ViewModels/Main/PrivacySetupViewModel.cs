using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.Core;
using Lamha.Core.Settings;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.Main;

/// <summary>
/// Settings → Privacy &amp; history: keeping the history and clearing it, a copy of my data (save and restore, Lamha 2's
/// format), Report a problem, and the data folder. Lamha 2's options.js <c>initBackup()</c> and <c>initReport()</c>.
/// </summary>
public sealed partial class PrivacySetupViewModel : ObservableObject
{
    /// <summary>Where a report is filed (Lamha 2's <c>ISSUES</c>).</summary>
    private const string Issues = "https://github.com/osamaomer/lamha/issues/new";

    /// <summary>An address has room for about 8,000 characters: a longer report is cut at a line (Lamha 2's).</summary>
    private const int MaxBody = 7000;

    private readonly Brain _brain;
    private readonly IPageHost _host;
    private bool _loading;

    public PrivacySetupViewModel(Brain brain, IPageHost host)
    {
        _brain = brain;
        _host = host;
    }

    [ObservableProperty]
    public partial bool SaveHistory { get; set; }

    /// <summary>"248 words saved", or "History is empty".</summary>
    [ObservableProperty]
    public partial string HistoryCount { get; private set; } = "";

    [ObservableProperty]
    public partial string BackupStatus { get; private set; } = "";

    [ObservableProperty]
    public partial StatusKind BackupStatusKind { get; private set; }

    [ObservableProperty]
    public partial string ReportStatus { get; private set; } = "";

    /// <summary>The report as it would leave, under "Show the report": read it first (Lamha 2's).</summary>
    [ObservableProperty]
    public partial string Report { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsReportShown { get; private set; }

    /// <summary>%LOCALAPPDATA%\Lamha Data, or the <c>--profile</c> folder: what Open the folder opens.</summary>
    public string DataFolder => _brain.DataFolder;

    public void Load(LamhaSettings s)
    {
        _loading = true;
        SaveHistory = s.SaveHistory;
        _loading = false;
        CountHistory();
        BackupStatus = "";
        ReportStatus = "";
    }

    partial void OnSaveHistoryChanged(bool value)
    {
        if (!_loading)
        {
            _brain.Settings.Update(s => s with { SaveHistory = value });
        }
    }

    // ----- the history -----

    /// <summary>Clear: as the History page's, no question and "History cleared · Undo" for a few seconds.</summary>
    [RelayCommand]
    private void ClearHistory()
    {
        var history = _brain.History;
        var cleared = history.Recent();
        history.Clear();
        CountHistory();
        _host.Toast(Localization.Get("Translate_HistoryCleared"), Localization.Get("Common_Undo"), () =>
        {
            history.Restore(cleared);
            CountHistory();
        });
    }

    private void CountHistory()
    {
        var n = _brain.History.Count();
        HistoryCount = n == 0 ? Localization.Get("Settings_HistEmpty") : Counted("Settings_HistCount", n);
    }

    // ----- a copy of my data -----

    /// <summary>Save a copy: "lamha-2026-10-25.json" where the user chooses (Lamha 2's name).</summary>
    [RelayCommand]
    private async Task SaveCopyAsync()
    {
        try
        {
            var path = await _host.PickSaveFileAsync($"lamha-{DateTime.Now:yyyy-MM-dd}.json").ConfigureAwait(true);
            if (path is null)
            {
                return;
            }

            await File.WriteAllTextAsync(path, await _brain.Backup.ExportAsync().ConfigureAwait(true)).ConfigureAwait(true);
            ShowBackup(Localization.Get("Settings_BackupSaved"), StatusKind.Ok);
            AppLog.Info("settings: a copy of the data was saved");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            AppLog.Error("backup save", e);
            ShowBackup(Localization.Get("Settings_BackupFailed"), StatusKind.Bad);
        }
    }

    /// <summary>Restore: what's in the file is added to what's here, nothing deleted (Lamha 2's; files over 50 MB aren't copies).</summary>
    [RelayCommand]
    private async Task RestoreAsync()
    {
        try
        {
            var path = await _host.PickOpenFileAsync().ConfigureAwait(true);
            if (path is null)
            {
                return;
            }

            if (new FileInfo(path).Length > 50 * 1024 * 1024)
            {
                ShowBackup(Localization.Get("Settings_BackupBad"), StatusKind.Bad);
                return;
            }

            var result = await _brain.Backup.ImportAsync(await File.ReadAllTextAsync(path).ConfigureAwait(true)).ConfigureAwait(true);
            ShowBackup(Localization.Format("Settings_BackupDone", Counted("Settings_Cards", result.Cards), Counted("Settings_Words", result.History)), StatusKind.Ok);
            CountHistory();
            _host.ReviewChanged();
            AppLog.Info($"settings: restored {result.Cards} cards and {result.History} words");
        }
        catch (LamhaException e)
        {
            ShowBackup(Localization.Get(e.Code == ErrorCodes.NotBackup ? "Settings_BackupBad" : "Settings_BackupFailed"), StatusKind.Bad);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            AppLog.Error("backup restore", e);
            ShowBackup(Localization.Get("Settings_BackupFailed"), StatusKind.Bad);
        }
    }

    private void ShowBackup(string text, StatusKind kind)
    {
        BackupStatus = text;
        BackupStatusKind = kind;
    }

    // ----- report a problem -----

    /// <summary>Copy: the report on the clipboard, for an email.</summary>
    [RelayCommand]
    private void CopyReport()
    {
        _host.Copy(BuildReport());
        ReportStatus = Localization.Get("Settings_ReportCopied");
    }

    /// <summary>Report on GitHub: a new issue with the report in it, which the user reads, completes and sends.</summary>
    [RelayCommand]
    private void SendReport()
    {
        var report = BuildReport();
        var intro = Localization.Get("Settings_ReportIssue");
        var body = intro + "```\n" + report + "\n```";
        if (Uri.EscapeDataString(body).Length > MaxBody)
        {
            var lines = report.Split('\n').ToList();
            while (lines.Count > 3 && Uri.EscapeDataString(intro + "```\n" + string.Join('\n', lines) + "\n…\n```").Length > MaxBody)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            body = intro + "```\n" + string.Join('\n', lines) + "\n…\n```";
        }

        var version = Brain.Version.Split('+')[0];
        _host.OpenLink(new Uri($"{Issues}?title={Uri.EscapeDataString($"[{version}] ")}&body={Uri.EscapeDataString(body)}"));
        ReportStatus = Localization.Get("Settings_ReportOpened");
    }

    [RelayCommand]
    private void ToggleReport()
    {
        IsReportShown = !IsReportShown;
        if (IsReportShown)
        {
            Report = BuildReport();
        }
    }

    private string BuildReport() => _brain.Diagnostics.Build();

    // ----- my data -----

    [RelayCommand]
    private void OpenFolder() => _host.OpenFolder(_brain.DataFolder);

    /// <summary>Delete my data, after its question: everything on this PC and the API keys go, and Lamha closes.</summary>
    [RelayCommand]
    private async Task DeleteDataAsync()
    {
        if (await _host.AskAsync(Localization.Get("Settings_DeleteDataQ"), Localization.Get("Settings_DeleteData"),
                Localization.Get("Settings_DeleteDataText")).ConfigureAwait(true) == Answer.Confirm)
        {
            _host.DeleteDataAndQuit();
        }
    }

    /// <summary>"What is sent, and where?": the answer shown under it, or hidden again.</summary>
    [ObservableProperty]
    public partial bool IsPrivacyTextShown { get; set; }

    [RelayCommand]
    private void TogglePrivacyText() => IsPrivacyTextShown = !IsPrivacyTextShown;

    /// <summary>Lamha 2's <c>arCount</c>: one, two, 3 to 10 and the rest each have their own words in Arabic.</summary>
    private static string Counted(string key, int n) => n switch
    {
        1 => Localization.Get(key + "One"),
        2 => Localization.Get(key + "Two"),
        >= 3 and <= 10 => Localization.Format(key + "Few", n),
        _ => Localization.Format(key + "Many", n),
    };
}
