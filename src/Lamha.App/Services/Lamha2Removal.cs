using System.Globalization;
using Lamha.Data.Import;
using Lamha.Windows.Shell;

namespace Lamha.App.Services;

/// <summary>
/// Lamha 2 removed once Lamha 3 replaces it (decision 0019), in the released Lamha 3 only: a beta runs next to Lamha 2.
/// First run's Ready offers it when Lamha 2 is installed (on after a move, off after Start fresh, the user's call of
/// 2026-10-26); its Start runs Lamha 2's uninstaller quietly, keeps Lamha 3's shortcuts through it, and removes Lamha 2's
/// start-with-Windows entry. Lamha 2's folder is emptied, but for its Wikipedias, 30 days after its data moved, at a
/// start once Lamha 2 is gone. That date is a file in Lamha's data folder, so a backup restored elsewhere never carries it.
/// </summary>
/// <param name="dataFolder">Lamha's data folder (or a <c>--profile</c> folder).</param>
/// <param name="lamha2Folder">Lamha 2's data folder (%APPDATA%\Lamha, or <c>--lamha2</c>'s copy).</param>
/// <param name="enabled">The released Lamha 3, or <c>--remove-lamha2</c> to try it in another build.</param>
public sealed class Lamha2Removal(string dataFolder, string lamha2Folder, bool enabled)
{
    private const string MovedName = "lamha2-moved.txt";
    private static readonly TimeSpan FolderKept = TimeSpan.FromDays(30);
    private static readonly TimeSpan UninstallWait = TimeSpan.FromMinutes(5);

    /// <summary>Lamha 3's install folder: Velopack's, the one above <c>current\</c>.</summary>
    private static readonly string Lamha3Folder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)) ?? AppContext.BaseDirectory;

    /// <summary>Lamha 2 as installed on this PC, when removing it is offered; null otherwise (or in a beta).</summary>
    public Lamha2Install? Installed
    {
        get
        {
            if (!enabled)
            {
                return null;
            }

            try
            {
                return Lamha2Install.Find();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                AppLog.Error("Lamha 2: finding its install", ex);
                return null;
            }
        }
    }

    /// <summary>Lamha 2's data moved: the 30 days start now (in a beta too, so the released Lamha 3 counts from the move).</summary>
    public void Moved()
    {
        try
        {
            Directory.CreateDirectory(dataFolder);
            File.WriteAllText(Path.Combine(dataFolder, MovedName), DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Lamha 2: the move's date", ex);
        }
    }

    /// <summary>Ready's Start with Remove Lamha 2 on: its uninstaller, then what it leaves behind once it's done.</summary>
    public async Task RemoveAsync(Lamha2Install install)
    {
        ArgumentNullException.ThrowIfNull(install);
        try
        {
            var kept = Kept().Keep(KeptShortcuts.Places);
            if (!install.Start())
            {
                AppLog.Info("Lamha 2: not removed (Windows' prompt was cancelled)");
                Kept().RestoreMissing(); // nothing was deleted: the copies go
                return;
            }

            AppLog.Info($"Lamha 2 {install.Version}: its uninstaller started ({(install.ForAllUsers ? "all users" : "this user")}, {kept} shortcuts kept)");
            for (var until = DateTime.UtcNow + UninstallWait; Lamha2Install.Find() is not null && DateTime.UtcNow < until;)
            {
                await Task.Delay(1000).ConfigureAwait(false);
            }

            Finish();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An uninstaller that wouldn't start, a shortcut that couldn't be copied: Lamha 2 stays, Lamha 3 goes on.
            AppLog.Error("Lamha 2: removing it", ex);
        }
    }

    /// <summary>
    /// At start, what a removal left: shortcuts to put back (Lamha closed while the uninstaller ran), and Lamha 2's folder
    /// once 30 days have passed since its data moved and Lamha 2 is no longer installed.
    /// </summary>
    public void AtStart()
    {
        if (!enabled)
        {
            return;
        }

        try
        {
            if (Kept().IsPending && Lamha2Install.Find() is null)
            {
                Finish();
            }

            var moved = Path.Combine(dataFolder, MovedName);
            if (File.Exists(moved) && DateTimeOffset.TryParse(File.ReadAllText(moved), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                && DateTimeOffset.UtcNow - at >= FolderKept && Lamha2Install.Find() is null)
            {
                var gone = new Lamha2Folder(lamha2Folder, _ => null).ClearKeepingWikipedia();
                File.Delete(moved);
                AppLog.Info($"Lamha 2's folder cleared after 30 days ({gone} entries; its Wikipedias kept)");
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Error("Lamha 2: at start", ex);
        }
    }

    /// <summary>Lamha 2 gone: its start-with-Windows entry too, and Lamha 3's shortcuts back where its uninstaller deleted them.</summary>
    private void Finish()
    {
        if (Lamha2Install.Find() is not null)
        {
            AppLog.Info("Lamha 2: still installed after its uninstaller (tried again at the next start)");
            return;
        }

        Lamha2Install.RemoveStartEntry();
        var back = Kept().RestoreMissing();
        AppLog.Info($"Lamha 2 removed ({back} shortcuts put back)");
    }

    private KeptShortcuts Kept() => new(Path.Combine(dataFolder, "lamha2-shortcuts"), Lamha3Folder);
}
