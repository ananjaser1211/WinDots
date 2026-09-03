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
