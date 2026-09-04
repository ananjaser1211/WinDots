using WinDots.Core.Lyrics;

namespace WinDots.Core.Tests.Lyrics;

public sealed class LyricsCacheTests : IDisposable
{
    private readonly string _dir;

    public LyricsCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "windots-lyrics-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private static LyricsQuery Query(string title = "Song", string artist = "Artist") =>
        new(title, new[] { artist }, "Album", TimeSpan.FromSeconds(200));

    private static LyricsResult Result() =>
        new("LRCLIB", "https://lrclib.net", new[] { new LyricsLine(TimeSpan.Zero, "hi") }, IsSynced: true);

    [Fact]
    public void RoundTrips_AFoundResult()
    {
        var cache = new LyricsCache(_dir);
        cache.Set(Query(), Result());

        Assert.True(cache.TryGet(Query(), out LyricsResult? got));
        Assert.NotNull(got);
        Assert.Equal("LRCLIB", got!.Provider);
        Assert.True(got.IsSynced);
        Assert.Single(got.Lines);
        Assert.Equal("hi", got.Lines[0].Text);
    }

    [Fact]
    public void CachesNotFound_AsNullWithTrue()
    {
        var cache = new LyricsCache(_dir);
        cache.Set(Query(), null);

        Assert.True(cache.TryGet(Query(), out LyricsResult? got));
        Assert.Null(got);
    }

    [Fact]
    public void Miss_ReturnsFalse()
    {
        var cache = new LyricsCache(_dir);
        Assert.False(cache.TryGet(Query("Unknown"), out LyricsResult? got));
        Assert.Null(got);
    }

    [Fact]
    public void NullDirectory_AlwaysMisses()
    {
        var cache = new LyricsCache(null);
        cache.Set(Query(), Result());
        Assert.False(cache.TryGet(Query(), out _));
    }

    [Fact]
    public void ExpiredEntry_IsNotReturned()
    {
        DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var writer = new LyricsCache(_dir, () => now);
        writer.Set(Query(), Result());

        var reader = new LyricsCache(_dir, () => now + TimeSpan.FromDays(31));
        Assert.False(reader.TryGet(Query(), out _));
    }

    [Fact]
    public void FreshEntry_WithinExpiry_IsReturned()
    {
        DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var writer = new LyricsCache(_dir, () => now);
        writer.Set(Query(), Result());

        var reader = new LyricsCache(_dir, () => now + TimeSpan.FromDays(29));
        Assert.True(reader.TryGet(Query(), out _));
    }

    [Fact]
    public void NormalizeKey_IsCaseAndWhitespaceInsensitive()
    {
        string a = LyricsCache.NormalizeKey(new LyricsQuery("Song", new[] { "Artist" }, "Album", TimeSpan.FromSeconds(200)));
        string b = LyricsCache.NormalizeKey(new LyricsQuery("  song ", new[] { "artist" }, "album", TimeSpan.FromSeconds(200.4)));
        Assert.Equal(a, b);
    }

    [Fact]
    public void CorruptFile_IsTreatedAsMiss()
    {
        Directory.CreateDirectory(_dir);
        var cache = new LyricsCache(_dir);
        cache.Set(Query(), Result());

        foreach (string f in Directory.EnumerateFiles(_dir, "*.json"))
        {
            File.WriteAllText(f, "{not valid json");
        }

        Assert.False(cache.TryGet(Query(), out _));
    }
}
