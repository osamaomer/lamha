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
/// (44 px in the design). It reads the same in both languages.
/// </summary>
public sealed partial class GoalRing : UserControl
{
    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(GoalRing), new PropertyMetadata(0.0, (d, _) => ((GoalRing)d).Draw()));

    public static readonly DependencyProperty TrackProperty =
        DependencyProperty.Register(nameof(Track), typeof(Brush), typeof(GoalRing), new PropertyMetadata(null, (d, _) => ((GoalRing)d).Paint()));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(GoalRing), new PropertyMetadata(null, (d, _) => ((GoalRing)d).Paint()));

    private const double Centre = 20;
    private const double Radius = 16;
    private const double Thickness = 4;

    private readonly Ellipse _track = new() { Width = 2 * Radius, Height = 2 * Radius, StrokeThickness = Thickness };
    private readonly Path _arc = new() { StrokeThickness = Thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

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
        Draw();
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

    private void Draw()
    {
        var p = Math.Clamp(Progress, 0, 1);
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
