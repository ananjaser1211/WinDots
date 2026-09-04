using WinDots.Core.Contracts;
using WinDots.Core.Media;

namespace WinDots.Core.Tests.Fakes;

/// <summary>Deterministic <see cref="IMediaSession"/> for coordinator tests. Commands are unused here.</summary>
public sealed class FakeMediaSession : IMediaSession
{
    private MediaSnapshot _current;

    public FakeMediaSession(MediaSnapshot snapshot)
    {
        _current = snapshot;
        Id = snapshot.SessionId;
        SourceAppId = snapshot.SourceAppId;
    }

    public string Id { get; }

    public string SourceAppId { get; }

    public MediaSnapshot Current => _current;

    /// <summary>Number of live subscribers on <see cref="Updated"/>, for leak assertions.</summary>
    public int SubscriberCount { get; private set; }

    private event EventHandler<MediaSnapshot>? UpdatedCore;

    public event EventHandler<MediaSnapshot>? Updated
    {
        add
        {
            UpdatedCore += value;
            SubscriberCount++;
        }
        remove
        {
            UpdatedCore -= value;
            SubscriberCount--;
        }
    }

    public void Push(MediaSnapshot snapshot)
    {
        _current = snapshot;
        UpdatedCore?.Invoke(this, snapshot);
    }

    public Task<CommandResult> TryPlayPauseAsync(CancellationToken ct) => throw new NotSupportedException();

    public Task<CommandResult> TryNextAsync(CancellationToken ct) => throw new NotSupportedException();

    public Task<CommandResult> TryPreviousAsync(CancellationToken ct) => throw new NotSupportedException();

    public Task<CommandResult> TrySeekAsync(TimeSpan position, CancellationToken ct) => throw new NotSupportedException();

    public Task<CommandResult> TrySetShuffleAsync(bool enabled, CancellationToken ct) => throw new NotSupportedException();

    public Task<CommandResult> TrySetRepeatAsync(RepeatMode mode, CancellationToken ct) => throw new NotSupportedException();

    public Task<ArtworkResult> LoadArtworkAsync(int maxBytes, CancellationToken ct) => throw new NotSupportedException();
}
