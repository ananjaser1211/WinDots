using WinDots.Core.Lyrics;

namespace WinDots.Core.Tests.Lyrics;

public sealed class LyricsSyncTests
{
    private static readonly IReadOnlyList<LyricsLine> Lines = new[]
    {
        new LyricsLine(TimeSpan.FromSeconds(0), "a"),
        new LyricsLine(TimeSpan.FromSeconds(10), "b"),
        new LyricsLine(TimeSpan.FromSeconds(20), "c"),
    };

    [Fact]
    public void BeforeFirstTimestamp_ReturnsMinusOne()
    {
        // The first line is at 0; a negative effective time is before it.
        Assert.Equal(-1, LyricsSync.CurrentIndex(Lines, TimeSpan.FromSeconds(-1), TimeSpan.Zero));
    }

    [Fact]
    public void AtExactTimestamp_SelectsThatLine()
    {
        Assert.Equal(1, LyricsSync.CurrentIndex(Lines, TimeSpan.FromSeconds(10), TimeSpan.Zero));
    }

    [Fact]
    public void BetweenTimestamps_SelectsPreviousLine()
    {
        Assert.Equal(1, LyricsSync.CurrentIndex(Lines, TimeSpan.FromSeconds(15), TimeSpan.Zero));
    }

    [Fact]
    public void PastLastTimestamp_SelectsLastLine()
    {
        Assert.Equal(2, LyricsSync.CurrentIndex(Lines, TimeSpan.FromSeconds(999), TimeSpan.Zero));
    }

    [Fact]
    public void PositiveOffset_AdvancesLinesEarlier()
    {
        // At 8 s we would normally be on line 0; a +3 s offset pushes effective time to 11 s -> line 1.
        Assert.Equal(1, LyricsSync.CurrentIndex(Lines, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void NegativeOffset_DelaysLines()
    {
        // At 11 s we would be on line 1; a -3 s offset pulls effective time to 8 s -> line 0.
        Assert.Equal(0, LyricsSync.CurrentIndex(Lines, TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(-3)));
    }

    [Fact]
    public void EmptyLines_ReturnsMinusOne()
    {
        Assert.Equal(-1, LyricsSync.CurrentIndex(Array.Empty<LyricsLine>(), TimeSpan.FromSeconds(5), TimeSpan.Zero));
    }

    [Fact]
    public void UnsyncedLines_AreIgnored()
    {
        var plain = new[] { new LyricsLine(null, "x"), new LyricsLine(null, "y") };
        Assert.Equal(-1, LyricsSync.CurrentIndex(plain, TimeSpan.FromSeconds(30), TimeSpan.Zero));
    }
}
