using System.Net;
using System.Text;
using WinDots.Core.Scrobbling;

namespace WinDots.Core.Tests.Scrobbling;

public sealed class LastFmClientTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;

        public FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) => _responder = responder;

        public HttpRequestMessage? LastRequest { get; private set; }

        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is not null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : string.Empty;
            return _responder(request, LastBody);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static LastFmClient Client(FakeHandler handler) => new(handler, "KEY", "SECRET");

    [Fact]
    public async Task GetToken_UsesGet_ParsesToken()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, "{\"token\":\"abc123\"}"));
        using var client = Client(handler);

        string token = await client.GetTokenAsync(CancellationToken.None);

        Assert.Equal("abc123", token);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Contains("api_sig=", handler.LastRequest.RequestUri!.Query, StringComparison.Ordinal);
        Assert.Contains("format=json", handler.LastRequest.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSession_ParsesNameAndKey()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, "{\"session\":{\"name\":\"alice\",\"key\":\"sk-xyz\"}}"));
        using var client = Client(handler);

        LastFmSession session = await client.GetSessionAsync("tok", CancellationToken.None);

        Assert.Equal("alice", session.Name);
        Assert.Equal("sk-xyz", session.Key);
    }

    [Fact]
    public async Task GetSession_TokenNotAuthorized_ThrowsWithCode14()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, "{\"error\":14,\"message\":\"This token has not been authorized\"}"));
        using var client = Client(handler);

        LastFmException ex = await Assert.ThrowsAsync<LastFmException>(() => client.GetSessionAsync("tok", CancellationToken.None));
        Assert.Equal(14, ex.Code);
        Assert.True(ex.IsTokenNotAuthorized);
    }

    [Fact]
    public async Task UpdateNowPlaying_PostsSignedForm()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, "{\"nowplaying\":{}}"));
        using var client = Client(handler);

        var scrobble = new Scrobble(new TrackIdentity("Artist", "Song", "Album"), DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(200));
        await client.UpdateNowPlayingAsync(scrobble, "sk", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("method=track.updateNowPlaying", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("api_sig=", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("sk=sk", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scrobble_Batch_IndexesParameters()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, "{\"scrobbles\":{}}"));
        using var client = Client(handler);

        var list = new[]
        {
            new Scrobble(new TrackIdentity("A1", "T1", null), DateTimeOffset.FromUnixTimeSeconds(1000), null),
            new Scrobble(new TrackIdentity("A2", "T2", null), DateTimeOffset.FromUnixTimeSeconds(2000), null),
        };
        await client.ScrobbleAsync(list, "sk", CancellationToken.None);

        Assert.Contains("artist%5B0%5D=A1", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("timestamp%5B1%5D=2000", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scrobble_OverBatchLimit_Throws()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, "{}"));
        using var client = Client(handler);
        var list = Enumerable.Range(0, LastFmClient.MaxScrobbleBatch + 1)
            .Select(i => new Scrobble(new TrackIdentity("A", "T" + i, null), DateTimeOffset.FromUnixTimeSeconds(i), null))
            .ToArray();

        await Assert.ThrowsAsync<ArgumentException>(() => client.ScrobbleAsync(list, "sk", CancellationToken.None));
    }

    [Fact]
    public async Task Love_PostsSigned()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, "{}"));
        using var client = Client(handler);

        await client.LoveAsync("Artist", "Song", "sk", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("method=track.love", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiError_InvalidSession_ThrowsAuthFailure()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, "{\"error\":9,\"message\":\"Invalid session key\"}"));
        using var client = Client(handler);

        LastFmException ex = await Assert.ThrowsAsync<LastFmException>(() => client.LoveAsync("A", "T", "sk", CancellationToken.None));
        Assert.Equal(9, ex.Code);
        Assert.True(ex.IsAuthFailure);
    }

    [Fact]
    public async Task RateLimitError_IsTransient()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, "{\"error\":29,\"message\":\"Rate limit exceeded\"}"));
        using var client = Client(handler);

        LastFmException ex = await Assert.ThrowsAsync<LastFmException>(() => client.LoveAsync("A", "T", "sk", CancellationToken.None));
        Assert.True(ex.IsTransient);
    }

    [Fact]
    public async Task GetUserInfo_ParsesFields()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK,
            "{\"user\":{\"name\":\"alice\",\"realname\":\"Alice A\",\"playcount\":\"1234\"," +
            "\"image\":[{\"size\":\"small\",\"#text\":\"http://small\"},{\"size\":\"large\",\"#text\":\"http://large\"}]}}"));
        using var client = Client(handler);

        LastFmUserInfo info = await client.GetUserInfoAsync("sk", CancellationToken.None);

        Assert.Equal("alice", info.Name);
        Assert.Equal("Alice A", info.RealName);
        Assert.Equal(1234, info.Playcount);
        Assert.Equal("http://large", info.ImageUrl);
    }

    [Fact]
    public async Task GetRecentTracks_ParsesNowPlayingAndDate()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK,
            "{\"recenttracks\":{\"track\":[" +
            "{\"name\":\"Live One\",\"artist\":{\"#text\":\"Band\"},\"album\":{\"#text\":\"Alb\"},\"@attr\":{\"nowplaying\":\"true\"}}," +
            "{\"name\":\"Old One\",\"artist\":{\"#text\":\"Band\"},\"date\":{\"uts\":\"1000\"}}" +
            "]}}"));
        using var client = Client(handler);

        IReadOnlyList<RecentTrack> tracks = await client.GetRecentTracksAsync("alice", "sk", 10, CancellationToken.None);

        Assert.Equal(2, tracks.Count);
        Assert.True(tracks[0].NowPlaying);
        Assert.Equal("Live One", tracks[0].Track);
        Assert.False(tracks[1].NowPlaying);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1000), tracks[1].PlayedAt);
    }

    [Fact]
    public async Task HttpFailure_MapsToTransientException()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("boom"));
        using var client = Client(handler);

        LastFmException ex = await Assert.ThrowsAsync<LastFmException>(() => client.GetTokenAsync(CancellationToken.None));
        Assert.Equal(0, ex.Code);
        Assert.True(ex.IsTransient);
    }
}
