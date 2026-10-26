using System.Numerics;
using Lamha.Core.Appearance;
using Lamha.Core.Settings;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace Lamha.App.Services;

/// <summary>How a page of the main window came: another page, a deeper one, or back.</summary>
public enum PageMove
{
    Entrance,
    DrillIn,
    Back,
}

/// <summary>
/// How much moves on screen now: the setting (Settings → Appearance → Animations), Windows' Animation effects, and what
/// this PC can do (Core's <see cref="Motion"/>). Read again when a window opens, so a change applies to the next one.
/// Also the windows' common motion: something coming in (<see cref="Enter"/>, Lamha 2's <c>LamhaMotion.play</c>).
/// </summary>
public static class Animations
{
    /// <summary>How far <see cref="Pop"/> grows (the design system's 1 → 1.2 → 1).</summary>
    private const float PopScale = 1.2f;

    /// <summary>How far <see cref="Dip"/> goes (the design system's 98 %).</summary>
    private const float DipScale = 0.98f;

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

    /// <summary>
    /// A page of the main window comes in, as Windows' Settings does it (docs/design/system.md, Motion): a new page rises
    /// 28 px (Entrance), a deeper one slides in from the text's end (Drill in), and going back slides in from its start;
    /// Windows' 250 ms and its curve, a quarter faster at subtle, at once at off.
    /// </summary>
    public static void Page(UIElement element, PageMove move)
    {
        ArgumentNullException.ThrowIfNull(element);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var duration = Motion.Time(Level, Motion.ResizeMilliseconds);
        if (duration == TimeSpan.Zero)
        {
            ShowNow(element);
            return;
        }

        // The window's layout is mirrored in Arabic, its visuals' offsets are not: "the text's end" is left there.
        var end = Localization.IsArabic ? -1f : 1f;
        var from = move switch
        {
            PageMove.DrillIn => new Vector3(40 * end, 0, 0),
            PageMove.Back => new Vector3(-40 * end, 0, 0),
            _ => new Vector3(0, 28, 0),
        };
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var c = visual.Compositor;
        var (x1, y1, x2, y2) = Motion.EnterEasing;
        var ease = c.CreateCubicBezierEasingFunction(new Vector2(x1, y1), new Vector2(x2, y2));

        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, ease);
        Time(fade, duration, TimeSpan.Zero);
        visual.StartAnimation("Opacity", fade);

        var slide = c.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0, from);
        slide.InsertKeyFrame(1, Vector3.Zero, ease);
        Time(slide, duration, TimeSpan.Zero);
        visual.StartAnimation("Translation", slide);
    }

    /// <summary>
    /// <paramref name="element"/> was just shown (a toast, an InfoBar, the card): hidden now, then it comes in once it's laid
    /// out, fading, moving <paramref name="rise"/> px up (down, when negative) and growing from <paramref name="scale"/> at
    /// <paramref name="origin"/> (see <see cref="Enter"/>), in Windows' 167 ms. At off it's simply there.
    /// </summary>
    public static void Appear(UIElement element, float rise, float scale = 1, Vector2? origin = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (Level == MotionLevel.Off)
        {
            ShowNow(element);
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Opacity"); // one that was leaving
        visual.Opacity = 0; // not one frame before it moves
        element.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low,
            () => Enter(element, Level, Motion.EnterMilliseconds, from: 0, rise, scale, Motion.EnterEasing, origin: origin));
    }

    /// <summary>
    /// <paramref name="element"/> goes away: it fades and moves <paramref name="drop"/> px down in Windows' 83 ms, then
    /// <paramref name="done"/> runs (to collapse it, or hide its window). It stays faded until it's shown again
    /// (<see cref="Appear"/>, <see cref="Enter"/>, <see cref="ShowNow"/>), so one shown meanwhile isn't disturbed. At off,
    /// <paramref name="done"/> runs at once.
    /// </summary>
    public static void Leave(UIElement element, float drop, Action done)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(done);
        var duration = Motion.Time(Level, Motion.ExitMilliseconds);
        if (duration == TimeSpan.Zero)
        {
            done();
            return;
        }

        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var c = visual.Compositor;
        var (x1, y1, x2, y2) = Motion.ExitEasing;
        var ease = c.CreateCubicBezierEasingFunction(new Vector2(x1, y1), new Vector2(x2, y2));
        var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);

        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 0, ease);
        fade.Duration = duration;
        visual.StartAnimation("Opacity", fade);

        var move = c.CreateVector3KeyFrameAnimation();
        move.InsertKeyFrame(1, new Vector3(0, drop, 0), ease);
        move.Duration = duration;
        visual.StartAnimation("Translation", move);

        batch.End();
        batch.Completed += (_, _) => done();
    }

    /// <summary>
    /// A small pop that confirms an action (the star filling): 1 → 1.2 → 1 from its middle in Windows' 167 ms, a quarter
    /// faster at subtle, nothing at off.
    /// </summary>
    public static void Pop(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var duration = Motion.Time(Level, Motion.EnterMilliseconds);
        if (duration == TimeSpan.Zero || element.ActualWidth <= 0)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        var c = visual.Compositor;
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        var (x1, y1, x2, y2) = Motion.EnterEasing;
        var ease = c.CreateCubicBezierEasingFunction(new Vector2(x1, y1), new Vector2(x2, y2));
        var pop = c.CreateVector3KeyFrameAnimation();
        pop.InsertKeyFrame(0.4f, new Vector3(PopScale, PopScale, 1), ease);
        pop.InsertKeyFrame(1, Vector3.One, ease);
        pop.Duration = duration;
        visual.StartAnimation("Scale", pop);
    }

    /// <summary>
    /// Pressed, <paramref name="element"/> dips to 98 % from its middle in Windows' 83 ms, and comes back in 167 ms when
    /// let go (Settings' cards, icon buttons); a quarter faster at subtle, nothing at off.
    /// </summary>
    public static void Dip(FrameworkElement element, bool down)
    {
        ArgumentNullException.ThrowIfNull(element);
        var duration = Motion.Time(Level, down ? Motion.ExitMilliseconds : Motion.EnterMilliseconds);
        if (duration == TimeSpan.Zero || element.ActualWidth <= 0)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        var c = visual.Compositor;
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        var (x1, y1, x2, y2) = Motion.EnterEasing;
        var dip = c.CreateVector3KeyFrameAnimation();
        var to = down ? DipScale : 1;
        dip.InsertKeyFrame(1, new Vector3(to, to, 1), c.CreateCubicBezierEasingFunction(new Vector2(x1, y1), new Vector2(x2, y2)));
        dip.Duration = duration;
        visual.StartAnimation("Scale", dip);
    }

    /// <summary>A field with a mistake shakes once, side to side (full only: it's decoration, the message says it).</summary>
    public static void Shake(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (Level != MotionLevel.Full)
        {
            return;
        }

        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var shake = visual.Compositor.CreateVector3KeyFrameAnimation();
        float[] steps = [-6, 5, -3, 2, 0];
        for (var i = 0; i < steps.Length; i++)
        {
            shake.InsertKeyFrame((i + 1f) / steps.Length, new Vector3(steps[i], 0, 0));
        }

        shake.Duration = TimeSpan.FromMilliseconds(Motion.ResizeMilliseconds);
        visual.StartAnimation("Translation", shake);
    }

    /// <summary>
    /// A list's first items come in one after another (Lamha 2's <c>stagger()</c>, full only): at most 8, 22 ms apart,
    /// each rising 6 px over 200 ms (or <paramref name="each"/> apart over <paramref name="milliseconds"/>). The ones further
    /// down are simply there.
    /// </summary>
    public static void Stagger(IEnumerable<UIElement> items, int each = Motion.StaggerEachMilliseconds, int milliseconds = Motion.StaggerMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (Level != MotionLevel.Full)
        {
            return;
        }

        var i = 0;
        foreach (var item in items.Take(Motion.StaggerMax))
        {
            Enter(item, Level, milliseconds, from: 0, (float)Motion.StaggerDistance, scale: 1, Motion.EnterEasing,
                TimeSpan.FromMilliseconds(i++ * each));
        }
    }

    /// <summary>A row of a list glides <paramref name="from"/> px (where it was) to where it is now, in Windows' 250 ms.</summary>
    public static void Glide(UIElement row, float from)
    {
        ArgumentNullException.ThrowIfNull(row);
        ElementCompositionPreview.SetIsTranslationEnabled(row, true);
        var visual = ElementCompositionPreview.GetElementVisual(row);
        var c = visual.Compositor;
        var (x1, y1, x2, y2) = Motion.EnterEasing;
        var glide = c.CreateVector3KeyFrameAnimation();
        glide.InsertKeyFrame(0, new Vector3(0, from, 0));
        glide.InsertKeyFrame(1, Vector3.Zero, c.CreateCubicBezierEasingFunction(new Vector2(x1, y1), new Vector2(x2, y2)));
        glide.Duration = Motion.Time(Level, Motion.ResizeMilliseconds);
        visual.StartAnimation("Translation", glide);
    }

    /// <summary>Shown as it is, with no motion (what <see cref="Enter"/> or <see cref="Leave"/> hid is shown again).</summary>
    public static void ShowNow(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1;
        visual.StopAnimation("Translation");
        visual.Properties.InsertVector3("Translation", Vector3.Zero); // read only where translation is on
    }

    /// <summary>The duration and the wait before it; while it waits, the first frame (Lamha 2's <c>fill: backwards</c>).</summary>
    private static void Time(KeyFrameAnimation animation, TimeSpan duration, TimeSpan delay)
    {
        animation.Duration = duration;
        animation.DelayTime = delay;
        animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
    }
}
