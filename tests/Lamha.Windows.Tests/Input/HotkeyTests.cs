using Lamha.Windows.Input;

namespace Lamha.Windows.Tests.Input;

/// <summary>A shortcut as Settings keeps it ("Ctrl+Alt+L") and back, and which ones a user may choose.</summary>
public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+L")]
    [InlineData("Alt+Shift+W")]
    [InlineData("Ctrl+Shift+7")]
    [InlineData("Win+F5")]
    [InlineData("Ctrl+Alt+Shift+F24")]
    public void AShortcutReadsBackAsItWasWritten(string text)
    {
        var hotkey = Hotkey.Parse(text);
        Assert.NotNull(hotkey);
        Assert.Equal(text, hotkey.Value.ToString());
    }

    [Fact]
    public void ReadingIgnoresCaseAndSpaces() =>
        Assert.Equal(new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey.L), Hotkey.Parse(" ctrl + alt + l "));

    [Theory]
    [InlineData("")]
    [InlineData("L")]
    [InlineData("Shift+L")] // Shift alone would take a capital letter from every program
    [InlineData("Ctrl+Space")]
    [InlineData("Ctrl+F25")]
    [InlineData("Hyper+L")]
    [InlineData("Ctrl+Alt")]
    public void OnlyShortcutsAUserMayChooseAreRead(string text) => Assert.Null(Hotkey.Parse(text));
}
