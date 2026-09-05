using System.Net;
using System.Text.Json;
using WinDots.Core.Updates;

namespace WinDots.Windows.Updates;

/// <summary>
/// An <see cref="IReleaseSource"/> backed by the public GitHub REST API: a read-only, unauthenticated GET of
/// <c>https://api.github.com/repos/AnanJaser1211/WinDots/releases/latest</c>. HTTPS only, a 10 s timeout, and a
/// 512 KB response cap. GitHub requires a User-Agent header. Every failure (network, non-200, oversized body, or
/// invalid JSON) is returned as <see cref="ReleaseFetch.Failed"/> rather than thrown, so a check never crashes the
/// caller. No auth, no token, no telemetry. See _docs/privacy.md and _docs/10-enhancement-plan.md (E7).
/// </summary>
public sealed class GitHubReleaseSource : IReleaseSource, IDisposable
{
    private const string Owner = "AnanJaser1211";
    private const string Repo = "WinDots";
    private const string LatestUrl = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";
    private const string UserAgent = "WinDots/0.1 (https://github.com/AnanJaser1211/WinDots)";
    private const int MaxResponseBytes = 512 * 1024;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly Action<string>? _log;
    private bool _disposed;

    /// <param name="handler">The message handler (a real <c>SocketsHttpHandler</c> in production; a fake in tests).</param>
    /// <param name="log">Optional log sink for redacted failure reasons.</param>
    /// <param name="timeout">Request timeout; defaults to 10 s.</param>
    public GitHubReleaseSource(HttpMessageHandler handler, Action<string>? log = null, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _http = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = timeout ?? DefaultTimeout,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        _log = log;
    }

    public async Task<ReleaseFetch> GetLatestReleaseAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(LatestUrl));
            using HttpResponseMessage response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // No published release yet: not an error state for the user, just nothing newer.
                return ReleaseFetch.Failed("No published releases were found.");
            }

            if (!response.IsSuccessStatusCode)
            {
                _log?.Invoke($"update: GitHub returned {(int)response.StatusCode}");
                return ReleaseFetch.Failed($"GitHub returned {(int)response.StatusCode}.");
            }

            if (response.Content.Headers.ContentLength is { } declared && declared > MaxResponseBytes)
            {
                _log?.Invoke($"update: response too large ({declared} bytes)");
                return ReleaseFetch.Failed("The release response was too large.");
            }

            byte[]? body = await ReadCappedAsync(response, ct).ConfigureAwait(false);
            if (body is null)
            {
                _log?.Invoke("update: response exceeded the size cap");
                return ReleaseFetch.Failed("The release response was too large.");
            }

            ReleaseInfo? info = Parse(body);
            if (info is null)
            {
                _log?.Invoke("update: release JSON missing tag_name or html_url");
                return ReleaseFetch.Failed("The release response was incomplete.");
            }

            return ReleaseFetch.Ok(info);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _log?.Invoke("update: request timed out");
            return ReleaseFetch.Failed("The update check timed out.");
        }
        catch (HttpRequestException ex)
        {
            _log?.Invoke($"update: request failed ({ex.GetType().Name})");
            return ReleaseFetch.Failed("Could not reach GitHub.");
        }
        catch (JsonException)
        {
            _log?.Invoke("update: response was not valid JSON");
            return ReleaseFetch.Failed("The release response was not valid JSON.");
        }
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

    private static ReleaseInfo? Parse(byte[] body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? tag = GetString(root, "tag_name");
        string? htmlUrl = GetString(root, "html_url");
        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(htmlUrl))
        {
            return null;
        }

        bool prerelease = root.TryGetProperty("prerelease", out JsonElement pre) &&
            pre.ValueKind == JsonValueKind.True;

        DateTimeOffset? publishedAt = null;
        if (root.TryGetProperty("published_at", out JsonElement pub) &&
            pub.ValueKind == JsonValueKind.String &&
            pub.TryGetDateTimeOffset(out DateTimeOffset parsed))
        {
            publishedAt = parsed;
        }

        return new ReleaseInfo(tag, htmlUrl, publishedAt, prerelease);
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
