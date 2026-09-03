namespace WinDots.Core.Media;

/// <summary>
/// Immutable view of a media session at one instant. Produced by adapters, consumed by the coordinator and UI.
/// </summary>
public sealed record MediaSnapshot(
    string SessionId,
    string SourceAppId,
    string SourceDisplayName,
    string? Title,
    IReadOnlyList<string> Artists,
    string? Album,
    MediaKind Kind,
    PlaybackState State,
    Capabilities Caps,
    Timeline Timeline,
    bool? Shuffle,
    RepeatMode? Repeat,
    string? ArtworkKey,
    DateTimeOffset CapturedAt)
{
    public static MediaSnapshot Empty(string sessionId, string sourceAppId, string sourceDisplayName, DateTimeOffset now) =>
        new(
            sessionId,
            sourceAppId,
            sourceDisplayName,
            Title: null,
            Artists: Array.Empty<string>(),
            Album: null,
            Kind: MediaKind.Unknown,
            State: PlaybackState.Unknown,
            Caps: Capabilities.None,
            Timeline: Timeline.Empty,
            Shuffle: null,
            Repeat: null,
            ArtworkKey: null,
            CapturedAt: now);

    public bool HasMetadata => !string.IsNullOrWhiteSpace(Title) || Artists.Count > 0 || !string.IsNullOrWhiteSpace(Album);

    public bool Can(Capabilities capability) => (Caps & capability) == capability;
}
