using WinDots.Core.Contracts;
using WinDots.Core.Media;
using WinDots.Core.Tests.Fakes;

namespace WinDots.Core.Tests.Media;

public class SessionCoordinatorSourceRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static FakeMediaSession Session(
        string id,
        string app,
        string display,
        MediaKind kind = MediaKind.Unknown,
        string? title = "Song",
        string[]? artists = null,
        string? album = null,
        PlaybackState state = PlaybackState.Playing)
    {
        MediaSnapshot snapshot = MediaSnapshot.Empty(id, app, display, Now) with
        {
            Kind = kind,
            Title = title,
            Artists = artists ?? Array.Empty<string>(),
            Album = album,
            State = state,
        };
        return new FakeMediaSession(snapshot);
    }

    private static MediaOptions Options(SourceMode mode = SourceMode.Tracked) => new()
    {
        SourceMode = mode,
        SourceRules = SourceRule.Defaults,
    };

    private static SessionCoordinator Coordinator(FakeMediaSessionProvider provider, MediaOptions options) =>
        new(provider, options, () => Now);

    [Fact]
    public void NeverSourceExcludedEntirely()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession discord = Session("d#0", "Discord.exe", "Discord", kind: MediaKind.Music);
        FakeMediaSession spotify = Session("s#0", "Spotify.exe", "Spotify");
        provider.SetSessions(discord, spotify);
        using SessionCoordinator c = Coordinator(provider, Options());

        Assert.DoesNotContain(discord, c.Candidates);
        Assert.Contains(spotify, c.Candidates);
    }

    [Fact]
    public void AlwaysSourceKeptEvenWithoutMusicSignals()
    {
        FakeMediaSessionProvider provider = new();
        // No artist, album, kind, or length: an Auto source would be rejected, but Always keeps it.
        FakeMediaSession spotify = Session("s#0", "Spotify.exe", "Spotify", kind: MediaKind.Unknown, title: "Podcast | Show");
        provider.SetSessions(spotify);
        using SessionCoordinator c = Coordinator(provider, Options());

        Assert.Contains(spotify, c.Candidates);
        Assert.True(c.Verdicts[spotify.Id].IsMusic);
    }

    [Fact]
    public void TrackedModeDropsAutoSourceRejectedByDetector()
    {
        FakeMediaSessionProvider provider = new();
        // A Chrome (Auto) video: piped title, no artist/album -> not music -> dropped in Tracked mode.
        FakeMediaSession video = Session("c#0", "Chrome", "Chrome", kind: MediaKind.Video, title: "Clip | Channel");
        provider.SetSessions(video);
        using SessionCoordinator c = Coordinator(provider, Options());

        Assert.Empty(c.Candidates);
        Assert.Null(c.Active);
    }

    [Fact]
    public void TrackedModeKeepsAutoSourceAcceptedByDetector()
    {
        FakeMediaSessionProvider provider = new();
        // A Chrome (Auto) YouTube Music tab: artist + album + track length -> music -> kept.
        FakeMediaSession track = Session(
            "c#0",
            "Chrome",
            "Chrome",
            artists: new[] { "Artist" },
            album: "Album");
        track.Push(track.Current with
        {
            Timeline = new Timeline(TimeSpan.Zero, TimeSpan.FromMinutes(4), TimeSpan.Zero, Now, 1.0),
        });
        provider.SetSessions(track);
        using SessionCoordinator c = Coordinator(provider, Options());

        Assert.Contains(track, c.Candidates);
        Assert.True(c.Verdicts[track.Id].IsMusic);
    }

    [Fact]
    public void AllModeKeepsRejectedAutoSourceButStillDropsNever()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession video = Session("c#0", "Chrome", "Chrome", kind: MediaKind.Video, title: "Clip | Channel");
        FakeMediaSession discord = Session("d#0", "Discord.exe", "Discord");
        provider.SetSessions(video, discord);
        using SessionCoordinator c = Coordinator(provider, Options(SourceMode.All));

        Assert.Contains(video, c.Candidates);
        Assert.DoesNotContain(discord, c.Candidates);
    }

    [Fact]
    public void ShowAllSourcesOverrideRevealsRejectedAutoSource()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession video = Session("c#0", "Chrome", "Chrome", kind: MediaKind.Video, title: "Clip | Channel");
        provider.SetSessions(video);
        using SessionCoordinator c = Coordinator(provider, Options());

        Assert.Empty(c.Candidates);

        int candidatesRaised = 0;
        int overrideRaised = 0;
        c.CandidatesChanged += (_, _) => candidatesRaised++;
        c.ShowAllSourcesChanged += (_, _) => overrideRaised++;

        c.ShowAllSources = true;

        Assert.Contains(video, c.Candidates);
        Assert.Equal(1, candidatesRaised);
        Assert.Equal(1, overrideRaised);

        c.ShowAllSources = false;
        Assert.Empty(c.Candidates);
    }

    [Fact]
    public void ShowAllSourcesStillExcludesNever()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession discord = Session("d#0", "Discord.exe", "Discord", kind: MediaKind.Music);
        provider.SetSessions(discord);
        using SessionCoordinator c = Coordinator(provider, Options());

        c.ShowAllSources = true;
        Assert.DoesNotContain(discord, c.Candidates);
    }

    [Fact]
    public void VerdictsExposedForCandidates()
    {
        FakeMediaSessionProvider provider = new();
        FakeMediaSession spotify = Session("s#0", "Spotify.exe", "Spotify");
        provider.SetSessions(spotify);
        using SessionCoordinator c = Coordinator(provider, Options());

        Assert.True(c.Verdicts.ContainsKey(spotify.Id));
        Assert.Contains("music", c.Verdicts[spotify.Id].Reason, StringComparison.Ordinal);
    }

    // ---- RuleFor -------------------------------------------------------------

    [Fact]
    public void RuleForReturnsFirstMatch()
    {
        MediaOptions options = new()
        {
            SourceRules = new SourceRule[]
            {
                new("Spotify", SourceRuleMode.Always),
                new("Spotify", SourceRuleMode.Never),
            },
        };
        Assert.Equal(SourceRuleMode.Always, options.RuleFor("Spotify.exe", "Spotify"));
    }

    [Fact]
    public void RuleForFallsBackToAuto()
    {
        MediaOptions options = new() { SourceRules = SourceRule.Defaults };
        Assert.Equal(SourceRuleMode.Auto, options.RuleFor("com.unknown.player", "Unknown"));
    }

    [Fact]
    public void RuleForMatchesDefaultsByDisplayName()
    {
        MediaOptions options = new() { SourceRules = SourceRule.Defaults };
        Assert.Equal(SourceRuleMode.Always, options.RuleFor("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify"));
        Assert.Equal(SourceRuleMode.Never, options.RuleFor("Microsoft.Teams", "Microsoft Teams"));
    }
}
