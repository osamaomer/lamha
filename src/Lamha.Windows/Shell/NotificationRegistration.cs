using Microsoft.Win32;

namespace Lamha.Windows.Shell;

/// <summary>A link of Lamha's own, taken apart: <c>lamha:show?key=reluctant</c> is "show" with key = "reluctant".</summary>
public sealed record AppLink(string Action, IReadOnlyDictionary<string, string> Values);

/// <summary>
/// What Windows needs to show Lamha's notifications and hand their clicks back, for this Windows user only. Lamha is
/// an app without a package (Velopack), so it says who it is in the registry (an AppUserModelId with its name and icon,
/// as Windows asks of such apps), and its notifications' clicks and buttons are links of its own
/// (<c>lamha:show?key=reluctant</c>): Windows starts Lamha with the link, and a Lamha already running gets it from
/// the new one (Program's single instance), whether the notification is on screen or in the notification centre.
/// The Windows App SDK's app notifications would need its Singleton package, which a self-contained app doesn't have.
/// </summary>
/// <param name="appId">The AppUserModelId the notifications are shown under.</param>
/// <param name="scheme">The links' scheme, without the colon ("lamha").</param>
/// <param name="command">What Windows runs for a link, with "%1" where the link goes.</param>
/// <param name="classesPath">HKEY_CURRENT_USER's Software\Classes; another key in tests.</param>
public sealed class NotificationRegistration(string appId, string scheme, string command, string classesPath = @"Software\Classes")
{
    public string AppId => appId;

    /// <summary>
    /// Lamha's name and icon on its notifications (and in Windows' notification settings), and its links: written each
    /// start, so a moved install folder or another interface language is caught up with.
    /// </summary>
    public void Register(string displayName, string iconPath)
    {
        using (var app = Registry.CurrentUser.CreateSubKey($@"{classesPath}\AppUserModelId\{appId}"))
        {
            app.SetValue("DisplayName", displayName);
            app.SetValue("IconUri", iconPath);
        }

        using var link = Registry.CurrentUser.CreateSubKey($@"{classesPath}\{scheme}");
        link.SetValue("", $"URL:{displayName}");
        link.SetValue("URL Protocol", "");
        using var open = link.CreateSubKey(@"shell\open\command");
        open.SetValue("", command);
    }

    /// <summary>Uninstalling: both keys go.</summary>
    public void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"{classesPath}\AppUserModelId\{appId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"{classesPath}\{scheme}", throwOnMissingSubKey: false);
    }

    /// <summary>"lamha:grade?key=bank&amp;grade=good", each value escaped (a word may hold ? &amp; = or spaces).</summary>
    public string Link(string action, IReadOnlyDictionary<string, string>? values = null)
    {
        var link = $"{scheme}:{action}";
        return values is { Count: > 0 }
            ? link + "?" + string.Join('&', values.Select(v => $"{Uri.EscapeDataString(v.Key)}={Uri.EscapeDataString(v.Value)}"))
            : link;
    }

    /// <summary>The link Lamha was started with, found in its command line (Windows puts it there, in quotes), or null.</summary>
    public AppLink? Find(string commandLine)
    {
        var start = commandLine.IndexOf(scheme + ":", StringComparison.OrdinalIgnoreCase);
        if (start < 0 || (start > 0 && commandLine[start - 1] is not ('"' or ' ')))
        {
            return null;
        }

        var end = commandLine.IndexOfAny(['"', ' '], start);
        var link = end < 0 ? commandLine[start..] : commandLine[start..end];
        var rest = link[(scheme.Length + 1)..];
        var query = rest.IndexOf('?', StringComparison.Ordinal);
        var action = query < 0 ? rest : rest[..query];
        var values = new Dictionary<string, string>();
        if (query >= 0)
        {
            foreach (var pair in rest[(query + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = pair.IndexOf('=', StringComparison.Ordinal);
                if (equals > 0)
                {
                    values[Uri.UnescapeDataString(pair[..equals])] = Uri.UnescapeDataString(pair[(equals + 1)..]);
                }
            }
        }

        return action.Length > 0 ? new AppLink(action, values) : null;
    }
}
