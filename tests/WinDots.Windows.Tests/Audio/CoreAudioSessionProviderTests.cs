using WinDots.Core.Contracts;
using WinDots.TestPlayer;
using WinDots.Windows.Audio;

namespace WinDots.Windows.Tests.Audio;

/// <summary>
/// End-to-end: the real Core Audio render endpoint exposes the fake player's silent session, the matcher scores it,
/// and per-application volume/mute round-trip through it. Requires an interactive desktop session with an audio
/// render endpoint. Only the test player's own volume is ever changed, and it is restored to 1.0 at the end.
/// </summary>
[Trait("Category", "Platform")]
public class CoreAudioSessionProviderTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task MatchesTestPlayerHighAndRoundTripsVolumeAndMute()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var ct = cts.Token;

        await using var provider = new CoreAudioSessionProvider();
        await using var player = await TestPlayerHost.StartAsync(ct);

        // The silent WAV opens a render session shortly after the player starts; wait for the matcher to see it.
        var match = await WaitForMatchAsync(provider, FakePlayer.AppUserModelId, AudioMatchConfidence.High, ct);
        Assert.Equal(AudioMatchConfidence.High, match.Confidence);
        Assert.Single(match.AudioSessionIds);

        try
        {
            Assert.True(await provider.TrySetVolumeAsync(match, 0.25f, ct), "Setting volume should succeed.");
            var volume = await provider.GetVolumeAsync(match, ct);
            Assert.NotNull(volume);
            Assert.True(Math.Abs(volume!.Value - 0.25f) < 0.02f, $"Volume was {volume}.");

            Assert.True(await provider.TrySetMuteAsync(match, true, ct), "Muting should succeed.");
            Assert.Equal(true, await provider.GetMuteAsync(match, ct));
            Assert.True(await provider.TrySetMuteAsync(match, false, ct), "Unmuting should succeed.");
            Assert.Equal(false, await provider.GetMuteAsync(match, ct));
        }
        finally
        {
            // Never leave the test player's volume changed.
            await provider.TrySetVolumeAsync(match, 1.0f, ct);
            await provider.TrySetMuteAsync(match, false, ct);
        }
    }

    [Fact]
    public async Task UnknownApplicationIsNone()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;

        await using var provider = new CoreAudioSessionProvider();
        var match = await provider.MatchAsync("no-such-app.exe", ct);

        Assert.Equal(AudioMatchConfidence.None, match.Confidence);
        Assert.Empty(match.AudioSessionIds);
    }

    [Fact]
    public async Task TwoInstancesOfSameExecutableAreMedium()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var ct = cts.Token;

        await using var provider = new CoreAudioSessionProvider();
        await using var first = await TestPlayerHost.StartAsync(ct);
        await WaitForMatchAsync(provider, FakePlayer.AppUserModelId, AudioMatchConfidence.High, ct);

        await using var second = await TestPlayerHost.StartAsync(ct);

        // Two independent WinDots.TestPlayer.exe processes, each with its own render session, resolve to Medium.
        var match = await WaitForMatchAsync(provider, FakePlayer.AppUserModelId, AudioMatchConfidence.Medium, ct);
        Assert.Equal(AudioMatchConfidence.Medium, match.Confidence);
        Assert.True(match.AudioSessionIds.Count >= 2, $"Expected >= 2 sessions, got {match.AudioSessionIds.Count}.");

        try
        {
            Assert.True(await provider.TrySetVolumeAsync(match, 0.5f, ct));
        }
        finally
        {
            await provider.TrySetVolumeAsync(match, 1.0f, ct);
        }
    }

    [Fact]
    public async Task DefaultRenderDeviceChangeReattachesManagerAndKeepsMatching()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var ct = cts.Token;

        await using var provider = new CoreAudioSessionProvider();
        await using var player = await TestPlayerHost.StartAsync(ct);

        // Prime: match once so the endpoint/manager are attached and the initial generation is recorded.
        var before = await WaitForMatchAsync(provider, FakePlayer.AppUserModelId, AudioMatchConfidence.High, ct);
        Assert.Single(before.AudioSessionIds);
        var generationBefore = provider.EndpointGeneration;
        Assert.True(generationBefore >= 1, $"Expected an attached endpoint, generation was {generationBefore}.");

        // Simulate the default-render-device change notification: run the exact re-attach path and await it.
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? _, EventArgs __) => changed.TrySetResult();
        provider.Changed += OnChanged;
        try
        {
            await provider.ForceDefaultRenderDeviceChangedForTestsAsync();
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        }
        finally
        {
            provider.Changed -= OnChanged;
        }

        // The manager must have re-bound to a fresh endpoint (generation advanced), not kept the stale one.
        Assert.True(
            provider.EndpointGeneration > generationBefore,
            $"Re-attach did not re-bind the manager: generation stayed at {provider.EndpointGeneration}.");

        // A follow-up match must enumerate the (re-attached) endpoint and still find the session, and volume/mute
        // must still round-trip through the re-bound manager.
        var after = await WaitForMatchAsync(provider, FakePlayer.AppUserModelId, AudioMatchConfidence.High, ct);
        Assert.Single(after.AudioSessionIds);

        try
        {
            Assert.True(await provider.TrySetVolumeAsync(after, 0.4f, ct), "Volume set after re-attach should succeed.");
            var volume = await provider.GetVolumeAsync(after, ct);
            Assert.NotNull(volume);
            Assert.True(Math.Abs(volume!.Value - 0.4f) < 0.02f, $"Volume after re-attach was {volume}.");
        }
        finally
        {
            await provider.TrySetVolumeAsync(after, 1.0f, ct);
            await provider.TrySetMuteAsync(after, false, ct);
        }
    }

    private static async Task<AudioMatch> WaitForMatchAsync(
        CoreAudioSessionProvider provider,
        string sourceAppId,
        AudioMatchConfidence expected,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + Timeout;
        AudioMatch match = AudioMatch.NoMatch("not evaluated");
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            match = await provider.MatchAsync(sourceAppId, ct);
            if (match.Confidence == expected)
            {
                return match;
            }

            await Task.Delay(250, ct);
        }

        throw new TimeoutException($"'{sourceAppId}' did not reach {expected} within {Timeout}. Last: {match.Confidence} ({match.Explanation}).");
    }
}
