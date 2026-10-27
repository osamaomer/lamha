using CommunityToolkit.Mvvm.ComponentModel;
using Lamha.App.Services;
using Lamha.App.ViewModels.Card;
using Lamha.Core;
using Lamha.Core.Translation;
using Microsoft.UI.Xaml;
using Localization = Lamha.App.Services.Localization;

namespace Lamha.App.ViewModels.Mini;

/// <summary>What the mini translator shows under its box.</summary>
public enum MiniView
{
    /// <summary>Nothing typed yet.</summary>
    Empty,

    /// <summary>The answer is on its way.</summary>
    Waiting,

    /// <summary>A word: the headword and its meaning on one line.</summary>
    Word,

    /// <summary>A sentence or more: its translation.</summary>
    Text,

    Error,
}

/// <summary>
/// The mini translator (docs/design/system.md, the study's features): a small Lamha always on top, in Windows' compact
/// overlay, with the Translate page's box and its answer as one line, for an article or a film's subtitles. Typing looks
/// up after the box's pause, Enter at once; the meaning is the card's (<see cref="WordAnswer"/>).
/// </summary>
public sealed partial class MiniViewModel(Brain brain) : ObservableObject, IAnswerActions
{
    /// <summary>The Translate page's pause (Lamha 2's <c>qTimer</c>).</summary>
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(420);

    private CancellationTokenSource? _lookup;
    private CancellationTokenSource? _pause;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuery))]
    public partial string Query { get; set; } = "";

    public bool HasQuery => Query.Trim().Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaiting), nameof(IsWord), nameof(IsText), nameof(IsError))]
    public partial MiniView View { get; private set; }

    public bool IsWaiting => View == MiniView.Waiting;

    public bool IsWord => View == MiniView.Word;

    public bool IsText => View == MiniView.Text;

    public bool IsError => View == MiniView.Error;

    /// <summary>A word as looked up (its dictionary form), and its direction.</summary>
    [ObservableProperty]
    public partial string Headword { get; private set; } = "";

    [ObservableProperty]
    public partial FlowDirection HeadwordFlow { get; private set; }

    /// <summary>The meaning, or a text's translation, and its direction.</summary>
    [ObservableProperty]
    public partial string Meaning { get; private set; } = "";

    [ObservableProperty]
    public partial FlowDirection MeaningFlow { get; private set; }

    /// <summary>What went wrong, in a line (the error's title).</summary>
    [ObservableProperty]
    public partial string ErrorLine { get; private set; } = "";

    /// <summary>"Type a word or sentence to translate…" (the Translate page's).</summary>
    public string Placeholder { get; } = Localization.Get("Translate_Placeholder");

    /// <summary>Narrator says the answer when it arrives.</summary>
    public event Action<string>? Announced;

    /// <summary>Enter in the box: no waiting for the pause.</summary>
    public void LookUpNow()
    {
        _pause?.Cancel();
        LookUp(Query.Trim());
    }

    /// <summary>The window closed: what it was waiting for is no longer wanted.</summary>
    public void Unload()
    {
        _pause?.Cancel();
        _lookup?.Cancel();
    }

    // ----- IAnswerActions: a meaning chosen in the mini is only ever looked up -----

    public void Navigate(string word)
    {
        Query = word;
        LookUpNow();
    }

    public Task ToggleReviewAsync(WordAnswer answer) => Task.CompletedTask;

    public void Copy(string text)
    {
    }

    public void OpenLink(Uri uri)
    {
    }

    public void OpenWritingTools()
    {
    }

    public void BetterTranslation()
    {
    }

    // ----- looking up -----

    partial void OnQueryChanged(string value)
    {
        _pause?.Cancel();
        if (value.Trim().Length == 0)
        {
            _lookup?.Cancel();
            View = MiniView.Empty;
            return;
        }

        _ = AfterPauseAsync(_pause = new CancellationTokenSource());
    }

    private async Task AfterPauseAsync(CancellationTokenSource pause)
    {
        try
        {
            await Task.Delay(TypingPause, pause.Token).ConfigureAwait(true);
            LookUp(Query.Trim());
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void LookUp(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        _lookup?.Cancel();
        var cancel = _lookup = new CancellationTokenSource();
        View = MiniView.Waiting;
        _ = LookUpAsync(text, cancel);
    }

    private async Task LookUpAsync(string text, CancellationTokenSource cancel)
    {
        try
        {
            var result = await brain.Lookup.LookupAsync(text, new LookupOptions(), cancel.Token).ConfigureAwait(true);
            if (cancel.IsCancellationRequested)
            {
                return;
            }

            var answer = WordAnswer.For(brain, result, this, writingTools: false);
            Headword = answer.Query;
            HeadwordFlow = answer.SourceFlow;
            Meaning = answer.HeroText.Length > 0 ? answer.HeroText : answer.HeroNone;
            MeaningFlow = answer.HeroText.Length > 0 ? answer.HeroFlow : Localization.FlowDirection;
            View = answer.IsWord ? MiniView.Word : MiniView.Text;
            Announced?.Invoke(answer.Spoken);
        }
        catch (LamhaException e) when (!cancel.IsCancellationRequested)
        {
            var settings = brain.Settings.Current;
            ErrorLine = ErrorText.ForLookup(e, settings.TrProvider ?? settings.AiProvider, settings, !brain.Network.IsOffline).Title;
            View = MiniView.Error;
            Announced?.Invoke(ErrorLine);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is not OutOfMemoryException && !cancel.IsCancellationRequested)
        {
            AppLog.Error("mini translator", e);
            ErrorLine = ErrorText.Unexpected.Title;
            View = MiniView.Error;
        }
    }
}
