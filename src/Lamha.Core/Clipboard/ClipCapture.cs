using System.Text;
using Lamha.Core.Settings;

namespace Lamha.Core.Clipboard;

/// <summary>Why a copy wasn't recorded (logged as it is: never the copy itself).</summary>
public enum ClipSkip
{
    /// <summary>Recorded.</summary>
    None,

    /// <summary>History is off or paused.</summary>
    Off,

    /// <summary>The program asked clipboard viewers to ignore it (<c>ExcludeClipboardContentFromMonitorProcessing</c>).</summary>
    ExcludedFormat,

    /// <summary>The program marked it as not for clipboard history (<c>CanIncludeInClipboardHistory</c> = 0).</summary>
    ExcludedFromHistory,

    /// <summary>A program in the excluded list copied it.</summary>
    ExcludedApp,

    /// <summary>Another program held the clipboard.</summary>
    Busy,

    Empty,

    TooLong,

    CardNumber,
}

/// <summary>
/// What the clipboard listener knows about a copy before reading its text: the formats a program uses to say "don't
/// keep this", and who copied it.
/// </summary>
/// <param name="HasExcludeFormat"><c>ExcludeClipboardContentFromMonitorProcessing</c> or <c>Clipboard Viewer Ignore</c> is on the clipboard.</param>
/// <param name="CanIncludeInHistory">The <c>CanIncludeInClipboardHistory</c> value; null when the format isn't there, 0 when it says no or can't be read.</param>
/// <param name="SourceApp">The program that copied it ("notepad.exe").</param>
public readonly record struct ClipFormats(bool HasExcludeFormat, uint? CanIncludeInHistory, string SourceApp);

/// <summary>
/// The rules that decide whether a copy goes into clipboard history, in Lamha 2's order (clipboard-monitor.js
/// <c>process()</c>): off or paused, the program's own "don't keep this" marks, excluded programs (whose copies are
/// never even read), then the text itself.
/// </summary>
public static class ClipCapture
{
    /// <summary>Longer copies aren't kept (characters).</summary>
    public const int MaxText = 200_000;

    /// <summary>A copy's formatting (its HTML) is kept only up to this size (bytes, UTF-8).</summary>
    public const int MaxHtmlBytes = 500 * 1024;

    /// <summary>Whether to read the copy's text at all.</summary>
    public static ClipSkip BeforeReading(ClipFormats formats, LamhaSettings settings, bool paused)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.ClipboardEnabled || paused)
        {
            return ClipSkip.Off;
        }

        if (formats.HasExcludeFormat)
        {
            return ClipSkip.ExcludedFormat;
        }

        // 0, or unreadable: stay on the safe side.
        if (formats.CanIncludeInHistory is { } can && can != 1)
        {
            return ClipSkip.ExcludedFromHistory;
        }

        return settings.ClipboardExcludedApps.Contains(formats.SourceApp, StringComparer.OrdinalIgnoreCase)
            ? ClipSkip.ExcludedApp
            : ClipSkip.None;
    }

    /// <summary>Whether to keep the text that was read (null: the clipboard was busy).</summary>
    public static ClipSkip AfterReading(string? text, LamhaSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (text is null)
        {
            return ClipSkip.Busy;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return ClipSkip.Empty;
        }

        if (text.Length > MaxText)
        {
            return ClipSkip.TooLong;
        }

        return settings.ClipboardSkipCards && ClipPrivacy.IsCardNumber(text) ? ClipSkip.CardNumber : ClipSkip.None;
    }

    /// <summary>The copy's HTML when it's small enough to keep, otherwise null.</summary>
    public static string? HtmlToKeep(string? html) =>
        !string.IsNullOrEmpty(html) && Encoding.UTF8.GetByteCount(html) <= MaxHtmlBytes ? html : null;
}
