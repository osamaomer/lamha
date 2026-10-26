using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Services;
using Lamha.Core;
using Lamha.Core.Dictionary;
using Microsoft.UI.Dispatching;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.Main;

/// <summary>
/// A language pack in Settings' list: its name, "33,902 words · 2.1 MB", and Download, its progress while it comes
/// ("42%"), or Remove; why the last download failed under it. Lamha 2's options.js <c>renderPacks()</c> row.
/// </summary>
public sealed partial class PackRow(string lang, bool isFirst, Func<PackRow, Task> act) : ObservableObject
{
    public string Lang { get; } = lang;

    public string Name { get; } = Localization.LanguageName(lang);

    public bool IsFirst { get; } = isFirst;

    [ObservableProperty]
    public partial string Info { get; private set; } = "";

    /// <summary>Download, "42%" while it downloads, or Remove.</summary>
    [ObservableProperty]
    public partial string Button { get; private set; } = "";

    /// <summary>The button's name for a screen reader: "Download French", "Remove French".</summary>
    [ObservableProperty]
    public partial string ButtonName { get; private set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActCommand))]
    public partial bool IsBusy { get; private set; }

    public bool IsInstalled { get; private set; }

    /// <summary>"Couldn't download it…" or "The file arrived damaged…", until the next try.</summary>
    [ObservableProperty]
    public partial string Error { get; set; } = "";

    public void Show(PackInfo pack)
    {
        IsInstalled = pack.Installed;
        IsBusy = pack.Progress is not null;
        Info = Localization.Format("Settings_PackInfo", pack.Words, Localization.Megabytes(pack.Bytes));
        Button = pack.Progress is { } p ? Localization.Format("Settings_PackGetting", (int)Math.Round(p * 100))
            : Localization.Get(pack.Installed ? "Common_Remove" : "Settings_PackGet");
        ButtonName = IsBusy ? $"{Name} {Button}" : $"{Button} {Name}";
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task ActAsync() => act(this);

    private bool CanAct() => !IsBusy;
}

/// <summary>
/// Settings → Without internet → Dictionaries to download: the language packs (Lamha 2's options.js
/// <c>renderPacks()</c>). A pack downloads with its progress on its button, and keeps downloading if the page is left.
/// </summary>
public sealed partial class OfflineSetupViewModel : ObservableObject
{
    private readonly Brain _brain;
    private readonly DispatcherQueue _queue;
    private bool _attached;

    public OfflineSetupViewModel(Brain brain)
    {
        _brain = brain;
        _queue = DispatcherQueue.GetForCurrentThread();
        Packs = [.. LanguagePacks.Catalog.Select((c, i) => new PackRow(c.Lang, i == 0, ActAsync))];
    }

    public List<PackRow> Packs { get; }

    /// <summary>The page is on screen: a download moving on shows on its button.</summary>
    public void Attach()
    {
        if (!_attached)
        {
            _attached = true;
            _brain.Packs.Changed += OnChanged;
            Refresh();
        }
    }

    public void Detach()
    {
        if (_attached)
        {
            _attached = false;
            _brain.Packs.Changed -= OnChanged;
        }
    }

    private void OnChanged() => _queue.TryEnqueue(Refresh);

    private void Refresh()
    {
        foreach (var pack in _brain.Packs.List())
        {
            Packs.First(r => r.Lang == pack.Lang).Show(pack);
        }
    }

    /// <summary>Download, or Remove (no question: it downloads again as easily, as in Lamha 2).</summary>
    private async Task ActAsync(PackRow row)
    {
        row.Error = "";
        var packs = _brain.Packs;
        try
        {
            if (row.IsInstalled)
            {
                packs.Remove(row.Lang);
                AppLog.Info($"pack removed: {row.Lang}");
            }
            else
            {
                await packs.InstallAsync(row.Lang).ConfigureAwait(true);
                AppLog.Info($"pack installed: {row.Lang}");
            }
        }
        catch (LamhaException e)
        {
            row.Error = Localization.Get(e.Code == ErrorCodes.PackBad ? "Settings_PackBad" : "Settings_PackFailed");
            AppLog.Info($"pack {row.Lang}: {e.Code}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            row.Error = Localization.Get("Settings_PackFailed");
            AppLog.Error("pack remove", e);
        }

        Refresh();
    }
}
