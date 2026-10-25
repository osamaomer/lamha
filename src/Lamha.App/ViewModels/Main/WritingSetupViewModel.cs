using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Controls;
using Lamha.App.Services;
using Lamha.Core;
using Lamha.Core.Ai;
using Lamha.Core.Settings;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.Main;

/// <summary>A model to choose: what the dropdown shows, and its id ("" for none chosen, or "same as the writing tools").</summary>
public sealed record ModelChoice(string Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// An API key (Gemini, Claude, OpenAI): checked with a tiny request before it's saved, then kept in Windows' Credential
/// Manager and never shown again. Lamha 2's <c>initKeyBox()</c> in options.js.
/// </summary>
public sealed partial class KeySetup : ObservableObject
{
    private readonly AiProviderKind _kind;
    private readonly WritingSetupViewModel _owner;
    private readonly string _placeholder;

    public KeySetup(AiProviderKind kind, WritingSetupViewModel owner, string placeholder)
    {
        _kind = kind;
        _owner = owner;
        _placeholder = placeholder;
        Reload();
    }

    /// <summary>The key being typed; "" once saved (the field never holds the saved key).</summary>
    [ObservableProperty]
    public partial string Input { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Placeholder))]
    public partial bool HasKey { get; private set; }

    /// <summary>"•••••••• (saved) — paste a new key to change it", or what a key looks like.</summary>
    public string Placeholder => HasKey ? Localization.Get("Settings_KeySavedPlaceholder") : _placeholder;

    /// <summary>What Narrator calls the field: "Claude · Key".</summary>
    public string FieldName => $"{AiModels.Name(_kind)} · {Localization.Get("Settings_ServerKey")}";

    public string SaveLabel { get; } = Localization.Get("Settings_SaveVerify");

    public string RemoveLabel { get; } = Localization.Get("Common_Remove");

    [ObservableProperty]
    public partial string Status { get; private set; } = "";

    [ObservableProperty]
    public partial StatusKind StatusKind { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsChecking { get; private set; }

    /// <summary>What the page says before anything is checked: "✓ Key saved." or "No key added yet."</summary>
    public void Reload()
    {
        HasKey = _owner.Brain.Secrets.Get(AiModels.Id(_kind)) is { Length: > 0 };
        Show(HasKey ? Localization.Get("Settings_KeySaved") : Localization.Get("Settings_NoKey"), HasKey ? StatusKind.Ok : StatusKind.Neutral);
    }

    /// <summary>Save &amp; verify (or Enter in the field): a tiny request with the key; saved only when it works.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var key = Input.Trim();
        if (key.Length == 0)
        {
            return;
        }

        IsChecking = true;
        Show(Localization.Get("Settings_CheckingKey"), StatusKind.Neutral);
        try
        {
            var note = await _owner.VerifyAsync(_kind, key).ConfigureAwait(true);
            _owner.Brain.Secrets.Set(AiModels.Id(_kind), key);
            Input = "";
            HasKey = true;
            Show(Localization.Get(note == ErrorCodes.GeminiBusy ? "Settings_GeminiBusySaved" : "Settings_Verified"), StatusKind.Ok);
            AppLog.Info($"settings: {AiModels.Id(_kind)} key saved");
            _owner.KeyChanged();
        }
        catch (LamhaException e)
        {
            Show(_owner.Words(e, _kind), StatusKind.Bad);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            AppLog.Error("settings key", e);
            Show(Localization.Get("Error_other_Title"), StatusKind.Bad);
        }
        finally
        {
            IsChecking = false;
        }
    }

    private bool CanSave() => !IsChecking;

    [RelayCommand]
    private void Remove()
    {
        _owner.Brain.Secrets.Set(AiModels.Id(_kind), null);
        Input = "";
        HasKey = false;
        Show(Localization.Get("Settings_NoKey"), StatusKind.Neutral);
        AppLog.Info($"settings: {AiModels.Id(_kind)} key removed");
        _owner.KeyChanged();
    }

    private void Show(string text, StatusKind kind)
    {
        Status = text;
        StatusKind = kind;
    }
}

/// <summary>
/// Settings → Writing tools (phase 1's design): the five providers, the chosen one's setup under it. Lamha 2's
/// <c>initAI()</c>, <c>initOllama()</c>, <c>refreshOllama()</c> and <c>testOllama()</c> in options.js, with OpenAI and
/// another server (decision 0007). Keys are in Windows' Credential Manager; nothing here is logged but what was done.
/// </summary>
public sealed partial class WritingSetupViewModel : ObservableObject
{
    /// <summary>Claude's and Gemini's models, the default first (Core's <see cref="AiModels"/>), in the interface's words.</summary>
    private static readonly string[] ClaudeLabels = ["Settings_Opus", "Settings_Sonnet", "Settings_Haiku"];
    private static readonly string[] GeminiLabels = ["Settings_GemFlash", "Settings_GemLite"];

    private readonly Action _changed;
    private bool _loading;
    private int _ollamaVersion;
    private int _customVersion;
    private int _openAiVersion;

    /// <param name="brain">Lamha's brain: the settings, the keys, the AI.</param>
    /// <param name="changed">A key or a model changed: Translation's model and its line are read again.</param>
    public WritingSetupViewModel(Brain brain, Action changed)
    {
        Brain = brain;
        _changed = changed;
        Gemini = new KeySetup(AiProviderKind.Gemini, this, "AIza…");
        Claude = new KeySetup(AiProviderKind.Claude, this, "sk-ant-api…");
        OpenAi = new KeySetup(AiProviderKind.OpenAi, this, "sk-proj-…");
        ClaudeModels = [.. AiModels.Claude.Select((id, i) => new ModelChoice(id, Localization.Get(ClaudeLabels[i])))];
        GeminiModels = [.. AiModels.Gemini.Select((id, i) => new ModelChoice(id, Localization.Get(GeminiLabels[i])))];
    }

    public Brain Brain { get; }

    public KeySetup Gemini { get; }

    public KeySetup Claude { get; }

    public KeySetup OpenAi { get; }

    public string OpenAiName { get; } = AiModels.Name(AiProviderKind.OpenAi);

    public string CustomName { get; } = Localization.Get("Ai_Custom");

    // ----- the provider -----

    [ObservableProperty]
    public partial bool IsOllama { get; set; }

    [ObservableProperty]
    public partial bool IsGemini { get; set; }

    [ObservableProperty]
    public partial bool IsClaude { get; set; }

    [ObservableProperty]
    public partial bool IsOpenAi { get; set; }

    [ObservableProperty]
    public partial bool IsCustom { get; set; }

    // ----- Gemini and Claude: their models -----

    public List<ModelChoice> GeminiModels { get; }

    [ObservableProperty]
    public partial int GeminiModelIndex { get; set; }

    public List<ModelChoice> ClaudeModels { get; }

    [ObservableProperty]
    public partial int ClaudeModelIndex { get; set; }

    // ----- OpenAI: the models its key can use -----

    [ObservableProperty]
    public partial List<ModelChoice> OpenAiModels { get; private set; } = [];

    [ObservableProperty]
    public partial int OpenAiModelIndex { get; set; } = -1;

    // ----- Ollama -----

    /// <summary>Its address; "" is this PC's.</summary>
    [ObservableProperty]
    public partial string OllamaUrl { get; set; } = "";

    [ObservableProperty]
    public partial List<ModelChoice> OllamaModels { get; private set; } = [];

    [ObservableProperty]
    public partial int OllamaModelIndex { get; set; } = -1;

    [ObservableProperty]
    public partial string OllamaStatus { get; private set; } = "";

    [ObservableProperty]
    public partial StatusKind OllamaStatusKind { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestOllamaCommand), nameof(RefreshOllamaCommand))]
    public partial bool IsOllamaBusy { get; private set; }

    // ----- another server -----

    [ObservableProperty]
    public partial string CustomUrl { get; set; } = "";

    /// <summary>The server's key being typed; saved with the address.</summary>
    [ObservableProperty]
    public partial string CustomKey { get; set; } = "";

    [ObservableProperty]
    public partial string CustomKeyPlaceholder { get; private set; } = "";

    [ObservableProperty]
    public partial List<ModelChoice> CustomModels { get; private set; } = [];

    [ObservableProperty]
    public partial int CustomModelIndex { get; set; } = -1;

    [ObservableProperty]
    public partial string CustomStatus { get; private set; } = "";

    [ObservableProperty]
    public partial StatusKind CustomStatusKind { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCustomCommand))]
    public partial bool IsCustomBusy { get; private set; }

    /// <summary>The page opens: what's saved, and the chosen provider's models read again.</summary>
    public void Load(LamhaSettings s)
    {
        _loading = true;
        try
        {
            IsOllama = s.AiProvider == AiProviderKind.Ollama;
            IsGemini = s.AiProvider == AiProviderKind.Gemini;
            IsClaude = s.AiProvider == AiProviderKind.Claude;
            IsOpenAi = s.AiProvider == AiProviderKind.OpenAi;
            IsCustom = s.AiProvider == AiProviderKind.Custom;
            GeminiModelIndex = Math.Max(0, AiModels.Gemini.ToList().IndexOf(s.GeminiModel));
            ClaudeModelIndex = Math.Max(0, AiModels.Claude.ToList().IndexOf(s.ClaudeModel));
            OllamaUrl = s.OllamaUrl;
            CustomUrl = s.CustomUrl;
            CustomKey = "";
            CustomKeyPlaceholder = Brain.Secrets.Get("custom") is { Length: > 0 } ? Localization.Get("Settings_KeySavedPlaceholder") : "—";
        }
        finally
        {
            _loading = false;
        }

        Gemini.Reload();
        Claude.Reload();
        OpenAi.Reload();
        ShowSavedModel(s);
        RefreshChosen();
    }

    /// <summary>Lamha 2's settings words for an error, else the card's ("title — hint"), else "Couldn't connect: …".</summary>
    public string Words(LamhaException e, AiProviderKind kind)
    {
        // Lamha 2's oerr.ai_no_credit names Claude's console: the other providers get the card's words, which name theirs.
        var own = e.Code is ErrorCodes.AiBadKey or ErrorCodes.AiBusy or ErrorCodes.AiForbidden or ErrorCodes.AiModel or ErrorCodes.AiRateLimited
            or ErrorCodes.AiTimeout or ErrorCodes.Network or ErrorCodes.OllamaModel or ErrorCodes.OllamaOffline
            || (e.Code == ErrorCodes.AiNoCredit && kind == AiProviderKind.Claude);
        if (own)
        {
            return Localization.Get("Settings_Err_" + e.Code);
        }

        if (e.Code == ErrorCodes.AiError)
        {
            return Localization.Get("Settings_ConnectFailed") + e.Detail;
        }

        var info = ErrorText.ForAi(e, kind, Brain.Settings.Current);
        return info.Hint.Length > 0 ? $"{info.Title} — {info.Hint}" : info.Title;
    }

    /// <summary>A key to save: a tiny request with it (OpenAI's: its list of models, which then fills the dropdown).</summary>
    public async Task<string?> VerifyAsync(AiProviderKind kind, string key)
    {
        var s = Brain.Settings.Current;
        switch (kind)
        {
            case AiProviderKind.OpenAi:
                var models = await Brain.Ai.Provider(kind).ListModelsAsync(new AiConfig(kind, "", key)).ConfigureAwait(true);
                ShowOpenAiModels(models, s.OpenAiModel);
                return null;
            case AiProviderKind.Gemini:
                return await Brain.Ai.TestAsync(new AiConfig(kind, GeminiModels[Math.Max(0, GeminiModelIndex)].Id, key)).ConfigureAwait(true);
            default:
                return await Brain.Ai.TestAsync(new AiConfig(kind, ClaudeModels[Math.Max(0, ClaudeModelIndex)].Id, key)).ConfigureAwait(true);
        }
    }

    /// <summary>A key was saved or removed: OpenAI's models with it, and Translation's line.</summary>
    public void KeyChanged()
    {
        if (IsOpenAi)
        {
            _ = ListOpenAiAsync();
        }

        _changed();
    }

    // ----- saving -----

    partial void OnIsOllamaChanged(bool value) => Choose(value, AiProviderKind.Ollama);

    partial void OnIsGeminiChanged(bool value) => Choose(value, AiProviderKind.Gemini);

    partial void OnIsClaudeChanged(bool value) => Choose(value, AiProviderKind.Claude);

    partial void OnIsOpenAiChanged(bool value) => Choose(value, AiProviderKind.OpenAi);

    partial void OnIsCustomChanged(bool value) => Choose(value, AiProviderKind.Custom);

    partial void OnGeminiModelIndexChanged(int value) => Save(s => value >= 0 ? s with { GeminiModel = GeminiModels[value].Id } : s);

    partial void OnClaudeModelIndexChanged(int value) => Save(s => value >= 0 ? s with { ClaudeModel = ClaudeModels[value].Id } : s);

    partial void OnOpenAiModelIndexChanged(int value)
    {
        if (value >= 0 && value < OpenAiModels.Count && OpenAiModels[value].Id.Length > 0)
        {
            Save(s => s with { OpenAiModel = OpenAiModels[value].Id });
        }
    }

    /// <summary>
    /// Another model chosen in Ollama: saved, then tested (Lamha 2's). The dropdown also sets its choice back when it's
    /// made; that's the saved model again, and isn't tested.
    /// </summary>
    partial void OnOllamaModelIndexChanged(int value)
    {
        if (!_loading && value >= 0 && value < OllamaModels.Count && OllamaModels[value].Id is { Length: > 0 } model
            && model != Brain.Settings.Current.OllamaModel)
        {
            Save(s => s with { OllamaModel = model });
            _ = TestOllamaAsync();
        }
    }

    partial void OnCustomModelIndexChanged(int value)
    {
        if (value >= 0 && value < CustomModels.Count && CustomModels[value].Id.Length > 0)
        {
            Save(s => s with { CustomModel = CustomModels[value].Id });
        }
    }

    private void Choose(bool chosen, AiProviderKind kind)
    {
        if (!chosen || _loading)
        {
            return;
        }

        Save(s => s with { AiProvider = kind });
        RefreshChosen();
    }

    private void Save(Func<LamhaSettings, LamhaSettings> change)
    {
        if (_loading)
        {
            return;
        }

        _loading = true; // the settings' Changed comes back through the page: what's shown is what was saved
        try
        {
            Brain.Settings.Update(change);
        }
        finally
        {
            _loading = false;
        }

        _changed();
    }

    /// <summary>The chosen provider's models, from where they are (Ollama, OpenAI, another server).</summary>
    private void RefreshChosen()
    {
        if (IsOllama)
        {
            _ = RefreshOllamaAsync();
        }
        else if (IsOpenAi)
        {
            _ = ListOpenAiAsync();
        }
        else if (IsCustom && CustomUrl.Trim().Length > 0)
        {
            _ = ListCustomAsync();
        }
    }

    /// <summary>Until a list is read, the dropdowns show the saved model alone, so the page doesn't look unset.</summary>
    private void ShowSavedModel(LamhaSettings s)
    {
        _loading = true;
        try
        {
            OllamaModels = Saved(s.OllamaModel);
            OllamaModelIndex = OllamaModels.Count > 1 ? 1 : 0;
            CustomModels = Saved(s.CustomModel);
            CustomModelIndex = CustomModels.Count > 1 ? 1 : 0;
            OpenAiModels = Saved(s.OpenAiModel);
            OpenAiModelIndex = OpenAiModels.Count > 1 ? 1 : 0;
        }
        finally
        {
            _loading = false;
        }
    }

    private static List<ModelChoice> Saved(string model) =>
        model.Length > 0 ? [new("", Localization.Get("Settings_PickModel")), new(model, model)] : [new("", Localization.Get("Settings_PickModel"))];

    // ----- Ollama -----

    /// <summary>The address field is left (or Enter): saved, and the models read from there.</summary>
    [RelayCommand]
    private void CommitOllamaUrl()
    {
        var url = OllamaUrl.Trim();
        if (url != Brain.Settings.Current.OllamaUrl)
        {
            Save(s => s with { OllamaUrl = url });
            _ = RefreshOllamaAsync();
        }
    }

    /// <summary>Refresh list: Lamha 2's <c>refreshOllama()</c>, the models downloaded in Ollama, a Qwen first time.</summary>
    [RelayCommand(CanExecute = nameof(IsOllamaIdle))]
    private async Task RefreshOllamaAsync()
    {
        var version = ++_ollamaVersion;
        ShowOllama(Localization.Get("Settings_OllamaConnecting"), StatusKind.Neutral);
        try
        {
            var url = OllamaProvider.BaseUrl(OllamaUrl);
            var models = await Brain.Ai.Provider(AiProviderKind.Ollama).ListModelsAsync(new AiConfig(AiProviderKind.Ollama, "", BaseUrl: url)).ConfigureAwait(true);
            if (version != _ollamaVersion)
            {
                return;
            }

            var saved = Brain.Settings.Current.OllamaModel;
            var pick = models.Any(m => m.Name == saved) ? saved
                : (models.FirstOrDefault(m => m.Name.StartsWith("qwen", StringComparison.OrdinalIgnoreCase)) ?? models.FirstOrDefault())?.Name ?? "";
            _loading = true;
            try
            {
                OllamaModels = [new("", Localization.Get("Settings_PickModel")), .. models.Select(m => new ModelChoice(m.Name, $"{m.Name} ({Gigabytes(m.Size)} GB)"))];
                OllamaModelIndex = Math.Max(0, OllamaModels.FindIndex(m => m.Id == pick));
            }
            finally
            {
                _loading = false;
            }

            if (models.Count == 0)
            {
                ShowOllama(Localization.Get("Settings_OllamaNoModels"), StatusKind.Bad);
                return;
            }

            if (pick != saved)
            {
                Save(s => s with { OllamaModel = pick }); // the first time: Lamha 2 chose one
            }

            ShowOllama(Counted(models.Count) + (PlainHttpElsewhere(OllamaUrl) ? Localization.Get("Settings_OllamaPlainHttp") : ""), StatusKind.Ok);
        }
        catch (LamhaException e)
        {
            if (version == _ollamaVersion)
            {
                ShowOllama(Words(e, AiProviderKind.Ollama), StatusKind.Bad);
            }
        }
    }

    /// <summary>Test: Lamha 2's <c>testOllama()</c>, a tiny request to the chosen model, timed.</summary>
    [RelayCommand(CanExecute = nameof(IsOllamaIdle))]
    private async Task TestOllamaAsync()
    {
        var model = OllamaModelIndex >= 0 && OllamaModelIndex < OllamaModels.Count ? OllamaModels[OllamaModelIndex].Id : "";
        if (model.Length == 0)
        {
            ShowOllama(Localization.Get("Settings_PickModelFirst"), StatusKind.Bad);
            return;
        }

        IsOllamaBusy = true;
        ShowOllama(Localization.Format("Settings_OllamaTesting", model), StatusKind.Neutral);
        var clock = Stopwatch.StartNew();
        try
        {
            await Brain.Ai.TestAsync(new AiConfig(AiProviderKind.Ollama, model, BaseUrl: OllamaProvider.BaseUrl(OllamaUrl))).ConfigureAwait(true);
            ShowOllama(Localization.Format("Settings_OllamaWorks", Seconds(clock.Elapsed)), StatusKind.Ok);
            _changed();
        }
        catch (LamhaException e)
        {
            ShowOllama(Words(e, AiProviderKind.Ollama), StatusKind.Bad);
        }
        finally
        {
            IsOllamaBusy = false;
        }
    }

    private bool IsOllamaIdle() => !IsOllamaBusy;

    private void ShowOllama(string text, StatusKind kind)
    {
        OllamaStatus = text;
        OllamaStatusKind = kind;
    }

    /// <summary>Lamha 2's <c>arCount</c> for "n models".</summary>
    private static string Counted(int n) => n switch
    {
        1 => Localization.Get("Settings_OllamaOkOne"),
        2 => Localization.Get("Settings_OllamaOkTwo"),
        >= 3 and <= 10 => Localization.Format("Settings_OllamaOkFew", n),
        _ => Localization.Format("Settings_OllamaOkMany", n),
    };

    /// <summary>"3.4": a size in GB with one decimal, as Lamha 2 wrote it (always Latin digits, like the GB beside it).</summary>
    private static string Gigabytes(long bytes) => (bytes / 1e9).ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>"2.4", "٢٫٤": seconds with one decimal, as Lamha 2's <c>num(…, { maximumFractionDigits: 1 })</c>.</summary>
    private static string Seconds(TimeSpan time)
    {
        var text = time.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture);
        return Localization.IsArabic ? string.Concat(text.Select(c => c is >= '0' and <= '9' ? (char)('٠' + (c - '0')) : c == '.' ? '٫' : c)) : text;
    }

    /// <summary>http:// to a computer other than this one: the text crosses the network unencrypted (Lamha 2's <c>plainHttpRemote</c>).</summary>
    private static bool PlainHttpElsewhere(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback;

    // ----- OpenAI -----

    private async Task ListOpenAiAsync()
    {
        var version = ++_openAiVersion;
        if (Brain.Secrets.Get("openai") is not { Length: > 0 } key)
        {
            return;
        }

        try
        {
            var models = await Brain.Ai.Provider(AiProviderKind.OpenAi).ListModelsAsync(new AiConfig(AiProviderKind.OpenAi, "", key)).ConfigureAwait(true);
            if (version == _openAiVersion)
            {
                ShowOpenAiModels(models, Brain.Settings.Current.OpenAiModel);
            }
        }
        catch (LamhaException e)
        {
            AppLog.Info($"settings: OpenAI's models not read ({e.Code})");
        }
    }

    private void ShowOpenAiModels(IReadOnlyList<AiModelInfo> models, string saved)
    {
        _loading = true;
        try
        {
            OpenAiModels = [new("", Localization.Get("Settings_PickModel")), .. models.Select(m => new ModelChoice(m.Name, m.Name))];
            OpenAiModelIndex = Math.Max(0, OpenAiModels.FindIndex(m => m.Id == saved));
        }
        finally
        {
            _loading = false;
        }
    }

    // ----- another server -----

    /// <summary>The address or key field is left (or Enter): saved, and the server's models read.</summary>
    [RelayCommand]
    private void CommitCustom()
    {
        var url = CustomUrl.Trim();
        if (CustomKey.Trim().Length > 0)
        {
            Brain.Secrets.Set("custom", CustomKey.Trim());
            CustomKey = "";
            CustomKeyPlaceholder = Localization.Get("Settings_KeySavedPlaceholder");
            AppLog.Info("settings: custom server key saved");
        }

        if (url != Brain.Settings.Current.CustomUrl)
        {
            Save(s => s with { CustomUrl = url });
        }

        _ = ListCustomAsync();
    }

    private async Task ListCustomAsync()
    {
        var version = ++_customVersion;
        if (CustomUrl.Trim().Length == 0)
        {
            ShowCustom("", StatusKind.Neutral);
            return;
        }

        if (CustomServer.Normalize(CustomUrl) is not { } url)
        {
            ShowCustom(Words(new LamhaException(ErrorCodes.CustomInsecure), AiProviderKind.Custom), StatusKind.Bad);
            return;
        }

        try
        {
            var models = await Brain.Ai.Provider(AiProviderKind.Custom)
                .ListModelsAsync(new AiConfig(AiProviderKind.Custom, "", Brain.Secrets.Get("custom") ?? "", url)).ConfigureAwait(true);
            if (version != _customVersion)
            {
                return;
            }

            var saved = Brain.Settings.Current.CustomModel;
            _loading = true;
            try
            {
                CustomModels = [new("", Localization.Get("Settings_PickModel")), .. models.Select(m => new ModelChoice(m.Name, m.Name))];
                if (saved.Length > 0 && !models.Any(m => m.Name == saved))
                {
                    CustomModels.Add(new(saved, saved)); // the server doesn't list it right now: keep it
                }

                CustomModelIndex = Math.Max(0, CustomModels.FindIndex(m => m.Id == saved));
            }
            finally
            {
                _loading = false;
            }

            ShowCustom("", StatusKind.Neutral);
        }
        catch (LamhaException e)
        {
            if (version == _customVersion)
            {
                ShowCustom(Words(e, AiProviderKind.Custom), StatusKind.Bad);
            }
        }
    }

    /// <summary>Test: a tiny request to the chosen model on the server.</summary>
    [RelayCommand(CanExecute = nameof(IsCustomIdle))]
    private async Task TestCustomAsync()
    {
        if (CustomUrl.Trim().Length > 0 && Brain.Settings.Current.CustomModel.Length == 0)
        {
            ShowCustom(Localization.Get("Settings_PickModelFirst"), StatusKind.Bad);
            return;
        }

        IsCustomBusy = true;
        var clock = Stopwatch.StartNew();
        try
        {
            var config = Brain.Ai.Config(AiProviderKind.Custom);
            ShowCustom(Localization.Format("Settings_OllamaTesting", config.Model), StatusKind.Neutral);
            await Brain.Ai.TestAsync(config).ConfigureAwait(true);
            ShowCustom(Localization.Format("Settings_CustomWorks", Seconds(clock.Elapsed)), StatusKind.Ok);
            _changed();
        }
        catch (LamhaException e)
        {
            ShowCustom(Words(e, AiProviderKind.Custom), StatusKind.Bad);
        }
        finally
        {
            IsCustomBusy = false;
        }
    }

    private bool IsCustomIdle() => !IsCustomBusy;

    private void ShowCustom(string text, StatusKind kind)
    {
        CustomStatus = text;
        CustomStatusKind = kind;
    }
}
