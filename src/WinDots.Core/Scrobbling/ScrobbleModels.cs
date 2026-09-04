using System.Globalization;

namespace WinDots.Core.Scrobbling;

/// <summary>
/// The identity of a track for scrobbling and de-duplication: artist and title (and album, when present). Normalised
/// case-insensitively so the same track reported with minor casing differences de-dupes. See _docs/10-enhancement-plan.md (E4).
/// </summary>
public sealed record TrackIdentity(string Artist, string Track, string? Album)
{
    /// <summary>A stable, case-insensitive key for de-duplication.</summary>
    public string Key => string.Create(
        CultureInfo.InvariantCulture,
        $"{Norm(Artist)}{Norm(Track)}{Norm(Album ?? string.Empty)}");

    /// <summary>True when there is enough metadata (an artist and a title) to scrobble.</summary>
    public bool IsUsable => !string.IsNullOrWhiteSpace(Artist) && !string.IsNullOrWhiteSpace(Track);

    private static string Norm(string value) => value.Trim().ToLowerInvariant();
}

/// <summary>
/// A single scrobble: the track identity, the whole-second start timestamp used by Last.fm to order plays, and the
/// optional duration. <see cref="Timestamp"/> is the moment playback began, per the Last.fm scrobble contract.
/// </summary>
public sealed record Scrobble(TrackIdentity Identity, DateTimeOffset Timestamp, TimeSpan? Duration)
{
    /// <summary>Unix seconds for the scrobble; the Last.fm API keys plays by this value.</summary>
    public long UnixTimestamp => Timestamp.ToUnixTimeSeconds();

    /// <summary>A queue de-duplication key: identity plus the whole-second timestamp (idempotent submission).</summary>
    public string DedupeKey => string.Create(CultureInfo.InvariantCulture, $"{Identity.Key}@{UnixTimestamp}");
}

/// <summary>An authenticated Last.fm session: the username and the long-lived session key stored in the secret store.</summary>
public sealed record LastFmSession(string Name, string Key);

/// <summary>The public profile fields WinDots shows after sign-in: username, real name, avatar URL, and play count.</summary>
public sealed record LastFmUserInfo(string Name, string? RealName, string? ImageUrl, long? Playcount);

/// <summary>One entry from <c>user.getRecentTracks</c> for the settings page.</summary>
public sealed record RecentTrack(string Artist, string Track, string? Album, string? ImageUrl, bool NowPlaying, DateTimeOffset? PlayedAt);
