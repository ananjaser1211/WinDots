namespace WinDots.Core.Media;

/// <summary>
/// Pure policies that clean up what media sessions report. Adapters apply them when building a snapshot so the
/// rest of the system can trust the values; the coordinator uses <see cref="IsStale"/> to rank leftovers last.
/// Rules are documented in _docs/05-architecture.md ("Snapshot normalisation").
/// </summary>
public static class SessionQuality
{
    /// <summary>Reported timestamps at or before this instant are treated as "never set".</summary>
    public static DateTimeOffset UnsetTimestampCeiling { get; } = DateTimeOffset.UnixEpoch;

    /// <summary>
    /// A session with no title, artist, or album that is not actively playing. Such sessions are typically
    /// leftovers (a viewer that finished, a browser tab that lost its media element) and should never be preferred.
    /// </summary>
    public static bool IsStale(MediaSnapshot snapshot) =>
        !snapshot.HasMetadata && snapshot.State is not (PlaybackState.Playing or PlaybackState.Changing);

    /// <summary>
    /// Playback rate to store in a snapshot. Players commonly report null, 0, or NaN while playing (Chromium reports 0);
    /// none of those is a usable rate, so anything that is not a finite positive number becomes 1.0.
    /// </summary>
    public static double NormalizeRate(double? reported) =>
        reported is { } rate && double.IsFinite(rate) && rate > 0 ? rate : 1.0;

    /// <summary>
    /// Timestamp at which a timeline position was valid. A missing timestamp (default or epoch) or one that lies in
    /// the future relative to the capture clock is replaced by <paramref name="capturedAt"/> so that interpolation
    /// starts from the capture instant instead of jumping to the end or standing still.
    /// </summary>
    public static DateTimeOffset NormalizeLastUpdated(DateTimeOffset reported, DateTimeOffset capturedAt) =>
        reported <= UnsetTimestampCeiling || reported > capturedAt ? capturedAt : reported;
}
