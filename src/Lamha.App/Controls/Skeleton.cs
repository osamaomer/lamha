using Lamha.App.Services;
using Lamha.Core.Appearance;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace Lamha.App.Controls;

/// <summary>
/// The grey shape of what's coming while the card waits, a light sweeping across it every 1.1 s: Lamha 2's
/// <c>.sk</c> and its shimmer (content/styles.js). Still when animations are off; the look is in Themes/Controls.xaml.
/// </summary>
public sealed partial class Skeleton : Control
{
    /// <summary>Lamha 2's <c>animation: shimmer 1.1s infinite</c>.</summary>
    private static readonly TimeSpan Sweep = TimeSpan.FromMilliseconds(1100);

    private Rectangle? _shine;
    private TranslateTransform? _move;
    private Storyboard? _story;

    public Skeleton()
    {
        DefaultStyleKey = typeof(Skeleton);
        SizeChanged += (_, _) => Start();
        Unloaded += (_, _) => Stop();
        Loaded += (_, _) => Start();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _shine = GetTemplateChild("Shine") as Rectangle;
        _move = GetTemplateChild("Move") as TranslateTransform;
        Start();
    }

    /// <summary>The band, as wide as the shape, from just before its start to just past its end.</summary>
    private void Start()
    {
        Stop();
        if (_shine is null || _move is null || ActualWidth <= 0 || Animations.Level == MotionLevel.Off)
        {
            return;
        }

        _shine.Visibility = Visibility.Visible;
        var animation = new DoubleAnimation
        {
            From = -ActualWidth,
            To = ActualWidth,
            Duration = Sweep,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Storyboard.SetTarget(animation, _move);
        Storyboard.SetTargetProperty(animation, "X");
        _story = new Storyboard { Children = { animation } };
        _story.Begin();
    }

    private void Stop()
    {
        _story?.Stop();
        _story = null;
        if (_shine is not null)
        {
            _shine.Visibility = Visibility.Collapsed;
        }
    }
}
