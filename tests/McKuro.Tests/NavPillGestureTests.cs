using McKuro.Services;

namespace McKuro.Tests;

public sealed class NavPillGestureTests
{
    [Fact]
    public void RubberBand_TracksExactlyWithinBounds()
    {
        Assert.True(NavPillGesture.RubberBand(-1, 0, 300) < 0);
        Assert.Equal(120, NavPillGesture.RubberBand(120, 0, 300));
        Assert.Equal(300, NavPillGesture.RubberBand(300, 0, 300));
    }

    [Theory]
    [InlineData(-100, 0, 300)]
    [InlineData(400, 0, 300)]
    public void RubberBand_ResistsOutsideBounds(double value, double minimum, double maximum)
    {
        var actual = NavPillGesture.RubberBand(value, minimum, maximum);
        Assert.True(actual < minimum || actual > maximum);
        Assert.InRange(actual, minimum - 36, maximum + 36);
        Assert.True(Math.Abs(actual - Math.Clamp(value, minimum, maximum)) < Math.Abs(value - Math.Clamp(value, minimum, maximum)));
    }

    [Fact]
    public void FindSnapIndex_SlowDragUsesNearestTarget()
    {
        double[] targets = [0, 50, 100, 150];
        Assert.Equal(1, NavPillGesture.FindSnapIndex(72, targets));
        Assert.Equal(2, NavPillGesture.FindSnapIndex(76, targets));
    }

    [Fact]
    public void FindSnapIndex_FlickProjectsShortDistanceIntoNextTarget()
    {
        double[] targets = [0, 50, 100];
        Assert.Equal(1, NavPillGesture.FindSnapIndex(18, targets, velocity: 450));
        Assert.Equal(0, NavPillGesture.FindSnapIndex(18, targets, velocity: -450));
    }

    [Fact]
    public void FindSnapIndex_ClampsAtEitherEndAndHandlesEmptyList()
    {
        double[] targets = [0, 50, 100];
        Assert.Equal(0, NavPillGesture.FindSnapIndex(-500, targets, velocity: -5000));
        Assert.Equal(2, NavPillGesture.FindSnapIndex(500, targets, velocity: 5000));
        Assert.Equal(-1, NavPillGesture.FindSnapIndex(10, []));
    }
}
