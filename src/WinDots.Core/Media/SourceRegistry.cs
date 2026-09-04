using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinDots.Core.Media;

/// <summary>One recorded source: its app id, display name, when it was last seen, and the detector's last verdict.</summary>
public sealed record SeenSource(
    string SourceAppId,
    string DisplayName,
    DateTimeOffset LastSeen,
    string LastVerdict);

/// <summary>
/// A small, bounded record of every media source ever seen, persisted as JSON (LocalState\sources.json). Backs the
/// settings Sources page: source app id, display name, last-seen time, and last verdict, newest first, capped at
/// <see cref="MaxEntries"/>. The file path is injected so it is unit-testable. See _docs/10-enhancement-plan.md (E1).
/// </summary>
public sealed class SourceRegistry
{
    /// <summary>The most sources kept; the oldest by last-seen time are evicted past this.</summary>
    public const int MaxEntries = 200;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private readonly Dictionary<string, SeenSource> _byId = new(StringComparer.Ordinal);

    public SourceRegistry(string path, Func<DateTimeOffset>? now = null)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _now = now ?? (() => DateTimeOffset.UtcNow);
        Load();
    }

    /// <summary>The recorded sources, newest last-seen first.</summary>
    public IReadOnlyList<SeenSource> Snapshot()
    {
        lock (_gate)
        {
            var list = new List<SeenSource>(_byId.Values);
            list.Sort((a, b) => b.LastSeen.CompareTo(a.LastSeen));
            return list;
        }
    }

    /// <summary>
    /// Records that a source was seen with the given verdict, updating its last-seen time. Returns true when the entry
    /// is new or its verdict text changed (a hint the caller should persist).
    /// </summary>
    public bool Record(string sourceAppId, string displayName, string verdict)
    {
        ArgumentNullException.ThrowIfNull(sourceAppId);
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(verdict);

        lock (_gate)
        {
            bool changed = !_byId.TryGetValue(sourceAppId, out SeenSource? existing)
                || !string.Equals(existing.LastVerdict, verdict, StringComparison.Ordinal)
                || !string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal);

            _byId[sourceAppId] = new SeenSource(sourceAppId, displayName, _now(), verdict);
            Evict();
            return changed;
        }
    }

    /// <summary>Loads the file into memory, tolerating a missing or malformed file (starts empty).</summary>
    public void Load()
    {
        lock (_gate)
        {
            _byId.Clear();
            try
            {
                if (!File.Exists(_path))
                {
                    return;
                }

                string json = File.ReadAllText(_path);
                SeenSource[]? entries = JsonSerializer.Deserialize<SeenSource[]>(json, JsonOptions);
                if (entries is null)
                {
                    return;
                }

                foreach (SeenSource entry in entries)
                {
                    if (!string.IsNullOrEmpty(entry.SourceAppId))
                    {
                        _byId[entry.SourceAppId] = entry;
                    }
                }

                Evict();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _byId.Clear();
            }
        }
    }

    /// <summary>Writes the current set to disk atomically (temp file then replace). Failures are swallowed.</summary>
    public void Save()
    {
        SeenSource[] entries;
        lock (_gate)
        {
            entries = new SeenSource[_byId.Count];
            _byId.Values.CopyTo(entries, 0);
        }

        Array.Sort(entries, (a, b) => b.LastSeen.CompareTo(a.LastSeen));

        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(entries, JsonOptions);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort persistence; the in-memory registry is authoritative for the session.
        }
    }

    private void Evict()
    {
        if (_byId.Count <= MaxEntries)
        {
            return;
        }

        var ordered = new List<SeenSource>(_byId.Values);
        ordered.Sort((a, b) => a.LastSeen.CompareTo(b.LastSeen));
        int remove = _byId.Count - MaxEntries;
        for (int i = 0; i < remove; i++)
        {
            _byId.Remove(ordered[i].SourceAppId);
        }
    }
}
