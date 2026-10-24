using Lamha.Core.Appearance;

namespace Lamha.Core.Tests.Appearance;

/// <summary>
/// Lamha 2's test-ui.mjs, "Animations: data-motion follows the setting (off / subtle / full) and, on Automatic, the
/// device hint", and test-desktop.mjs, "Automatic animations: Subtle on a weak PC…".
/// </summary>
public class MotionTests
{
    [Theory]
    [InlineData("off", true, MotionLevel.Full, MotionLevel.Off)]
    [InlineData("full", true, MotionLevel.Subtle, MotionLevel.Full)]
    [InlineData("full", false, MotionLevel.Full, MotionLevel.Full)] // chosen in Lamha: Windows' switch doesn't overrule it
    [InlineData("subtle", true, MotionLevel.Full, MotionLevel.Subtle)]
    [InlineData("auto", true, MotionLevel.Full, MotionLevel.Full)]
    [InlineData("auto", true, MotionLevel.Subtle, MotionLevel.Subtle)] // a weak PC
    [InlineData("auto", false, MotionLevel.Full, MotionLevel.Off)] // Windows' Animation effects off
    [InlineData("sideways", true, MotionLevel.Full, MotionLevel.Full)] // unknown: automatic
    [InlineData(null, false, MotionLevel.Full, MotionLevel.Off)]
    public void TheLevelFollowsTheSettingAndOnAutomaticWindowsAndThePc(string? setting, bool windows, MotionLevel hint, MotionLevel expected) =>
        Assert.Equal(expected, Motion.Level(setting, windows, hint));

    [Fact]
    public void AutomaticIsSubtleOnAWeakPcOrWithoutTheGraphicsCard()
    {
        const long Gb = 1024L * 1024 * 1024;
        Assert.Equal(MotionLevel.Full, Motion.Hint(16 * Gb, 16, accelerated: true));
        Assert.Equal(MotionLevel.Subtle, Motion.Hint(16 * Gb, 16, accelerated: false));
        Assert.Equal(MotionLevel.Subtle, Motion.Hint(4 * Gb, 8, accelerated: true)); // 4 GB
        Assert.Equal(MotionLevel.Subtle, Motion.Hint(8 * Gb, 2, accelerated: true)); // 2 cores
    }

    [Fact]
    public void SubtleIsALittleFasterAndOffIsNone()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(180), Motion.Time(MotionLevel.Full, Motion.EnterMilliseconds));
        Assert.Equal(TimeSpan.FromMilliseconds(135), Motion.Time(MotionLevel.Subtle, Motion.EnterMilliseconds));
        Assert.Equal(TimeSpan.FromMilliseconds(83), Motion.Time(MotionLevel.Subtle, Motion.ExitMilliseconds)); // 82.5, rounded up as Lamha 2 did
        Assert.Equal(TimeSpan.Zero, Motion.Time(MotionLevel.Off, Motion.EnterMilliseconds));
    }
}
