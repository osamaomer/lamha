namespace Lamha.Core.Review;

/// <summary>An answer in review.</summary>
public enum Grade
{
    Again,
    Hard,
    Good,
}

/// <summary>
/// A simplified SM-2, as in Lamha 2 (kept for 3.0 so imported decks behave exactly as before): again → 10 minutes;
/// hard → 1 day, then × 1.2; good → 2 days, 5 days, then × ease.
/// </summary>
public static class Scheduler
{
    public const long Minute = 60_000;
    public const long Day = 86_400_000;

    /// <summary>The card after answering <paramref name="grade"/> at <paramref name="now"/> (Lamha 2's <c>schedule</c>).</summary>
    public static Card Schedule(Card card, Grade grade, long now)
    {
        if (grade == Grade.Again)
        {
            return card with
            {
                Last = now,
                Reps = 0,
                Interval = 0,
                Lapses = card.Lapses + 1,
                Ease = Math.Max(1.3, card.Ease - 0.2),
                Due = now + (10 * Minute),
            };
        }

        int interval;
        var ease = card.Ease;
        if (grade == Grade.Hard)
        {
            interval = card.Reps == 0 ? 1 : Math.Max(1, JsRound(card.Interval * 1.2));
            ease = Math.Max(1.3, card.Ease - 0.15);
        }
        else
        {
            interval = card.Reps switch
            {
                0 => 2,
                1 => 5,
                _ => Math.Max(card.Interval + 1, JsRound(card.Interval * card.Ease)),
            };
        }

        return card with { Last = now, Interval = interval, Ease = ease, Reps = card.Reps + 1, Due = now + (interval * Day) };
    }

    /// <summary>JavaScript's <c>Math.round</c>: halves go up.</summary>
    private static int JsRound(double x) => (int)Math.Floor(x + 0.5);
}
