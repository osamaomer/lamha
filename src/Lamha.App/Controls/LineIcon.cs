using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Lamha.App.Controls;

/// <summary>Lamha's icons, by name.</summary>
public enum Glyph
{
    None,
    Speak,
    SpeakSmall,
    Copy,
    Close,
    Settings,
    Back,
    Forward,
    External,
    Search,
    Translate,
    Check,
    Retry,
    Book,
    Sparkle,
    Bookmark,
    Pin,
}

/// <summary>
/// One of Lamha 2's line icons (content.js <c>ICONS</c>, 24 × 24, stroked 2 px with round ends), drawn in the
/// foreground colour at the control's size; phase 1's card design kept them over Segoe Fluent Icons. They read the same
/// in both languages, except Back and Forward, which point the way the text goes.
/// </summary>
public sealed partial class LineIcon : UserControl
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(Glyph), typeof(LineIcon), new PropertyMetadata(Glyph.None, (d, _) => ((LineIcon)d).Draw()));

    public static readonly DependencyProperty FilledProperty =
        DependencyProperty.Register(nameof(Filled), typeof(bool), typeof(LineIcon), new PropertyMetadata(false, (d, _) => ((LineIcon)d).Paint()));

    public static readonly DependencyProperty StrokeWidthProperty =
        DependencyProperty.Register(nameof(StrokeWidth), typeof(double), typeof(LineIcon), new PropertyMetadata(2.0, (d, _) => ((LineIcon)d).Paint()));

    private static readonly Dictionary<Glyph, string[]> Paths = new()
    {
        [Glyph.Speak] = ["M11 5 6 9H2v6h4l5 4V5z", "M15.54 8.46a5 5 0 0 1 0 7.07", "M19.07 4.93a10 10 0 0 1 0 14.14"],
        [Glyph.SpeakSmall] = ["M11 5 6 9H2v6h4l5 4V5z", "M15.54 8.46a5 5 0 0 1 0 7.07"],
        [Glyph.Copy] = ["M20 9h-9a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h9a2 2 0 0 0 2-2v-9a2 2 0 0 0-2-2z", "M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"],
        [Glyph.Close] = ["M18 6 6 18", "M6 6l12 12"],
        [Glyph.Settings] = ["M4 21v-7", "M4 10V3", "M12 21v-9", "M12 8V3", "M20 21v-5", "M20 12V3", "M1 14h6", "M9 8h6", "M17 16h6"],
        // Drawn pointing left, as in English; in Arabic the window's right-to-left layout turns them around.
        [Glyph.Back] = ["M15 18l-6-6 6-6"],
        [Glyph.Forward] = ["M9 18l6-6-6-6"],
        [Glyph.External] = ["M15 3h6v6", "M10 14 21 3", "M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"],
        [Glyph.Search] = ["M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14z", "M20 20l-3.5-3.5"],
        [Glyph.Translate] = ["M5 8l6 6", "M4 14l6-6 2-3", "M2 5h12", "M7 2h1", "M22 22l-5-10-5 10", "M14 18h6"],
        [Glyph.Check] = ["M20 6 9 17l-5-5"],
        [Glyph.Retry] = ["M3 12a9 9 0 1 0 3-6.7L3 8", "M3 3v5h5"],
        [Glyph.Book] = ["M4 19.5A2.5 2.5 0 0 1 6.5 17H20", "M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z"],
        [Glyph.Sparkle] = ["M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9L12 3z", "M19 15l.8 2.2L22 18l-2.2.8L19 21l-.8-2.2L16 18l2.2-.8L19 15z"],
        [Glyph.Bookmark] = ["M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"],
        // Phase 1's card design (new in Lamha 3).
        [Glyph.Pin] = ["M12 17v5", "M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H8a2 2 0 0 0 0 4 1 1 0 0 1 1 1z"],
    };

    private readonly Canvas _canvas = new() { Width = 24, Height = 24 };

    public LineIcon()
    {
        Width = 16;
        Height = 16;
        IsTabStop = false;
        Content = new Viewbox { Child = _canvas };
        RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => Paint());
    }

    public Glyph Glyph
    {
        get => (Glyph)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Filled in the foreground colour too: a word in review (the bookmark), a pinned card.</summary>
    public bool Filled
    {
        get => (bool)GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    /// <summary>The line's width in the icon's 24 × 24 grid (Lamha 2: 2, or 2.4–2.6 for the smallest).</summary>
    public double StrokeWidth
    {
        get => (double)GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    private void Draw()
    {
        _canvas.Children.Clear();
        // Only the arrows follow the text's direction.
        if (Glyph is Glyph.Back or Glyph.Forward)
        {
            _canvas.ClearValue(FlowDirectionProperty);
        }
        else
        {
            _canvas.FlowDirection = FlowDirection.LeftToRight;
        }
        if (!Paths.TryGetValue(Glyph, out var paths))
        {
            return;
        }

        foreach (var data in paths)
        {
            // A geometry can't be shared between paths, so each icon parses its own (XamlReader: no reflection).
            _canvas.Children.Add((Path)XamlReader.Load($"<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"{data}\" />"));
        }

        Paint();
    }

    private void Paint()
    {
        foreach (var path in _canvas.Children.OfType<Path>())
        {
            path.Stroke = Foreground;
            path.Fill = Filled ? Foreground : null;
            path.StrokeThickness = StrokeWidth;
            path.StrokeStartLineCap = path.StrokeEndLineCap = PenLineCap.Round;
            path.StrokeLineJoin = PenLineJoin.Round;
        }
    }
}
