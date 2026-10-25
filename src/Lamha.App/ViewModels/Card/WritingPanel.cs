using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Services;
using Lamha.Core;
using Lamha.Core.Settings;
using Lamha.Core.Text;
using Lamha.Core.Writing;
using Microsoft.UI.Xaml;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.Card;

/// <summary>What the writing tools need from the card.</summary>
public interface IWritingHost
{
    Task<WritingResult> RunAsync(WritingTool tool, string text, WritingOptions options, CancellationToken cancel);

    /// <summary>Closes the card and puts the text back in the program: over the selection, or at the cursor.</summary>
    Task ReplaceAsync(string text);

    void Copy(string text);

    /// <summary>Settings exist to open (the main window, built after the card in phase 4).</summary>
    bool CanOpenSettings { get; }

    void OpenSettings(string section);

    /// <summary>Narrator says it (an answer arrived).</summary>
    void Announce(string text);
}

/// <summary>What the panel under the tools shows.</summary>
public enum WritingView
{
    /// <summary>Nothing yet: a tool hasn't been picked.</summary>
    None,
    Working,
    Text,
    Proofread,
    NoMistakes,
    Error,

    /// <summary>Reply's request, or Write new's: a box, its choices, and the button.</summary>
    Form,
}

/// <summary>A writing tool's chip: its number (1–9 runs it), its name, and whether it's the one shown.</summary>
public sealed partial class ToolChip(WritingTool tool, int number, IRelayCommand pick) : ObservableObject
{
    public WritingTool Tool { get; } = tool;

    public string Number { get; } = Localization.Number(number);

    public string Label { get; } = Localization.Get("Tool_" + tool);

    public string Tooltip { get; } = Localization.Format("Card_ToolKey", number);

    public string Keys { get; } = number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public IRelayCommand Pick { get; } = pick;

    [ObservableProperty]
    public partial bool IsOn { get; set; }
}

/// <summary>A choice among chips (Write new's kind, a tone): one is on.</summary>
public sealed partial class ChoiceChip(string value, string label) : ObservableObject
{
    public string Value { get; } = value;

    public string Label { get; } = label;

    [ObservableProperty]
    public partial bool IsOn { get; set; }
}

/// <summary>A proofreading correction as the card lists it: what was wrong, its fix, its kind, and why.</summary>
public sealed record IssueItem(string Original, string Fix, string Category, string Why, FlowDirection WhyFlow)
{
    public bool HasCategory => Category.Length > 0;
}

/// <summary>
/// The writing tools on the card (Alt+Shift+W, or "Writing tools" under a sentence), and Write new when nothing was
/// selected: Lamha 2's <c>renderWrite()</c>, <c>runTool()</c>, <c>renderProofread()</c>, <c>replyForm()</c> and
/// <c>composeForm()</c> in content.js.
/// </summary>
public sealed partial class WritingPanel : ObservableObject
{
    /// <summary>Tools whose result can go back in place of the selection (Lamha 2's <c>REPLACEABLE</c>).</summary>
    private static readonly HashSet<WritingTool> Replaceable =
        [WritingTool.Proofread, WritingTool.Improve, WritingTool.Formal, WritingTool.Friendly, WritingTool.Concise, WritingTool.Expand, WritingTool.ToEnglish, WritingTool.Compose];

    private readonly IWritingHost _host;
    private readonly string _text;
    private readonly bool _canReplace;
    private readonly AiProviderKind _provider;
    private readonly LamhaSettings _settings;
    private CancellationTokenSource? _run;
    private WritingTool _tool;
    private WritingOptions _options = new();

    /// <param name="host">The card.</param>
    /// <param name="text">The selected text; "" for Write new.</param>
    /// <param name="canReplace">The program the text came from can take it back (Replace, or Insert for Write new).</param>
    /// <param name="settings">The settings now: the provider, its name.</param>
    /// <param name="notReady">The writing tools aren't set up: why, in Lamha 2's code ("ai_no_key"); null when they are.</param>
    public WritingPanel(IWritingHost host, string text, bool canReplace, LamhaSettings settings, LamhaException? notReady)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _host = host;
        _text = text.Trim();
        _canReplace = canReplace;
        _settings = settings;
        _provider = settings.AiProvider;
        IsCompose = _text.Length == 0;
        IsReady = notReady is null;
        var name = AiNames.Of(_provider, settings);
        Title = IsCompose ? Localization.Format("Card_ComposeTitle", name)
            : IsReady ? Localization.Format("Card_WriteTitle", name)
            : Localization.Get("Card_WritingTools"); // no provider named before one is set up
        SourceFlow = Scripts.IsArabicText(_text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        // Arabic: write it in English, reply, summarize; English: all nine (Shorter and Longer side by side).
        WritingTool[] tools = IsCompose ? []
            : Scripts.IsArabicText(_text) ? [WritingTool.ToEnglish, WritingTool.Reply, WritingTool.Summarize]
            : [WritingTool.Proofread, WritingTool.Improve, WritingTool.Formal, WritingTool.Friendly, WritingTool.Concise, WritingTool.Expand, WritingTool.Summarize, WritingTool.Explain, WritingTool.Reply];
        Tools = [.. tools.Select((t, i) => new ToolChip(t, i + 1, new RelayCommand(() => Pick(t))))];

        Kinds = [new("message", Localization.Get("Card_KindMessage")), new("email", Localization.Get("Card_KindEmail"))];
        Tones =
        [
            new("", Localization.Get("Card_ToneAuto")), new("friendly", Localization.Get("Tool_Friendly")), new("formal", Localization.Get("Tool_Formal")),
            new("short", Localization.Get("Card_ToneShort")), new("long", Localization.Get("Card_ToneLong")),
        ];

        if (notReady is not null)
        {
            ShowError(notReady);
        }
        else if (IsCompose)
        {
            ShowForm(WritingTool.Compose, new WritingOptions());
        }
    }

    public string Title { get; }

    public bool IsCompose { get; }

    public bool IsReady { get; }

    public string Source => _text;

    public bool HasSource => !IsCompose;

    public FlowDirection SourceFlow { get; }

    public ObservableCollection<ToolChip> Tools { get; }

    public bool HasTools => Tools.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorking), nameof(IsResult), nameof(IsProofread), nameof(IsNoMistakes), nameof(IsError), nameof(IsForm))]
    public partial WritingView View { get; private set; }

    public bool IsWorking => View == WritingView.Working;

    /// <summary>A text to use: a tool's, or the corrected one (with its actions under it).</summary>
    public bool IsResult => View is WritingView.Text or WritingView.Proofread;

    public bool IsProofread => View == WritingView.Proofread;

    public bool IsNoMistakes => View == WritingView.NoMistakes;

    public bool IsError => View == WritingView.Error;

    public bool IsForm => View == WritingView.Form;

    /// <summary>A tool's text, or the corrected text.</summary>
    [ObservableProperty]
    public partial string Result { get; private set; } = "";

    [ObservableProperty]
    public partial FlowDirection ResultFlow { get; private set; }

    /// <summary>Proofreading: each mistake struck through and its fix (null: too long to compare, the text alone).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<DiffPart>? Diff { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<IssueItem> Issues { get; private set; } = [];

    [ObservableProperty]
    public partial string CorrectionsTitle { get; private set; } = "";

    [ObservableProperty]
    public partial ErrorInfo? Error { get; private set; }

    // The actions under a result.
    [ObservableProperty]
    public partial bool CanReplaceResult { get; private set; }

    public string ReplaceLabel => Localization.Get(IsCompose ? "Card_Insert" : "Card_Replace");

    /// <summary>"In Arabic" / "In English" after a summary, "Edit the request" after a reply or Write new; "" for none.</summary>
    [ObservableProperty]
    public partial string SecondaryLabel { get; private set; } = "";

    // Reply's and Write new's form.
    [ObservableProperty]
    public partial string Intent { get; set; } = "";

    [ObservableProperty]
    public partial string IntentPlaceholder { get; private set; } = "";

    /// <summary>Write new chooses a message or an email; Reply doesn't.</summary>
    [ObservableProperty]
    public partial bool ShowKinds { get; private set; }

    [ObservableProperty]
    public partial string GoLabel { get; private set; } = "";

    public ObservableCollection<ChoiceChip> Kinds { get; }

    public ObservableCollection<ChoiceChip> Tones { get; }

    /// <summary>Runs the tool with this number (1–9 on the keyboard), if there is one.</summary>
    public bool PickNumber(int number)
    {
        if (number < 1 || number > Tools.Count)
        {
            return false;
        }

        Tools[number - 1].Pick.Execute(null);
        return true;
    }

    /// <summary>The card closed or moved on: a tool still running isn't wanted.</summary>
    public void Cancel()
    {
        _run?.Cancel();
        _run = null;
    }

    private void Pick(WritingTool tool)
    {
        if (!IsReady)
        {
            return;
        }

        if (tool == WritingTool.Reply)
        {
            ShowForm(tool, new WritingOptions());
        }
        else
        {
            // The summary in the interface language; the card offers the other one.
            _ = RunAsync(tool, tool == WritingTool.Summarize ? new WritingOptions { Language = Localization.Language } : new WritingOptions());
        }
    }

    private void Mark(WritingTool tool)
    {
        foreach (var chip in Tools)
        {
            chip.IsOn = chip.Tool == tool;
        }
    }

    private async Task RunAsync(WritingTool tool, WritingOptions options)
    {
        Cancel();
        var run = new CancellationTokenSource();
        _run = run;
        _tool = tool;
        _options = options;
        Mark(tool);
        View = WritingView.Working;
        try
        {
            var result = await _host.RunAsync(tool, tool == WritingTool.Compose || tool == WritingTool.Reply ? options.Intent : _text, options, run.Token).ConfigureAwait(true);
            if (run.IsCancellationRequested)
            {
                return;
            }

            if (tool == WritingTool.Proofread)
            {
                ShowProofread(result);
            }
            else
            {
                ShowText(result.Text.Trim(), tool, options);
            }
        }
        catch (LamhaException e) when (!run.IsCancellationRequested)
        {
            ShowError(e);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ShowText(string text, WritingTool tool, WritingOptions options)
    {
        Result = text;
        ResultFlow = Scripts.IsArabicText(text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Diff = null;
        CanReplaceResult = _canReplace && Replaceable.Contains(tool);
        SecondaryLabel = tool switch
        {
            WritingTool.Summarize => Localization.Get(options.Language == "en" ? "Card_InArabic" : "Card_InEnglish"),
            WritingTool.Reply or WritingTool.Compose => Localization.Get("Card_EditRequest"),
            _ => "",
        };
        View = WritingView.Text;
        _host.Announce(text);
    }

    private void ShowProofread(WritingResult result)
    {
        var corrected = result.Text.Trim();
        if (result.Issues.Count == 0 || corrected.Length == 0 || corrected == _text)
        {
            View = WritingView.NoMistakes;
            _host.Announce(Localization.Get("Card_NoMistakes"));
            return;
        }

        Result = corrected;
        ResultFlow = FlowDirection.LeftToRight;
        Diff = WordDiff.Compare(_text, corrected);
        CanReplaceResult = _canReplace;
        SecondaryLabel = "";
        CorrectionsTitle = Localization.Format("Card_Corrections", result.Issues.Count);
        Issues = [.. result.Issues.Select(i => new IssueItem(
            i.Original,
            i.Fix,
            i.Category == MistakeCategories.Other || !MistakeCategories.IsKnown(i.Category) ? "" : Localization.Get("Category_" + i.Category),
            i.Why,
            Scripts.IsArabicText(i.Why) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight))];
        View = WritingView.Proofread;
        _host.Announce(corrected);
    }

    private void ShowError(LamhaException error)
    {
        Error = ErrorText.ForAi(error, _provider, _settings).Offering(_host.CanOpenSettings);
        View = WritingView.Error;
        _host.Announce(Error.Title);
    }

    /// <summary>Reply's request or Write new's: what to write, its kind (Write new), and its tone.</summary>
    private void ShowForm(WritingTool tool, WritingOptions previous)
    {
        Cancel();
        _tool = tool;
        Mark(tool);
        Intent = previous.Intent;
        ShowKinds = tool == WritingTool.Compose;
        IntentPlaceholder = Localization.Get(tool == WritingTool.Compose ? "Card_ComposePlaceholder" : "Card_ReplyPlaceholder");
        GoLabel = Localization.Get(tool == WritingTool.Compose ? "Card_ComposeGo" : "Card_WriteReply");
        Select(Kinds, previous.Email ? "email" : "message");
        Select(Tones, previous.Tone);
        View = WritingView.Form;
    }

    private static void Select(IEnumerable<ChoiceChip> chips, string value)
    {
        foreach (var chip in chips)
        {
            chip.IsOn = chip.Value == value;
        }
    }

    [RelayCommand]
    private void Choose(ChoiceChip? chip)
    {
        if (chip is not null)
        {
            Select(Kinds.Contains(chip) ? Kinds : Tones, chip.Value);
        }
    }

    /// <summary>Write (Ctrl+Enter): Write new needs something to write; Reply may go without.</summary>
    [RelayCommand]
    private void Go()
    {
        var intent = Intent.Trim();
        if (_tool == WritingTool.Compose && intent.Length == 0)
        {
            return;
        }

        _ = RunAsync(_tool, new WritingOptions
        {
            Intent = intent,
            Email = _tool == WritingTool.Compose && Kinds.First(k => k.IsOn).Value == "email",
            Tone = Tones.FirstOrDefault(t => t.IsOn)?.Value ?? "",
        });
    }

    [RelayCommand]
    private Task Replace() => _host.ReplaceAsync(Result);

    [RelayCommand]
    private void Copy() => _host.Copy(Result);

    /// <summary>The summary in the other language, or back to the request's form.</summary>
    [RelayCommand]
    private void Secondary()
    {
        if (_tool == WritingTool.Summarize)
        {
            _ = RunAsync(_tool, _options with { Language = _options.Language == "en" ? "ar" : "en" });
        }
        else
        {
            ShowForm(_tool, _options);
        }
    }

    /// <summary>Asks again, not from the cache (Lamha 2's "Try again" icon).</summary>
    [RelayCommand]
    private void Again() => _ = RunAsync(_tool, _options with { Fresh = true });

    [RelayCommand]
    private void RunErrorAction()
    {
        if (Error?.Action == ErrorAction.Settings)
        {
            _host.OpenSettings(Error.SettingsSection);
        }
        else if (IsReady)
        {
            _ = RunAsync(_tool, _options);
        }
    }
}
