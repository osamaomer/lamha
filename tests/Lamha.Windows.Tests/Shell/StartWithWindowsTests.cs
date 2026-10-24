using Lamha.Windows.Shell;
using Microsoft.Win32;

namespace Lamha.Windows.Tests.Shell;

/// <summary>Against keys of the tests' own, never the real Run key (that would start something at sign-in).</summary>
public sealed class StartWithWindowsTests : IDisposable
{
    private readonly string _root = $@"Software\Lamha-tests-{Guid.NewGuid():N}";

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);

    [Fact]
    public void TurnsOnAndOff()
    {
        var start = New("\"C:\\Lamha\\Lamha.exe\"");
        Assert.False(start.IsOn);
        start.Set(true);
        Assert.True(start.IsOn);
        using (var run = Registry.CurrentUser.OpenSubKey(_root + @"\Run"))
        {
            Assert.Equal("\"C:\\Lamha\\Lamha.exe\"", run!.GetValue(StartWithWindows.Name));
        }

        start.Set(false);
        Assert.False(start.IsOn);
    }

    [Fact]
    public void SwitchedOffInWindowsSettingsCountsAsOffUntilTurnedOnAgain()
    {
        var start = New("\"C:\\Lamha\\Lamha.exe\"");
        start.Set(true);
        using (var approved = Registry.CurrentUser.CreateSubKey(_root + @"\Approved"))
        {
            approved.SetValue(StartWithWindows.Name, new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }

        Assert.False(start.IsOn);
        start.Set(true);
        Assert.True(start.IsOn);
    }

    [Fact]
    public void ANewInstallFolderIsFollowed()
    {
        New("\"C:\\Old\\Lamha.exe\"").Set(true);
        var moved = New("\"C:\\New\\Lamha.exe\"");
        moved.Refresh();
        using var run = Registry.CurrentUser.OpenSubKey(_root + @"\Run");
        Assert.Equal("\"C:\\New\\Lamha.exe\"", run!.GetValue(StartWithWindows.Name));
    }

    [Fact]
    public void RefreshDoesntTurnItOn()
    {
        var start = New("\"C:\\Lamha\\Lamha.exe\"");
        start.Refresh();
        Assert.False(start.IsOn);
    }

    private StartWithWindows New(string command) => new(command, _root + @"\Run", _root + @"\Approved");
}
