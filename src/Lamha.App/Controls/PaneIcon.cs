using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Lamha.App.Controls;

/// <summary>
/// The main window's pages in the pane: Lamha's own coloured icons, in Windows Settings' style (20 × 20, one main
/// colour each, soft shading, the same in both themes). Drawn as XAML from the signed-off drafts in
/// docs/design/system.html (decision 0020): Translate, Write, Review, History, Clipboard and Settings.
/// </summary>
public sealed partial class PaneIcon : UserControl
{
    public static readonly DependencyProperty PageProperty =
        DependencyProperty.Register(nameof(Page), typeof(string), typeof(PaneIcon), new PropertyMetadata("", (d, _) => ((PaneIcon)d).Draw()));

    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";

    /// <summary>A gradient from the top left to the bottom right, as the drafts' SVG.</summary>
    private static string Gradient(string from, string to, string end = "1,1") =>
        $"<LinearGradientBrush StartPoint=\"0,0\" EndPoint=\"{end}\"><GradientStop Offset=\"0\" Color=\"{from}\" /><GradientStop Offset=\"1\" Color=\"{to}\" /></LinearGradientBrush>";

    private static readonly Dictionary<string, string> Drawings = new()
    {
        // Two speech bubbles: ع in the cyan one behind, A in the indigo one in front.
        ["translate"] =
            $"<Path Data=\"M8.2 2.2h8.3a1.6 1.6 0 0 1 1.6 1.6v5.9a1.6 1.6 0 0 1-1.6 1.6h-1.3v2.3l-2.7-2.3H8.2a1.6 1.6 0 0 1-1.6-1.6V3.8a1.6 1.6 0 0 1 1.6-1.6z\"><Path.Fill>{Gradient("#5EE0F2", "#0891B2")}</Path.Fill></Path>"
            + "<TextBlock Canvas.Left=\"8.35\" Canvas.Top=\"2.2\" Width=\"8\" TextAlignment=\"Center\" FontFamily=\"Segoe UI\" FontSize=\"6.6\" FontWeight=\"Bold\" Foreground=\"White\" Text=\"ع\" IsTextScaleFactorEnabled=\"False\" />"
            + $"<Path Data=\"M3.5 7.3h8.4a1.6 1.6 0 0 1 1.6 1.6v5.5a1.6 1.6 0 0 1-1.6 1.6H7.5l-2.8 2.3v-2.3H3.5a1.6 1.6 0 0 1-1.6-1.6V8.9a1.6 1.6 0 0 1 1.6-1.6z\"><Path.Fill>{Gradient("#9EA6FF", "#4F46E5")}</Path.Fill></Path>"
            + "<TextBlock Canvas.Left=\"3.7\" Canvas.Top=\"7.3\" Width=\"8\" TextAlignment=\"Center\" FontFamily=\"Segoe UI\" FontSize=\"6.4\" FontWeight=\"Bold\" Foreground=\"White\" Text=\"A\" IsTextScaleFactorEnabled=\"False\" />",
        // A yellow pencil, tilted, over a grey line.
        ["write"] =
            "<Canvas Width=\"20\" Height=\"20\"><Canvas.RenderTransform><RotateTransform Angle=\"-45\" CenterX=\"10\" CenterY=\"10\" /></Canvas.RenderTransform>"
            + $"<Rectangle Canvas.Left=\"4.2\" Canvas.Top=\"7.9\" Width=\"9.6\" Height=\"4.2\"><Rectangle.Fill>{Gradient("#FCD34D", "#F59E0B", "0,1")}</Rectangle.Fill></Rectangle>"
            + "<Rectangle Canvas.Left=\"4.2\" Canvas.Top=\"7.9\" Width=\"9.6\" Height=\"1.4\" Fill=\"#B3FDE68A\" />"
            + "<Rectangle Canvas.Left=\"2.1\" Canvas.Top=\"7.9\" Width=\"1.6\" Height=\"4.2\" RadiusX=\"0.2\" RadiusY=\"0.2\" Fill=\"#9CA3AF\" />"
            + "<Path Data=\"M2.1 7.9h-.4a1.3 1.3 0 0 0-1.3 1.3v1.6a1.3 1.3 0 0 0 1.3 1.3h.4z\" Fill=\"#F472B6\" />"
            + "<Path Data=\"M13.8 7.9 18.4 10l-4.6 2.1z\" Fill=\"#FDE3B5\" />"
            + "<Path Data=\"M16.9 9.3 18.4 10l-1.5.7z\" Fill=\"#374151\" /></Canvas>"
            + "<Path Data=\"M3 18.2h6\" Stroke=\"#9CA3AF\" StrokeThickness=\"1.1\" StrokeStartLineCap=\"Round\" StrokeEndLineCap=\"Round\" />",
        // Two flashcards: teal behind, indigo in front with a check.
        ["review"] =
            "<Rectangle Canvas.Left=\"6.4\" Canvas.Top=\"1.9\" Width=\"10.6\" Height=\"12.8\" RadiusX=\"2\" RadiusY=\"2\">"
            + "<Rectangle.RenderTransform><RotateTransform Angle=\"9\" CenterX=\"5.3\" CenterY=\"6.4\" /></Rectangle.RenderTransform>"
            + $"<Rectangle.Fill>{Gradient("#5EEAD4", "#0D9488")}</Rectangle.Fill></Rectangle>"
            + $"<Rectangle Canvas.Left=\"2.8\" Canvas.Top=\"5\" Width=\"10.6\" Height=\"13\" RadiusX=\"2\" RadiusY=\"2\"><Rectangle.Fill>{Gradient("#A5B4FC", "#4F46E5")}</Rectangle.Fill></Rectangle>"
            + "<Path Data=\"M5.6 11.6l2 2 3.9-4.3\" Stroke=\"White\" StrokeThickness=\"1.6\" StrokeStartLineCap=\"Round\" StrokeEndLineCap=\"Round\" StrokeLineJoin=\"Round\" />",
        // A blue clock with its hands, and the arrow of going back in time.
        ["history"] =
            $"<Ellipse Canvas.Left=\"2.9\" Canvas.Top=\"2.9\" Width=\"15.2\" Height=\"15.2\"><Ellipse.Fill>{Gradient("#7DD3FC", "#0369A1")}</Ellipse.Fill></Ellipse>"
            + "<Ellipse Canvas.Left=\"2.9\" Canvas.Top=\"2.9\" Width=\"15.2\" Height=\"15.2\" Stroke=\"#47FFFFFF\" StrokeThickness=\"0.8\" />"
            + "<Path Data=\"M10.5 6.3v4.4l2.9 1.8\" Stroke=\"White\" StrokeThickness=\"1.6\" StrokeStartLineCap=\"Round\" StrokeEndLineCap=\"Round\" StrokeLineJoin=\"Round\" />"
            + "<Path Data=\"M1.6 4.6 2 8.1l3.4-.9\" Stroke=\"#0369A1\" StrokeThickness=\"1.4\" StrokeStartLineCap=\"Round\" StrokeEndLineCap=\"Round\" StrokeLineJoin=\"Round\" />",
        // An amber clipboard with a white page and a grey clip.
        ["clipboard"] =
            $"<Rectangle Canvas.Left=\"3.3\" Canvas.Top=\"3\" Width=\"13.4\" Height=\"15.6\" RadiusX=\"2\" RadiusY=\"2\"><Rectangle.Fill>{Gradient("#FBBF24", "#B45309")}</Rectangle.Fill></Rectangle>"
            + "<Rectangle Canvas.Left=\"5.3\" Canvas.Top=\"5.6\" Width=\"9.4\" Height=\"11.2\" RadiusX=\"1\" RadiusY=\"1\" Fill=\"White\" />"
            + "<Path Data=\"M7.3 9.2h5.4M7.3 11.7h5.4M7.3 14.2h3.2\" Stroke=\"#CBD5E1\" StrokeThickness=\"1.1\" StrokeStartLineCap=\"Round\" StrokeEndLineCap=\"Round\" />"
            + $"<Rectangle Canvas.Left=\"6.9\" Canvas.Top=\"1.6\" Width=\"6.2\" Height=\"3.6\" RadiusX=\"1.3\" RadiusY=\"1.3\" Stroke=\"#6B7280\" StrokeThickness=\"0.5\"><Rectangle.Fill>{Gradient("#F3F4F6", "#9CA3AF", "0,1")}</Rectangle.Fill></Rectangle>",
        // A grey gear, as Windows' Settings.
        ["settings"] =
            "<Path Data=\"F0 M16.17 8.29 L18.30 8.73 L18.30 11.27 L16.17 11.71 L15.57 13.15 L16.77 14.98 L14.98 16.77 L13.15 15.57 L11.71 16.17 L11.27 18.30 L8.73 18.30 L8.29 16.17 L6.85 15.57 L5.02 16.77 L3.23 14.98 L4.43 13.15 L3.83 11.71 L1.70 11.27 L1.70 8.73 L3.83 8.29 L4.43 6.85 L3.23 5.02 L5.02 3.23 L6.85 4.43 L8.29 3.83 L8.73 1.70 L11.27 1.70 L11.71 3.83 L13.15 4.43 L14.98 3.23 L16.77 5.02 L15.57 6.85 Z M10 6.9a3.1 3.1 0 1 0 0 6.2a3.1 3.1 0 1 0 0-6.2z\">"
            + $"<Path.Fill>{Gradient("#C3C8D2", "#555C6B")}</Path.Fill></Path>"
            + "<Ellipse Canvas.Left=\"6.9\" Canvas.Top=\"6.9\" Width=\"6.2\" Height=\"6.2\" Stroke=\"#59FFFFFF\" StrokeThickness=\"0.7\" />",
    };

    public PaneIcon()
    {
        Width = Height = FluentIcon.Pixels(IconSize.Large);
        IsTabStop = false;
        // Arabic mirrors the window, not the drawings: a pencil and a clock read the same either way.
        FlowDirection = FlowDirection.LeftToRight;
    }

    /// <summary>The page: "translate", "write", "review", "history", "clipboard" or "settings".</summary>
    public string Page
    {
        get => (string)GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    private void Draw()
    {
        Content = Drawings.TryGetValue(Page, out var drawing)
            ? new Viewbox { Child = (UIElement)XamlReader.Load($"<Canvas {Ns} Width=\"20\" Height=\"20\">{drawing}</Canvas>") }
            : null;
    }
}
