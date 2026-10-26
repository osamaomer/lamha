using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace Lamha.Windows.Shell;

/// <summary>
/// Lamha 2 as its setup installed it (electron-builder's NSIS installer), found by its uninstall entry and removed by its
/// own uninstaller, quietly (decision 0019). The entry is in HKEY_CURRENT_USER for "only me" and in HKEY_LOCAL_MACHINE
/// for all users, where Windows asks for an administrator. The Portable has no entry: nothing to remove.
/// </summary>
/// <param name="Version">"2.0.1".</param>
/// <param name="Uninstaller">Its "Uninstall Lamha.exe".</param>
/// <param name="Arguments">What its entry gives for a quiet uninstall: "/currentuser /S" or "/allusers /S".</param>
/// <param name="ForAllUsers">Installed for all users: removing it takes Windows' administrator prompt.</param>
public sealed record Lamha2Install(string Version, string Uninstaller, string Arguments, bool ForAllUsers)
{
    /// <summary>
    /// electron-builder names the entry after a GUID made from the app id (UUID v5 of "com.artworklab.lamha" in its own
    /// namespace, app-builder-lib's NsisTarget.js). Velopack's entry for Lamha 3 is "Lamha", so the two never meet.
    /// </summary>
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\209ef46e-fba3-5a6c-954e-87ea4eaef519";

    /// <summary>Lamha 2's start-with-Windows value (Electron's <c>setLoginItemSettings</c>, named after the app id), which its uninstaller leaves.</summary>
    public const string RunValue = "com.artworklab.lamha";

    /// <summary>The user cancelled Windows' administrator prompt.</summary>
    private const int ErrorCancelled = 1223;

    /// <summary>Lamha 2 on this PC, or null: for this user first, then for all users (64- and 32-bit views of the registry).</summary>
    public static Lamha2Install? Find()
    {
        using var machine64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var machine32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        return Find([(Registry.CurrentUser, false), (machine64, true), (machine32, true)], UninstallKey);
    }

    /// <summary>The first of <paramref name="places"/> with a usable entry at <paramref name="keyPath"/> (tests use keys of their own).</summary>
    public static Lamha2Install? Find(IEnumerable<(RegistryKey Root, bool ForAllUsers)> places, string keyPath)
    {
        ArgumentNullException.ThrowIfNull(places);
        foreach (var (root, allUsers) in places)
        {
            // Lamha 1.x had the same app id and the same uninstaller, so any version is Lamha 2's to remove.
            using var entry = root.OpenSubKey(keyPath);
            if (entry is not null && Usable(entry, allUsers) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// "Uninstall Lamha.exe" with its arguments, as Windows' Settings → Apps would run it quietly. For all users it's
    /// started with the administrator prompt ("runas").
    /// </summary>
    public ProcessStartInfo StartInfo() => new(Uninstaller, Arguments)
    {
        UseShellExecute = true,
        Verb = ForAllUsers ? "runas" : "",
    };

    /// <summary>
    /// Starts the uninstaller. False when the user said no to Windows' prompt. It returns at once: NSIS copies itself to
    /// the temp folder and runs from there, so its end shows as the entry gone (<see cref="Find()"/> null).
    /// </summary>
    public bool Start()
    {
        try
        {
            using var process = Process.Start(StartInfo());
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return false;
        }
    }

    /// <summary>Lamha 2's start-with-Windows entry removed (and Windows' switch for it), so it doesn't start next to Lamha 3.</summary>
    public static void RemoveStartEntry(
        string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run",
        string approvedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run")
    {
        foreach (var path in new[] { runPath, approvedPath })
        {
            using var key = Registry.CurrentUser.OpenSubKey(path, writable: true);
            key?.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }

    /// <summary>The entry's quiet command, when it's Lamha 2's uninstaller and it's there.</summary>
    private static Lamha2Install? Usable(RegistryKey entry, bool allUsers)
    {
        if (entry.GetValue("QuietUninstallString") is not string quiet || Parse(quiet) is not { } command)
        {
            return null;
        }

        var (exe, args) = command;
        if (!string.Equals(Path.GetFileName(exe), "Uninstall Lamha.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exe))
        {
            return null;
        }

        var version = entry.GetValue("DisplayVersion") as string ?? "";
        return new Lamha2Install(version, exe, args, allUsers);
    }

    /// <summary>'"C:\…\Uninstall Lamha.exe" /allusers /S' into the program and its arguments (installer.nsh writes it so).</summary>
    internal static (string Exe, string Args)? Parse(string command)
    {
        command = command.Trim();
        if (!command.StartsWith('"'))
        {
            return null;
        }

        var end = command.IndexOf('"', 1);
        return end > 1 ? (command[1..end], command[(end + 1)..].Trim()) : null;
    }
}
