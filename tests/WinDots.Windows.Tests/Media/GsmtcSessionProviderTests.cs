using WinDots.Core.Contracts;
using WinDots.Core.Media;
using WinDots.TestPlayer;
using WinDots.Windows.Media;

namespace WinDots.Windows.Tests.Media;

/// <summary>
/// End-to-end: the real Windows media-session manager sees the fake player, and commands round-trip through it.
/// Requires an interactive desktop session. Other media players may be running; the test only looks at its own.
/// </summary>
[Trait("Category", "Platform")]
public class GsmtcSessionProviderTests
{

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task DiscoversControlsAndDropsTheTestPlayer()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var ct = cts.Token;

        await using var provider = new GsmtcSessionProvider();
        await provider.InitializeAsync(ct);

        await using var player = await TestPlayerHost.StartAsync(ct);

        // Discovery: session appears with metadata, artwork key, and the advertised capabilities.
        var session = await WaitForSessionAsync(provider, s => s.Current.Title == "Test Track 1", ct);
        var snap = session.Current;
        Assert.Equal(FakePlayer.AppUserModelId, snap.SourceAppId);
        Assert.Contains("WinDots QA", snap.Artists);
        Assert.Equal("Fixtures", snap.Album);
        Assert.Equal(PlaybackState.Playing, snap.State);
        Assert.True(snap.Can(Capabilities.PlayPause | Capabilities.Next | Capabilities.Previous | Capabilities.Seek), $"Caps were {snap.Caps}");
        Assert.True(snap.Timeline.HasDuration);
        Assert.Equal(TimeSpan.FromMinutes(3), snap.Timeline.Duration);
        Assert.NotNull(snap.ArtworkKey);

        // Artwork loads and is bounded.
        var art = await session.LoadArtworkAsync(1024 * 1024, ct);
        Assert.True(art.Success, art.Error);
        Assert.True(art.Bytes.Length > 100);
        var tooSmall = await session.LoadArtworkAsync(16, ct);
        Assert.False(tooSmall.Success);

        // Next: command is accepted and the player reports the button press and the new track flows back.
        AssertSuccess(await session.TryNextAsync(ct));
        await player.WaitForLineAsync(l => l == "[event] ButtonPressed Next", Timeout, ct);
        await WaitForSnapshotAsync(session, s => s.Title == "Test Track 2", ct);

        // Pause via toggle.
        AssertSuccess(await session.TryPlayPauseAsync(ct));
        await player.WaitForLineAsync(l => l == "[event] ButtonPressed Pause", Timeout, ct);
        await WaitForSnapshotAsync(session, s => s.State == PlaybackState.Paused, ct);

        // Seek.
        Assert.True((await session.TrySeekAsync(TimeSpan.FromSeconds(30), ct)).IsSuccess);
        await player.WaitForLineAsync(l => l.StartsWith("[event] SeekRequested 30", StringComparison.Ordinal), Timeout, ct);
        await WaitForSnapshotAsync(session, s => Math.Abs((s.Timeline.Position - TimeSpan.FromSeconds(30)).TotalSeconds) < 1.5, ct);

        // Shuffle and repeat round-trip (the fake player advertises both because it handles the change requests).
        AssertSuccess(await session.TrySetShuffleAsync(true, ct));
        await player.WaitForLineAsync(l => l == "[event] ShuffleRequested True", Timeout, ct);
        await WaitForSnapshotAsync(session, s => s.Shuffle == true, ct);
        AssertSuccess(await session.TrySetRepeatAsync(RepeatMode.Track, ct));
        await player.WaitForLineAsync(l => l == "[event] RepeatRequested Track", Timeout, ct);
        await WaitForSnapshotAsync(session, s => s.Repeat == RepeatMode.Track, ct);

        // Removal: quitting the player drops the session without exceptions.
        await player.SendAsync("quit");
        await WaitUntilAsync(() => provider.Sessions.All(s => s.SourceAppId != FakePlayer.AppUserModelId), ct);

        // Commands on a vanished session are rejected, not thrown.
        var late = await session.TryNextAsync(ct);
        Assert.False(late.IsSuccess);
    }

    [Fact]
    public async Task DuplicateLeavingKeepsSurvivorIdentity()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var ct = cts.Token;

        await using var provider = new GsmtcSessionProvider();
        await provider.InitializeAsync(ct);

        await using var first = await TestPlayerHost.StartAsync(ct);
        await first.SendAsync("title First Window");
        var firstSession = await WaitForSessionAsync(provider, s => s.Current.Title == "First Window", ct);

        await using var second = await TestPlayerHost.StartAsync(ct);
        await second.SendAsync("title Second Window");
        var secondSession = await WaitForSessionAsync(provider, s => s.Current.Title == "Second Window", ct);

        Assert.NotEqual(firstSession.Id, secondSession.Id);
        Assert.Equal(FakePlayer.AppUserModelId + "#0", firstSession.Id);
        Assert.Equal(FakePlayer.AppUserModelId + "#1", secondSession.Id);
        Assert.Same(firstSession, provider.Sessions.Single(s => s.Id == firstSession.Id));

        // The first duplicate leaves: the survivor keeps its wrapper and its ordinal instead of being renumbered to #0.
        await first.SendAsync("quit");
        await WaitUntilAsync(() => provider.Sessions.Count(s => s.SourceAppId == FakePlayer.AppUserModelId) == 1, ct);
        var survivor = provider.Sessions.Single(s => s.SourceAppId == FakePlayer.AppUserModelId);
        Assert.Same(secondSession, survivor);
        Assert.Equal(FakePlayer.AppUserModelId + "#1", survivor.Id);
        Assert.Equal("Second Window", survivor.Current.Title);

        // A newcomer takes the lowest free ordinal, and the survivor is still untouched.
        await using var third = await TestPlayerHost.StartAsync(ct);
        await third.SendAsync("title Third Window");
        var thirdSession = await WaitForSessionAsync(provider, s => s.Current.Title == "Third Window", ct);
        Assert.Equal(FakePlayer.AppUserModelId + "#0", thirdSession.Id);
        Assert.Same(secondSession, provider.Sessions.Single(s => s.Id == FakePlayer.AppUserModelId + "#1"));

        // Every ID is unique regardless of the order the platform enumerated the sessions in.
        var ids = provider.Sessions.Where(s => s.SourceAppId == FakePlayer.AppUserModelId).Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    private static void AssertSuccess(CommandResult result) =>
        Assert.True(result.IsSuccess, $"Command failed: {result.Status} {result.Message}");

    private static async Task<IMediaSession> WaitForSessionAsync(IMediaSessionProvider provider, Func<IMediaSession, bool> predicate, CancellationToken ct)
    {
        IMediaSession? found = null;
        await WaitUntilAsync(() =>
        {
            found = provider.Sessions.FirstOrDefault(s => s.SourceAppId == FakePlayer.AppUserModelId && predicate(s));
            return found is not null;
        }, ct);
        return found!;
    }

    private static Task WaitForSnapshotAsync(IMediaSession session, Func<MediaSnapshot, bool> predicate, CancellationToken ct) =>
        WaitUntilAsync(() => predicate(session.Current), ct);

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met within the timeout.");
            }

            await Task.Delay(100, ct);
        }
    }
}

/// <summary>
/// The ordinal rule is pure so that the order-dependent case (a newcomer enumerated before a survivor) can be
/// covered without a platform session: the provider numbers newcomers only after every survivor is matched.
/// </summary>
public class GsmtcSessionOrdinalTests
{
    private const string Aumid = "Example.App_abc!App";

    [Fact]
    public void FirstSessionTakesZero() =>
        Assert.Equal(0, GsmtcSessionProvider.NextOrdinal([]));

    [Fact]
    public void SkipsEveryHeldOrdinalEvenWhenSurvivorsAreListedInAnyOrder()
    {
        Assert.Equal(1, GsmtcSessionProvider.NextOrdinal([$"{Aumid}#0"]));
        Assert.Equal(1, GsmtcSessionProvider.NextOrdinal([$"{Aumid}#2", $"{Aumid}#0"]));
        Assert.Equal(0, GsmtcSessionProvider.NextOrdinal([$"{Aumid}#1", $"{Aumid}#2"]));
    }

    [Fact]
    public void ReusesTheLowestFreedOrdinal() =>
        Assert.Equal(1, GsmtcSessionProvider.NextOrdinal([$"{Aumid}#0", $"{Aumid}#2"]));

    [Fact]
    public void IgnoresMalformedIds() =>
        Assert.Equal(0, GsmtcSessionProvider.NextOrdinal([Aumid, $"{Aumid}#x"]));

    [Fact]
    public void NewcomersNumberedAfterAllSurvivorsNeverCollide()
    {
        // Review scenario: survivor W#0 (live) and W#1 (exited); the platform enumerates the newcomer first.
        // With survivors resolved before numbering, the newcomer must land on #1, not on the survivor's #0.
        var survivors = new List<string> { $"{Aumid}#0" };
        var taken = new List<string>(survivors);
        var assigned = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            var id = $"{Aumid}#{GsmtcSessionProvider.NextOrdinal(taken)}";
            taken.Add(id);
            assigned.Add(id);
        }

        Assert.Equal([$"{Aumid}#1", $"{Aumid}#2"], assigned);
        Assert.Equal(taken.Count, taken.Distinct(StringComparer.Ordinal).Count());
    }
}
