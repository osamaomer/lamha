using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Lamha.Windows.Shell;
using global::Windows.Data.Xml.Dom;
using global::Windows.UI.Notifications;

namespace Lamha.App.Services;

/// <summary>A button on a notification: its words, and what a click on it asks for (an action and its values).</summary>
public sealed record NoteButton(string Text, string Action, IReadOnlyDictionary<string, string>? Values = null);

/// <summary>
/// A notification's content. <see cref="Title"/> and <see cref="Body"/> are its lines; <see cref="Word"/> and
/// <see cref="WordLine"/> a word under them, set apart (Time to review); <see cref="Tag"/> lets a later one replace it.
/// </summary>
public sealed record Note(string Title, string Body = "")
{
    public string? Word { get; init; }

    public string? WordLine { get; init; }

    /// <summary>What a click on the notification itself asks for; none: nothing.</summary>
    public string? Action { get; init; }

    public IReadOnlyList<NoteButton> Buttons { get; init; } = [];

    public string Tag { get; init; } = "";

    /// <summary>Shown without Windows' sound (the next word after an answer).</summary>
    public bool Silent { get; init; }
}

/// <summary>
/// Lamha's notifications as Windows draws them (its toasts, so they can have buttons), titled without Lamha 2's
/// "لمحة — " (Windows shows the app's name and icon: the tray design, 2026-10-25). Clicks come back as Lamha's links
/// (<see cref="NotificationRegistration"/>), from the screen or the notification centre, with Lamha running or not:
/// <see cref="Open"/> takes them. When Windows' notifications can't be used, the tray icon's own (no buttons).
/// </summary>
public sealed partial class NotificationService
{
    private const string Group = "lamha";
    private readonly NotificationRegistration _registration;
    private readonly Action<string, string> _fallback;
    private ToastNotifier? _notifier;

    /// <param name="registration">Lamha's name, icon and links in Windows.</param>
    /// <param name="fallback">The tray icon's notification (title, body).</param>
    public NotificationService(NotificationRegistration registration, Action<string, string> fallback)
    {
        _registration = registration;
        _fallback = fallback;
    }

    /// <summary>A click on a notification or one of its buttons: the link's action and values.</summary>
    public event Action<AppLink>? Clicked;

    /// <summary>Tells Windows Lamha's name (in the interface language), icon and links, each start.</summary>
    public void Register()
    {
        try
        {
            _registration.Register(Localization.Get("Common_Lamha"), Path.Combine(AppContext.BaseDirectory, "Assets", "Lamha-256.png"));
            // Its Setting can't be read for an app registered this way ("element not found"): Windows still shows them.
            _notifier = ToastNotificationManager.CreateToastNotifier(_registration.AppId);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException)
        {
            AppLog.Error("notifications", ex);
        }
    }

    /// <summary>
    /// A command line (Lamha's own at start, or a second start's): a notification's link in it is handed to
    /// <see cref="Clicked"/>. True when there was one.
    /// </summary>
    public bool Open(string commandLine)
    {
        if (_registration.Find(commandLine) is not { } link)
        {
            return false;
        }

        AppLog.Info($"notification clicked: {link.Action}");
        Clicked?.Invoke(link);
        return true;
    }

    /// <summary>A notification with a title and a line (Couldn't replace, Shortcut in use…).</summary>
    public void Show(string title, string body) => Show(new Note(title, body));

    public void Show(Note note)
    {
        if (_notifier is null)
        {
            _fallback(note.Title, note.Body.Length > 0 ? note.Body : note.Word ?? "");
            return;
        }

        try
        {
            var xml = new XmlDocument();
            xml.LoadXml(Xml(note));
            var toast = new ToastNotification(xml) { Group = Group };
            if (note.Tag.Length > 0)
            {
                toast.Tag = note.Tag;
            }

            _notifier.Show(toast);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            AppLog.Error("notification", ex);
            _fallback(note.Title, note.Body);
        }
    }

    /// <summary>Takes away the notifications with <paramref name="tag"/>, from the screen and the notification centre.</summary>
    public void Remove(string tag)
    {
        if (_notifier is null)
        {
            return;
        }

        try
        {
            ToastNotificationManager.History.Remove(tag, Group, _registration.AppId);
        }
        catch (COMException ex)
        {
            AppLog.Error("notification", ex);
        }
    }

    /// <summary>
    /// The notification as Windows' XML: the title and line, the word in a group of its own (a toast takes only three
    /// lines outside one), and the buttons, each click a link. Every value is escaped.
    /// </summary>
    private string Xml(Note note)
    {
        var xml = new StringBuilder("<toast");
        if (note.Action is { } action)
        {
            xml.Append(" activationType=\"protocol\" launch=\"").Append(Escape(_registration.Link(action))).Append('"');
        }

        // No lang: Windows lays notifications out in its own display language whatever the content's (tried 2026-10-26).
        xml.Append("><visual><binding template=\"ToastGeneric\">");
        xml.Append("<text>").Append(Escape(note.Title)).Append("</text>");
        if (note.Body.Length > 0)
        {
            xml.Append("<text>").Append(Escape(note.Body)).Append("</text>");
        }

        if (note.Word is { Length: > 0 } word)
        {
            xml.Append("<group><subgroup><text hint-style=\"base\">").Append(Escape(word)).Append("</text>");
            if (note.WordLine is { Length: > 0 } line)
            {
                xml.Append("<text hint-style=\"captionSubtle\" hint-wrap=\"true\">").Append(Escape(line)).Append("</text>");
            }

            xml.Append("</subgroup></group>");
        }

        xml.Append("</binding></visual>");
        if (note.Silent)
        {
            xml.Append("<audio silent=\"true\"/>");
        }

        if (note.Buttons.Count > 0)
        {
            xml.Append("<actions>");
            foreach (var button in note.Buttons)
            {
                xml.Append("<action activationType=\"protocol\" content=\"").Append(Escape(button.Text))
                    .Append("\" arguments=\"").Append(Escape(_registration.Link(button.Action, button.Values))).Append("\"/>");
            }

            xml.Append("</actions>");
        }

        return xml.Append("</toast>").ToString();
    }

    private static string Escape(string text) => SecurityElement.Escape(text);
}
