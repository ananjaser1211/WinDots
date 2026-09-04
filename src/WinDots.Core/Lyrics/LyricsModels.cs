namespace WinDots.Core.Lyrics;

/// <summary>
/// The identity of a track to look lyrics up for. Only the fields a lyrics provider needs; no session or app state.
/// See _docs/10-enhancement-plan.md (E3) and _docs/privacy.md (the LRCLIB row lists exactly these four fields).
/// </summary>
public sealed record LyricsQuery(string Title, IReadOnlyList<string> Artists, string? Album, TimeSpan? Duration)
{
    /// <summary>The artists joined for a request or display; empty when unknown.</summary>
    public string ArtistText => Artists.Count > 0 ? string.Join(", ", Artists) : string.Empty;

    /// <summary>True when there is enough metadata (a title and at least one artist) to attempt a lookup.</summary>
    public bool IsUsable => !string.IsNullOrWhiteSpace(Title) && Artists.Count > 0;
}

/// <summary>
/// One lyric line. <see cref="At"/> is the synced timestamp from the track start, or null for a plain (unsynced) line.
/// </summary>
public sealed record LyricsLine(TimeSpan? At, string Text);

/// <summary>
/// A lyrics lookup result: the provider name, an attribution URL, the lines (synced or plain), and whether the lines
/// carry timestamps. A provider returns null (never an empty result) when it finds nothing.
/// </summary>
public sealed record LyricsResult(
    string Provider,
    string? AttributionUrl,
    IReadOnlyList<LyricsLine> Lines,
    bool IsSynced);

/// <summary>A source of lyrics. Implementations are keyless where possible and enforce the network rules in privacy.md.</summary>
public interface ILyricsProvider
{
    /// <summary>Looks up lyrics for <paramref name="query"/>. Returns null when nothing is found or the lookup fails.</summary>
    Task<LyricsResult?> LookupAsync(LyricsQuery query, CancellationToken ct);
}
