namespace Lamha.App.ViewModels.Main;

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
}
