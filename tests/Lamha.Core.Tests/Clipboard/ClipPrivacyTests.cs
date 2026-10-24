using Lamha.Core.Clipboard;
using Lamha.Core.Settings;

namespace Lamha.Core.Tests.Clipboard;

/// <summary>Lamha 2's test-clipboard.mjs, section "privacy", and the capture rules of clipboard-monitor.js.</summary>
public class ClipPrivacyTests
{
    private static readonly LamhaSettings On = new() { ClipboardEnabled = true };

    [Theory]
    [InlineData("4111 1111 1111 1111", true)] // Visa test number, spaces
    [InlineData("4111-1111-1111-1111", true)]
    [InlineData("5555555555554444", true)] // Mastercard, no separators
    [InlineData("378282246310005", true)] // Amex, 15 digits
    [InlineData("  4012888888881881  ", true)] // surrounding whitespace
    [InlineData("4111 1111 1111 1112", false)] // fails Luhn
    [InlineData("+966 50 123 4567", false)] // phone number
    [InlineData("0501234567", false)] // too short
    [InlineData("SA03 8000 0000 6080 1016 7519", false)] // IBAN (letters)
    [InlineData("Card: 4111 1111 1111 1111", false)] // not a number on its own
    [InlineData("4111  1111 1111 1111", false)] // double space
    [InlineData("41111111111111111111", false)] // 20 digits
    [InlineData("٤١١١١١١١١١١١١١١١", false)] // Arabic-Indic digits aren't what card forms take
    public void CardNumbers(string text, bool isCard) => Assert.Equal(isCard, ClipPrivacy.IsCardNumber(text));

    [Fact]
    public void ExeNamesAreNormalizedForTheExcludedList()
    {
        Assert.Equal("keepassxc.exe", ClipPrivacy.ExeName("KeePassXC"));
        Assert.Equal("bitwarden.exe", ClipPrivacy.ExeName("  C:\\Program Files\\Bitwarden\\Bitwarden.EXE "));
        Assert.Equal("notepad.exe", ClipPrivacy.ExeName("\"notepad.exe\""));
        Assert.Equal("", ClipPrivacy.ExeName(""));
        Assert.Equal("", ClipPrivacy.ExeName("a<b>.exe"));
        Assert.Equal(["keepass.exe", "keepassxc.exe"], new LamhaSettings().ClipboardExcludedApps.Take(2));
        Assert.False(new LamhaSettings().ClipboardEnabled);
    }

    [Fact]
    public void NothingIsReadWhileHistoryIsOffOrPaused()
    {
        var copy = new ClipFormats(false, null, "notepad.exe");
        Assert.Equal(ClipSkip.Off, ClipCapture.BeforeReading(copy, new LamhaSettings(), paused: false));
        Assert.Equal(ClipSkip.Off, ClipCapture.BeforeReading(copy, On, paused: true));
        Assert.Equal(ClipSkip.None, ClipCapture.BeforeReading(copy, On, paused: false));
    }

    [Fact]
    public void AProgramsOwnMarksAreHonouredAsWindowsHistoryHonoursThem()
    {
        Assert.Equal(ClipSkip.ExcludedFormat, ClipCapture.BeforeReading(new(true, 1, "a.exe"), On, false));
        Assert.Equal(ClipSkip.ExcludedFromHistory, ClipCapture.BeforeReading(new(false, 0, "a.exe"), On, false));
        Assert.Equal(ClipSkip.None, ClipCapture.BeforeReading(new(false, 1, "a.exe"), On, false));
    }

    [Fact]
    public void ExcludedProgramsAreNeverRead()
    {
        Assert.Equal(ClipSkip.ExcludedApp, ClipCapture.BeforeReading(new(false, null, "KeePassXC.exe"), On, false));
        var mine = On with { ClipboardExcludedApps = ["winword.exe"] };
        Assert.Equal(ClipSkip.ExcludedApp, ClipCapture.BeforeReading(new(false, null, "winword.exe"), mine, false));
        Assert.Equal(ClipSkip.None, ClipCapture.BeforeReading(new(false, null, "keepassxc.exe"), mine, false));
    }

    [Fact]
    public void TheTextItself()
    {
        Assert.Equal(ClipSkip.Busy, ClipCapture.AfterReading(null, On));
        Assert.Equal(ClipSkip.Empty, ClipCapture.AfterReading(" \n ", On));
        Assert.Equal(ClipSkip.TooLong, ClipCapture.AfterReading(new string('x', ClipCapture.MaxText + 1), On));
        Assert.Equal(ClipSkip.None, ClipCapture.AfterReading(new string('x', ClipCapture.MaxText), On));
        Assert.Equal(ClipSkip.CardNumber, ClipCapture.AfterReading("4111 1111 1111 1111", On));
        Assert.Equal(ClipSkip.None, ClipCapture.AfterReading("4111 1111 1111 1111", On with { ClipboardSkipCards = false }));
    }

    [Fact]
    public void FormattingIsKeptOnlyWhenSmall()
    {
        Assert.Equal("<b>x</b>", ClipCapture.HtmlToKeep("<b>x</b>"));
        Assert.Null(ClipCapture.HtmlToKeep(""));
        Assert.Null(ClipCapture.HtmlToKeep(new string('ب', ClipCapture.MaxHtmlBytes / 2 + 1))); // 2 bytes each in UTF-8
    }
}
