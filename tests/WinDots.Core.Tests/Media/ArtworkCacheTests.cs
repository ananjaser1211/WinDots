using System.Security.Cryptography;
using System.Text;
using WinDots.Core.Contracts;
using WinDots.Core.Media;

namespace WinDots.Core.Tests.Media;

public sealed class ArtworkCacheTests : IDisposable
{
    private readonly string _dir;

    public ArtworkCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "windots-artcache-" + Guid.NewGuid().ToString("N"));
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

    private static Func<CancellationToken, Task<ArtworkResult>> Loader(byte[] bytes, string? ct = "image/png") =>
        _ => Task.FromResult(ArtworkResult.Loaded(bytes, ct));

    private static byte[] Bytes(int len, byte fill = 0x42)
    {
        var b = new byte[len];
        Array.Fill(b, fill);
        return b;
    }

    private static string Hash(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    [Fact]
    public async Task MissThenHitFromMemory()
    {
        var cache = new ArtworkCache();
        int calls = 0;
        var art = await cache.GetOrAddAsync("k", _ => { calls++; return Task.FromResult(ArtworkResult.Loaded(Bytes(10), "image/png")); }, default);

        Assert.NotNull(art);
        Assert.Equal("k", art!.Key);
        Assert.Equal(10, art.Bytes.Length);
        Assert.Equal("image/png", art.ContentType);

        var again = await cache.GetOrAddAsync("k", _ => { calls++; return Task.FromResult(ArtworkResult.None); }, default);
        Assert.NotNull(again);
        Assert.Equal(1, calls);

        var stats = cache.GetStats();
        Assert.Equal(1, stats.Entries);
        Assert.Equal(1, stats.Hits);
        Assert.Equal(1, stats.Misses);
    }

    [Fact]
    public async Task LruEvictsByBytes()
    {
        // Budget 1000; max entry = 250. Three 200-byte entries fit (600); adding more evicts oldest.
        var cache = new ArtworkCache(directory: null, byteBudget: 1000);

        for (int i = 0; i < 6; i++)
        {
            await cache.GetOrAddAsync("k" + i, Loader(Bytes(200, (byte)i)), default);
        }

        var stats = cache.GetStats();
        Assert.True(stats.Bytes <= 1000);
        Assert.True(stats.Entries <= 5);

        // Oldest (k0) should have been evicted; reloading counts as a miss.
        long missesBefore = cache.GetStats().Misses;
        await cache.GetOrAddAsync("k0", Loader(Bytes(200)), default);
        Assert.Equal(missesBefore + 1, cache.GetStats().Misses);
    }

    [Fact]
    public async Task OversizeReturnedButNotRetained()
    {
        var cache = new ArtworkCache(directory: null, byteBudget: 1000); // max entry 250
        var art = await cache.GetOrAddAsync("big", Loader(Bytes(400)), default);
        Assert.NotNull(art);
        Assert.Equal(400, art!.Bytes.Length);

        var stats = cache.GetStats();
        Assert.Equal(0, stats.Entries);
        Assert.Equal(0, stats.Bytes);
    }

    [Fact]
    public async Task SingleFlightLoaderCalledOnce()
    {
        var cache = new ArtworkCache();
        var tcs = new TaskCompletionSource<ArtworkResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;

        Func<CancellationToken, Task<ArtworkResult>> loader = _ =>
        {
            Interlocked.Increment(ref calls);
            return tcs.Task;
        };

        var tasks = new Task<CachedArtwork?>[10];
        for (int i = 0; i < 10; i++)
        {
            tasks[i] = cache.GetOrAddAsync("k", loader, default);
        }

        // Give callers a moment to coalesce onto the single in-flight load.
        await Task.Delay(50);
        tcs.SetResult(ArtworkResult.Loaded(Bytes(10), "image/png"));

        var results = await Task.WhenAll(tasks);
        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.NotNull(r));
    }

    [Fact]
    public async Task CancelOneWaiterDoesNotFailOthers()
    {
        var cache = new ArtworkCache();
        var tcs = new TaskCompletionSource<ArtworkResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;

        Func<CancellationToken, Task<ArtworkResult>> loader = _ =>
        {
            Interlocked.Increment(ref calls);
            return tcs.Task;
        };

        using var cts = new CancellationTokenSource();
        var cancellable = cache.GetOrAddAsync("k", loader, cts.Token);
        var survivor = cache.GetOrAddAsync("k", loader, default);

        await Task.Delay(50);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancellable);

        tcs.SetResult(ArtworkResult.Loaded(Bytes(10), "image/png"));
        var art = await survivor;
        Assert.NotNull(art);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FailedResultNotCached()
    {
        var cache = new ArtworkCache();
        int calls = 0;

        var first = await cache.GetOrAddAsync("k", _ => { calls++; return Task.FromResult(ArtworkResult.Failed("nope")); }, default);
        Assert.Null(first);

        var second = await cache.GetOrAddAsync("k", _ => { calls++; return Task.FromResult(ArtworkResult.Loaded(Bytes(10), null)); }, default);
        Assert.NotNull(second);
        Assert.Equal(2, calls); // retried because failure was not cached
        Assert.Equal(0, cache.GetStats().Hits);
    }

    [Fact]
    public async Task ExceptionPropagatesAndNotCached()
    {
        var cache = new ArtworkCache();
        int calls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrAddAsync("k", _ => { calls++; throw new InvalidOperationException("boom"); }, default));

        var ok = await cache.GetOrAddAsync("k", _ => { calls++; return Task.FromResult(ArtworkResult.Loaded(Bytes(10), null)); }, default);
        Assert.NotNull(ok);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task DiskRoundTrip()
    {
        var first = new ArtworkCache(directory: _dir);
        await first.GetOrAddAsync("song", Loader(Bytes(64), "image/jpeg"), default);

        Assert.True(File.Exists(Path.Combine(_dir, Hash("song") + ".bin")));
        Assert.True(File.Exists(Path.Combine(_dir, Hash("song") + ".meta")));

        var second = new ArtworkCache(directory: _dir);
        int calls = 0;
        var art = await second.GetOrAddAsync("song", _ => { calls++; return Task.FromResult(ArtworkResult.None); }, default);

        Assert.NotNull(art);
        Assert.Equal(64, art!.Bytes.Length);
        Assert.Equal("image/jpeg", art.ContentType);
        Assert.Equal(0, calls); // served from disk, loader never invoked
        Assert.Equal(1, second.GetStats().Hits);
    }

    [Fact]
    public async Task ExpiredFileIgnoredAndDeleted()
    {
        DateTime now = DateTime.UtcNow;
        var writer = new ArtworkCache(directory: _dir, utcNow: () => now.AddDays(-40));
        await writer.GetOrAddAsync("old", Loader(Bytes(32), "image/png"), default);

        string bin = Path.Combine(_dir, Hash("old") + ".bin");
        string meta = Path.Combine(_dir, Hash("old") + ".meta");
        Assert.True(File.Exists(bin));

        // A fresh cache at present time should treat the entry as expired.
        var reader = new ArtworkCache(directory: _dir, utcNow: () => now);

        // Lazy purge on construction should have removed the expired files.
        Assert.False(File.Exists(bin));
        Assert.False(File.Exists(meta));

        int calls = 0;
        var art = await reader.GetOrAddAsync("old", _ => { calls++; return Task.FromResult(ArtworkResult.Loaded(Bytes(8), null)); }, default);
        Assert.NotNull(art);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CorruptSidecarIgnored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, Hash("k") + ".bin"), Bytes(16));
        File.WriteAllText(Path.Combine(_dir, Hash("k") + ".meta"), "garbage-not-valid");

        var cache = new ArtworkCache(directory: _dir);
        int calls = 0;
        var art = await cache.GetOrAddAsync("k", _ => { calls++; return Task.FromResult(ArtworkResult.Loaded(Bytes(8), null)); }, default);

        Assert.NotNull(art);
        Assert.Equal(8, art!.Bytes.Length);
        Assert.Equal(1, calls); // corrupt sidecar not usable, loader ran
    }

    private long DiskBinBytes()
    {
        if (!Directory.Exists(_dir))
        {
            return 0;
        }

        long total = 0;
        foreach (string bin in Directory.EnumerateFiles(_dir, "*.bin"))
        {
            total += new FileInfo(bin).Length;
        }

        return total;
    }

    [Fact]
    public async Task DiskEvictsLeastRecentlyUsedToBudget()
    {
        // Budget 1000 applies to disk too. Ten 200-byte entries cannot all persist.
        var cache = new ArtworkCache(directory: _dir, byteBudget: 1000);

        for (int i = 0; i < 10; i++)
        {
            await cache.GetOrAddAsync("k" + i, Loader(Bytes(200, (byte)i)), default);
        }

        Assert.True(DiskBinBytes() <= 1000, $"disk held {DiskBinBytes()} bytes, budget 1000");

        // The oldest key (k0) must have been evicted from disk; a fresh cache re-loads it.
        var fresh = new ArtworkCache(directory: _dir, byteBudget: 1000);
        int calls = 0;
        var art = await fresh.GetOrAddAsync("k0", _ => { calls++; return Task.FromResult(ArtworkResult.Loaded(Bytes(200), "image/png")); }, default);
        Assert.NotNull(art);
        Assert.Equal(1, calls); // not on disk -> loader ran
    }

    [Fact]
    public async Task OversizeForMemoryStillBoundedOnDisk()
    {
        // Entries of 400 bytes exceed the 250-byte in-memory retention cap but fit the disk budget.
        // Without a disk LRU these would accumulate unbounded; here disk stays within 1000 bytes.
        var cache = new ArtworkCache(directory: _dir, byteBudget: 1000);

        for (int i = 0; i < 8; i++)
        {
            await cache.GetOrAddAsync("big" + i, Loader(Bytes(400, (byte)i)), default);
        }

        Assert.Equal(0, cache.GetStats().Entries); // none retained in memory (each oversize)
        Assert.True(DiskBinBytes() <= 1000, $"disk held {DiskBinBytes()} bytes, budget 1000");
    }

    [Fact]
    public async Task DiskReadRefreshesRecency()
    {
        // Write four 200-byte entries (800 bytes on disk, under the 1000 budget).
        var writer = new ArtworkCache(directory: _dir, byteBudget: 1000);
        for (int i = 0; i < 4; i++)
        {
            await writer.GetOrAddAsync("k" + i, Loader(Bytes(200, (byte)i)), default);
        }

        // A fresh cache indexes existing files by write order (k0 oldest). Reading k0 from disk
        // must promote it to most-recently-used so a later eviction spares it.
        var reader = new ArtworkCache(directory: _dir, byteBudget: 1000);
        var hit = await reader.GetOrAddAsync("k0", _ => { Assert.Fail("should be a disk hit"); return Task.FromResult(ArtworkResult.None); }, default);
        Assert.NotNull(hit);

        // Add two more entries, forcing disk eviction. k0 (freshly touched) survives; k1, now the
        // least-recently-used, is evicted.
        await reader.GetOrAddAsync("k4", Loader(Bytes(200, 4)), default);
        await reader.GetOrAddAsync("k5", Loader(Bytes(200, 5)), default);

        Assert.True(File.Exists(Path.Combine(_dir, Hash("k0") + ".bin")), "k0 was touched and should survive");
        Assert.False(File.Exists(Path.Combine(_dir, Hash("k1") + ".bin")), "k1 was least-recently-used and should be evicted");
    }

    [Fact]
    public async Task ClearAsyncEmptiesMemoryAndDisk()
    {
        var cache = new ArtworkCache(directory: _dir);
        await cache.GetOrAddAsync("a", Loader(Bytes(10)), default);
        await cache.GetOrAddAsync("b", Loader(Bytes(10)), default);

        Assert.Equal(2, cache.GetStats().Entries);
        Assert.NotEmpty(Directory.EnumerateFiles(_dir));

        await cache.ClearAsync();

        Assert.Equal(0, cache.GetStats().Entries);
        Assert.Equal(0, cache.GetStats().Bytes);
        Assert.Empty(Directory.EnumerateFiles(_dir));
    }
}
