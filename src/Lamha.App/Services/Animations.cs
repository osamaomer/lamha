using System.Numerics;
using Lamha.Core.Appearance;
using Lamha.Core.Settings;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace Lamha.App.Services;

/// <summary>
/// How much moves on screen now: the setting (Settings → Appearance → Animations), Windows' Animation effects, and what
/// this PC can do (Core's <see cref="Motion"/>). Read again when a window opens, so a change applies to the next one.
/// Also the windows' common motion: something coming in (<see cref="Enter"/>, Lamha 2's <c>LamhaMotion.play</c>).
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

    /// <summary>
    /// <paramref name="element"/> comes in: from <paramref name="from"/> opacity, <paramref name="rise"/> px lower (above,
    /// when negative) and at <paramref name="scale"/>, to where it is; a quarter faster at subtle, at once at off. It grows
    /// from <paramref name="origin"/>, a share of its size (0, 0 is its top left), or from its middle.
    /// </summary>
    public static void Enter(UIElement element, MotionLevel level, int milliseconds, float from, float rise, float scale,
        (float X1, float Y1, float X2, float Y2) easing, TimeSpan delay = default, Vector2? origin = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var duration = Motion.Time(level, milliseconds);
        if (duration == TimeSpan.Zero || element is not FrameworkElement { ActualWidth: > 0 } framed)
        {
            ShowNow(element);
            return;
        }

        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var c = visual.Compositor;
        var at = origin ?? new Vector2(0.5f, 0.5f);
        visual.CenterPoint = new Vector3((float)framed.ActualWidth * at.X, (float)framed.ActualHeight * at.Y, 0);
        var ease = c.CreateCubicBezierEasingFunction(new Vector2(easing.X1, easing.Y1), new Vector2(easing.X2, easing.Y2));

        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, from);
        fade.InsertKeyFrame(1, 1, ease);
        Time(fade, duration, delay);
        visual.StartAnimation("Opacity", fade);

        if (rise != 0)
        {
            var move = c.CreateVector3KeyFrameAnimation();
            move.InsertKeyFrame(0, new Vector3(0, rise, 0));
            move.InsertKeyFrame(1, Vector3.Zero, ease);
            Time(move, duration, delay);
            visual.StartAnimation("Translation", move);
        }

        if (scale != 1)
        {
            var grow = c.CreateVector3KeyFrameAnimation();
            grow.InsertKeyFrame(0, new Vector3(scale, scale, 1));
            grow.InsertKeyFrame(1, Vector3.One, ease);
            Time(grow, duration, delay);
            visual.StartAnimation("Scale", grow);
        }
    }

    /// <summary>Shown as it is, with no motion (what <see cref="Enter"/> hid is shown again).</summary>
    public static void ShowNow(UIElement element) => ElementCompositionPreview.GetElementVisual(element).Opacity = 1;

    /// <summary>The duration and the wait before it; while it waits, the first frame (Lamha 2's <c>fill: backwards</c>).</summary>
    private static void Time(KeyFrameAnimation animation, TimeSpan duration, TimeSpan delay)
    {
        animation.Duration = duration;
        animation.DelayTime = delay;
        animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
    }
}
