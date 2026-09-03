using WinDots.Core.Media;

namespace WinDots.Core.Contracts;

/// <summary>A live media session. <see cref="Current"/> is always a complete immutable snapshot.</summary>
public interface IMediaSession
{
    /// <summary>Stable for the lifetime of the session as seen by the provider.</summary>
    string Id { get; }

    /// <summary>Application user model ID or executable name of the source player.</summary>
    string SourceAppId { get; }

    MediaSnapshot Current { get; }

    event EventHandler<MediaSnapshot>? Updated;

    Task<CommandResult> TryPlayPauseAsync(CancellationToken ct);

    Task<CommandResult> TryNextAsync(CancellationToken ct);

    Task<CommandResult> TryPreviousAsync(CancellationToken ct);

    Task<CommandResult> TrySeekAsync(TimeSpan position, CancellationToken ct);

    Task<CommandResult> TrySetShuffleAsync(bool enabled, CancellationToken ct);

    Task<CommandResult> TrySetRepeatAsync(RepeatMode mode, CancellationToken ct);

    /// <summary>Loads artwork bytes bounded by <paramref name="maxBytes"/>. Never throws for missing or malformed artwork.</summary>
    Task<ArtworkResult> LoadArtworkAsync(int maxBytes, CancellationToken ct);
}
