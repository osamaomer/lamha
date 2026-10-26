using Lamha.Core.Layout;

namespace Lamha.Core.Tests.Layout;

/// <summary>The main window opens where it was (decision 0020), and only where it can still be grabbed.</summary>
public class WindowPlaceTests
{
    private static readonly PixelRect Screen = new(0, 0, 1920, 1040);

    [Fact]
    public void APlaceIsWrittenAndReadBack()
    {
        var place = new WindowPlace(-8, 120, 1500, 900, Maximized: true);
        Assert.Equal("-8,120,1500,900,1", place.ToString());
        Assert.Equal(place, WindowPlace.Parse(place.ToString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1,2,3,4")]
    [InlineData("1,2,0,4,0")]
    [InlineData("a,2,3,4,0")]
    [InlineData("1,2,3,4,yes")]
    public void AnythingElseIsNoPlace(string? text) => Assert.Null(WindowPlace.Parse(text));

    [Fact]
    public void ItOpensWhereItWasOnTheScreen()
    {
        var place = new WindowPlace(300, 200, 1000, 680, false);
        Assert.Equal(place, place.On(Screen));
    }

    [Fact]
    public void ItShrinksToAScreenSmallerThanItWas()
    {
        var place = new WindowPlace(0, 0, 2560, 1400, false).On(Screen)!;
        Assert.Equal((1920, 1040), (place.Width, place.Height));
    }

    [Theory]
    [InlineData(2000, 100)] // on a screen that's gone (to the right)
    [InlineData(300, -50)] // its title bar above the screen
    [InlineData(300, 1010)] // its title bar at the foot, out of reach
    [InlineData(1880, 100)] // only 40 px of it showing
    public void ItOpensInTheMiddleWhenItCouldntBeGrabbed(int x, int y) =>
        Assert.Null(new WindowPlace(x, y, 1000, 680, false).On(Screen));
}
