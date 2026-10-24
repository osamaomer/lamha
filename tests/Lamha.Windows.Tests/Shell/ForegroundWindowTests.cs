using Lamha.Windows.Shell;

namespace Lamha.Windows.Tests.Shell;

public class ForegroundWindowTests
{
    [Theory]
    [InlineData("ConsoleWindowClass")]
    [InlineData("CASCADIA_HOSTING_WINDOW_CLASS")]
    [InlineData("mintty")]
    [InlineData("PuTTY")]
    public void TerminalsAreRecognised(string className) => Assert.True(ForegroundWindow.IsTerminalClass(className));

    [Theory]
    [InlineData("Notepad")]
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("OpusApp")]
    [InlineData("ConsoleWindowClassic")]
    [InlineData("")]
    public void OtherProgramsAreNot(string className) => Assert.False(ForegroundWindow.IsTerminalClass(className));

    [Fact]
    public void NoWindowHasNoClassName() => Assert.Equal("", ForegroundWindow.ClassName(0));
}

public sealed class ProcessNameTests
{
    [Fact]
    public void AWindowsProgramIsNamedByItsFile()
    {
        using var window = new MessageWindow();
        var expected = Path.GetFileName(Environment.ProcessPath!).ToLowerInvariant();
        Assert.Equal(expected, ForegroundWindow.ProcessName(window.Handle));
    }

    [Fact]
    public void NoWindowHasNoProgram() => Assert.Equal("", ForegroundWindow.ProcessName(0));
}
