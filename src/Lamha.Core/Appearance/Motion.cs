namespace Lamha.Core.Appearance;

/// <summary>How much moves on screen.</summary>
public enum MotionLevel
{
    /// <summary>Every animation.</summary>
    Full,

    /// <summary>The same animations, a little faster, without the decorative ones.</summary>
    Subtle,

    /// <summary>Nothing is animated.</summary>
    Off,
}

/// <summary>
/// Settings → Appearance → Animations ("auto", "full", "subtle", "off") on this PC, and the timings every window uses.
/// The levels are Lamha 2's shared/motion.js (<c>resolve()</c>, <c>time()</c>) and desktop/app-rules.js
/// (<c>motionHint()</c>), with their numbers; the durations and curves are Windows' (WinUI's ControlFaster/Fast/Normal
/// AnimationDuration and ControlFastOutSlowInKeySpline), as the design system has them (decision 0020).
/// </summary>
public static class Motion
{
    /// <summary>The settings, in Settings' order.</summary>
    public static readonly IReadOnlyList<string> Settings = ["auto", "full", "subtle", "off"];

    /// <summary>Something appearing (the card, a panel, a menu): Windows' 167 ms.</summary>
    public const int EnterMilliseconds = 167;

    /// <summary>Something going away: Windows' 83 ms. Nothing waits on it longer than this.</summary>
    public const int ExitMilliseconds = 83;

    /// <summary>A box growing or shrinking to its new content, a page, a list making room: Windows' 250 ms.</summary>
    public const int ResizeMilliseconds = 250;

    /// <summary>Celebrations and Review's moments (the goal's ring filling): the design system's 333 ms.</summary>
    public const int SlowMilliseconds = 333;

    /// <summary>A list's items coming in one after another (full only): each this much after the last, at most
    /// <see cref="StaggerMax"/> of them, rising <see cref="StaggerDistance"/> px over <see cref="StaggerMilliseconds"/>.</summary>
    public const int StaggerEachMilliseconds = 22;

    public const int StaggerMax = 8;

    public const int StaggerMilliseconds = 200;

    public const double StaggerDistance = 6;

    /// <summary>The waiting mark shows only after this, so an answer from the dictionary doesn't flicker.</summary>
    public const int WaitMarkDelayMilliseconds = 250;

    /// <summary>Windows' fast out, slow in (0, 0, 0, 1): quick, then settling.</summary>
    public static readonly (float X1, float Y1, float X2, float Y2) EnterEasing = (0f, 0f, 0f, 1f);

    /// <summary>Windows' (1, 0, 1, 1): speeding up as it leaves.</summary>
    public static readonly (float X1, float Y1, float X2, float Y2) ExitEasing = (1f, 0f, 1f, 1f);

    /// <summary>
    /// The level for <paramref name="setting"/>. Automatic (and anything unknown) is off when Windows' Animation
    /// effects are off (Settings → Accessibility), otherwise what <paramref name="hint"/> says about this PC.
    /// </summary>
    public static MotionLevel Level(string? setting, bool windowsAnimations, MotionLevel hint) => setting switch
    {
        "full" => MotionLevel.Full,
        "subtle" => MotionLevel.Subtle,
        "off" => MotionLevel.Off,
        _ when !windowsAnimations => MotionLevel.Off,
        _ => hint == MotionLevel.Subtle ? MotionLevel.Subtle : MotionLevel.Full,
    };

    /// <summary>
    /// What Automatic means on this PC: subtle on a weak machine (4.5 GB of memory or less, 2 cores or fewer) or when
    /// drawing isn't accelerated by the graphics card, full otherwise.
    /// </summary>
    public static MotionLevel Hint(long totalMemoryBytes, int cores, bool accelerated) =>
        totalMemoryBytes <= 4.5 * 1024 * 1024 * 1024 || cores <= 2 || !accelerated ? MotionLevel.Subtle : MotionLevel.Full;

    /// <summary>
    /// How far along a curve is at <paramref name="progress"/> (0 to 1 of its time): the CSS cubic-bezier() a compositor
    /// animation uses, for motion that's stepped by hand (the card's window growing to its answer).
    /// </summary>
    public static double Ease((float X1, float Y1, float X2, float Y2) curve, double progress)
    {
        if (progress <= 0)
        {
            return 0;
        }

        if (progress >= 1)
        {
            return 1;
        }

        // The point of the curve whose x is the time: x(t) only grows, so halving finds t.
        double low = 0, high = 1, t = progress;
        for (var i = 0; i < 32; i++)
        {
            t = (low + high) / 2;
            if (Bezier(curve.X1, curve.X2, t) < progress)
            {
                low = t;
            }
            else
            {
                high = t;
            }
        }

        return Bezier(curve.Y1, curve.Y2, t);
    }

    /// <summary>A duration at <paramref name="level"/>: subtle is the same animation a little faster, off is none.</summary>
    public static TimeSpan Time(MotionLevel level, int milliseconds) => level switch
    {
        MotionLevel.Off => TimeSpan.Zero,
        MotionLevel.Subtle => TimeSpan.FromMilliseconds(Math.Floor((milliseconds * 0.75) + 0.5)), // JavaScript's Math.round
        _ => TimeSpan.FromMilliseconds(milliseconds),
    };

    /// <summary>One coordinate of a cubic Bézier from 0 to 1 through <paramref name="a"/> and <paramref name="b"/>.</summary>
    private static double Bezier(double a, double b, double t) => (3 * (1 - t) * (1 - t) * t * a) + (3 * (1 - t) * t * t * b) + (t * t * t);
}
