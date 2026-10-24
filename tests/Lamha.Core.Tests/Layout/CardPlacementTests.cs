using Lamha.Core.Layout;

namespace Lamha.Core.Tests.Layout;

/// <summary>Ported from Lamha 2's test-desktop.mjs: "the card window stays on the mouse's monitor".</summary>
public class CardPlacementTests
{
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1040);
    private const int W = 480;
    private const int H = 620;

    private static bool Inside(PixelRect r, PixelRect area) =>
        r.X >= area.X && r.Y >= area.Y && r.Right <= area.Right && r.Bottom <= area.Bottom;

    [Fact]
    public void BiggerTextMakesABiggerWindowButNeverPastTheScreen()
    {
        Assert.Equal((W, H), CardPlacement.Size(W, H, 1, WorkArea));
        Assert.Equal((720, 930), CardPlacement.Size(W, H, 1.5, WorkArea));
        Assert.Equal((1440, 1040), CardPlacement.Size(W, H, 3, WorkArea));
        Assert.Equal((W, H), CardPlacement.Size(W, H, 0.5, WorkArea));
        Assert.Equal((W, H), CardPlacement.Size(W, H, double.NaN, WorkArea));
    }

    [Fact]
    public void BelowTheMouseWhenThereIsRoom()
    {
        var top = CardPlacement.Place(new PixelPoint(960, 100), WorkArea, W, H);
        Assert.Equal(new PixelRect(720, 116, W, H), top.Window);
        Assert.Equal(new PixelPoint(240, 12), top.Anchor);
    }

    [Fact]
    public void AboveTheMouseNearTheBottom()
    {
        var low = CardPlacement.Place(new PixelPoint(960, 1000), WorkArea, W, H);
        Assert.True(Inside(low.Window, WorkArea));
        Assert.True(low.Window.Y + low.Anchor.Y < 1000);
    }

    [Fact]
    public void NeverOffTheScreensLeftEdge()
    {
        var corner = CardPlacement.Place(new PixelPoint(5, 5), WorkArea, W, H);
        Assert.Equal((0, 21), (corner.Window.X, corner.Window.Y));
    }

    [Fact]
    public void OnTheLeftMonitorNotAcrossTwo()
    {
        var second = new PixelRect(-1920, 0, 1920, 1040);
        Assert.True(CardPlacement.Place(new PixelPoint(-10, 300), second, W, H).Window.Right <= 0);
    }

    [Fact]
    public void AToastGoesAboveTheMouse()
    {
        var toast = CardPlacement.Place(new PixelPoint(960, 100), WorkArea, 300, 80, toast: true);
        Assert.Equal(80, toast.Window.Y);
    }

    [Fact]
    public void ThePanelGoesAboveTheMouseWhenThereIsNoRoomBelow()
    {
        var panel = CardPlacement.PlacePanel(new PixelPoint(1910, 1030), WorkArea, 380, 460);
        Assert.True(Inside(panel, WorkArea));
        Assert.True(panel.Bottom <= 1030 - 16);
    }
}
