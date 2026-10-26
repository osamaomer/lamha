using Microsoft.UI.Dispatching;

namespace Lamha.App.Services;

/// <summary>
/// <c>--measure</c>: a fixed timeline that tools/Measure/measure.ps1 samples from outside, the way Task Manager sees it.
/// 0–20 s in the tray · 20 s the card opens · 35 s it closes · 60 s the main window opens · 75 s it closes · 100 s Lamha
/// quits. The script reads at 15, 30, 55, 70 and 95 s; at those moments the log says how much of the memory is .NET's
/// heap, so what's left can be put down to WinUI and Windows' own (numbers only).
/// </summary>
internal static class MeasureTimeline
{
    /// <summary>Holds the timers: one that's no longer referenced may be collected before it fires.</summary>
    private static readonly List<DispatcherQueueTimer> Timers = [];

    public static void Start(DispatcherQueue queue, CardService card, MainWindowService main, Action quit)
    {
        At(queue, TimeSpan.FromSeconds(20), card.ShowSample);
        At(queue, TimeSpan.FromSeconds(35), card.Hide);
        At(queue, TimeSpan.FromSeconds(60), () => main.Show());
        At(queue, TimeSpan.FromSeconds(75), main.Close);
        At(queue, TimeSpan.FromSeconds(100), quit);
        foreach (var (seconds, stage) in new[] { (15, "tray"), (30, "card open"), (55, "card closed"), (70, "main window open"), (95, "main window closed") })
        {
            At(queue, TimeSpan.FromSeconds(seconds), () => Log(stage));
        }
    }

    /// <summary>.NET's share of the commit: its heap's committed memory, and what's live on it.</summary>
    private static void Log(string stage)
    {
        var gc = GC.GetGCMemoryInfo();
        AppLog.Info($"measure {stage}: .NET heap {gc.TotalCommittedBytes / 1024 / 1024} MB committed, {GC.GetTotalMemory(forceFullCollection: false) / 1024 / 1024} MB in use; process {Environment.WorkingSet / 1024 / 1024} MB working set");
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
