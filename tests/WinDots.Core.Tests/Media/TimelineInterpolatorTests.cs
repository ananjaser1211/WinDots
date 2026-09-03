using WinDots.Core.Media;

namespace WinDots.Core.Tests.Media;

public class TimelineInterpolatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static Timeline Track(TimeSpan position, double rate = 1.0) =>
        new(TimeSpan.Zero, TimeSpan.FromMinutes(4), position, T0, rate);

    [Fact]
    public void PlayingAdvancesByElapsedTime()
    {
        var t = Track(TimeSpan.FromSeconds(10));
        var shown = TimelineInterpolator.Displayed(t, PlaybackState.Playing, T0 + TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(15), shown);
    }

    [Fact]
    public void PausedHoldsPosition()
    {
        var t = Track(TimeSpan.FromSeconds(10));
        var shown = TimelineInterpolator.Displayed(t, PlaybackState.Paused, T0 + TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(10), shown);
    }

    [Fact]
    public void RateScalesElapsedTime()
    {
        var t = Track(TimeSpan.FromSeconds(10), rate: 2.0);
        var shown = TimelineInterpolator.Displayed(t, PlaybackState.Playing, T0 + TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(20), shown);
    }

    [Fact]
    public void ClampsToEnd()
    {
        var t = Track(TimeSpan.FromSeconds(230));
        var shown = TimelineInterpolator.Displayed(t, PlaybackState.Playing, T0 + TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.FromMinutes(4), shown);
    }

    [Fact]
    public void ClockGoingBackwardsDoesNotRewind()
    {
        var t = Track(TimeSpan.FromSeconds(10));
        var shown = TimelineInterpolator.Displayed(t, PlaybackState.Playing, T0 - TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(10), shown);
    }

    [Fact]
    public void ProgressIsNullWithoutDuration()
    {
        var live = new Timeline(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(30), T0, 1.0);
        Assert.Null(TimelineInterpolator.Progress(live, PlaybackState.Playing, T0));
    }

    [Fact]
    public void ProgressIsFractionOfDuration()
    {
        var t = Track(TimeSpan.FromMinutes(1));
        Assert.Equal(0.25, TimelineInterpolator.Progress(t, PlaybackState.Paused, T0)!.Value, precision: 6);
    }
}
