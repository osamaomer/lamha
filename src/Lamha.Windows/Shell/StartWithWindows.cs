using Microsoft.Win32;

namespace Lamha.Windows.Shell;

/// <summary>
/// Lamha starting when the user signs in to Windows: a value in the user's Run key, as Lamha 2's
/// <c>app.setLoginItemSettings()</c> wrote. Windows 11 can also switch it off in Settings → Apps → Startup (or Task
/// Manager); that choice is kept in StartupApproved, and Lamha counts it as off.
/// </summary>
/// <param name="command">What Windows runs: the app's own path, in quotes.</param>
/// <param name="keyPath">The Run key under HKEY_CURRENT_USER; another one in tests.</param>
/// <param name="approvedPath">The StartupApproved key Windows 11 keeps its switches in; another one in tests.</param>
public sealed class StartWithWindows(
    string command,
    string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run",
    string approvedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run")
{
    /// <summary>The value's name: Lamha 2's was the installer's app id, so the two don't clash.</summary>
    public const string Name = "Lamha";

    /// <summary>On when Windows will start Lamha at sign-in.</summary>
    public bool IsOn
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(keyPath);
            if (run?.GetValue(Name) is not string)
            {
                return false;
            }

            // StartupApproved: 12 bytes, the first one even (02, 06) when on, odd (03, 07) when switched off.
            using var approved = Registry.CurrentUser.OpenSubKey(approvedPath);
            return approved?.GetValue(Name) is not byte[] { Length: > 0 } flags || (flags[0] & 1) == 0;
        }
    }

    /// <summary>Turns it on or off. On also clears a switch-off from Windows' Startup settings, as the user just chose on.</summary>
    public void Set(bool on)
    {
        using var run = Registry.CurrentUser.CreateSubKey(keyPath);
        if (on)
        {
            run.SetValue(Name, command);
            using var approved = Registry.CurrentUser.OpenSubKey(approvedPath, writable: true);
            approved?.DeleteValue(Name, throwOnMissingValue: false);
        }
        else
        {
            run.DeleteValue(Name, throwOnMissingValue: false);
        }
    }

    /// <summary>Points an existing entry at this copy of Lamha, in case it moved (a new install folder).</summary>
    public void Refresh()
    {
        using var run = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        if (run?.GetValue(Name) is string current && current != command)
        {
            run.SetValue(Name, command);
        }
    }
}
