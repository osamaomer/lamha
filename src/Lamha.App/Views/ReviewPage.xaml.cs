using System.ComponentModel;
using System.Numerics;
using Lamha.App.Services;
using Lamha.App.ViewModels.Main;
using Lamha.Core.Appearance;
using Lamha.Core.Review;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;
using Windows.UI.Core;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace Lamha.App.Views;

/// <summary>
/// The main window's Review page: the counts, the progress and the flashcard (ViewModels/Main/ReviewViewModel), and
/// Lamha 2's motion for it (popup.js <c>flyOff</c> and <c>renderReview</c>, with their numbers): the answered card
/// leaves the way it was answered, the next one rises in, the meaning fades in, and "Well done" pops in.
/// </summary>
public sealed partial class ReviewPage : UserControl
{
    /// <summary>How far an answered card flies along the line, and how far "Hard" drops it, in pixels.</summary>
    private const float FlyDistance = 170;
    private const float DropDistance = 46;

    private static readonly TimeSpan FlyTime = TimeSpan.FromMilliseconds(280);

    /// <summary>The next card rises once the answered one is on its way (Lamha 2: 240 ms, 110 ms later at full).</summary>
    private const int EnterMilliseconds = 240;
    private static readonly TimeSpan EnterDelay = TimeSpan.FromMilliseconds(110);
    private const int RevealMilliseconds = 180;
    private const int DoneMilliseconds = 300;

    public ReviewPage(ReviewViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.Answering = FlyOffAsync;
        ViewModel.Answered += BringInNext;
    }

    public ReviewViewModel ViewModel { get; }

    // Its own words (x:Bind reads them once).
    public string ProgressLabel { get; } = Localization.Get("Review_Progress");

    public string NewWord { get; } = Localization.Get("Review_NewWord");

    public string ShowLabel { get; } = Localization.Get("Review_Show").Trim();

    public string AgainLabel { get; } = Localization.Get("Review_Again");

    public string HardLabel { get; } = Localization.Get("Review_Hard");

    public string GoodLabel { get; } = Localization.Get("Review_Good");

    public string KeyOne { get; } = Localization.Format("Review_Key", 1);

    public string KeyTwo { get; } = Localization.Format("Review_Key", 2);

    public string KeyThree { get; } = Localization.Format("Review_Key", 3);

    public string RemoveLabel { get; } = Localization.Get("Review_Remove");

    public string DoneTitle { get; } = Localization.Get("Review_DoneTitle");

    public string EmptyTitle { get; } = Localization.Get("Review_EmptyTitle");

    public string EmptyText { get; } = Localization.Get("Review_EmptyText");

    /// <summary>
    /// The focus on the card, so Space works without a click first; as if clicked, like the pane's, so no focus
    /// rectangle shows on a window opened with the mouse.
    /// </summary>
    public void FocusCard() => DispatcherQueue.TryEnqueue(() => (ViewModel.IsShown ? GoodButton : ShowButton).Focus(FocusState.Pointer));

    /// <summary>
    /// Space or Enter shows the meaning; 1, 2, 3 answer once it shows (Arabic-Indic digits too), as in Lamha 2. Not
    /// with Ctrl or Alt, which belong to the window and Windows.
    /// </summary>
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu))
        {
            return;
        }

        if (e.Key is VirtualKey.Space or VirtualKey.Enter && !ViewModel.IsShown && ViewModel.IsCard)
        {
            e.Handled = true;
            ViewModel.RevealCommand.Execute(null);
            return;
        }

        var number = e.Key is >= VirtualKey.Number1 and <= VirtualKey.Number3 ? e.Key - VirtualKey.Number1 + 1
            : e.Key is >= VirtualKey.NumberPad1 and <= VirtualKey.NumberPad3 ? e.Key - VirtualKey.NumberPad1 + 1
            : 0;
        if (number > 0 && ViewModel.PickGrade(number))
        {
            e.Handled = true;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReviewViewModel.IsShown) or nameof(ReviewViewModel.Current) && ViewModel.IsCard)
        {
            FocusCard();
        }

        if (e.PropertyName == nameof(ReviewViewModel.IsShown) && ViewModel.IsShown)
        {
            // The back fades in (Lamha 2's .rv-back), once it's laid out.
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => Animate(BackBox, Animations.Level, RevealMilliseconds, from: 0, rise: 0, scale: 1, Motion.EnterEasing));
        }
    }

    // ----- motion -----

    /// <summary>
    /// The answered card leaves the way it was answered: Knew it on toward the end of the line, Forgot back toward its
    /// start, Hard drops a little (full motion only). A picture of the card (the word and its meaning; the buttons stay
    /// behind) flies while the real card waits, hidden, for the next word.
    /// </summary>
    private async Task FlyOffAsync(Grade grade)
    {
        var level = Animations.Level;
        if (level == MotionLevel.Off)
        {
            return;
        }

        var card = ElementCompositionPreview.GetElementVisual(CardBox);
        if (level == MotionLevel.Full && CardBox.ActualWidth > 0)
        {
            try
            {
                await LaunchPictureAsync(grade).ConfigureAwait(true);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                AppLog.Error("review motion", e); // the answer still counts; only the picture is missing
            }
        }

        // Hidden until the next one rises in (or "Well done" pops in); the done panel waits the same way.
        card.Opacity = 0;
        ElementCompositionPreview.GetElementVisual(DoneBox).Opacity = 0;
    }

    private async Task LaunchPictureAsync(Grade grade)
    {
        // The buttons stay behind: hidden for the picture only, the card's size unchanged.
        GradesBox.Opacity = 0;
        RemoveLink.Opacity = 0;
        var picture = new RenderTargetBitmap();
        try
        {
            await picture.RenderAsync(CardBox);
        }
        finally
        {
            GradesBox.Opacity = 1;
            RemoveLink.Opacity = 1;
        }

        var bounds = CardBox.TransformToVisual(Flying).TransformBounds(new global::Windows.Foundation.Rect(0, 0, CardBox.ActualWidth, CardBox.ActualHeight));
        var ghost = new Image { Source = picture, Width = bounds.Width, Height = bounds.Height, Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill };
        Canvas.SetLeft(ghost, bounds.X);
        Canvas.SetTop(ghost, bounds.Y);
        Flying.Children.Add(ghost);

        ElementCompositionPreview.SetIsTranslationEnabled(ghost, true);
        var visual = ElementCompositionPreview.GetElementVisual(ghost);
        var c = visual.Compositor;
        visual.CenterPoint = new Vector3((float)bounds.Width / 2, (float)bounds.Height / 2, 0);
        var ease = c.CreateCubicBezierEasingFunction(new Vector2(0.5f, 0), new Vector2(0.75f, 0));

        // Toward the end of the line: right in English, left in Arabic.
        var end = Localization.IsArabic ? -1f : 1f;
        var dx = grade == Grade.Good ? FlyDistance * end : grade == Grade.Again ? -FlyDistance * end : 0;
        var hard = grade == Grade.Hard;

        var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
        var move = c.CreateVector3KeyFrameAnimation();
        move.InsertKeyFrame(0.3f, hard ? new Vector3(0, 10, 0) : new Vector3(dx * 0.25f, 0, 0), ease);
        move.InsertKeyFrame(1, hard ? new Vector3(0, DropDistance, 0) : new Vector3(dx, 0, 0), ease);
        move.Duration = FlyTime;
        visual.StartAnimation("Translation", move);

        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 1);
        fade.InsertKeyFrame(0.3f, 0.9f, ease);
        fade.InsertKeyFrame(1, 0, ease);
        fade.Duration = FlyTime;
        visual.StartAnimation("Opacity", fade);

        if (hard)
        {
            var shrink = c.CreateVector3KeyFrameAnimation();
            shrink.InsertKeyFrame(0.3f, new Vector3(0.98f, 0.98f, 1), ease);
            shrink.InsertKeyFrame(1, new Vector3(0.92f, 0.92f, 1), ease);
            shrink.Duration = FlyTime;
            visual.StartAnimation("Scale", shrink);
        }
        else
        {
            var turn = c.CreateScalarKeyFrameAnimation();
            turn.InsertKeyFrame(0.3f, dx > 0 ? 2 : -2, ease);
            turn.InsertKeyFrame(1, dx > 0 ? 9 : -9, ease);
            turn.Duration = FlyTime;
            visual.StartAnimation("RotationAngleInDegrees", turn);
        }

        batch.End();
        batch.Completed += (_, _) => Flying.Children.Remove(ghost);
    }

    /// <summary>The next card rises in; or, when that was the last one, "Well done" pops in (Lamha 2's celebration).</summary>
    private void BringInNext()
    {
        var level = Animations.Level;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            // The part that isn't coming in is reset, so it's there the next time it shows without motion.
            if (ViewModel.IsCard)
            {
                Animate(CardBox, level, EnterMilliseconds, from: 0, rise: 14, scale: 0.98f, Motion.EnterEasing, level == MotionLevel.Full ? EnterDelay : TimeSpan.Zero);
                ShowNow(DoneBox);
            }
            else if (ViewModel.IsDone)
            {
                Animate(DoneBox, level, DoneMilliseconds, from: 0, rise: 0, scale: 0.94f, (0.2f, 0.9f, 0.3f, 1.25f));
                ShowNow(CardBox);
            }
            else
            {
                ShowNow(CardBox);
                ShowNow(DoneBox);
            }
        });
    }

    /// <summary>
    /// <paramref name="element"/> comes in: from <paramref name="from"/> opacity, <paramref name="rise"/> px lower and at
    /// <paramref name="scale"/>, to where it is (Lamha 2's <c>LamhaMotion.play</c>: a quarter faster at subtle, none at off).
    /// </summary>
    private static void Animate(UIElement element, MotionLevel level, int milliseconds, float from, float rise, float scale,
        (float X1, float Y1, float X2, float Y2) easing, TimeSpan delay = default)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var duration = Motion.Time(level, milliseconds);
        if (duration == TimeSpan.Zero || element is not FrameworkElement { ActualWidth: > 0 } framed)
        {
            ShowNow(element);
            return;
        }

        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var c = visual.Compositor;
        visual.CenterPoint = new Vector3((float)framed.ActualWidth / 2, (float)framed.ActualHeight / 2, 0);
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

    /// <summary>The duration and the wait before it; while it waits, the first frame (Lamha 2's <c>fill: backwards</c>).</summary>
    private static void Time(KeyFrameAnimation animation, TimeSpan duration, TimeSpan delay)
    {
        animation.Duration = duration;
        animation.DelayTime = delay;
        animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
    }

    private static void ShowNow(UIElement element) => ElementCompositionPreview.GetElementVisual(element).Opacity = 1;

    private static bool IsDown(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
