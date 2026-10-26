using System.Globalization;

namespace Lamha.Core.Layout;

/// <summary>
/// Where the main window was when it closed, in screen pixels, and whether it was maximised: it opens there again, as
/// Windows' own apps do (docs/design/system.md, The window). Kept as one line in the state store ("x,y,w,h,1").
/// </summary>
public sealed record WindowPlace(int X, int Y, int Width, int Height, bool Maximized)
{
    /// <summary>The state store's key.</summary>
    public const string Key = "mainWindow.place";

    /// <summary>How much of its top band must still be on the screen to open there: enough to grab and move it.</summary>
    private const int Grab = 100;

    public PixelRect Rect => new(X, Y, Width, Height);

    /// <summary>The place written by <see cref="ToString"/>, or null for anything else.</summary>
    public static WindowPlace? Parse(string? text)
    {
        var parts = (text ?? "").Split(',');
        if (parts.Length != 5)
        {
            return null;
        }

        var n = new int[4];
        for (var i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out n[i]))
            {
                return null;
            }
        }

        return n[2] > 0 && n[3] > 0 && parts[4] is "0" or "1" ? new WindowPlace(n[0], n[1], n[2], n[3], parts[4] == "1") : null;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{X},{Y},{Width},{Height},{(Maximized ? 1 : 0)}");

    /// <summary>
    /// This place on the screen with <paramref name="workArea"/>: as it was when its top band can still be grabbed there
    /// (a screen unplugged since, a resolution lowered), no larger than the screen; otherwise null, and the window opens
    /// in the middle as the first time.
    /// </summary>
    public WindowPlace? On(PixelRect workArea)
    {
        var width = Math.Min(Width, workArea.Width);
        var height = Math.Min(Height, workArea.Height);
        var visible = Math.Min(X + width, workArea.Right) - Math.Max(X, workArea.X);
        var onScreen = Y >= workArea.Y && Y + Grab / 2 <= workArea.Bottom && visible >= Math.Min(Grab, width);
        return onScreen ? this with { Width = width, Height = height } : null;
    }
}
