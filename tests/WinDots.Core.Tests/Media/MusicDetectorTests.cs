using WinDots.Core.Media;

namespace WinDots.Core.Tests.Media;

public class MusicDetectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static MediaSnapshot Snapshot(
        MediaKind kind = MediaKind.Unknown,
        string? title = null,
        string[]? artists = null,
        string? album = null,
        TimeSpan? duration = null)
    {
        Timeline timeline = duration is { } d
            ? new Timeline(TimeSpan.Zero, d, TimeSpan.Zero, Now, 1.0)
            : Timeline.Empty;

        return MediaSnapshot.Empty("id#0", "app", "App", Now) with
        {
            Kind = kind,
            Title = title,
            Artists = artists ?? Array.Empty<string>(),
            Album = album,
            Timeline = timeline,
        };
    }

    // ---- Individual weights --------------------------------------------------

    [Fact]
    public void MusicKindAddsThree()
    {
        Assert.Equal(3, MusicDetector.Score(Snapshot(kind: MediaKind.Music)).Score);
    }

    [Fact]
    public void ArtistAddsTwo()
    {
        Assert.Equal(2, MusicDetector.Score(Snapshot(artists: new[] { "Artist" })).Score);
    }

    [Fact]
    public void AlbumAddsTwo()
    {
        Assert.Equal(2, MusicDetector.Score(Snapshot(album: "Album")).Score);
    }

    [Fact]
    public void TrackLengthWindowAddsOne()
    {
        Assert.Equal(1, MusicDetector.Score(Snapshot(duration: TimeSpan.FromMinutes(3))).Score);
    }

    [Fact]
    public void ThirtySecondsIsInsideTheWindow()
    {
        Assert.Equal(1, MusicDetector.Score(Snapshot(duration: TimeSpan.FromSeconds(30))).Score);
    }

    [Fact]
    public void TwentyMinutesIsInsideTheWindow()
    {
        Assert.Equal(1, MusicDetector.Score(Snapshot(duration: TimeSpan.FromMinutes(20))).Score);
    }

    [Fact]
    public void ShortClipBelowThirtySecondsScoresZeroForLength()
    {
        Assert.Equal(0, MusicDetector.Score(Snapshot(duration: TimeSpan.FromSeconds(10))).Score);
    }

    [Fact]
    public void DurationBetweenTwentyAndFortyFiveMinutesIsNeutral()
    {
        Assert.Equal(0, MusicDetector.Score(Snapshot(duration: TimeSpan.FromMinutes(30))).Score);
    }

    [Fact]
    public void VeryLongDurationSubtractsThree()
    {
        Assert.Equal(-3, MusicDetector.Score(Snapshot(duration: TimeSpan.FromMinutes(60))).Score);
    }

    [Theory]
    [InlineData("Some Video | Channel")]
    [InlineData("Show Episode 4")]
    [InlineData("Official Trailer")]
    [InlineData("24/7 Live stream")]
    [InlineData("Series S01E02")]
    public void VideoTitlePatternsSubtractTwo(string title)
    {
        Assert.Equal(-2, MusicDetector.Score(Snapshot(title: title)).Score);
    }

    [Fact]
    public void PlainTitleHasNoVideoPenalty()
    {
        Assert.Equal(0, MusicDetector.Score(Snapshot(title: "Just A Song")).Score);
    }

    // ---- Source rules --------------------------------------------------------

    [Fact]
    public void AlwaysRuleAddsTen()
    {
        Assert.Equal(10, MusicDetector.Score(Snapshot(), SourceRuleMode.Always).Score);
    }

    [Fact]
    public void NeverRuleIsExcludedAndNotMusic()
    {
        MusicVerdict verdict = MusicDetector.Score(Snapshot(kind: MediaKind.Music), SourceRuleMode.Never);
        Assert.False(verdict.IsMusic);
        Assert.Equal(int.MinValue, verdict.Score);
        Assert.Contains("Never", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoRuleBehavesLikeNoRule()
    {
        int auto = MusicDetector.Score(Snapshot(kind: MediaKind.Music), SourceRuleMode.Auto).Score;
        int none = MusicDetector.Score(Snapshot(kind: MediaKind.Music)).Score;
        Assert.Equal(none, auto);
    }

    // ---- Threshold -----------------------------------------------------------

    [Fact]
    public void ScoreOfThreeIsMusic()
    {
        // Music kind alone = 3.
        Assert.True(MusicDetector.Score(Snapshot(kind: MediaKind.Music)).IsMusic);
    }

    [Fact]
    public void ScoreOfTwoIsNotMusic()
    {
        Assert.False(MusicDetector.Score(Snapshot(artists: new[] { "Artist" })).IsMusic);
    }

    // ---- Realistic snapshots -------------------------------------------------

    [Fact]
    public void BrowserVideoIsNotMusic()
    {
        // A browser video: video kind (no bonus), a piped title (-2), a long duration, no artist/album.
        MediaSnapshot video = Snapshot(
            kind: MediaKind.Video,
            title: "How to build a drawer | DIY Channel",
            duration: TimeSpan.FromMinutes(12));
        MusicVerdict verdict = MusicDetector.Score(video, SourceRuleMode.Auto);
        Assert.False(verdict.IsMusic);
        Assert.Contains("not music", verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void YouTubeMusicLikeSnapshotIsMusic()
    {
        // A YouTube Music tab: artist (+2), album (+2), a track length (+1) even without a music kind = 5.
        MediaSnapshot track = Snapshot(
            title: "Song Title",
            artists: new[] { "The Band" },
            album: "The Album",
            duration: TimeSpan.FromMinutes(4));
        MusicVerdict verdict = MusicDetector.Score(track, SourceRuleMode.Auto);
        Assert.True(verdict.IsMusic);
        Assert.Equal(5, verdict.Score);
    }
}
