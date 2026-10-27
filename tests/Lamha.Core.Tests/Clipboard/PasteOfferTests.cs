using Lamha.Core.Clipboard;
using Lamha.Core.Settings;

namespace Lamha.Core.Tests.Clipboard;

/// <summary>Paste and translate: what the Translate page's empty box may offer from the clipboard.</summary>
public class PasteOfferTests
{
    [Fact]
    public void ACopyIsReadUnderClipboardHistorysRulesEvenWithHistoryOff()
    {
        var off = new LamhaSettings();
        Assert.False(off.ClipboardEnabled);
        Assert.True(PasteOffer.MayRead(new ClipFormats(false, null, "notepad.exe"), off));
        Assert.True(PasteOffer.MayRead(new ClipFormats(false, 1, "notepad.exe"), off));

        // A program's "don't keep this", and the excluded programs (password managers out of the box).
        Assert.False(PasteOffer.MayRead(new ClipFormats(true, null, "notepad.exe"), off));
        Assert.False(PasteOffer.MayRead(new ClipFormats(false, 0, "notepad.exe"), off));
        Assert.False(PasteOffer.MayRead(new ClipFormats(false, null, "keepassxc.exe"), off));
        Assert.False(PasteOffer.MayRead(new ClipFormats(false, null, "word.exe"), off with { ClipboardExcludedApps = ["word.exe"] }));
    }

    [Theory]
    [InlineData("  reluctant \n", "reluctant")]
    [InlineData("The committee was reluctant.", "The committee was reluctant.")]
    [InlineData("", null)]
    [InlineData(" \r\n\t ", null)]
    [InlineData(null, null)]
    [InlineData("4111 1111 1111 1111", null)] // a card number, even with card numbers kept in history
    public void WhatIsOffered(string? copy, string? offered) => Assert.Equal(offered, PasteOffer.Offer(copy));

    [Fact]
    public void ALongCopyIsntOffered()
    {
        Assert.NotNull(PasteOffer.Offer(new string('a', PasteOffer.MaxText)));
        Assert.Null(PasteOffer.Offer(new string('a', PasteOffer.MaxText + 1)));
    }

    [Fact]
    public void ThePreviewIsOneQuotedLine()
    {
        Assert.Equal("“reluctant”", PasteOffer.Preview("reluctant"));
        Assert.Equal("“The committee was reluctant to approve it.”", PasteOffer.Preview("The committee\r\n  was reluctant\tto approve it.\n"));
        Assert.Equal("«كانت اللجنة مترددة»", PasteOffer.Preview("كانت اللجنة\nمترددة"));
    }

    [Fact]
    public void ALongPreviewIsCut()
    {
        // 80 characters are 16 times "word ": cut there, the space dropped before the ellipsis.
        var preview = PasteOffer.Preview(string.Join(' ', Enumerable.Repeat("word", 40)));
        Assert.Equal("“" + string.Join(' ', Enumerable.Repeat("word", 16)) + "…”", preview);
    }
}
