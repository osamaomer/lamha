using Lamha.Windows.Shell;

namespace Lamha.Windows.Tests.Shell;

public class TrayIconArtTests
{
    private const int Size = 16;

    /// <summary>A 16 × 16 icon of one opaque colour (blue, green, red).</summary>
    private static byte[] Icon(byte b = 0xE5, byte g = 0x46, byte r = 0x4F)
    {
        var pixels = new byte[Size * Size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (b, g, r, 255);
        }

        return pixels;
    }

    private static (byte B, byte G, byte R, byte A) At(byte[] pixels, int x, int y)
    {
        var i = ((y * Size) + x) * 4;
        return (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
    }

    [Fact]
    public void FadedIsGreyAt55Percent()
    {
        var pixels = Icon();
        TrayIconArt.Fade(pixels);
        var (b, g, r, a) = At(pixels, 5, 5);
        Assert.True(b == g && g == r, $"{b} {g} {r}");
        Assert.Equal(Math.Round((0.0722 * 0xE5) + (0.7152 * 0x46) + (0.2126 * 0x4F)), b);
        Assert.Equal(140, a); // 255 × 0.55
    }

    [Fact]
    public void TheDotIsAmberInTheTopRightCornerWithAClearGapAroundIt()
    {
        var pixels = Icon();
        TrayIconArt.Dot(pixels, Size);
        Assert.Equal((0x0B, 0x9E, 0xF5, 255), At(pixels, 12, 3)); // the dot's middle (radius 3.5, centre 12.5, 3.5)
        Assert.Equal(0, At(pixels, 8, 3).A); // the gap, left of the dot
        Assert.Equal(0, At(pixels, 12, 7).A); // and below it
        Assert.Equal((0xE5, 0x46, 0x4F, 255), At(pixels, 2, 13)); // the rest of the icon is as it was
        Assert.Equal((0xE5, 0x46, 0x4F, 255), At(pixels, 5, 2));
    }

    [Fact]
    public void TheDotScalesWithTheIcon()
    {
        const int Big = 32;
        var pixels = new byte[Big * Big * 4];
        TrayIconArt.Dot(pixels, Big);
        var i = ((7 * Big) + 25) * 4; // the middle: radius 7, centre 25, 7
        Assert.Equal(255, pixels[i + 3]);
        Assert.Equal(0, pixels[((25 * Big) + 5) * 4 + 3]); // a transparent icon stays so away from the dot
    }
}
