using WinDots.Core.Lyrics;

namespace WinDots.Core.Tests.Lyrics;

public sealed class LrcParserTests
{
    [Fact]
    public void Parses_MmSsXx_Timestamps()
    {
        LrcParseResult result = LrcParser.Parse("[00:12.34]Hello\n[01:05.50]World");

        Assert.True(result.IsSynced);
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(new TimeSpan(0, 0, 0, 12, 340), result.Lines[0].At);
        Assert.Equal("Hello", result.Lines[0].Text);
        Assert.Equal(new TimeSpan(0, 0, 1, 5, 500), result.Lines[1].At);
    }

    [Fact]
    public void Parses_MmSs_WithoutFraction()
    {
        LrcParseResult result = LrcParser.Parse("[02:00]Chorus");

        Assert.True(result.IsSynced);
        Assert.Equal(TimeSpan.FromMinutes(2), result.Lines[0].At);
    }

    [Fact]
    public void Parses_ThreeDigitFraction()
    {
        LrcParseResult result = LrcParser.Parse("[00:01.250]Quarter");

        Assert.Equal(new TimeSpan(0, 0, 0, 1, 250), result.Lines[0].At);
    }

    [Fact]
    public void MultipleTimestamps_OnOneLine_ExpandToOnePerTimestamp()
    {
        LrcParseResult result = LrcParser.Parse("[00:10.00][00:40.00][01:10.00]Repeat");

        Assert.True(result.IsSynced);
        Assert.Equal(3, result.Lines.Count);
        Assert.All(result.Lines, l => Assert.Equal("Repeat", l.Text));
        Assert.Equal(TimeSpan.FromSeconds(10), result.Lines[0].At);
        Assert.Equal(TimeSpan.FromSeconds(40), result.Lines[1].At);
        Assert.Equal(TimeSpan.FromSeconds(70), result.Lines[2].At);
    }

    [Fact]
    public void Lines_AreSortedByTimestamp()
    {
        LrcParseResult result = LrcParser.Parse("[00:30.00]Second\n[00:05.00]First");

        Assert.Equal("First", result.Lines[0].Text);
        Assert.Equal("Second", result.Lines[1].Text);
    }

    [Fact]
    public void MetadataTags_AreIgnored()
    {
        LrcParseResult result = LrcParser.Parse("[ar:Artist]\n[ti:Title]\n[00:00.00]Line one");

        Assert.True(result.IsSynced);
        Assert.Single(result.Lines);
        Assert.Equal("Line one", result.Lines[0].Text);
    }

    [Fact]
    public void NoTimestamps_FallsBackToPlainUnsyncedLines()
    {
        LrcParseResult result = LrcParser.Parse("First line\n\nSecond line");

        Assert.False(result.IsSynced);
        Assert.Equal(2, result.Lines.Count);
        Assert.Null(result.Lines[0].At);
        Assert.Equal("First line", result.Lines[0].Text);
        Assert.Equal("Second line", result.Lines[1].Text);
    }

    [Fact]
    public void EmptyOrWhitespace_YieldsNoLines()
    {
        Assert.Empty(LrcParser.Parse(null).Lines);
        Assert.Empty(LrcParser.Parse("   ").Lines);
    }

    [Fact]
    public void EmptyLyricText_AfterTimestamp_IsKeptAsBlank()
    {
        LrcParseResult result = LrcParser.Parse("[00:20.00]");

        Assert.True(result.IsSynced);
        Assert.Single(result.Lines);
        Assert.Equal(string.Empty, result.Lines[0].Text);
    }
}
