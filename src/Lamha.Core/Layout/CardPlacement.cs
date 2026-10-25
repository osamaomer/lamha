namespace Lamha.Core.Layout;

/// <summary>A point or rectangle in screen pixels.</summary>
public readonly record struct PixelPoint(int X, int Y);

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>Where the card's window goes, and where the card points from inside it.</summary>
public readonly record struct CardBounds(PixelRect Window, PixelPoint Anchor);

/// <summary>
/// Places windows next to the mouse inside the screen's work area. Ported from Lamha 2's app-rules.js
/// (<c>cardPlacement</c>, <c>cardSize</c>, <c>panelPlacement</c>), with the same numbers.
/// </summary>
public static class CardPlacement
{
    /// <summary>Distance between the mouse and the card's edge.</summary>
    private const int Gap = 16;

    /// <summary>Below the mouse when there's this much room under it, or more room under it than above.</summary>
    private const int RoomBelow = 380;

    /// <summary>
    /// The card (<paramref name="width"/> × <paramref name="height"/>) under the mouse, or above it when there's no room
    /// below; centred on the mouse and kept inside the work area. A toast (a short hint) always goes above.
    /// </summary>
    public static CardBounds Place(PixelPoint cursor, PixelRect workArea, int width, int height, bool toast = false)
    {
        var x = Clamp(cursor.X - (int)Math.Round(width / 2.0, MidpointRounding.AwayFromZero), workArea.X, workArea.Right - width);
        var down = !toast && (workArea.Bottom - cursor.Y >= RoomBelow || workArea.Bottom - cursor.Y >= cursor.Y - workArea.Y);
        var y = Clamp(down ? cursor.Y + Gap : cursor.Y + (toast ? 60 : -Gap) - height, workArea.Y, workArea.Bottom - height);
        var anchor = new PixelPoint(
            cursor.X - x,
            down ? Math.Max(12, cursor.Y + Gap - y) : Math.Min(height - 12, cursor.Y - Gap - y));
        return new CardBounds(new PixelRect(x, y, width, height), anchor);
    }

    /// <summary>The card's size at 100 % grown by the text scale, never bigger than the work area.</summary>
    public static (int Width, int Height) Size(int width, int height, double scale, PixelRect workArea)
    {
        var z = double.IsFinite(scale) && scale > 1 ? scale : 1;
        return (
            Math.Min((int)Math.Round(width * z, MidpointRounding.AwayFromZero), workArea.Width),
            Math.Min((int)Math.Round(height * z, MidpointRounding.AwayFromZero), workArea.Height));
    }

    /// <summary>
    /// The card's window at a new <paramref name="height"/> (its answer arrived, or a section opened): new in Lamha 3,
    /// whose window follows the card's height. A card under the mouse keeps its top edge and grows down, one above it
    /// keeps its bottom edge and grows up, so the edge next to the selection stays put; either way inside the work area.
    /// </summary>
    public static PixelRect Resize(PixelRect window, int height, PixelRect workArea, bool above)
    {
        var y = above ? window.Bottom - height : window.Y;
        return window with { Y = Clamp(y, workArea.Y, workArea.Bottom - height), Height = height };
    }

    /// <summary>The clipboard panel under the mouse, or above it when there's no room below, inside the work area.</summary>
    public static PixelRect PlacePanel(PixelPoint cursor, PixelRect workArea, int width, int height)
    {
        var below = workArea.Bottom - cursor.Y >= height + Gap;
        return new PixelRect(
            Clamp(cursor.X - (int)Math.Round(width / 2.0, MidpointRounding.AwayFromZero), workArea.X, workArea.Right - width),
            Clamp(below ? cursor.Y + Gap : cursor.Y - Gap - height, workArea.Y, workArea.Bottom - height),
            width,
            height);
    }

    /// <summary>Like JavaScript's clamp in app-rules.js: the lower bound wins when the window is bigger than the area.</summary>
    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(value, max));
}
