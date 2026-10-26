using System.Text;
using Lamha.Windows.Shell;
using Microsoft.Win32;

namespace Lamha.Windows.Tests.Shell;

/// <summary>
/// Lamha 2's install as electron-builder registers it (installer.nsh), against keys of the tests' own: never the real
/// uninstall entries or Run key, and no uninstaller is ever started.
/// </summary>
public sealed class Lamha2InstallTests : IDisposable
{
    private readonly string _root = $@"Software\Lamha-tests-{Guid.NewGuid():N}";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lamha2-install-" + Guid.NewGuid().ToString("N"));

    public Lamha2InstallTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
        Directory.Delete(_dir, recursive: true);
    }

    /// <summary>An uninstall entry under <paramref name="place"/>, as installer.nsh's registryAddInstallInfo writes it.</summary>
    private string Entry(string place, string mode, string exeName = "Uninstall Lamha.exe", bool exeThere = true)
    {
        var exe = Path.Combine(_dir, place, exeName);
        if (exeThere)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(exe, "");
        }

        using var key = Registry.CurrentUser.CreateSubKey($@"{_root}\{place}\Uninstall");
        key.SetValue("DisplayName", "Lamha 2.0.1");
        key.SetValue("DisplayVersion", "2.0.1");
        key.SetValue("Publisher", "Artwork Lab");
        key.SetValue("UninstallString", $"\"{exe}\" {mode}");
        key.SetValue("QuietUninstallString", $"\"{exe}\" {mode} /S");
        return exe;
    }

    private Lamha2Install? Find(params (string Place, bool AllUsers)[] places)
    {
        var roots = places.Select(p => (Registry.CurrentUser.CreateSubKey($@"{_root}\{p.Place}"), p.AllUsers)).ToList();
        try
        {
            return Lamha2Install.Find(roots, "Uninstall");
        }
        finally
        {
            roots.ForEach(r => r.Item1.Dispose());
        }
    }

    [Fact]
    public void FoundForThisUserWithItsQuietUninstall()
    {
        var exe = Entry("user", "/currentuser");
        var found = Find(("user", false), ("machine", true));
        Assert.Equal(new Lamha2Install("2.0.1", exe, "/currentuser /S", ForAllUsers: false), found);
        var start = found!.StartInfo();
        Assert.Equal((exe, "/currentuser /S", true, ""), (start.FileName, start.Arguments, start.UseShellExecute, start.Verb));
    }

    [Fact]
    public void ForAllUsersItAsksWindowsForAnAdministrator()
    {
        Entry("machine", "/allusers");
        var found = Find(("user", false), ("machine", true));
        Assert.True(found!.ForAllUsers);
        Assert.Equal(("/allusers /S", "runas"), (found.Arguments, found.StartInfo().Verb));
    }

    [Fact]
    public void NothingWhenTheUninstallerIsGoneOrIsntLamhasOrTheEntryIsMissing()
    {
        Assert.Null(Find(("user", false)));
        Entry("gone", "/currentuser", exeThere: false);
        Entry("other", "/currentuser", exeName: "Update.exe");
        Assert.Null(Find(("gone", false), ("other", false)));
    }

    [Fact]
    public void TheQuietCommandIsSplitAtItsQuotes()
    {
        Assert.Equal((@"C:\Program Files\Lamha\Uninstall Lamha.exe", "/allusers /S"), Lamha2Install.Parse("\"C:\\Program Files\\Lamha\\Uninstall Lamha.exe\" /allusers /S"));
        Assert.Null(Lamha2Install.Parse(@"C:\Lamha\Uninstall Lamha.exe /S"));
        Assert.Null(Lamha2Install.Parse("\"unclosed"));
    }

    [Fact]
    public void ItsStartWithWindowsEntryGoesAndLamha3sStays()
    {
        using (var run = Registry.CurrentUser.CreateSubKey(_root + @"\Run"))
        {
            run.SetValue(Lamha2Install.RunValue, "\"C:\\Program Files\\Lamha\\Lamha.exe\" --hidden");
            run.SetValue(StartWithWindows.Name, "\"C:\\Lamha 3\\Lamha.exe\"");
        }

        using (var approved = Registry.CurrentUser.CreateSubKey(_root + @"\Approved"))
        {
            approved.SetValue(Lamha2Install.RunValue, new byte[12], RegistryValueKind.Binary);
        }

        Lamha2Install.RemoveStartEntry(_root + @"\Run", _root + @"\Approved");

        using var after = Registry.CurrentUser.OpenSubKey(_root + @"\Run")!;
        Assert.Equal([StartWithWindows.Name], after.GetValueNames());
        using var approvedAfter = Registry.CurrentUser.OpenSubKey(_root + @"\Approved")!;
        Assert.Empty(approvedAfter.GetValueNames());
    }
}

/// <summary>Lamha 3's shortcuts through Lamha 2's uninstaller, which deletes every "Lamha.lnk" (uninstaller.nsh).</summary>
public sealed class KeptShortcutsTests : IDisposable
{
    private const string Lamha3 = @"C:\Users\someone\AppData\Local\Lamha";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lamha-shortcuts-" + Guid.NewGuid().ToString("N"));

    public KeptShortcutsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>A stand-in .lnk naming <paramref name="target"/> as shortcuts do: in the system's code page, then as UTF-16.</summary>
    private static byte[] Link(string target, bool unicodeOnly = false) =>
        [0x4C, 0, 0, 0, .. unicodeOnly ? [] : Encoding.Latin1.GetBytes(target + "\0"), .. Encoding.Unicode.GetBytes(target + "\0")];

    private string Shortcut(string name, byte[] link)
    {
        var path = Path.Combine(_dir, "shortcuts", name, "Lamha.lnk");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, link);
        return path;
    }

    [Fact]
    public void ALinkIntoLamha3sFolderIsLamha3s()
    {
        Assert.True(KeptShortcuts.PointsInto(Link(Lamha3 + @"\current\Lamha.exe"), Lamha3));
        Assert.True(KeptShortcuts.PointsInto(Link(@"c:\users\SOMEONE\appdata\local\lamha\Lamha.exe", unicodeOnly: true), Lamha3 + @"\"));
        Assert.True(KeptShortcuts.PointsInto([0x4C, .. Encoding.Unicode.GetBytes(Lamha3 + @"\Lamha.exe")], Lamha3), "at an odd offset");
        Assert.True(KeptShortcuts.PointsInto(Link(@"C:\Users\someone\AppData\Local\Lamha\current\Lamha.exe"), @"C:\Users\SOMEON~1\AppData\Local\Lamha"), "the user's folder written short");
        Assert.True(KeptShortcuts.PointsInto(Link(@"%USERPROFILE%\AppData\Local\Lamha\current\Lamha.exe", unicodeOnly: true), Lamha3), "as Velopack also writes it");
        Assert.False(KeptShortcuts.PointsInto(Link(@"C:\Users\someone\AppData\Local\Programs\Lamha\Lamha.exe"), Lamha3), "Lamha 2 for this user");
        Assert.False(KeptShortcuts.PointsInto(Link(@"C:\Program Files\Lamha\Lamha.exe"), Lamha3), "Lamha 2 for all users");
        Assert.False(KeptShortcuts.PointsInto(Link(@"C:\Users\someone\AppData\Local\Lamha Data\x"), Lamha3));
    }

    [Fact]
    public void Lamha3sShortcutsComeBackAfterTheUninstallerDeletedThem()
    {
        var start = Shortcut("start", Link(Lamha3 + @"\current\Lamha.exe"));
        var desktop = Shortcut("desktop", Link(Lamha3 + @"\Lamha.exe"));
        var lamha2s = Shortcut("old", Link(@"C:\Program Files\Lamha\Lamha.exe"));
        var kept = new KeptShortcuts(Path.Combine(_dir, "kept"), Lamha3);

        Assert.Equal(2, kept.Keep([start, desktop, lamha2s, Path.Combine(_dir, "none.lnk")]));
        Assert.True(kept.IsPending);
        File.Delete(start);
        File.Delete(lamha2s);
        Directory.Delete(Path.GetDirectoryName(desktop)!, recursive: true);

        Assert.Equal(2, kept.RestoreMissing());
        Assert.Equal(Link(Lamha3 + @"\current\Lamha.exe"), File.ReadAllBytes(start));
        Assert.True(File.Exists(desktop));
        Assert.False(File.Exists(lamha2s), "Lamha 2's own shortcut stays gone");
        Assert.False(kept.IsPending);
        Assert.False(Directory.Exists(Path.Combine(_dir, "kept")));
    }

    [Fact]
    public void AShortcutStillThereIsLeftAsItIs()
    {
        var start = Shortcut("start", Link(Lamha3 + @"\current\Lamha.exe"));
        var kept = new KeptShortcuts(Path.Combine(_dir, "kept"), Lamha3);
        kept.Keep([start]);
        File.WriteAllBytes(start, Link(Lamha3 + @"\Lamha.exe")); // Velopack made it again meanwhile

        Assert.Equal(0, kept.RestoreMissing());
        Assert.Equal(Link(Lamha3 + @"\Lamha.exe"), File.ReadAllBytes(start));
    }

    [Fact]
    public void NoLamha3ShortcutsMeansNothingPending()
    {
        var kept = new KeptShortcuts(Path.Combine(_dir, "kept"), Lamha3);
        Assert.Equal(0, kept.Keep([Shortcut("old", Link(@"C:\Program Files\Lamha\Lamha.exe"))]));
        Assert.False(kept.IsPending);
        Assert.Equal(0, kept.RestoreMissing());
    }
}
