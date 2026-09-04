using WinDots.Core.Media;

namespace WinDots.Core.Tests.Media;

public class SourceRegistryTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public SourceRegistryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "windots-sources-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "sources.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private SourceRegistry New(Func<DateTimeOffset>? now = null) => new(_path, now);

    [Fact]
    public void RecordThenSnapshotReturnsEntry()
    {
        SourceRegistry registry = New();
        Assert.True(registry.Record("Spotify.exe", "Spotify", "music"));

        IReadOnlyList<SeenSource> snapshot = registry.Snapshot();
        Assert.Single(snapshot);
        Assert.Equal("Spotify.exe", snapshot[0].SourceAppId);
        Assert.Equal("Spotify", snapshot[0].DisplayName);
        Assert.Equal("music", snapshot[0].LastVerdict);
    }

    [Fact]
    public void RecordSameVerdictReportsNoChange()
    {
        SourceRegistry registry = New();
        Assert.True(registry.Record("app", "App", "music"));
        Assert.False(registry.Record("app", "App", "music"));
        Assert.True(registry.Record("app", "App", "not music: video title"));
    }

    [Fact]
    public void SnapshotIsNewestFirst()
    {
        int seconds = 0;
        SourceRegistry registry = New(() => new DateTimeOffset(2026, 1, 1, 0, 0, seconds, TimeSpan.Zero));

        seconds = 10;
        registry.Record("a", "A", "music");
        seconds = 20;
        registry.Record("b", "B", "music");

        IReadOnlyList<SeenSource> snapshot = registry.Snapshot();
        Assert.Equal("b", snapshot[0].SourceAppId);
        Assert.Equal("a", snapshot[1].SourceAppId);
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        SourceRegistry registry = New();
        registry.Record("app", "App", "music");
        registry.Save();

        SourceRegistry reloaded = New();
        IReadOnlyList<SeenSource> snapshot = reloaded.Snapshot();
        Assert.Single(snapshot);
        Assert.Equal("app", snapshot[0].SourceAppId);
    }

    [Fact]
    public void BoundedToMaxEntries()
    {
        int seconds = 0;
        SourceRegistry registry = New(() => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(seconds));

        for (int i = 0; i < SourceRegistry.MaxEntries + 25; i++)
        {
            seconds = i;
            registry.Record($"app{i}", $"App{i}", "music");
        }

        IReadOnlyList<SeenSource> snapshot = registry.Snapshot();
        Assert.Equal(SourceRegistry.MaxEntries, snapshot.Count);

        // The oldest entries (app0..app24) were evicted; the newest survive.
        Assert.DoesNotContain(snapshot, s => s.SourceAppId == "app0");
        Assert.Contains(snapshot, s => s.SourceAppId == $"app{SourceRegistry.MaxEntries + 24}");
    }

    [Fact]
    public void MalformedFileLoadsEmpty()
    {
        File.WriteAllText(_path, "{ not valid json");
        SourceRegistry registry = New();
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public void MissingFileLoadsEmpty()
    {
        SourceRegistry registry = New();
        Assert.Empty(registry.Snapshot());
    }
}
