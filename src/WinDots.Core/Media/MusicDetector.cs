using System.Text.RegularExpressions;

namespace WinDots.Core.Media;

/// <summary>
/// The verdict for a single media snapshot: the computed <see cref="Score"/>, whether it is treated as music
/// (<see cref="IsMusic"/>), and a short human-readable <see cref="Reason"/> for the chooser tooltip and diagnostics.
/// </summary>
public readonly record struct MusicVerdict(int Score, bool IsMusic, string Reason);

/// <summary>
/// Pure, deterministic music detection for a <see cref="MediaSnapshot"/>. Windows media sessions include every app
/// that publishes transport controls (local video, browser videos, meetings, games); WinDots is a music drawer, so
/// this scores each snapshot against the weights in _docs/10-enhancement-plan.md (E1) and reports a verdict.
/// </summary>
/// <remarks>
/// Weights: <see cref="MediaKind.Music"/> +3; an artist +2; an album +2; a duration of 30 s to 20 min +1, above
/// 45 min -3; a video-style title pattern -2; a source rule of <see cref="SourceRuleMode.Always"/> +10; a rule of
/// <see cref="SourceRuleMode.Never"/> excludes it outright. A score of at least <see cref="MusicThreshold"/> is music.
/// </remarks>
public static class MusicDetector
{
    /// <summary>A score of at least this value is treated as music.</summary>
    public const int MusicThreshold = 3;

    internal const int MusicKindWeight = 3;
    internal const int ArtistWeight = 2;
    internal const int AlbumWeight = 2;
    internal const int TrackLengthWeight = 1;
    internal const int VeryLongPenalty = -3;
    internal const int VideoTitlePenalty = -2;
    internal const int AlwaysRuleWeight = 10;

    internal static readonly TimeSpan MinTrackLength = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan MaxTrackLength = TimeSpan.FromMinutes(20);
    internal static readonly TimeSpan VeryLongThreshold = TimeSpan.FromMinutes(45);

    // Season/episode markers such as "S01E02" (one or two digits for each part).
    private static readonly Regex SeasonEpisode = new(@"S\d{1,2}E\d{1,2}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Scores <paramref name="snapshot"/> and returns the verdict. <paramref name="rule"/> is the resolved
    /// <see cref="SourceRuleMode"/> for the snapshot's source, or null when no rule applies (treated as
    /// <see cref="SourceRuleMode.Auto"/>).
    /// </summary>
    public static MusicVerdict Score(MediaSnapshot snapshot, SourceRuleMode? rule = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (rule == SourceRuleMode.Never)
        {
            return new MusicVerdict(int.MinValue, false, "not music: source excluded (Never)");
        }

        int score = 0;
        var positives = new List<string>();
        var negatives = new List<string>();

        if (rule == SourceRuleMode.Always)
        {
            score += AlwaysRuleWeight;
            positives.Add("source rule Always");
        }

        if (snapshot.Kind == MediaKind.Music)
        {
            score += MusicKindWeight;
            positives.Add("music kind");
        }

        if (snapshot.Artists.Count > 0)
        {
            score += ArtistWeight;
            positives.Add("artist");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Album))
        {
            score += AlbumWeight;
            positives.Add("album");
        }

        if (snapshot.Timeline.HasDuration)
        {
            TimeSpan duration = snapshot.Timeline.Duration;
            if (duration > VeryLongThreshold)
            {
                score += VeryLongPenalty;
                negatives.Add("very long (>45m)");
            }
            else if (duration >= MinTrackLength && duration <= MaxTrackLength)
            {
                score += TrackLengthWeight;
                positives.Add("track length");
            }
        }

        if (HasVideoTitlePattern(snapshot.Title))
        {
            score += VideoTitlePenalty;
            negatives.Add("video title");
        }

        bool isMusic = score >= MusicThreshold;
        string reason = BuildReason(isMusic, positives, negatives);
        return new MusicVerdict(score, isMusic, reason);
    }

    /// <summary>True when the title carries a marker typical of video, not music (a pipe, episode/season markers, etc.).</summary>
    internal static bool HasVideoTitlePattern(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        if (title.Contains('|', StringComparison.Ordinal))
        {
            return true;
        }

        if (title.Contains("Episode", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Trailer", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Live stream", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return SeasonEpisode.IsMatch(title);
    }

    private static string BuildReason(bool isMusic, List<string> positives, List<string> negatives)
    {
        if (isMusic)
        {
            return positives.Count == 0 ? "music" : $"music: {string.Join(", ", positives)}";
        }

        var why = new List<string>(negatives);
        if (why.Count == 0)
        {
            why.Add(positives.Count == 0 ? "no music signals" : "weak music signals");
        }

        return $"not music: {string.Join(", ", why)}";
    }
}
