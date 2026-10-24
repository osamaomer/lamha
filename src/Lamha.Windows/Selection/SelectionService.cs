using Lamha.Windows.Input;
using Lamha.Windows.Shell;
using ClipboardApi = Lamha.Windows.Clipboard.Clipboard;

namespace Lamha.Windows.Selection;

/// <summary>What was selected in the program in front when the shortcut was pressed.</summary>
/// <param name="Window">That program's window, to paste into later (0 when there was none).</param>
/// <param name="Text">The selected text; "" when nothing was selected.</param>
/// <param name="IsTerminal">A terminal: nothing was copied, and nothing may be pasted with keystrokes.</param>
public sealed record SelectionCapture(nint Window, string Text, bool IsTerminal)
{
    /// <summary>Replace can paste back into the program.</summary>
    public bool CanReplace => Window != 0 && !IsTerminal;
}

/// <summary>
/// Reads the text selected in any program (a simulated Ctrl+C) and puts a result back (Ctrl+V), leaving the user's
/// clipboard as it was. Lamha 2's selection.js, with one change: it watches the clipboard's sequence number instead of
/// writing a marker first, so when nothing is selected the clipboard is never touched.
/// </summary>
/// <param name="owner">The window that owns the clipboard while Lamha uses it (the <see cref="MessageWindow"/>).</param>
public sealed class SelectionService(MessageWindow owner)
{
    private static readonly TimeSpan CopyTimeout = TimeSpan.FromMilliseconds(600);

    private nint Owner => owner.Handle;

    /// <summary>The selection in the window in front. Call it first, before any Lamha window takes the focus.</summary>
    public async Task<SelectionCapture> CaptureAsync()
    {
        var window = ForegroundWindow.Current;
        if (ForegroundWindow.IsTerminal(window))
        {
            return new SelectionCapture(window, "", IsTerminal: true);
        }

        await Keyboard.ReleaseModifiersAsync(TimeSpan.FromMilliseconds(800)).ConfigureAwait(true);
        var saved = ClipboardApi.Save(Owner);
        var before = ClipboardApi.SequenceNumber;
        Keyboard.CtrlChord(VirtualKey.C);

        string? text = null;
        for (var until = Environment.TickCount64 + (long)CopyTimeout.TotalMilliseconds; Environment.TickCount64 < until;)
        {
            await Task.Delay(20).ConfigureAwait(true);
            if (ClipboardApi.SequenceNumber != before)
            {
                await Task.Delay(10).ConfigureAwait(true); // some programs put several formats one after another
                text = ClipboardApi.ReadText(Owner);
                break;
            }
        }

        if (ClipboardApi.SequenceNumber != before && saved.IsComplete)
        {
            ClipboardApi.Restore(Owner, saved);
        }

        return new SelectionCapture(window, string.IsNullOrWhiteSpace(text) ? "" : text, IsTerminal: false);
    }

    /// <summary>
    /// Brings <paramref name="window"/> back to the front and pastes <paramref name="text"/> over its selection.
    /// False when the window is gone or refused the focus: the caller then leaves the text on the clipboard.
    /// </summary>
    public async Task<bool> PasteIntoAsync(nint window, string text)
    {
        if (!ForegroundWindow.IsWindow(window) || ForegroundWindow.IsTerminal(window))
        {
            return false;
        }

        if (!await ForegroundWindow.FocusAsync(window).ConfigureAwait(true))
        {
            return false;
        }

        await Keyboard.ReleaseModifiersAsync(TimeSpan.FromMilliseconds(300)).ConfigureAwait(true);
        var saved = ClipboardApi.Save(Owner);
        if (!ClipboardApi.WriteText(Owner, text, keepOutOfHistory: true))
        {
            return false;
        }

        await Task.Delay(40).ConfigureAwait(true);
        Keyboard.CtrlChord(VirtualKey.V);
        await Task.Delay(350).ConfigureAwait(true); // let the program read the clipboard before it's put back
        if (saved.IsComplete)
        {
            ClipboardApi.Restore(Owner, saved);
        }

        return true;
    }

    /// <summary>Puts <paramref name="text"/> on the clipboard as the user's own copy (Copy, or when Replace couldn't paste).</summary>
    public bool Copy(string text) => ClipboardApi.WriteText(Owner, text, keepOutOfHistory: false);
}
