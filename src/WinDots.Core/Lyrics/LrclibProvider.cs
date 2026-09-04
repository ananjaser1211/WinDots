using System.Globalization;
using System.Net;
using System.Text.Json;

namespace WinDots.Core.Lyrics;

/// <summary>
/// The LRCLIB lyrics provider (keyless). Calls <c>GET https://lrclib.net/api/get</c> with the track name, artist,
/// album, and whole-second duration. HTTPS only, a 5 s timeout, and a 256 KB response cap. A 404 (no match) returns
/// null; any other failure returns null and logs a redacted reason (never the title). Attribution: "Lyrics from LRCLIB".
/// See _docs/10-enhancement-plan.md (E3) and _docs/privacy.md.
/// </summary>
public sealed class LrclibProvider : ILyricsProvider, IDisposable
{
    /// <summary>The provider name surfaced in <see cref="LyricsResult.Provider"/>.</summary>
    public const string ProviderName = "LRCLIB";

    /// <summary>The attribution link shown under the lyrics.</summary>
    public const string Attribution = "https://lrclib.net";

    private const string BaseUrl = "https://lrclib.net/api/get";
    private const string UserAgent = "WinDots/0.1 (https://github.com/ananjaser1211/WinDots)";
    private const int MaxResponseBytes = 256 * 1024;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly Action<string>? _log;
    private bool _disposed;

    /// <param name="handler">The message handler (a real <c>SocketsHttpHandler</c> in production; a fake in tests).</param>
    /// <param name="log">Optional log sink for redacted failure reasons.</param>
    /// <param name="timeout">Request timeout; defaults to 5 s.</param>
    public LrclibProvider(HttpMessageHandler handler, Action<string>? log = null, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _http = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = timeout ?? DefaultTimeout,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _log = log;
    }

    public async Task<LyricsResult?> LookupAsync(LyricsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!query.IsUsable)
        {
            return null;
        }

        Uri uri = BuildUri(query);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using HttpResponseMessage response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _log?.Invoke($"lyrics: LRCLIB returned {(int)response.StatusCode}");
                return null;
            }

            if (response.Content.Headers.ContentLength is { } declared && declared > MaxResponseBytes)
            {
                _log?.Invoke($"lyrics: LRCLIB response too large ({declared} bytes)");
                return null;
            }

            byte[]? body = await ReadCappedAsync(response, ct).ConfigureAwait(false);
            if (body is null)
            {
                _log?.Invoke("lyrics: LRCLIB response exceeded the size cap");
                return null;
            }

            return Parse(body);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // Our own timeout (not the caller's cancellation).
            _log?.Invoke("lyrics: LRCLIB request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _log?.Invoke($"lyrics: LRCLIB request failed ({ex.GetType().Name})");
            return null;
        }
        catch (JsonException)
        {
            _log?.Invoke("lyrics: LRCLIB response was not valid JSON");
            return null;
        }
    }

    private static Uri BuildUri(LyricsQuery query)
    {
        var q = new List<string>
        {
            "track_name=" + Uri.EscapeDataString(query.Title.Trim()),
            "artist_name=" + Uri.EscapeDataString(query.ArtistText),
        };

        if (!string.IsNullOrWhiteSpace(query.Album))
        {
            q.Add("album_name=" + Uri.EscapeDataString(query.Album.Trim()));
        }

        if (query.Duration is { } d && d > TimeSpan.Zero)
        {
            long seconds = (long)Math.Round(d.TotalSeconds);
            q.Add("duration=" + seconds.ToString(CultureInfo.InvariantCulture));
        }

        return new Uri(BaseUrl + "?" + string.Join("&", q));
    }

    private static async Task<byte[]?> ReadCappedAsync(HttpResponseMessage response, CancellationToken ct)
    {
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

    private static LyricsResult? Parse(byte[] body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // Instrumental tracks report no lyrics.
        if (root.TryGetProperty("instrumental", out JsonElement inst) &&
            inst.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        string? synced = GetString(root, "syncedLyrics");
        string? plain = GetString(root, "plainLyrics");

        LrcParseResult parsed;
        if (!string.IsNullOrWhiteSpace(synced))
        {
            parsed = LrcParser.Parse(synced);
        }
        else if (!string.IsNullOrWhiteSpace(plain))
        {
            parsed = LrcParser.Parse(plain);
        }
        else
        {
            return null;
        }

        if (parsed.Lines.Count == 0)
        {
            return null;
        }

        return new LyricsResult(ProviderName, Attribution, parsed.Lines, parsed.IsSynced);
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

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
