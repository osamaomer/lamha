using Lamha.Windows.Shell;

namespace Lamha.App.Services;

/// <summary>
/// Lamha's jump list (docs/design/system.md, the study's features): Translate, Write, Review and Clipboard on a
/// right-click of its taskbar button, each with its pane icon. A task starts Lamha with <c>--page=</c>, which the
/// running Lamha takes as a second start (<see cref="PageOf"/>); with <c>--profile</c>, the tasks keep the profile.
/// </summary>
public static class JumpListService
{
    /// <summary>The pages offered, in the pane's order, and the main window's name for each.</summary>
    private static readonly (string Page, string Label)[] Pages =
    [
        ("translate", "Main_Translate"),
        ("write", "Main_Write"),
        ("review", "Main_Review"),
        ("clipboard", "Main_Clipboard"),
    ];

    /// <summary>The tasks in the interface's language: at start, and when first run changes the language.</summary>
    public static void Refresh(string? profile)
    {
        if (Environment.ProcessPath is not { } exe)
        {
            return;
        }

        var keep = profile is null ? "" : $" --profile=\"{profile}\"";
        var tasks = Pages
            .Select(p => new JumpTask(Localization.Get(p.Label), $"--page={p.Page}{keep}", Path.Combine(AppContext.BaseDirectory, "Assets", "Jump", p.Page + ".ico")))
            .ToList();
        _ = SetAsync(exe, tasks);
    }

    /// <summary>
    /// The page a second start asks for (a jump list task's <c>--page=write</c>), or null. Only the pane's pages: a
    /// screenshot's <c>--page=settings:about</c> isn't a jump list's.
    /// </summary>
    public static string? PageOf(string arguments)
    {
        foreach (var (page, _) in Pages)
        {
            if (arguments.Contains($"--page={page}", StringComparison.OrdinalIgnoreCase))
            {
                return page;
            }
        }

        return null;
    }

    private static async Task SetAsync(string exe, List<JumpTask> tasks)
    {
        if (!await JumpList.SetAsync(exe, tasks).ConfigureAwait(false))
        {
            AppLog.Info("jump list not set");
        }
    }
}
