using Lamha.App.Services;
using Lamha.Core.Appearance;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Lamha.App.Controls;

/// <summary>
/// Today's goal as a ring (Lamha 2's <c>ring(p)</c> in popup.js): a circle of radius 16 in a 40 × 40 square, 4 thick,
/// filled clockwise from the top to <see cref="Progress"/> with round ends, over a track. Its size scales the drawing
/// (44 px in the design). It reads the same in both languages. A new progress fills in from the last one on Windows'
/// curve in 333 ms (the design system's Review moments), at once at off or while it isn't on screen.
/// </summary>
public sealed partial class GoalRing : UserControl
{
    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(GoalRing), new PropertyMetadata(0.0, (d, e) => ((GoalRing)d).FillFrom((double)e.OldValue)));

    public static readonly DependencyProperty TrackProperty =
        DependencyProperty.Register(nameof(Track), typeof(Brush), typeof(GoalRing), new PropertyMetadata(null, (d, _) => ((GoalRing)d).Paint()));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(GoalRing), new PropertyMetadata(null, (d, _) => ((GoalRing)d).Paint()));

    private const double Centre = 20;
    private const double Radius = 16;
    private const double Thickness = 4;

    private readonly Ellipse _track = new() { Width = 2 * Radius, Height = 2 * Radius, StrokeThickness = Thickness };
    private readonly Path _arc = new() { StrokeThickness = Thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

    // The fill on its way from one progress to the next.
    private double _shown;
    private double _from;
    private long _fillStart;
    private bool _filling;

    public GoalRing()
    {
        Width = 44;
        Height = 44;
        IsTabStop = false;
        Canvas.SetLeft(_track, Centre - Radius);
        Canvas.SetTop(_track, Centre - Radius);
        // Clockwise from the top in Arabic too, as Lamha 2's SVG drew it.
        var canvas = new Canvas { Width = 2 * Centre, Height = 2 * Centre, FlowDirection = FlowDirection.LeftToRight, Children = { _track, _arc } };
        Content = new Viewbox { Child = canvas };
        Unloaded += (_, _) => Stop();
        Draw(0);
    }

    /// <summary>How much of the goal is done: 0 to 1.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Brush? Track
    {
        get => (Brush?)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>The ring fills from <paramref name="old"/> (or from where it is, if it's still filling) to the new progress.</summary>
    private void FillFrom(double old)
    {
        if (!IsLoaded || Motion.Time(Animations.Level, Motion.SlowMilliseconds) == TimeSpan.Zero)
        {
            Stop();
            Draw(Progress);
            return;
        }

        _from = _filling ? _shown : old;
        _fillStart = Environment.TickCount64;
        if (!_filling)
        {
            _filling = true;
            CompositionTarget.Rendering += OnFillFrame;
        }
    }

    private void OnFillFrame(object? sender, object e)
    {
        var progress = (Environment.TickCount64 - _fillStart) / Motion.Time(Animations.Level, Motion.SlowMilliseconds).TotalMilliseconds;
        Draw(_from + ((Progress - _from) * Motion.Ease(Motion.EnterEasing, progress)));
        if (progress >= 1)
        {
            Stop();
        }
    }

    private void Stop()
    {
        if (_filling)
        {
            _filling = false;
            CompositionTarget.Rendering -= OnFillFrame;
            Draw(Progress);
        }
    }

    private void Draw(double progress)
    {
        _shown = progress;
        var p = Math.Clamp(progress, 0, 1);
        if (p <= 0)
        {
            _arc.Data = null;
            return;
        }

        // A whole circle is two half arcs: an arc that ends where it starts draws nothing.
        var angle = Math.Min(p, 0.9999) * 2 * Math.PI;
        var start = new Point(Centre, Centre - Radius);
        var end = new Point(Centre + (Radius * Math.Sin(angle)), Centre - (Radius * Math.Cos(angle)));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        if (p >= 1)
        {
            figure.Segments.Add(Arc(new Point(Centre, Centre + Radius), large: false));
            figure.Segments.Add(Arc(start, large: false));
        }
        else
        {
            figure.Segments.Add(Arc(end, large: angle > Math.PI));
        }

        _arc.Data = new PathGeometry { Figures = { figure } };
        Paint();
    }

    private void Paint()
    {
        _track.Stroke = Track;
        _arc.Stroke = Fill;
    }

    private static ArcSegment Arc(Point to, bool large) => new()
    {
        Point = to,
        Size = new Size(Radius, Radius),
        IsLargeArc = large,
        SweepDirection = SweepDirection.Clockwise,
    };
}
