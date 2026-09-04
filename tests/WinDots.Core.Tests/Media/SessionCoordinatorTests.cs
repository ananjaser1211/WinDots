using WinDots.Core.Contracts;
using WinDots.Core.Media;
using WinDots.Core.Tests.Fakes;

namespace WinDots.Core.Tests.Media;

public class SessionCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    // "Old" is well outside the default 30 s recent-activity window.
    private static readonly DateTimeOffset Old = Now - TimeSpan.FromMinutes(5);

    private static FakeMediaSession Session(
        string id,
        PlaybackState state = PlaybackState.Stopped,
        string? title = "Song",
        string app = "app",
        string display = "App",
        DateTimeOffset? capturedAt = null)
    {
        MediaSnapshot snapshot = MediaSnapshot.Empty(id, app, display, capturedAt ?? Old) with
        {
            State = state,
            Title = title,
        };
        return new FakeMediaSession(snapshot);
    }

    private static SessionCoordinator Coordinator(FakeMediaSessionProvider provider, MediaOptions? options = null) =>
        new(provider, options ?? new MediaOptions(), () => Now);

    // ---- Scoring rows -------------------------------------------------------

    [Fact]
    public void PlayingBeatsPaused()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession paused = Session("a#0", PlaybackState.Paused);
        FakeMediaSession playing = Session("b#0", PlaybackState.Playing);
        provider.SetSessions(paused, playing);
        using SessionCoordinator c = Coordinator(provider);

        Assert.Same(playing, c.Active);
        Assert.Equal(SelectionReason.Playing, c.Reason);
    }

    [Fact]
    public void PausedBeatsStopped()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession stopped = Session("a#0", PlaybackState.Stopped);
        FakeMediaSession paused = Session("b#0", PlaybackState.Paused);
        provider.SetSessions(stopped, paused);
        using SessionCoordinator c = Coordinator(provider);

        Assert.Same(paused, c.Active);
        Assert.Equal(SelectionReason.Paused, c.Reason);
    }

    [Fact]
    public void RecentActivityBeatsSystemCurrent()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession recent = Session("a#0", PlaybackState.Paused, capturedAt: Now);
        FakeMediaSession system = Session("b#0", PlaybackState.Paused, capturedAt: Old);
        provider.SetSessions(recent, system);
        provider.SetSystemCurrent(system);
        using SessionCoordinator c = Coordinator(provider);

        // recent: paused 20 + recent 100 = 120; system: paused 20 + system 50 = 70.
        Assert.Same(recent, c.Active);
        Assert.Equal(SelectionReason.RecentActivity, c.Reason);
    }

    [Fact]
    public void SystemCurrentScoresFifty()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession plain = Session("a#0", PlaybackState.Stopped);
        FakeMediaSession system = Session("b#0", PlaybackState.Stopped);
        provider.SetSessions(plain, system);
        provider.SetSystemCurrent(system);
        using SessionCoordinator c = Coordinator(provider);

        Assert.Same(system, c.Active);
        Assert.Equal(SelectionReason.SystemCurrent, c.Reason);
    }

    [Fact]
    public void PreferredPlayerBeatsPlaying()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession playing = Session("a#0", PlaybackState.Playing, app: "other", display: "Other");
        FakeMediaSession preferred = Session("b#0", PlaybackState.Stopped, app: "Spotify.exe", display: "Spotify");
        provider.SetSessions(playing, preferred);
        using SessionCoordinator c = Coordinator(provider, new MediaOptions { PreferredPlayer = "Spotify" });

        // preferred: 400; playing: 300.
        Assert.Same(preferred, c.Active);
        Assert.Equal(SelectionReason.PreferredPlayer, c.Reason);
    }

    [Fact]
    public void PinnedBeatsPreferred()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession preferred = Session("a#0", PlaybackState.Playing, app: "Spotify", display: "Spotify");
        FakeMediaSession other = Session("b#0", PlaybackState.Stopped);
        provider.SetSessions(preferred, other);
        using SessionCoordinator c = Coordinator(provider, new MediaOptions { PreferredPlayer = "Spotify" });

        c.Pin("b#0");

        Assert.Same(other, c.Active);
        Assert.Equal(SelectionReason.PinnedByUser, c.Reason);
    }

    [Fact]
    public void StoppedWithMetadataIsOnlyCandidateReason()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession only = Session("a#0", PlaybackState.Stopped);
        provider.SetSessions(only);
        using SessionCoordinator c = Coordinator(provider);

        Assert.Same(only, c.Active);
        Assert.Equal(SelectionReason.OnlyCandidate, c.Reason);
    }

    [Fact]
    public void NoSessionsMeansNoActive()
    {
        FakeMediaSessionProvider provider = new();
        using SessionCoordinator c = Coordinator(provider);

        Assert.Null(c.Active);
        Assert.Equal(SelectionReason.None, c.Reason);
        Assert.Empty(c.Candidates);
    }

    // ---- Tie-break ----------------------------------------------------------

    [Fact]
    public void TieBreaksByLatestCapturedAt()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession older = Session("a#0", PlaybackState.Paused, capturedAt: Now - TimeSpan.FromSeconds(10));
        FakeMediaSession newer = Session("b#0", PlaybackState.Paused, capturedAt: Now - TimeSpan.FromSeconds(2));
        provider.SetSessions(older, newer);
        using SessionCoordinator c = Coordinator(provider);

        Assert.Same(newer, c.Active);
    }

    [Fact]
    public void TieBreaksByIdWhenCapturedAtEqual()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession b = Session("b#0", PlaybackState.Paused, capturedAt: Now);
        FakeMediaSession a = Session("a#0", PlaybackState.Paused, capturedAt: Now);
        provider.SetSessions(b, a);
        using SessionCoordinator c = Coordinator(provider);

        Assert.Same(a, c.Active);
        Assert.Equal(new[] { a, b }, c.Candidates);
    }

    // ---- Stale --------------------------------------------------------------

    [Fact]
    public void StaleRankedBelowEveryNonStaleSession()
    {
        FakeMediaSessionProvider provider = new();
        // Stale: no metadata, not playing. Even though playing-scored high normally, staleness demotes it.
        FakeMediaSession stalePlaying = Session("a#0", PlaybackState.Paused, title: null, capturedAt: Now);
        FakeMediaSession nonStale = Session("b#0", PlaybackState.Stopped, capturedAt: Old);
        provider.SetSessions(stalePlaying, nonStale);
        using SessionCoordinator c = Coordinator(provider);

        Assert.Same(nonStale, c.Active);
        Assert.Equal(new[] { nonStale, stalePlaying }, c.Candidates);
    }

    [Fact]
    public void StaleChosenOnlyWhenNothingElse()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession stale = Session("a#0", PlaybackState.Paused, title: null, capturedAt: Old);
        provider.SetSessions(stale);
        using SessionCoordinator c = Coordinator(provider);

        Assert.Same(stale, c.Active);
    }

    // ---- Pin behaviour ------------------------------------------------------

    [Fact]
    public void PinSticksAcrossReevaluation()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession pinned = Session("a#0", PlaybackState.Stopped);
        FakeMediaSession loud = Session("b#0", PlaybackState.Playing);
        provider.SetSessions(pinned, loud);
        using SessionCoordinator c = Coordinator(provider);

        c.Pin("a#0");
        Assert.Same(pinned, c.Active);

        // A new playing session must not steal the pin.
        FakeMediaSession loud2 = Session("c#0", PlaybackState.Playing);
        provider.SetSessions(pinned, loud, loud2);
        Assert.Same(pinned, c.Active);
        Assert.Equal(SelectionReason.PinnedByUser, c.Reason);
    }

    [Fact]
    public void PinVanishesFallsBackToAutomatic()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession pinned = Session("a#0", PlaybackState.Stopped);
        FakeMediaSession playing = Session("b#0", PlaybackState.Playing);
        provider.SetSessions(pinned, playing);
        using SessionCoordinator c = Coordinator(provider);

        c.Pin("a#0");
        Assert.Same(pinned, c.Active);

        provider.SetSessions(playing);
        Assert.Same(playing, c.Active);
        Assert.Equal(SelectionReason.Playing, c.Reason);
    }

    [Fact]
    public void ClearPinReturnsToAutomatic()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession pinned = Session("a#0", PlaybackState.Stopped);
        FakeMediaSession playing = Session("b#0", PlaybackState.Playing);
        provider.SetSessions(pinned, playing);
        using SessionCoordinator c = Coordinator(provider);

        c.Pin("a#0");
        Assert.Same(pinned, c.Active);

        c.ClearPin();
        Assert.Same(playing, c.Active);
        Assert.Equal(SelectionReason.Playing, c.Reason);
    }

    [Fact]
    public void PinToUnknownIdIsIgnoredUntilItAppears()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession playing = Session("b#0", PlaybackState.Playing);
        provider.SetSessions(playing);
        using SessionCoordinator c = Coordinator(provider);

        c.Pin("ghost#0");
        // No such session: pin cleared, automatic selection stands.
        Assert.Same(playing, c.Active);
        Assert.Equal(SelectionReason.Playing, c.Reason);
    }

    // ---- Ignore filter ------------------------------------------------------

    [Fact]
    public void IgnoredPlayersExcludedEntirely()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession ignored = Session("a#0", PlaybackState.Playing, app: "Widget.exe", display: "Widget");
        FakeMediaSession kept = Session("b#0", PlaybackState.Stopped);
        provider.SetSessions(ignored, kept);
        using SessionCoordinator c = Coordinator(provider, new MediaOptions { IgnoredPlayers = new[] { "Widget" } });

        Assert.Same(kept, c.Active);
        Assert.DoesNotContain(ignored, c.Candidates);
        Assert.Single(c.Candidates);
    }

    [Fact]
    public void AllSessionsIgnoredMeansNoActive()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession ignored = Session("a#0", PlaybackState.Playing, app: "Widget.exe", display: "Widget");
        provider.SetSessions(ignored);
        using SessionCoordinator c = Coordinator(provider, new MediaOptions { IgnoredPlayers = new[] { "Widget" } });

        Assert.Null(c.Active);
        Assert.Equal(SelectionReason.None, c.Reason);
    }

    // ---- Candidates ---------------------------------------------------------

    [Fact]
    public void CandidatesAreInRankedOrder()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession paused = Session("a#0", PlaybackState.Paused, capturedAt: Old);
        FakeMediaSession playing = Session("b#0", PlaybackState.Playing, capturedAt: Old);
        FakeMediaSession stopped = Session("c#0", PlaybackState.Stopped, capturedAt: Old);
        provider.SetSessions(paused, playing, stopped);
        using SessionCoordinator c = Coordinator(provider);

        Assert.Equal(new[] { playing, paused, stopped }, c.Candidates);
    }

    [Fact]
    public void CandidatesChangedRaisedWhenOrderChanges()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession a = Session("a#0", PlaybackState.Stopped, capturedAt: Old);
        provider.SetSessions(a);
        using SessionCoordinator c = Coordinator(provider);

        int raised = 0;
        c.CandidatesChanged += (_, _) => raised++;

        FakeMediaSession b = Session("b#0", PlaybackState.Playing, capturedAt: Old);
        provider.SetSessions(a, b);

        Assert.Equal(1, raised);
    }

    // ---- Alias lookup -------------------------------------------------------

    [Fact]
    public void AliasForMatchesExactAumid()
    {
        MediaOptions options = new()
        {
            PlayerAliases = new Dictionary<string, string> { ["Spotify.exe"] = "Spotify" },
        };
        Assert.Equal("Spotify", options.AliasFor("Spotify.exe", "spotify.exe"));
    }

    [Fact]
    public void AliasForMatchesCaseInsensitiveSubstring()
    {
        MediaOptions options = new()
        {
            PlayerAliases = new Dictionary<string, string> { ["chrome"] = "Google Chrome" },
        };
        Assert.Equal("Google Chrome", options.AliasFor("Google.Chrome_abc!App", "Chrome"));
    }

    [Fact]
    public void AliasForMatchesAgainstDisplayName()
    {
        MediaOptions options = new()
        {
            PlayerAliases = new Dictionary<string, string> { ["music"] = "Apple Music" },
        };
        Assert.Equal("Apple Music", options.AliasFor("AppleInc.AppleMusic!App", "Music Player"));
    }

    [Fact]
    public void AliasForFallsBackToDisplayName()
    {
        MediaOptions options = new()
        {
            PlayerAliases = new Dictionary<string, string> { ["spotify"] = "Spotify" },
        };
        Assert.Equal("Foobar", options.AliasFor("com.foobar", "Foobar"));
    }

    // ---- Subscription lifetime ---------------------------------------------

    [Fact]
    public void HandlersUnsubscribedWhenSessionRemoved()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession a = Session("a#0", PlaybackState.Playing);
        FakeMediaSession b = Session("b#0", PlaybackState.Stopped);
        provider.SetSessions(a, b);
        using SessionCoordinator c = Coordinator(provider);

        Assert.Equal(1, a.SubscriberCount);
        Assert.Equal(1, b.SubscriberCount);

        provider.SetSessions(a);

        Assert.Equal(1, a.SubscriberCount);
        Assert.Equal(0, b.SubscriberCount);
    }

    [Fact]
    public void DisposeUnsubscribesEverything()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession a = Session("a#0", PlaybackState.Playing);
        SessionCoordinator c = Coordinator(provider);
        provider.SetSessions(a);
        Assert.Equal(1, a.SubscriberCount);

        c.Dispose();

        Assert.Equal(0, a.SubscriberCount);

        // Provider events after dispose are inert.
        provider.SetSessions(a);
        Assert.Equal(0, a.SubscriberCount);
    }

    [Fact]
    public void SessionUpdateTriggersReevaluation()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession a = Session("a#0", PlaybackState.Paused);
        FakeMediaSession b = Session("b#0", PlaybackState.Paused);
        provider.SetSessions(a, b);
        using SessionCoordinator c = Coordinator(provider);

        // a and b tie on score; a wins by id. Now make b playing.
        b.Push(b.Current with { State = PlaybackState.Playing });

        Assert.Same(b, c.Active);
        Assert.Equal(SelectionReason.Playing, c.Reason);
    }

    // ---- ActiveChanged semantics -------------------------------------------

    [Fact]
    public void ActiveChangedNotRaisedWhenNothingChanges()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession a = Session("a#0", PlaybackState.Playing);
        provider.SetSessions(a);
        using SessionCoordinator c = Coordinator(provider);

        int raised = 0;
        c.ActiveChanged += (_, _) => raised++;

        // Re-raise SessionsChanged with the same set: active and reason unchanged.
        provider.SetSessions(a);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void ActiveChangedRaisedWhenActiveChanges()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession a = Session("a#0", PlaybackState.Paused);
        provider.SetSessions(a);
        using SessionCoordinator c = Coordinator(provider);

        int raised = 0;
        c.ActiveChanged += (_, _) => raised++;

        FakeMediaSession b = Session("b#0", PlaybackState.Playing);
        provider.SetSessions(a, b);

        Assert.Equal(1, raised);
        Assert.Same(b, c.Active);
    }

    [Fact]
    public void ActiveChangedRaisedWhenOnlyReasonChanges()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession a = Session("a#0", PlaybackState.Paused);
        provider.SetSessions(a);
        using SessionCoordinator c = Coordinator(provider);
        Assert.Equal(SelectionReason.Paused, c.Reason);

        int raised = 0;
        c.ActiveChanged += (_, _) => raised++;

        // Same active session, but now it becomes playing: reason changes Paused -> Playing.
        a.Push(a.Current with { State = PlaybackState.Playing });

        Assert.Equal(1, raised);
        Assert.Same(a, c.Active);
        Assert.Equal(SelectionReason.Playing, c.Reason);
    }
}
