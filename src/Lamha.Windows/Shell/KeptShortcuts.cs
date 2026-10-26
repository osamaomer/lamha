using System.Text;

namespace Lamha.Windows.Shell;

/// <summary>
/// Lamha 3's shortcuts kept through Lamha 2's uninstaller, which deletes the Start menu's and the desktop's "Lamha.lnk"
/// whatever they point at (app-builder-lib's uninstaller.nsh), and those are Velopack's names for Lamha 3's too. Each one
/// that points into Lamha 3's folder is copied aside first and put back once the uninstaller is done. The copies are on
/// disk, so a Lamha closed in between puts them back at its next start.
/// </summary>
/// <param name="folder">Where the copies are kept, in Lamha's data folder.</param>
/// <param name="lamha3Folder">Lamha 3's install folder (Velopack's, %LOCALAPPDATA%\Lamha): a shortcut naming it is Lamha 3's.</param>
public sealed class KeptShortcuts(string folder, string lamha3Folder)
{
    private const string ListName = "shortcuts.txt";

    /// <summary>Where Velopack puts Lamha 3's shortcuts, and where Lamha 2's uninstaller deletes "Lamha.lnk".</summary>
    public static IReadOnlyList<string> Places { get; } =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Lamha.lnk"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Lamha.lnk"),
    ];

    /// <summary>Copies are waiting to be put back.</summary>
    public bool IsPending => File.Exists(Path.Combine(folder, ListName));

    /// <summary>Copies of the <paramref name="shortcuts"/> that are Lamha 3's; how many.</summary>
    public int Keep(IEnumerable<string> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        var kept = new List<string>();
        foreach (var shortcut in shortcuts)
        {
            if (File.Exists(shortcut) && PointsInto(File.ReadAllBytes(shortcut), lamha3Folder))
            {
                Directory.CreateDirectory(folder);
                File.Copy(shortcut, Copy(kept.Count), overwrite: true);
                kept.Add(shortcut);
            }
        }

        if (kept.Count > 0)
        {
            File.WriteAllLines(Path.Combine(folder, ListName), kept);
        }

        return kept.Count;
    }

    /// <summary>Each kept shortcut that's gone put back; the copies then go. How many came back.</summary>
    public int RestoreMissing()
    {
        var list = Path.Combine(folder, ListName);
        if (!File.Exists(list))
        {
            return 0;
        }

        var back = 0;
        var shortcuts = File.ReadAllLines(list);
        for (var i = 0; i < shortcuts.Length; i++)
        {
            if (!File.Exists(shortcuts[i]) && File.Exists(Copy(i)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(shortcuts[i])!);
                File.Copy(Copy(i), shortcuts[i]);
                back++;
            }
        }

        Directory.Delete(folder, recursive: true);
        return back;
    }

    /// <summary>
    /// Whether a .lnk file names a path in <paramref name="installFolder"/>: shortcuts keep their target as UTF-16 text
    /// (and in the system's code page in older parts), so it's looked for in both. Only the folder's last two parts
    /// ("\Local\Lamha\") are compared: the user's folder may be written short (C:\Users\ARTWOR~1) or as %USERPROFILE%.
    /// Lamha 2's own shortcut names its folder (\Local\Programs\Lamha\ or \Program Files\Lamha\), never Lamha 3's.
    /// </summary>
    internal static bool PointsInto(byte[] link, string installFolder)
    {
        var folder = Path.TrimEndingDirectorySeparator(installFolder);
        var sep = Path.DirectorySeparatorChar;
        var wanted = $"{sep}{Path.GetFileName(Path.GetDirectoryName(folder))}{sep}{Path.GetFileName(folder)}{sep}";
        return Encoding.Unicode.GetString(link).Contains(wanted, StringComparison.OrdinalIgnoreCase)
            || (link.Length > 1 && Encoding.Unicode.GetString(link, 1, link.Length - 1).Contains(wanted, StringComparison.OrdinalIgnoreCase))
            || Encoding.Latin1.GetString(link).Contains(wanted, StringComparison.OrdinalIgnoreCase);
    }

    private string Copy(int i) => Path.Combine(folder, $"{i}.lnk");
}
