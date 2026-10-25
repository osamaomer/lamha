using Lamha.Windows.Shell;
using Microsoft.UI.Dispatching;

namespace Lamha.App.Services;

/// <summary>
/// Hands Lamha's idle memory back to Windows once nothing has been open for a few seconds: after start-up, and after
/// each window closes. Windows tell it when they open and close. See <see cref="ProcessMemory.TrimIdle"/>.
/// </summary>
public sealed class IdleMemory
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(5);

    private readonly DispatcherQueueTimer _timer;
    private int _open;

    public IdleMemory(DispatcherQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        _timer = queue.CreateTimer();
        _timer.Interval = Delay;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            if (_open == 0)
            {
                // First what .NET keeps for reuse after a window's objects are gone: an aggressive collection gives it
                // back to Windows, so commit goes down too (the trim alone only moves pages out of the working set).
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                ProcessMemory.TrimIdle();
                AppLog.Info("idle: memory handed back to Windows");
            }
        };
    }

    public void WindowOpened()
    {
        _open++;
        _timer.Stop();
    }

    public void WindowClosed()
    {
        _open = Math.Max(0, _open - 1);
        Schedule();
    }

    /// <summary>Trims in a few seconds if nothing opens meanwhile (also called once after start-up).</summary>
    public void Schedule()
    {
        _timer.Stop();
        _timer.Start();
    }
}
