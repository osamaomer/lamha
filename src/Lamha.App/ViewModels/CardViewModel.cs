using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Services;
using Lamha.Core;
using Lamha.Core.Dictionary;
using Lamha.Core.Text;
using Lamha.Windows.Selection;

namespace Lamha.App.ViewModels;

/// <summary>What the card can ask of whoever opened it.</summary>
public interface ICardHost
{
    /// <summary>The lookup for the selected text, with the sentence around it when it was read.</summary>
    Task<string> LookupAsync(string text, WordContext? context, CancellationToken cancel);

    /// <summary>
    /// Closes the card and pastes <paramref name="text"/> into the program it came from: over its selection (Replace),
    /// or at its cursor when nothing was selected (Insert).
    /// </summary>
    Task ReplaceAsync(string text);

    void Copy(string text);

    void Close();
}

/// <summary>Which shortcut opened the card.</summary>
public enum CardMode
{
    /// <summary>Look up and translate (Ctrl+Alt+L in the beta).</summary>
    Lookup,

    /// <summary>The writing tools (Ctrl+Alt+W): with nothing selected, "Write new", which Insert pastes at the cursor.</summary>
    Write,
}

/// <summary>
/// The prototype card on the real brain: what was selected, its translation from the lookup (or, for the writing
/// shortcut, the text to edit), and Replace / Insert to put the result back. Phase 4 replaces it with the real card
/// (meanings, senses, Wikipedia, writing tools).
/// </summary>
public sealed partial class CardViewModel(ICardHost host) : ObservableObject
{
    private CancellationTokenSource? _lookup;

    [ObservableProperty]
    public partial string Text { get; private set; } = "";

    [ObservableProperty]
    public partial string Kind { get; private set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReplaceCommand))]
    public partial string Result { get; set; } = "";

    /// <summary>"Looking up…", or why the lookup failed; "" otherwise.</summary>
    [ObservableProperty]
    public partial string Status { get; private set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReplaceCommand))]
    public partial bool CanReplace { get; private set; }

    /// <summary>Replace when text was selected, Insert when it wasn't (the writing shortcut's "Write new").</summary>
    [ObservableProperty]
    public partial string ReplaceLabel { get; private set; } = "";

    /// <summary>Shows <paramref name="capture"/>, and starts its lookup. A lookup still running for an earlier card is dropped.</summary>
    public void Load(SelectionCapture capture, CardMode mode, WordContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        _lookup?.Cancel();
        _lookup = null;

        var text = capture.Text.Trim();
        var nothing = text.Length == 0;
        Text = nothing ? Localization.Get(mode == CardMode.Write ? "Card_WriteNewHere" : "Card_SelectFirst") : text;
        Kind = mode == CardMode.Write
            ? Localization.Get("Proto_Write")
            : nothing ? "" : Localization.Get(TextKind.IsLookupCandidate(text) ? "Proto_Word" : "Proto_Sentence");
        ReplaceLabel = Localization.Get(nothing ? "Card_Insert" : "Card_Replace");
        CanReplace = capture.CanReplace && (mode == CardMode.Write || !nothing);
        Result = mode == CardMode.Write ? text : "";
        Status = "";

        if (mode == CardMode.Lookup && !nothing)
        {
            _ = LookUpAsync(text, context);
        }
    }

    /// <summary>The card closed: its lookup is no longer wanted.</summary>
    public void Unload()
    {
        _lookup?.Cancel();
        _lookup = null;
    }

    private async Task LookUpAsync(string text, WordContext? context)
    {
        var cancel = new CancellationTokenSource();
        _lookup = cancel;
        Status = Localization.Get(context is null ? "Proto_Looking" : "Proto_LookingInSentence");
        try
        {
            var translation = await host.LookupAsync(text, context, cancel.Token).ConfigureAwait(true);
            if (!cancel.IsCancellationRequested)
            {
                Result = translation;
                Status = "";
            }
        }
        catch (LamhaException e) when (!cancel.IsCancellationRequested)
        {
            // The codes get their words with the real card (phase 4); the log already has the code.
            Status = Localization.Format("Proto_LookupFailed", e.Code);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool CanReplaceNow() => CanReplace && Result.Length > 0;

    [RelayCommand(CanExecute = nameof(CanReplaceNow))]
    private Task ReplaceAsync() => host.ReplaceAsync(Result);

    [RelayCommand]
    private void Copy() => host.Copy(Result.Length > 0 ? Result : Text);

    [RelayCommand]
    private void Close() => host.Close();
}
