using Lamha.Windows.Shell;
using Microsoft.Win32;

namespace Lamha.Windows.Tests.Shell;

/// <summary>Against keys of the tests' own, never the real Software\Classes (that would take over Lamha's links).</summary>
public sealed class NotificationRegistrationTests : IDisposable
{
    private const string Command = "\"C:\\Lamha\\Lamha.exe\" \"%1\"";
    private readonly string _root = $@"Software\Lamha-tests-{Guid.NewGuid():N}";

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);

    private NotificationRegistration New() => new("ArtworkLab.Lamha", "lamha", Command, _root);

    [Fact]
    public void RegistersTheNameTheIconAndTheLinksThenTakesThemAway()
    {
        var registration = New();
        registration.Register("لمحة", @"C:\Lamha\Assets\Lamha-256.png");
        using (var app = Registry.CurrentUser.OpenSubKey(_root + @"\AppUserModelId\ArtworkLab.Lamha"))
        {
            Assert.Equal("لمحة", app!.GetValue("DisplayName"));
            Assert.Equal(@"C:\Lamha\Assets\Lamha-256.png", app.GetValue("IconUri"));
        }

        using (var link = Registry.CurrentUser.OpenSubKey(_root + @"\lamha"))
        {
            Assert.Equal("", link!.GetValue("URL Protocol"));
            using var open = link.OpenSubKey(@"shell\open\command");
            Assert.Equal(Command, open!.GetValue(""));
        }

        registration.Unregister();
        Assert.Null(Registry.CurrentUser.OpenSubKey(_root + @"\AppUserModelId\ArtworkLab.Lamha"));
        Assert.Null(Registry.CurrentUser.OpenSubKey(_root + @"\lamha"));
        registration.Unregister(); // nothing left: no error
    }

    [Fact]
    public void ALinkComesBackWholeFromTheCommandLineWindowsStartsLamhaWith()
    {
        var registration = New();
        var link = registration.Link("grade", new Dictionary<string, string> { ["key"] = "don't stop? a & b = c", ["grade"] = "good" });
        Assert.DoesNotContain(' ', link);
        Assert.StartsWith("lamha:grade?", link, StringComparison.Ordinal);

        var found = registration.Find($"\"C:\\Lamha\\Lamha.exe\" \"{link}\"");
        Assert.NotNull(found);
        Assert.Equal("grade", found.Action);
        Assert.Equal("don't stop? a & b = c", found.Values["key"]);
        Assert.Equal("good", found.Values["grade"]);

        // Unquoted, as a launch's arguments may hand it over, and with nothing after the action.
        Assert.Equal("review", registration.Find($"Lamha.exe {registration.Link("review")}")?.Action);
        Assert.Empty(registration.Find(registration.Link("review"))!.Values);
    }

    [Theory]
    [InlineData("\"C:\\Lamha\\Lamha.exe\"")]
    [InlineData("\"C:\\Lamha\\Lamha.exe\" --profile=\"C:\\lamha-data\"")]
    [InlineData("\"C:\\Lamha\\Lamha.exe\" \"lamha:\"")]
    [InlineData("\"C:\\Lamha\\Lamha.exe\" \"notlamha:review\"")]
    public void ACommandLineWithoutALinkHasNone(string commandLine) => Assert.Null(New().Find(commandLine));
}
