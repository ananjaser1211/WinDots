using WinDots.Core.Media;

namespace WinDots.Core.Tests.Media;

public class SessionQualityTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static MediaSnapshot Snapshot(PlaybackState state, string? title = null) =>
        MediaSnapshot.Empty("id", "app", "App", T0) with { State = state, Title = title };

    [Theory]
    [InlineData(PlaybackState.Paused)]
    [InlineData(PlaybackState.Stopped)]
    [InlineData(PlaybackState.Unknown)]
    public void MetadataLessIdleSessionIsStale(PlaybackState state) =>
        Assert.True(SessionQuality.IsStale(Snapshot(state)));

    [Theory]
    [InlineData(PlaybackState.Playing)]
    [InlineData(PlaybackState.Changing)]
    public void MetadataLessActiveSessionIsNotStale(PlaybackState state) =>
        Assert.False(SessionQuality.IsStale(Snapshot(state)));

    [Fact]
    public void SessionWithMetadataIsNeverStale() =>
        Assert.False(SessionQuality.IsStale(Snapshot(PlaybackState.Paused, title: "Song")));

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidRateBecomesOne(double? reported) =>
        Assert.Equal(1.0, SessionQuality.NormalizeRate(reported));

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void ValidRateIsKept(double reported) =>
        Assert.Equal(reported, SessionQuality.NormalizeRate(reported));

    [Fact]
    public void PastTimestampIsKept()
    {
        var reported = T0 - TimeSpan.FromSeconds(3);
        Assert.Equal(reported, SessionQuality.NormalizeLastUpdated(reported, T0));
    }

    [Fact]
    public void FutureTimestampIsClampedToCapture() =>
        Assert.Equal(T0, SessionQuality.NormalizeLastUpdated(T0 + TimeSpan.FromSeconds(3), T0));

    [Theory]
    [InlineData(0L)]
    [InlineData(504911232000000000L)] // 1601-01-01, the FILETIME zero that unset WinRT DateTimes report
    public void UnsetTimestampBecomesCapture(long ticks) =>
        Assert.Equal(T0, SessionQuality.NormalizeLastUpdated(new DateTimeOffset(ticks, TimeSpan.Zero), T0));

    [Fact]
    public void EpochTimestampBecomesCapture() =>
        Assert.Equal(T0, SessionQuality.NormalizeLastUpdated(DateTimeOffset.UnixEpoch, T0));
}
