using Microsoft.UI.Dispatching;

namespace Lamha.App.Services;

/// <summary>
/// <c>--measure</c>: a fixed timeline that tools/Measure/measure.ps1 samples from outside, the way Task Manager sees it.
/// 0–20 s in the tray · 20 s the card opens · 35 s it closes · 60 s Lamha quits. The script reads at 15, 30 and 55 s.
/// </summary>
internal static class MeasureTimeline
{
    /// <summary>Holds the timers: one that's no longer referenced may be collected before it fires.</summary>
    private static readonly List<DispatcherQueueTimer> Timers = [];

    public static void Start(DispatcherQueue queue, CardService card, Action quit)
    {
        At(queue, TimeSpan.FromSeconds(20), card.ShowSample);
        At(queue, TimeSpan.FromSeconds(35), card.Hide);
        At(queue, TimeSpan.FromSeconds(60), quit);
    }

    private static void At(DispatcherQueue queue, TimeSpan when, Action action)
    {
        var timer = queue.CreateTimer();
        timer.Interval = when;
        timer.IsRepeating = false;
        timer.Tick += (t, _) =>
        {
            t.Stop();
            action();
        };
        Timers.Add(timer);
        timer.Start();
    }
}
