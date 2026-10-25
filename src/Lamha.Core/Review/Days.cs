namespace Lamha.Core.Review;

/// <summary>
/// Days go by the calendar on this PC, not by 24-hour steps: the night the clocks go forward is 23 hours long, so
/// "now − 24 h" just after midnight lands two days back (Lamha 2's <c>dayKey</c>).
/// </summary>
public static class Days
{
    public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    public static long NowMs(TimeProvider time) => time.GetUtcNow().ToUnixTimeMilliseconds();
}
