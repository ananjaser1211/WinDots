using WinDots.Core.Contracts;
using WinDots.Core.Drawer;

namespace WinDots.Core.Tests.Drawer;

public class VelocityTrackerTests
{
    private static PointerSample At(double y, double ms) => new(0, y, TimeSpan.FromMilliseconds(ms));

    [Fact]
    public void NoSamplesIsZero()
    {
        var t = new VelocityTracker();
        Assert.Equal(0, t.VelocityPxPerSecond);
    }

    [Fact]
    public void SingleSampleIsZero()
    {
        var t = new VelocityTracker();
        t.Add(At(10, 0));
        Assert.Equal(0, t.VelocityPxPerSecond);
    }

    [Fact]
    public void WindowedAverageUsesFirstAndLastSampleInWindow()
    {
        var t = new VelocityTracker();
        t.Add(At(0, 0));
        t.Add(At(10, 20));
        t.Add(At(30, 40));
        Assert.Equal(750, t.VelocityPxPerSecond, 6);
    }

    [Fact]
    public void EvictsSamplesOlderThanSixtyMilliseconds()
    {
        var t = new VelocityTracker();
        t.Add(At(0, 0));
        t.Add(At(1000, 10));
        t.Add(At(1000, 100));
        t.Add(At(1000, 150));
        Assert.Equal(2, t.SampleCount);
        Assert.Equal(0, t.VelocityPxPerSecond, 6);
    }

    [Fact]
    public void KeepsSampleExactlyOnWindowBoundary()
    {
        var t = new VelocityTracker();
        t.Add(At(0, 0));
        t.Add(At(60, 60));
        Assert.Equal(2, t.SampleCount);
        Assert.Equal(1000, t.VelocityPxPerSecond, 6);
    }

    [Fact]
    public void AlwaysKeepsNewestSampleEvenWhenStale()
    {
        var t = new VelocityTracker();
        t.Add(At(0, 0));
        t.Add(At(5, 500));
        Assert.Equal(1, t.SampleCount);
        Assert.Equal(0, t.VelocityPxPerSecond);
    }

    [Fact]
    public void DownwardIsPositiveUpwardIsNegative()
    {
        var down = new VelocityTracker();
        down.Add(At(0, 0));
        down.Add(At(30, 30));
        Assert.True(down.VelocityPxPerSecond > 0);

        var up = new VelocityTracker();
        up.Add(At(100, 0));
        up.Add(At(70, 30));
        Assert.Equal(-1000, up.VelocityPxPerSecond, 6);
    }

    [Fact]
    public void HorizontalMotionDoesNotContribute()
    {
        var t = new VelocityTracker();
        t.Add(new PointerSample(0, 0, TimeSpan.Zero));
        t.Add(new PointerSample(400, 0, TimeSpan.FromMilliseconds(30)));
        Assert.Equal(0, t.VelocityPxPerSecond);
    }

    [Fact]
    public void ClearResetsState()
    {
        var t = new VelocityTracker();
        t.Add(At(0, 0));
        t.Add(At(30, 30));
        t.Clear();
        Assert.Equal(0, t.SampleCount);
        Assert.Equal(0, t.VelocityPxPerSecond);
    }

    [Fact]
    public void BackwardsTimestampRestartsWindow()
    {
        var t = new VelocityTracker();
        t.Add(At(0, 100));
        t.Add(At(30, 130));
        t.Add(At(30, 50));
        Assert.Equal(1, t.SampleCount);
        Assert.Equal(0, t.VelocityPxPerSecond);
    }

    [Fact]
    public void IdenticalTimestampsYieldZeroNotInfinity()
    {
        var t = new VelocityTracker();
        t.Add(At(0, 10));
        t.Add(At(50, 10));
        Assert.Equal(0, t.VelocityPxPerSecond);
    }

    [Fact]
    public void RejectsNonPositiveWindow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VelocityTracker(TimeSpan.Zero));
    }
}
