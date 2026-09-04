using System.Globalization;
using System.Net;
using System.Text.Json;

namespace WinDots.Core.Scrobbling;

/// <summary>
/// A thin, testable Last.fm 2.0 API client. All requests use HTTPS with a 10 s timeout and a 512 KB response cap;
/// read methods use GET, write methods POST form-encoded bodies; every response is JSON (<c>format=json</c>). Signed
/// methods carry an <c>api_sig</c> computed by <see cref="LastFmSigner"/>. API error bodies are surfaced as
/// <see cref="LastFmException"/> with the numeric code. Titles and secrets are never logged. The <see cref="HttpMessageHandler"/>
/// is injected so tests supply a fake. See _docs/10-enhancement-plan.md (E4) and _docs/privacy.md.
/// </summary>
public sealed class LastFmClient : IDisposable
{
    private const string RootUrl = "https://ws.audioscrobbler.com/2.0/";
    private const string UserAgent = "WinDots/0.1 (https://github.com/ananjaser1211/WinDots)";
    private const int MaxResponseBytes = 512 * 1024;

    /// <summary>The largest batch <c>track.scrobble</c> accepts in one request.</summary>
    public const int MaxScrobbleBatch = 50;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _secret;
    private readonly Action<string>? _log;
    private bool _disposed;

    public LastFmClient(HttpMessageHandler handler, string apiKey, string secret, Action<string>? log = null, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _secret = secret ?? throw new ArgumentNullException(nameof(secret));
        _http = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = timeout ?? DefaultTimeout,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _log = log;
    }

    /// <summary>Requests an unauthorised request token to begin the browser sign-in flow.</summary>
    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        var p = Signed(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "auth.getToken",
        });
        JsonElement root = await SendAsync(p, post: false, ct).ConfigureAwait(false);
        return GetRequiredString(root, "token");
    }

    /// <summary>Exchanges an authorised token for a long-lived session. Throws <see cref="LastFmException"/> (code 14) while the token is still unauthorised.</summary>
    public async Task<LastFmSession> GetSessionAsync(string token, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var p = Signed(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "auth.getSession",
            ["token"] = token,
        });
        JsonElement root = await SendAsync(p, post: false, ct).ConfigureAwait(false);
        JsonElement session = root.GetProperty("session");
        return new LastFmSession(GetRequiredString(session, "name"), GetRequiredString(session, "key"));
    }

    /// <summary>Sends a now-playing notification for the current track. Best-effort; not scrobbled.</summary>
    public async Task UpdateNowPlayingAsync(Scrobble scrobble, string sessionKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scrobble);
        var p = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "track.updateNowPlaying",
            ["artist"] = scrobble.Identity.Artist,
            ["track"] = scrobble.Identity.Track,
            ["sk"] = sessionKey,
        };
        AddOptional(p, scrobble);
        await SendAsync(Signed(p), post: true, ct).ConfigureAwait(false);
    }

    /// <summary>Submits a batch of up to <see cref="MaxScrobbleBatch"/> scrobbles.</summary>
    public async Task ScrobbleAsync(IReadOnlyList<Scrobble> scrobbles, string sessionKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scrobbles);
        if (scrobbles.Count == 0)
        {
            return;
        }

        if (scrobbles.Count > MaxScrobbleBatch)
        {
            throw new ArgumentException($"A scrobble batch is limited to {MaxScrobbleBatch} tracks.", nameof(scrobbles));
        }

        var p = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "track.scrobble",
            ["sk"] = sessionKey,
        };

        for (int i = 0; i < scrobbles.Count; i++)
        {
            Scrobble s = scrobbles[i];
            string suffix = "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            p["artist" + suffix] = s.Identity.Artist;
            p["track" + suffix] = s.Identity.Track;
            p["timestamp" + suffix] = s.UnixTimestamp.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(s.Identity.Album))
            {
                p["album" + suffix] = s.Identity.Album!;
            }

            if (s.Duration is { } d && d > TimeSpan.Zero)
            {
                p["duration" + suffix] = ((long)Math.Round(d.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }
        }

        await SendAsync(Signed(p), post: true, ct).ConfigureAwait(false);
    }

    /// <summary>Loves a track for the signed-in user.</summary>
    public Task LoveAsync(string artist, string track, string sessionKey, CancellationToken ct) =>
        LoveInternalAsync("track.love", artist, track, sessionKey, ct);

    /// <summary>Removes the love flag from a track.</summary>
    public Task UnloveAsync(string artist, string track, string sessionKey, CancellationToken ct) =>
        LoveInternalAsync("track.unlove", artist, track, sessionKey, ct);

    private async Task LoveInternalAsync(string method, string artist, string track, string sessionKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artist);
        ArgumentException.ThrowIfNullOrWhiteSpace(track);
        var p = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = method,
            ["artist"] = artist,
            ["track"] = track,
            ["sk"] = sessionKey,
        };
        await SendAsync(Signed(p), post: true, ct).ConfigureAwait(false);
    }

    /// <summary>Fetches the signed-in user's public profile (name, avatar, play count).</summary>
    public async Task<LastFmUserInfo> GetUserInfoAsync(string sessionKey, CancellationToken ct)
    {
        var p = Signed(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "user.getInfo",
            ["sk"] = sessionKey,
        });
        JsonElement root = await SendAsync(p, post: false, ct).ConfigureAwait(false);
        JsonElement user = root.GetProperty("user");
        long? playcount = null;
        if (user.TryGetProperty("playcount", out JsonElement pc) &&
            pc.ValueKind == JsonValueKind.String &&
            long.TryParse(pc.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long count))
        {
            playcount = count;
        }

        return new LastFmUserInfo(
            GetRequiredString(user, "name"),
            GetOptionalString(user, "realname"),
            LargestImage(user),
            playcount);
    }

    /// <summary>Fetches the signed-in user's most recent tracks (including a now-playing entry when present).</summary>
    public async Task<IReadOnlyList<RecentTrack>> GetRecentTracksAsync(string user, string sessionKey, int limit, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        var p = Signed(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "user.getRecentTracks",
            ["user"] = user,
            ["sk"] = sessionKey,
            ["limit"] = Math.Clamp(limit, 1, 50).ToString(CultureInfo.InvariantCulture),
        });
        JsonElement root = await SendAsync(p, post: false, ct).ConfigureAwait(false);

        var results = new List<RecentTrack>();
        if (root.TryGetProperty("recenttracks", out JsonElement rt) &&
            rt.TryGetProperty("track", out JsonElement tracks) &&
            tracks.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement t in tracks.EnumerateArray())
            {
                string artist = t.TryGetProperty("artist", out JsonElement a)
                    ? (GetOptionalString(a, "#text") ?? a.GetString() ?? string.Empty)
                    : string.Empty;
                string track = GetOptionalString(t, "name") ?? string.Empty;
                string? album = t.TryGetProperty("album", out JsonElement al) ? GetOptionalString(al, "#text") : null;
                bool nowPlaying = t.TryGetProperty("@attr", out JsonElement attr) &&
                                  attr.TryGetProperty("nowplaying", out JsonElement np) &&
                                  string.Equals(np.GetString(), "true", StringComparison.OrdinalIgnoreCase);
                DateTimeOffset? playedAt = null;
                if (t.TryGetProperty("date", out JsonElement date) &&
                    date.TryGetProperty("uts", out JsonElement uts) &&
                    long.TryParse(uts.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long secs))
                {
                    playedAt = DateTimeOffset.FromUnixTimeSeconds(secs);
                }

                results.Add(new RecentTrack(artist, track, album, LargestImage(t), nowPlaying, playedAt));
            }
        }

        return results;
    }

    private static void AddOptional(Dictionary<string, string> p, Scrobble s)
    {
        if (!string.IsNullOrWhiteSpace(s.Identity.Album))
        {
            p["album"] = s.Identity.Album!;
        }

        if (s.Duration is { } d && d > TimeSpan.Zero)
        {
            p["duration"] = ((long)Math.Round(d.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }
    }

    private Dictionary<string, string> Signed(Dictionary<string, string> p)
    {
        p["api_key"] = _apiKey;
        p["api_sig"] = LastFmSigner.Sign(p, _secret);
        return p;
    }

    private async Task<JsonElement> SendAsync(Dictionary<string, string> parameters, bool post, CancellationToken ct)
    {
        parameters["format"] = "json";

        using HttpRequestMessage request = post
            ? new HttpRequestMessage(HttpMethod.Post, RootUrl) { Content = new FormUrlEncodedContent(parameters) }
            : new HttpRequestMessage(HttpMethod.Get, RootUrl + "?" + Encode(parameters));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _log?.Invoke("last.fm: request timed out");
            throw new LastFmException(0, "The Last.fm request timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            _log?.Invoke($"last.fm: request failed ({ex.GetType().Name})");
            throw new LastFmException(0, "The Last.fm request failed.", ex);
        }

        using (response)
        {
            byte[]? body = await ReadCappedAsync(response, ct).ConfigureAwait(false);
            if (body is null)
            {
                _log?.Invoke("last.fm: response exceeded the size cap");
                throw new LastFmException(0, "The Last.fm response was too large.");
            }

            JsonElement root;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                root = doc.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                // A non-JSON body on a transport failure (e.g. a gateway HTML page).
                if (!response.IsSuccessStatusCode)
                {
                    _log?.Invoke($"last.fm: returned {(int)response.StatusCode}");
                    throw new LastFmException(0, $"Last.fm returned HTTP {(int)response.StatusCode}.", ex);
                }

                _log?.Invoke("last.fm: response was not valid JSON");
                throw new LastFmException(0, "The Last.fm response was not valid JSON.", ex);
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out JsonElement err))
            {
                int code = err.ValueKind == JsonValueKind.Number ? err.GetInt32() : 0;
                string message = GetOptionalString(root, "message") ?? "Last.fm reported an error.";
                _log?.Invoke($"last.fm: api error {code}");
                throw new LastFmException(code, message);
            }

            if (!response.IsSuccessStatusCode)
            {
                _log?.Invoke($"last.fm: returned {(int)response.StatusCode}");
                throw new LastFmException(0, $"Last.fm returned HTTP {(int)response.StatusCode}.");
            }

            return root;
        }
    }

    private static string Encode(IReadOnlyDictionary<string, string> parameters) =>
        string.Join("&", parameters.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));

    private static async Task<byte[]?> ReadCappedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is { } declared && declared > MaxResponseBytes)
        {
            return null;
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string GetRequiredString(JsonElement element, string name)
    {
        string? value = GetOptionalString(element, name);
        return value ?? throw new LastFmException(0, $"The Last.fm response was missing '{name}'.");
    }

    private static string? GetOptionalString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out JsonElement el) &&
        el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    // Last.fm image arrays are ordered small -> extralarge; pick the last non-empty URL.
    private static string? LargestImage(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("image", out JsonElement images) ||
            images.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? best = null;
        foreach (JsonElement img in images.EnumerateArray())
        {
            string? url = GetOptionalString(img, "#text");
            if (!string.IsNullOrWhiteSpace(url))
            {
                best = url;
            }
        }

        return best;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http.Dispose();
    }
}
