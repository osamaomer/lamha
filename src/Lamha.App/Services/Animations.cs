using Lamha.Core.Appearance;
using Lamha.Core.Settings;
using Windows.UI.ViewManagement;

namespace Lamha.App.Services;

/// <summary>
/// How much moves on screen now: the setting (Settings → Appearance → Animations), Windows' Animation effects, and what
/// this PC can do (Core's <see cref="Motion"/>). Read again when a window opens, so a change applies to the next one.
/// </summary>
public static class Animations
{
    public static MotionLevel Level { get; private set; } = MotionLevel.Full;

    public static void Refresh(LamhaSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var hint = Motion.Hint(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, Environment.ProcessorCount, accelerated: true);
        Level = Motion.Level(settings.Motion, new UISettings().AnimationsEnabled, hint);
    }
}
