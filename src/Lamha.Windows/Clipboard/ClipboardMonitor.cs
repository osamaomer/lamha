using global::Windows.Win32;
using Lamha.Core.Clipboard;
using Lamha.Core.Settings;
using Lamha.Windows.Shell;

namespace Lamha.Windows.Clipboard;

/// <summary>
/// Notices every copy the user makes in any program and gives it to <see cref="ClipboardHistory"/>, following
/// <see cref="ClipCapture"/>'s rules. Lamha's own clipboard work (the shortcut's Ctrl+C) runs inside
/// <see cref="SuppressAsync{T}"/> and is never recorded; Lamha's pastes and restores are marked private, which the rules
/// skip too. Lamha 2's desktop/clipboard-monitor.js. Copies are never logged, only why one was skipped.
/// </summary>
/// <param name="window">Receives WM_CLIPBOARDUPDATE, and owns the clipboard while it's read.</param>
/// <param name="history">Where copies go.</param>
/// <param name="settings">History on or off, and its privacy rules.</param>
/// <param name="clock">When a copy was made.</param>
public sealed class ClipboardMonitor(MessageWindow window, ClipboardHistory history, SettingsService settings, TimeProvider clock) : IDisposable
{
    /// <summary>Office and browsers write several formats per copy: wait for them to settle.</summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);

    /// <summary>After Lamha's own clipboard work, late updates still belong to it.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(400);

    private readonly HashSet<uint> _seen = [];
    private bool _listening;
    private int _suppressed;
    private int _pending;
    private Task? _settling;
    private uint _lastSeq;

    /// <summary>A copy was skipped, with why (for the log: never the copy).</summary>
    public event Action<ClipSkip>? Skipped;

    /// <summary>A copy was recorded, with its length (for the log).</summary>
    public event Action<int>? Recorded;

    /// <summary>Reading or recording a copy failed (the exception's type is logged, never its message).</summary>
    public event Action<Exception>? Failed;

    /// <summary>The tray's pause: nothing is recorded meanwhile.</summary>
    public bool Paused { get; set; }

    public bool IsListening => _listening;

    /// <summary>Listens while history is on in the settings, and stops when it's off. Call it when they change.</summary>
    public void Apply()
    {
        if (settings.Current.ClipboardEnabled)
        {
            Listen();
        }
        else
        {
            Unlisten();
        }
    }

    /// <summary>Runs Lamha's own clipboard work; nothing it (or the next 400 ms) puts on the clipboard is recorded.</summary>
    public async Task<T> SuppressAsync<T>(Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        // A copy still settling (copied, then the shortcut within 150 ms) is the user's: record it first, rather than
        // let the suppression that starts now swallow it.
        if (_settling is { IsCompleted: false } && _suppressed == 0)
        {
            _pending++;
            await ProcessAsync().ConfigureAwait(true);
        }

        _suppressed++;
        try
        {
            return await work().ConfigureAwait(true);
        }
        finally
        {
            MarkSeen();
            _ = ReleaseLaterAsync();
        }
    }

    public void Dispose() => Unlisten();

    private async Task ReleaseLaterAsync()
    {
        await Task.Delay(Grace).ConfigureAwait(true);
        MarkSeen();
        _suppressed--;
    }

    private void Listen()
    {
        if (_listening)
        {
            return;
        }

        _lastSeq = Clipboard.SequenceNumber; // what's on the clipboard already isn't a new copy
        if (PInvoke.AddClipboardFormatListener(window.Handle))
        {
            window.ClipboardUpdated += OnUpdate;
            _listening = true;
        }
    }

    private void Unlisten()
    {
        if (!_listening)
        {
            return;
        }

        window.ClipboardUpdated -= OnUpdate;
        PInvoke.RemoveClipboardFormatListener(window.Handle);
        _listening = false;
        _pending++; // a copy still settling is dropped
    }

    private void MarkSeen()
    {
        _seen.Add(Clipboard.SequenceNumber);
        if (_seen.Count > 200)
        {
            _seen.Remove(_seen.First());
        }
    }

    private void OnUpdate()
    {
        if (_suppressed > 0)
        {
            MarkSeen();
            return;
        }

        var ticket = ++_pending;
        _settling = SettleAsync(ticket);
    }

    private async Task SettleAsync(int ticket)
    {
        await Task.Delay(Debounce).ConfigureAwait(true);
        if (ticket == _pending)
        {
            await ProcessAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Reads the clipboard once it has settled and applies the rules, in order.</summary>
    private async Task ProcessAsync()
    {
        try
        {
            var seq = Clipboard.SequenceNumber;
            if (seq == _lastSeq || _seen.Contains(seq))
            {
                return;
            }

            _lastSeq = seq;
            if (_suppressed > 0)
            {
                _seen.Add(seq);
                return;
            }

            var current = settings.Current;
            var owner = window.Handle;
            var formats = new ClipFormats(
                Clipboard.HasFormat("ExcludeClipboardContentFromMonitorProcessing") || Clipboard.HasFormat("Clipboard Viewer Ignore"),
                Clipboard.HasFormat("CanIncludeInClipboardHistory") ? Clipboard.ReadDword(owner, "CanIncludeInClipboardHistory") ?? 0 : null,
                SourceApp());
            var skip = ClipCapture.BeforeReading(formats, current, Paused);
            if (skip == ClipSkip.Off)
            {
                return;
            }

            if (skip != ClipSkip.None)
            {
                Skipped?.Invoke(skip);
                return;
            }

            if (!Clipboard.HasText)
            {
                return; // a picture or files: history keeps text only
            }

            var text = await ReadWithRetryAsync(() => Clipboard.ReadText(owner)).ConfigureAwait(true);
            skip = ClipCapture.AfterReading(text, current);
            if (skip != ClipSkip.None)
            {
                if (skip != ClipSkip.Empty)
                {
                    Skipped?.Invoke(skip);
                }

                return;
            }

            var html = Clipboard.HasFormat("HTML Format")
                ? ClipCapture.HtmlToKeep(await ReadWithRetryAsync(() => Clipboard.ReadHtml(owner, ClipCapture.MaxHtmlBytes)).ConfigureAwait(true))
                : null;

            // The clipboard changed, or Lamha started using it, while it was read: the next update takes care of it.
            if (Clipboard.SequenceNumber != seq || _suppressed > 0 || !_listening)
            {
                return;
            }

            history.Ingest(new CapturedClip(text!, html, formats.SourceApp, clock.GetUtcNow().ToUnixTimeMilliseconds()));
            Recorded?.Invoke(text!.Length);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Failed?.Invoke(ex); // a copy must never break the listener
        }
    }

    /// <summary>Who copied it: the clipboard's owner, else the program in front.</summary>
    private static string SourceApp()
    {
        var name = ForegroundWindow.ProcessName(PInvoke.GetClipboardOwner());
        if (name.Length == 0)
        {
            name = ForegroundWindow.ProcessName(ForegroundWindow.Current);
        }

        return name.Length > 0 ? name : "unknown";
    }

    /// <summary>Another program may hold the clipboard: three more tries, 50 ms apart, then give up quietly.</summary>
    private static async Task<string?> ReadWithRetryAsync(Func<string?> read)
    {
        for (var i = 0; i < 4; i++)
        {
            var value = read();
            if (value is not null)
            {
                return value;
            }

            if (i < 3)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }
        }

        return null;
    }
}
