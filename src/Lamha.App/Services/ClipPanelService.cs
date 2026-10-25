using Lamha.App.ViewModels.Main;
using Lamha.App.ViewModels.Panel;
using Lamha.App.Windows;
using Lamha.Windows.Selection;
using Lamha.Windows.Shell;

namespace Lamha.App.Services;

/// <summary>
/// Opens the clipboard panel over the program in front and carries out what it asks: paste a copy (or a tool's
/// result) into that program, look a copy up in the card, open Settings. Lamha 2's openPanel / pasteFromPanel /
/// pasteResultFromPanel / lookupClip in main.js. <c>notify</c> shows a notification (title, body) when a paste could
/// only copy.
/// </summary>
public sealed partial class ClipPanelService(
    SelectionService selection,
    ClipboardService clipboard,
    Brain brain,
    CardService card,
    CardLifetime lifetime,
    IdleMemory idle,
    Action<string, string> notify) : IClipHost, IClipPanelHost, IDisposable
{
    /// <summary>Clicking elsewhere this soon after the panel opened doesn't close it (Lamha 2's 300 ms).</summary>
    private const long DeactivateGraceMs = 300;

    private ClipPanelWindow? _window;
    private bool _windowSolid;
    private nint _target;
    private long _shownAt;
    private bool _visible;

    /// <summary><c>--theme</c>: the panel's theme for this run instead of the setting.</summary>
    public string? Theme { get; init; }

    /// <summary>Opens Settings at a category ("writing"…) in the main window: an error's Settings button.</summary>
    public Action<string>? SettingsOpener { get; set; }

    /// <summary>
    /// The shortcut: the panel at the mouse, remembering the program in front to paste into; from the tray
    /// (<paramref name="fromTray"/>) there's none, so a paste only copies. Pressed again, it closes.
    /// </summary>
    public void Toggle(bool fromTray = false)
    {
        try
        {
            if (_visible)
            {
                Hide();
                return;
            }

            var started = Environment.TickCount64;
            var front = fromTray ? 0 : ForegroundWindow.Current;

            // The background is chosen when the window is made (Acrylic or solid, the card's setting): a change makes a new one.
            var solid = brain.Settings.Current.CardSolid;
            if (_window is not null && solid != _windowSolid)
            {
                Dispose();
            }

            Animations.Refresh(brain.Settings.Current);
            var created = _window is null;
            var window = _window ??= Create(solid);
            _target = front != window.Handle ? front : 0;
            window.UseTheme(Theme ?? brain.Settings.Current.Theme);
            window.ViewModel.Open();
            _visible = true;
            idle.WindowOpened();
            _shownAt = Environment.TickCount64;
            var cursor = Screens.Cursor;
            window.ShowAt(cursor, Screens.WorkAreaAt(cursor), Screens.ScaleAt(cursor));
            AppLog.Info($"clipboard panel shown in {Environment.TickCount64 - started} ms ({(created ? "new window" : "kept window")}, {(_target == 0 ? "nothing" : "a program")} to paste into)");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Called from a shortcut and the tray: an exception there would end Lamha without a word in the log.
            AppLog.Error("clipboard panel", ex);
        }
    }

    public void Hide()
    {
        if (_window is null || !_visible)
        {
            return;
        }

        _visible = false;
        _window.ViewModel.Leave();
        idle.WindowClosed();
        if (lifetime == CardLifetime.Keep)
        {
            _window.Hide();
        }
        else
        {
            _window.Close();
            _window = null;
        }
    }

    public void Dispose()
    {
        var window = _window;
        _window = null; // first, so Closing doesn't turn the close into a hide
        window?.Close();
    }

    // ----- IClipPanelHost -----

    /// <summary>Enter: the copy (its formatting too, unless plain) into the program; it moves up, as a copy used again.</summary>
    void IClipPanelHost.Paste(ClipRow row, bool plain)
    {
        // Read and marked before the panel closes, which lets go of the decrypted copies.
        if (brain.ClipboardHistory.Get(row.Id) is not { } clip)
        {
            return;
        }

        brain.ClipboardHistory.MarkUsed(row.Id);
        _ = PasteAsync(clip.Text, plain ? null : clip.Html);
    }

    /// <summary>Paste on a tool's result, after its preview: into the program, plain text.</summary>
    void IClipPanelHost.PasteResult(ClipRow row, string text)
    {
        if (text.Trim().Length == 0)
        {
            return;
        }

        if (brain.ClipboardHistory.Get(row.Id) is not null)
        {
            brain.ClipboardHistory.MarkUsed(row.Id);
        }

        _ = PasteAsync(text, null);
    }

    void IClipPanelHost.Close() => Hide();

    // ----- IClipHost -----

    void IClipHost.Copy(string text) => selection.Copy(text);

    void IClipHost.Announce(string text) => _window?.Announce(text);

    void IClipHost.Toast(string text, string? action, Action? run) => _window?.ViewModel.ShowToast(text, action, run);

    /// <summary>Look up: the card on the copy, as Alt+Shift+L opens it; the panel goes.</summary>
    void IClipHost.Translate(string text)
    {
        var target = _target;
        Hide();
        card.LookUp(text, target);
    }

    /// <summary>The main window counts the words to review again whenever one of its pages opens.</summary>
    void IClipHost.ReviewChanged()
    {
    }

    void IClipHost.OpenSettings(string section)
    {
        Hide();
        SettingsOpener?.Invoke(section);
    }

    /// <summary>The panel goes first, so the program gets the focus back; then Ctrl+V. Lamha's own copy isn't recorded.</summary>
    private async Task PasteAsync(string text, string? html)
    {
        var target = _target;
        Hide();
        try
        {
            var pasted = await clipboard.Monitor.SuppressAsync(() => selection.PasteClipAsync(target, text, html)).ConfigureAwait(true);
            AppLog.Info($"clipboard panel: {(pasted ? "pasted" : "copied only")} {text.Length} characters{(html is null ? "" : " with formatting")}");
            if (!pasted)
            {
                notify(Localization.Get("Note_Copied"), Localization.Get("Note_PasteYourself"));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Error("clipboard panel paste", ex);
        }
    }

    private ClipPanelWindow Create(bool solid)
    {
        _windowSolid = solid;
        var window = new ClipPanelWindow(new ClipPanelViewModel(new ClipboardViewModel(brain, clipboard, this, panel: true), this), solid);
        window.Deactivated += () =>
        {
            if (Environment.TickCount64 - _shownAt > DeactivateGraceMs)
            {
                Hide();
            }
        };
        window.AppWindow.Closing += (_, args) =>
        {
            // Alt+F4 on a kept panel only hides it.
            if (lifetime == CardLifetime.Keep && _window is not null)
            {
                args.Cancel = true;
                Hide();
            }
        };
        return window;
    }
}
