namespace Lamha.App.ViewModels.Main;

/// <summary>What was chosen in a question: its first choice, the other one, or nothing (Cancel, Esc).</summary>
public enum Answer
{
    Cancel,
    Confirm,
    Other,
}

/// <summary>What a page of the main window asks of the window.</summary>
public interface IPageHost
{
    /// <summary>The user's Copy: on the clipboard and in Windows' clipboard history, as any program's.</summary>
    void Copy(string text);

    void OpenLink(Uri uri);

    /// <summary>Narrator says it.</summary>
    void Announce(string text);

    /// <summary>
    /// A short message at the foot of the window ("Copied"); with an action ("Undo") it stays longer and offers it
    /// (Lamha 2's <c>flash()</c>).
    /// </summary>
    void Toast(string text, string? action = null, Action? run = null);

    /// <summary>The Translate page, with <paramref name="text"/> in its box and looked up (a word in the history).</summary>
    void Translate(string text);

    /// <summary>The Write page, with <paramref name="text"/> in its draft (Translate's "Writing tools").</summary>
    void Write(string text);

    /// <summary>A word went into the review list or out of it: the count on Review is read again.</summary>
    void ReviewChanged();

    /// <summary>Settings, at the category <paramref name="section"/> ("writing", "translation", "offline"): an error's Settings button.</summary>
    void OpenSettings(string section);

    /// <summary>
    /// Lamha 2's question before deleting something (shared/dialog.js <c>ask()</c>): <paramref name="question"/> and the
    /// line under it, <paramref name="confirm"/> ("Delete"), another choice if given ("Keep it"), and Cancel, the
    /// default (Esc too).
    /// </summary>
    Task<Answer> AskAsync(string question, string confirm, string text = "", string? other = null);
}
