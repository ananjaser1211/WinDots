using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace WinDots.App.Media;

/// <summary>
/// A small, bounded map of per-track lyrics offsets (milliseconds), keyed by the normalised track hash, persisted as
/// JSON (LocalState\lyrics-offsets.json). A track with no entry uses the global <c>lyrics.offsetMs</c> default. Follows
/// the <see cref="WinDots.Core.Media.SourceRegistry"/> conventions: atomic save, corruption tolerance, LRU-ish eviction
/// by last-touched order. See _docs/10-enhancement-plan.md (E3).
/// </summary>
public sealed class LyricsOffsetStore
{
    /// <summary>The most tracks kept; the oldest touched are evicted past this.</summary>
    public const int MaxEntries = 500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private sealed record Entry(string Key, int OffsetMs, long Seq);

    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _byKey = new(StringComparer.Ordinal);
    private long _seq;

    public LyricsOffsetStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        Load();
    }

    /// <summary>Returns the stored offset (ms) for the track, or null when none is set.</summary>
    public int? Get(string trackKey)
    {
        ArgumentNullException.ThrowIfNull(trackKey);
        lock (_gate)
        {
            return _byKey.TryGetValue(trackKey, out Entry? e) ? e.OffsetMs : null;
        }
    }

    /// <summary>Sets the offset (ms) for a track and persists. An offset of 0 removes the entry (falls back to default).</summary>
    public void Set(string trackKey, int offsetMs)
    {
        ArgumentNullException.ThrowIfNull(trackKey);
        lock (_gate)
        {
            if (offsetMs == 0)
            {
                _byKey.Remove(trackKey);
            }
            else
            {
                _byKey[trackKey] = new Entry(trackKey, offsetMs, ++_seq);
                Evict();
            }
        }

        Save();
    }

    private void Load()
    {
        lock (_gate)
        {
            _byKey.Clear();
            try
            {
                if (!File.Exists(_path))
                {
                    return;
                }

                string json = File.ReadAllText(_path);
                Entry[]? entries = JsonSerializer.Deserialize<Entry[]>(json, JsonOptions);
                if (entries is null)
                {
                    return;
                }

                foreach (Entry entry in entries)
                {
                    if (!string.IsNullOrEmpty(entry.Key))
                    {
                        _byKey[entry.Key] = entry with { Seq = ++_seq };
                    }
                }

                Evict();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _byKey.Clear();
            }
        }
    }

    private void Save()
    {
        Entry[] entries;
        lock (_gate)
        {
            entries = new Entry[_byKey.Count];
            _byKey.Values.CopyTo(entries, 0);
        }

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
        }
    }

    private void Evict()
    {
        if (_byKey.Count <= MaxEntries)
        {
            return;
        }

        var ordered = new List<Entry>(_byKey.Values);
        ordered.Sort((a, b) => a.Seq.CompareTo(b.Seq));
        int remove = _byKey.Count - MaxEntries;
        for (int i = 0; i < remove; i++)
        {
            _byKey.Remove(ordered[i].Key);
        }
    }
}
