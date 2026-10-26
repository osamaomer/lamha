using System.Numerics;
using Lamha.App.Services;
using Lamha.Core.Appearance;
using Microsoft.UI.Composition;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Lamha.App.Controls;

/// <summary>
/// A small celebration: Lamha's letters and dots bursting from an element, Lamha 2's <c>LamhaMotion.burst()</c>
/// (shared/motion.js <c>burstAt</c>, with its numbers). Full motion only, and not in high contrast. Decoration only:
/// hidden from Narrator, never in the way of a click, gone after a second, and never an error.
/// </summary>
public static class Burst
{
    private static readonly string[] Glyphs = ["ل", "م", "ح", "ة", "✦", "•"];

    /// <summary>Lamha 2's five colours: its indigo, cyan, green, amber and pink (decoration, the same in both themes).</summary>
    private static readonly Color[] Colours =
    [
        Color.FromArgb(255, 0x4F, 0x46, 0xE5),
        Color.FromArgb(255, 0x06, 0xB6, 0xD4),
        Color.FromArgb(255, 0x16, 0xA3, 0x4A),
        Color.FromArgb(255, 0xF5, 0x9E, 0x0B),
        Color.FromArgb(255, 0xEC, 0x48, 0x99),
    ];

    /// <summary>
    /// <paramref name="count"/> letters burst from the middle of <paramref name="anchor"/>, drawn on
    /// <paramref name="layer"/> (a canvas over the content, left to right, that doesn't take clicks).
    /// </summary>
    public static void From(FrameworkElement anchor, Canvas layer, int count = 14)
    {
        if (Animations.Level != MotionLevel.Full || anchor is not { ActualWidth: > 0 } || new AccessibilitySettings().HighContrast)
        {
            return;
        }

        try
        {
            Start(anchor, layer, count);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            AppLog.Error("burst", e);
        }
    }

    private static void Start(FrameworkElement anchor, Canvas layer, int count)
    {
        var centre = anchor.TransformToVisual(layer).TransformPoint(new Point(anchor.ActualWidth / 2, anchor.ActualHeight / 2));
        var c = ElementCompositionPreview.GetElementVisual(layer).Compositor;
        var ease = c.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.7f), new Vector2(0.3f, 1f));
        var random = Random.Shared;
        var glyphs = new List<TextBlock>(count);
        var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
        for (var i = 0; i < count; i++)
        {
            var glyph = new TextBlock
            {
                Text = Glyphs[i % Glyphs.Length],
                FontSize = 14 + (i % 3 * 4),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colours[i % Colours.Length]),
                IsHitTestVisible = false,
            };
            AutomationProperties.SetAccessibilityView(glyph, AccessibilityView.Raw);
            glyph.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = glyph.DesiredSize;
            Canvas.SetLeft(glyph, centre.X - (size.Width / 2)); // Lamha 2's translate(-50%, -50%)
            Canvas.SetTop(glyph, centre.Y - (size.Height / 2));
            layer.Children.Add(glyph);
            glyphs.Add(glyph);

            ElementCompositionPreview.SetIsTranslationEnabled(glyph, true);
            var visual = ElementCompositionPreview.GetElementVisual(glyph);
            visual.CenterPoint = new Vector3((float)size.Width / 2, (float)size.Height / 2, 0);
            var angle = (Math.PI * 2 * i / count) + (random.NextDouble() * 0.4);
            var distance = 46 + (random.NextDouble() * 40);
            var duration = TimeSpan.FromMilliseconds(700 + (random.NextDouble() * 200));

            var move = c.CreateVector3KeyFrameAnimation();
            move.InsertKeyFrame(1, new Vector3((float)(Math.Cos(angle) * distance), (float)((Math.Sin(angle) * distance) - 18), 0), ease);
            move.Duration = duration;
            visual.StartAnimation("Translation", move);

            var grow = c.CreateVector3KeyFrameAnimation();
            grow.InsertKeyFrame(0, new Vector3(0.4f, 0.4f, 1));
            grow.InsertKeyFrame(1, Vector3.One, ease);
            grow.Duration = duration;
            visual.StartAnimation("Scale", grow);

            var turn = c.CreateScalarKeyFrameAnimation();
            turn.InsertKeyFrame(1, (float)((random.NextDouble() - 0.5) * 60), ease);
            turn.Duration = duration;
            visual.StartAnimation("RotationAngleInDegrees", turn);

            var fade = c.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 1);
            fade.InsertKeyFrame(1, 0, ease);
            fade.Duration = duration;
            visual.StartAnimation("Opacity", fade);
        }

        batch.End();
        batch.Completed += (_, _) =>
        {
            foreach (var glyph in glyphs)
            {
                layer.Children.Remove(glyph);
            }
        };
    }
}
