using Lamha.Core.Reading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Lamha.App.Controls;

/// <summary>
/// The reader's Aa (<see cref="ReaderLook"/>) in the design system's terms: the book's face and sizes, its line height,
/// the column's width, and the page's colours. A chapter's heading is the text size + 10 (28 at 18), the headings under
/// it + 2, preformatted text − 4; each on its own line height.
/// </summary>
public sealed class BookLook(ReaderLook look)
{
    public ReaderLook Look { get; } = look;

    public FontFamily Font => (FontFamily)Resource(Look.Font switch
    {
        "georgia" => "LamhaBookSerifFontFamily",
        "segoe" => "LamhaFontFamily",
        _ => "LamhaBookFontFamily",
    });

    public double TextSize => Look.Size;

    public double ChapterSize => Look.Size + 10;

    public double HeadingSize => Look.Size + 2;

    public double FixedSize => Look.Size - 4;

    public double ColumnWidth => Look.ColumnWidth;

    /// <summary>The page's colour, or null for the window's own layer.</summary>
    public Brush? Page => Brush(Look.Page switch { "paper" => "LamhaBookPaperBrush", "black" => "LamhaBookBlackBrush", _ => null });

    /// <summary>The book's text on that page, or null for the theme's.</summary>
    public Brush? Ink => Brush(Look.Page switch { "paper" => "LamhaBookPaperTextBrush", "black" => "LamhaBookBlackTextBrush", _ => null });

    /// <summary>The footer's quieter text on that page, or null for the theme's.</summary>
    public Brush? Muted => Brush(Look.Page switch { "paper" => "LamhaBookPaperMutedBrush", "black" => "LamhaBookBlackMutedBrush", _ => null });

    /// <summary>Lines on that page (the footer's, a scene break's), or null for the theme's.</summary>
    public Brush? Line => Brush(Look.Page switch { "paper" => "LamhaBookPaperLineBrush", "black" => "LamhaBookBlackLineBrush", _ => null });

    /// <summary>A box's quiet fill on that page (a paragraph's translation), or null for the theme's chip fill.</summary>
    public Brush? Fill => Brush(Look.Page switch { "paper" => "LamhaBookPaperFillBrush", "black" => "LamhaBookBlackFillBrush", _ => null });

    /// <summary>The controls' theme on that page: paper is light, black is dark, Lamha's follows the window.</summary>
    public ElementTheme Theme => Look.Page switch { "paper" => ElementTheme.Light, "black" => ElementTheme.Dark, _ => ElementTheme.Default };

    /// <summary>A line's height for text of <paramref name="size"/>: the look's spacing for the book's text, a little less for headings.</summary>
    public double LineHeight(double size, bool heading = false) => Math.Round(size * (heading ? 1.3 : Look.LineSpacing));

    private static object Resource(string key) => Application.Current.Resources[key];

    private static Brush? Brush(string? key) => key is null ? null : (Brush)Resource(key);
}
