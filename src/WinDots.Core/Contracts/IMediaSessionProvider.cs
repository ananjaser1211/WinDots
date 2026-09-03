namespace WinDots.Core.Contracts;

public sealed class MediaSessionsChangedEventArgs(IReadOnlyList<IMediaSession> sessions) : EventArgs
{
    public IReadOnlyList<IMediaSession> Sessions { get; } = sessions;
}

/// <summary>Observes the set of media sessions available on the system. Events may arrive on any thread.</summary>
public interface IMediaSessionProvider : IAsyncDisposable
{
    IReadOnlyList<IMediaSession> Sessions { get; }

    /// <summary>The session the platform itself considers current, if any.</summary>
    IMediaSession? SystemCurrent { get; }

    event EventHandler<MediaSessionsChangedEventArgs>? SessionsChanged;

    event EventHandler? SystemCurrentChanged;

    Task InitializeAsync(CancellationToken ct);
}
