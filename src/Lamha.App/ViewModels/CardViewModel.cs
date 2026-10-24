using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lamha.App.Services;
using Lamha.Core.Text;
using Lamha.Windows.Selection;

namespace Lamha.App.ViewModels;

/// <summary>What the card can ask of whoever opened it.</summary>
public interface ICardHost
{
    /// <summary>Closes the card and pastes <paramref name="text"/> over the selection in the program it came from.</summary>
    Task ReplaceAsync(string text);

    void Copy(string text);

    void Close();
}

/// <summary>
/// The prototype card: what was selected, what kind of text it is, and a result Replace can put back.
/// Phase 4 replaces it with the real card (meanings, translation, writing tools).
/// </summary>
public sealed partial class CardViewModel(ICardHost host) : ObservableObject
{
    [ObservableProperty]
    public partial string Text { get; private set; } = "";

    [ObservableProperty]
    public partial string Kind { get; private set; } = "";

    [ObservableProperty]
    public partial string Result { get; private set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReplaceCommand))]
    public partial bool CanReplace { get; private set; }

    public void Load(SelectionCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        var text = capture.Text.Trim();
        Text = text.Length > 0 ? text : Localization.Get("Card_SelectFirst");
        Kind = text.Length == 0 ? "" : Localization.Get(TextKind.IsLookupCandidate(text) ? "Proto_Word" : "Proto_Sentence");
        Result = text.ToUpperInvariant();
        CanReplace = capture.CanReplace && text.Length > 0;
    }

    [RelayCommand(CanExecute = nameof(CanReplace))]
    private Task ReplaceAsync() => host.ReplaceAsync(Result);

    [RelayCommand]
    private void Copy() => host.Copy(Result.Length > 0 ? Result : Text);

    [RelayCommand]
    private void Close() => host.Close();
}
