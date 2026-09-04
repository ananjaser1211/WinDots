using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WinDots.Core.Contracts;

namespace WinDots.Core.Media;

/// <summary>
/// In-memory LRU artwork cache with an optional persistent directory backing store.
/// Both tiers are bounded by the same byte budget: the in-memory dictionary and the
/// on-disk directory each enforce an independent LRU eviction to that budget, and disk
/// files additionally expire after 30 days.
/// Keyed by an opaque artwork key. Thread-safe. BCL only. See _docs/05-architecture.md.
/// </summary>
public sealed class ArtworkCache : IArtworkCache, IDisposable
{
    /// <summary>Cache counters. Bytes is the retained in-memory total.</summary>
    public sealed record Stats(int Entries, long Bytes, long Hits, long Misses);

    private const long DefaultByteBudget = 32L * 1024 * 1024;
    private static readonly TimeSpan Expiry = TimeSpan.FromDays(30);

    private readonly long _byteBudget;
    private readonly long _maxEntryBytes;
    private readonly string? _directory;
    private readonly Func<DateTime> _utcNow;

    private readonly object _gate = new();

    // LRU: most-recently-used at the end of the linked list.
    private readonly LinkedList<Entry> _lru = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _map = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InFlight> _inFlight = new(StringComparer.Ordinal);

    private long _bytes;
    private long _hits;
    private long _misses;

    // Disk LRU index. Keyed by file hash; deterministic recency via a monotonic sequence.
    private readonly object _diskGate = new();
    private readonly Dictionary<string, DiskItem> _diskIndex = new(StringComparer.Ordinal);
    private long _diskBytes;
    private long _diskSeq;

    private sealed record Entry(string Key, CachedArtwork Artwork, long Size);

    private sealed class DiskItem
    {
        public long Size { get; set; }
        public long Seq { get; set; }
    }

    private sealed class InFlight
    {
        public Task<CachedArtwork?> Task { get; set; } = null!;
        public required CancellationTokenSource LinkedCts { get; init; }
        public int Waiters;
    }

    /// <param name="directory">Optional persistent backing directory. Null disables disk persistence.</param>
    /// <param name="byteBudget">In-memory retention budget in bytes.</param>
    /// <param name="utcNow">Clock override for tests. Defaults to <see cref="DateTime.UtcNow"/>.</param>
    public ArtworkCache(string? directory = null, long byteBudget = DefaultByteBudget, Func<DateTime>? utcNow = null)
    {
        if (byteBudget <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteBudget), "Byte budget must be positive.");
        }

        _byteBudget = byteBudget;
        _maxEntryBytes = byteBudget / 4;
        _directory = string.IsNullOrWhiteSpace(directory) ? null : directory;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);

        if (_directory is not null)
        {
            TryEnsureDirectory();
            PurgeExpiredFiles();
            IndexDiskFiles();
            EvictDiskToBudget();
        }
    }

    public Stats GetStats()
    {
        lock (_gate)
        {
            return new Stats(_map.Count, _bytes, _hits, _misses);
        }
    }

    public async Task<CachedArtwork?> GetOrAddAsync(
        string key,
        Func<CancellationToken, Task<ArtworkResult>> loader,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(loader);
        ct.ThrowIfCancellationRequested();

        // 1. Memory hit.
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                Touch(node);
                _hits++;
                return node.Value.Artwork;
            }
        }

        // 2. Disk hit (outside the lock; I/O may be slow).
        if (_directory is not null)
        {
            var fromDisk = TryReadFromDisk(key);
            if (fromDisk is not null)
            {
                lock (_gate)
                {
                    _hits++;
                    Insert(key, fromDisk);
                    return fromDisk;
                }
            }
        }

        // 3. Single-flight load.
        InFlight flight;
        lock (_gate)
        {
            // Re-check: another caller may have populated memory while we hit disk.
            if (_map.TryGetValue(key, out var node))
            {
                Touch(node);
                _hits++;
                return node.Value.Artwork;
            }

            if (!_inFlight.TryGetValue(key, out var existing))
            {
                _misses++;
                var cts = new CancellationTokenSource();
                existing = new InFlight { LinkedCts = cts };
                _inFlight[key] = existing;
                // Assign the task after registering so the loader's synchronous-completion
                // path (finally removes the key) cannot run before the key is present.
                existing.Task = RunLoaderAsync(key, loader, cts.Token, existing);
            }

            flight = existing;
            flight.Waiters++;
        }

        try
        {
            // Await the shared task, but honor this caller's own cancellation without
            // cancelling the shared load for others.
            return await flight.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // This waiter cancelled. If every waiter has cancelled, cancel the shared load.
            lock (_gate)
            {
                flight.Waiters--;
                if (flight.Waiters == 0)
                {
                    try
                    {
                        flight.LinkedCts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
            }

            throw;
        }
    }

    private async Task<CachedArtwork?> RunLoaderAsync(
        string key,
        Func<CancellationToken, Task<ArtworkResult>> loader,
        CancellationToken sharedToken,
        InFlight flight)
    {
        try
        {
            ArtworkResult result = await loader(sharedToken).ConfigureAwait(false);

            if (!result.Success)
            {
                // Not cached; retry next time.
                return null;
            }

            // Copy the bytes so the cached entry owns its buffer.
            byte[] bytes = result.Bytes.ToArray();
            var artwork = new CachedArtwork(key, bytes, result.ContentType);

            lock (_gate)
            {
                Insert(key, artwork);
            }

            if (_directory is not null)
            {
                TryWriteToDisk(key, bytes, result.ContentType);
            }

            return artwork;
        }
        finally
        {
            lock (_gate)
            {
                // Only remove if still the current in-flight for this key.
                if (_inFlight.TryGetValue(key, out var current) && ReferenceEquals(current, flight))
                {
                    _inFlight.Remove(key);
                }
            }

            flight.LinkedCts.Dispose();
        }
    }

    public Task ClearAsync()
    {
        lock (_gate)
        {
            _map.Clear();
            _lru.Clear();
            _bytes = 0;
        }

        if (_directory is not null)
        {
            lock (_diskGate)
            {
                _diskIndex.Clear();
                _diskBytes = 0;
            }

            try
            {
                if (Directory.Exists(_directory))
                {
                    foreach (var file in Directory.EnumerateFiles(_directory))
                    {
                        TryDelete(file);
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var flight in _inFlight.Values)
            {
                try
                {
                    flight.LinkedCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }

    // --- LRU internals (must be called under _gate) ---

    private void Touch(LinkedListNode<Entry> node)
    {
        _lru.Remove(node);
        _lru.AddLast(node);
    }

    private void Insert(string key, CachedArtwork artwork)
    {
        long size = artwork.Bytes.Length;

        // Oversize entries are returned but not retained.
        if (size > _maxEntryBytes)
        {
            return;
        }

        if (_map.TryGetValue(key, out var existing))
        {
            _bytes -= existing.Value.Size;
            _lru.Remove(existing);
            _map.Remove(key);
        }

        var node = new LinkedListNode<Entry>(new Entry(key, artwork, size));
        _lru.AddLast(node);
        _map[key] = node;
        _bytes += size;

        EvictToBudget();
    }

    private void EvictToBudget()
    {
        while (_bytes > _byteBudget && _lru.First is { } oldest)
        {
            _lru.RemoveFirst();
            _map.Remove(oldest.Value.Key);
            _bytes -= oldest.Value.Size;
        }
    }

    // --- Disk internals ---

    private void TryEnsureDirectory()
    {
        try
        {
            Directory.CreateDirectory(_directory!);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Hash(string key)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexStringLower(digest);
    }

    private string BinPath(string key) => Path.Combine(_directory!, Hash(key) + ".bin");

    private string MetaPath(string key) => Path.Combine(_directory!, Hash(key) + ".meta");

    private CachedArtwork? TryReadFromDisk(string key)
    {
        try
        {
            string hash = Hash(key);
            string binPath = BinPath(key);
            string metaPath = MetaPath(key);
            if (!File.Exists(binPath) || !File.Exists(metaPath))
            {
                return null;
            }

            (string? contentType, DateTime storedAt)? meta = TryParseMeta(metaPath);
            if (meta is null)
            {
                // Corrupt sidecar: ignore.
                return null;
            }

            if (_utcNow() - meta.Value.storedAt >= Expiry)
            {
                TryDelete(binPath);
                TryDelete(metaPath);
                RemoveDiskIndex(hash);
                return null;
            }

            byte[] bytes = File.ReadAllBytes(binPath);
            string? contentType = string.IsNullOrEmpty(meta.Value.contentType) ? null : meta.Value.contentType;
            TouchDisk(hash, bytes.Length);
            return new CachedArtwork(key, bytes, contentType);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (string? contentType, DateTime storedAt)? TryParseMeta(string metaPath)
    {
        try
        {
            string[] lines = File.ReadAllLines(metaPath);
            if (lines.Length < 2)
            {
                return null;
            }

            string contentType = lines[0];
            if (!DateTime.TryParse(
                    lines[1],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTime storedAt))
            {
                return null;
            }

            return (contentType, DateTime.SpecifyKind(storedAt, DateTimeKind.Utc));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void TryWriteToDisk(string key, byte[] bytes, string? contentType)
    {
        try
        {
            string hash = Hash(key);
            string binPath = BinPath(key);
            string metaPath = MetaPath(key);
            string storedAt = _utcNow().ToString("O", CultureInfo.InvariantCulture);
            string metaContent = (contentType ?? string.Empty) + "\n" + storedAt + "\n";

            WriteAtomic(binPath, bytes);
            WriteAtomic(metaPath, Encoding.UTF8.GetBytes(metaContent));

            TouchDisk(hash, bytes.Length);
            EvictDiskToBudget();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // --- Disk LRU index (guarded by _diskGate) ---

    /// <summary>Scans the backing directory once at startup to seed the disk LRU index.</summary>
    private void IndexDiskFiles()
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            // Order existing files by last-write time so startup preserves prior recency.
            var found = new List<(string hash, long size, DateTime written)>();
            foreach (string binPath in Directory.EnumerateFiles(_directory!, "*.bin"))
            {
                try
                {
                    var info = new FileInfo(binPath);
                    string hash = Path.GetFileNameWithoutExtension(binPath);
                    found.Add((hash, info.Length, info.LastWriteTimeUtc));
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            found.Sort((a, b) => a.written.CompareTo(b.written));

            lock (_diskGate)
            {
                foreach (var (hash, size, _) in found)
                {
                    if (_diskIndex.TryGetValue(hash, out var existing))
                    {
                        _diskBytes -= existing.Size;
                        existing.Size = size;
                        existing.Seq = ++_diskSeq;
                    }
                    else
                    {
                        _diskIndex[hash] = new DiskItem { Size = size, Seq = ++_diskSeq };
                    }

                    _diskBytes += size;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Records or refreshes a disk entry as most-recently-used.</summary>
    private void TouchDisk(string hash, long size)
    {
        lock (_diskGate)
        {
            if (_diskIndex.TryGetValue(hash, out var item))
            {
                _diskBytes -= item.Size;
                item.Size = size;
                item.Seq = ++_diskSeq;
            }
            else
            {
                _diskIndex[hash] = new DiskItem { Size = size, Seq = ++_diskSeq };
            }

            _diskBytes += size;
        }
    }

    private void RemoveDiskIndex(string hash)
    {
        lock (_diskGate)
        {
            if (_diskIndex.Remove(hash, out var item))
            {
                _diskBytes -= item.Size;
            }
        }
    }

    /// <summary>Evicts least-recently-used files until on-disk bytes fit the budget.</summary>
    private void EvictDiskToBudget()
    {
        while (true)
        {
            string? victim = null;
            lock (_diskGate)
            {
                if (_diskBytes <= _byteBudget)
                {
                    return;
                }

                long oldestSeq = long.MaxValue;
                foreach (var kvp in _diskIndex)
                {
                    if (kvp.Value.Seq < oldestSeq)
                    {
                        oldestSeq = kvp.Value.Seq;
                        victim = kvp.Key;
                    }
                }

                if (victim is null)
                {
                    return;
                }

                if (_diskIndex.Remove(victim, out var item))
                {
                    _diskBytes -= item.Size;
                }
            }

            TryDelete(Path.Combine(_directory!, victim + ".bin"));
            TryDelete(Path.Combine(_directory!, victim + ".meta"));
        }
    }

    private void WriteAtomic(string path, byte[] content)
    {
        string temp = Path.Combine(_directory!, Path.GetRandomFileName() + ".tmp");
        try
        {
            File.WriteAllBytes(temp, content);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private void PurgeExpiredFiles()
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            DateTime now = _utcNow();
            foreach (string metaPath in Directory.EnumerateFiles(_directory!, "*.meta"))
            {
                (string? contentType, DateTime storedAt)? meta = TryParseMeta(metaPath);
                bool expired = meta is null || now - meta.Value.storedAt >= Expiry;
                if (expired)
                {
                    TryDelete(metaPath);
                    string binPath = Path.ChangeExtension(metaPath, ".bin");
                    TryDelete(binPath);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
