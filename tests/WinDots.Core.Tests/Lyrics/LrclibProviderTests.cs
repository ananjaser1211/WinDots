using System.Net;
using System.Text;
using WinDots.Core.Lyrics;

namespace WinDots.Core.Tests.Lyrics;

public sealed class LrclibProviderTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static LyricsQuery Query() => new("Song", new[] { "Artist" }, "Album", TimeSpan.FromSeconds(200));

    [Fact]
    public async Task Success_WithSyncedLyrics_ReturnsSyncedResult()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK,
            "{\"syncedLyrics\":\"[00:01.00]Line A\\n[00:05.00]Line B\",\"plainLyrics\":\"Line A\\nLine B\",\"instrumental\":false}"));
        using var provider = new LrclibProvider(handler);

        LyricsResult? result = await provider.LookupAsync(Query(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("LRCLIB", result!.Provider);
        Assert.Equal("https://lrclib.net", result.AttributionUrl);
        Assert.True(result.IsSynced);
        Assert.Equal(2, result.Lines.Count);
    }

    [Fact]
    public async Task Success_WithOnlyPlainLyrics_ReturnsUnsyncedResult()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK,
            "{\"syncedLyrics\":null,\"plainLyrics\":\"Just words\\nMore words\",\"instrumental\":false}"));
        using var provider = new LrclibProvider(handler);

        LyricsResult? result = await provider.LookupAsync(Query(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result!.IsSynced);
        Assert.Equal(2, result.Lines.Count);
    }

    [Fact]
    public async Task NotFound_ReturnsNull()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.NotFound, "{\"code\":404}"));
        using var provider = new LrclibProvider(handler);

        Assert.Null(await provider.LookupAsync(Query(), CancellationToken.None));
    }

    [Fact]
    public async Task ServerError_ReturnsNull_AndLogs()
    {
        string? logged = null;
        var handler = new FakeHandler(_ => Json(HttpStatusCode.InternalServerError, "boom"));
        using var provider = new LrclibProvider(handler, log: m => logged = m);

        Assert.Null(await provider.LookupAsync(Query(), CancellationToken.None));
        Assert.NotNull(logged);
        Assert.Contains("500", logged!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Instrumental_ReturnsNull()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK,
            "{\"syncedLyrics\":null,\"plainLyrics\":null,\"instrumental\":true}"));
        using var provider = new LrclibProvider(handler);

        Assert.Null(await provider.LookupAsync(Query(), CancellationToken.None));
    }

    [Fact]
    public async Task NoLyricsFields_ReturnsNull()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK,
            "{\"syncedLyrics\":null,\"plainLyrics\":\"\",\"instrumental\":false}"));
        using var provider = new LrclibProvider(handler);

        Assert.Null(await provider.LookupAsync(Query(), CancellationToken.None));
    }

    [Fact]
    public async Task OversizeResponse_ReturnsNull()
    {
        string big = new('x', 300 * 1024);
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK,
            "{\"plainLyrics\":\"" + big + "\"}"));
        using var provider = new LrclibProvider(handler);

        Assert.Null(await provider.LookupAsync(Query(), CancellationToken.None));
    }

    [Fact]
    public async Task Request_UsesHttpsGet_WithUserAgentAndQueryParams()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK,
            "{\"syncedLyrics\":\"[00:00.00]hi\"}"));
        using var provider = new LrclibProvider(handler);

        await provider.LookupAsync(Query(), CancellationToken.None);

        HttpRequestMessage req = handler.LastRequest!;
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Equal("https", req.RequestUri!.Scheme);
        Assert.Equal("lrclib.net", req.RequestUri.Host);
        Assert.Contains("track_name=Song", req.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("artist_name=Artist", req.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("duration=200", req.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("WinDots/0.1", req.Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnusableQuery_DoesNotHitNetwork()
    {
        bool called = false;
        var handler = new FakeHandler(_ =>
        {
            called = true;
            return Json(HttpStatusCode.OK, "{}");
        });
        using var provider = new LrclibProvider(handler);

        Assert.Null(await provider.LookupAsync(new LyricsQuery("", Array.Empty<string>(), null, null), CancellationToken.None));
        Assert.False(called);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "{}"));
        using var provider = new LrclibProvider(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.LookupAsync(Query(), cts.Token));
    }
}
