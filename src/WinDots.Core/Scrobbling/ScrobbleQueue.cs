using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinDots.Core.Scrobbling;

/// <summary>
/// A disk-backed queue of pending scrobbles. Submission is idempotent by <see cref="Scrobble.DedupeKey"/> (identity plus
/// whole-second timestamp), the queue is bounded to <see cref="MaxEntries"/> (the oldest are dropped when full), and each
/// failed batch backs off exponentially before it is retried. Persisted atomically as JSON under LocalState, corruption
/// tolerant (a bad file loads empty). Thread-safe, BCL only. See _docs/10-enhancement-plan.md (E4) and _docs/privacy.md.
/// </summary>
public sealed class ScrobbleQueue
{
    /// <summary>The maximum number of pending scrobbles retained; the oldest are dropped beyond this.</summary>
    public const int MaxEntries = 500;

    private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(2);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed record Entry(Scrobble Scrobble, int Attempts, DateTimeOffset NextAttempt);

    private readonly string? _path;
    private readonly object _gate = new();
    private readonly List<Entry> _entries = new();

    /// <param name="path">Backing file path. Null disables persistence (an in-memory queue for tests).</param>
    public ScrobbleQueue(string? path)
    {
        _path = string.IsNullOrWhiteSpace(path) ? null : path;
        Load();
    }

    /// <summary>The number of pending scrobbles.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Adds a scrobble. A duplicate (same identity and timestamp) is ignored; the oldest is dropped when full.</summary>
    public void Enqueue(Scrobble scrobble)
    {
        ArgumentNullException.ThrowIfNull(scrobble);
        lock (_gate)
        {
            if (_entries.Any(e => string.Equals(e.Scrobble.DedupeKey, scrobble.DedupeKey, StringComparison.Ordinal)))
            {
                return;
            }

            _entries.Add(new Entry(scrobble, Attempts: 0, NextAttempt: DateTimeOffset.MinValue));
            while (_entries.Count > MaxEntries)
            {
                _entries.RemoveAt(0);
            }

            Save();
        }
    }

    /// <summary>Returns up to <paramref name="max"/> scrobbles whose backoff has elapsed, oldest first.</summary>
    public IReadOnlyList<Scrobble> DueBatch(DateTimeOffset now, int max = LastFmClient.MaxScrobbleBatch)
    {
        lock (_gate)
        {
            return _entries
                .Where(e => e.NextAttempt <= now)
                .OrderBy(e => e.Scrobble.UnixTimestamp)
                .Take(Math.Clamp(max, 1, LastFmClient.MaxScrobbleBatch))
                .Select(e => e.Scrobble)
                .ToList();
        }
    }

    /// <summary>Removes the given scrobbles after a successful submission.</summary>
    public void MarkSuccess(IReadOnlyList<Scrobble> scrobbles)
    {
        ArgumentNullException.ThrowIfNull(scrobbles);
        if (scrobbles.Count == 0)
        {
            return;
        }

        var keys = scrobbles.Select(s => s.DedupeKey).ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            int removed = _entries.RemoveAll(e => keys.Contains(e.Scrobble.DedupeKey));
            if (removed > 0)
            {
                Save();
            }
        }
    }

    /// <summary>Records a failed submission for the given scrobbles, scheduling the next attempt with exponential backoff.</summary>
    public void MarkFailure(IReadOnlyList<Scrobble> scrobbles, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(scrobbles);
        if (scrobbles.Count == 0)
        {
            return;
        }

        var keys = scrobbles.Select(s => s.DedupeKey).ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            bool changed = false;
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (keys.Contains(e.Scrobble.DedupeKey))
                {
                    int attempts = e.Attempts + 1;
                    _entries[i] = e with { Attempts = attempts, NextAttempt = now + BackoffFor(attempts) };
                    changed = true;
                }
            }

            if (changed)
            {
                Save();
            }
        }
    }

    /// <summary>The backoff delay after <paramref name="attempts"/> failed attempts (exponential, capped).</summary>
    public static TimeSpan BackoffFor(int attempts)
    {
        if (attempts <= 0)
        {
            return TimeSpan.Zero;
        }

        double seconds = BaseBackoff.TotalSeconds * Math.Pow(2, attempts - 1);
        if (seconds >= MaxBackoff.TotalSeconds || double.IsInfinity(seconds))
        {
            return MaxBackoff;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private void Load()
    {
        if (_path is null)
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return;
                }

                string json = File.ReadAllText(_path);
                List<Entry>? loaded = JsonSerializer.Deserialize<List<Entry>>(json, JsonOptions);
                if (loaded is not null)
                {
                    _entries.Clear();
                    _entries.AddRange(loaded.Where(e => e.Scrobble is not null && e.Scrobble.Identity is not null));
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _entries.Clear();
            }
        }
    }

    private void Save()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(_entries, JsonOptions);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort persistence.
        }
    }
}
