using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinDots.Core.Lyrics;

/// <summary>
/// A disk cache for lyrics results, keyed by the SHA-256 of the normalised query. Entries expire after 30 days.
/// Both found and not-found answers are cached so a track without lyrics is not re-requested on every open. Follows
/// the <see cref="Media.ArtworkCache"/> disk conventions: atomic writes, corruption tolerance, expiry sweep on start.
/// Thread-safe, BCL only. See _docs/10-enhancement-plan.md (E3) and _docs/privacy.md.
/// </summary>
public sealed class LyricsCache
{
    private static readonly TimeSpan Expiry = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string? _directory;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();

    // The persisted envelope: the found result (null when the lookup returned nothing) plus the store time for expiry.
    private sealed record Envelope(LyricsResult? Result, DateTimeOffset StoredAt);

    /// <param name="directory">Backing directory. Null disables persistence (every lookup is a miss).</param>
    /// <param name="now">Clock override for tests.</param>
    public LyricsCache(string? directory, Func<DateTimeOffset>? now = null)
    {
        _directory = string.IsNullOrWhiteSpace(directory) ? null : directory;
        _now = now ?? (() => DateTimeOffset.UtcNow);

        if (_directory is not null)
        {
            TryEnsureDirectory();
            PurgeExpired();
        }
    }

    /// <summary>
    /// Looks up a cached answer. Returns true when a fresh entry exists (<paramref name="result"/> may be null, meaning
    /// a cached not-found); false when there is nothing cached (or it expired) and the caller should query the network.
    /// </summary>
    public bool TryGet(LyricsQuery query, out LyricsResult? result)
    {
        ArgumentNullException.ThrowIfNull(query);
        result = null;
        if (_directory is null)
        {
            return false;
        }

        string path = PathFor(query);
        lock (_gate)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                string json = File.ReadAllText(path);
                Envelope? env = JsonSerializer.Deserialize<Envelope>(json, JsonOptions);
                if (env is null)
                {
                    return false;
                }

                if (_now() - env.StoredAt >= Expiry)
                {
                    TryDelete(path);
                    return false;
                }

                result = env.Result;
                return true;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>Stores an answer (a found result, or null for a not-found) for the query. Best-effort; failures are swallowed.</summary>
    public void Set(LyricsQuery query, LyricsResult? result)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (_directory is null)
        {
            return;
        }

        string path = PathFor(query);
        var env = new Envelope(result, _now());
        lock (_gate)
        {
            try
            {
                TryEnsureDirectory();
                string json = JsonSerializer.Serialize(env, JsonOptions);
                string temp = path + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort persistence.
            }
        }
    }

    /// <summary>Normalises a query to a stable key: lower-cased, trimmed title/artists/album and whole-second duration.</summary>
    public static string NormalizeKey(LyricsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        string title = Normalize(query.Title);
        string artists = Normalize(query.ArtistText);
        string album = Normalize(query.Album ?? string.Empty);
        long seconds = query.Duration is { } d ? (long)Math.Round(d.TotalSeconds) : -1;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{title}{artists}{album}{seconds}");
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private string PathFor(LyricsQuery query)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeKey(query)));
        return Path.Combine(_directory!, Convert.ToHexStringLower(digest) + ".json");
    }

    private void TryEnsureDirectory()
    {
        try
        {
            Directory.CreateDirectory(_directory!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void PurgeExpired()
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            DateTimeOffset now = _now();
            foreach (string path in Directory.EnumerateFiles(_directory!, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    Envelope? env = JsonSerializer.Deserialize<Envelope>(json, JsonOptions);
                    if (env is null || now - env.StoredAt >= Expiry)
                    {
                        TryDelete(path);
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    TryDelete(path);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
