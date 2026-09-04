using WinDots.Core.Contracts;

namespace WinDots.Core.Tests.Fakes;

/// <summary>Deterministic <see cref="IMediaSessionProvider"/> whose session set and current marker are set by tests.</summary>
public sealed class FakeMediaSessionProvider : IMediaSessionProvider
{
    private List<IMediaSession> _sessions = new();

    public IReadOnlyList<IMediaSession> Sessions => _sessions;

    public IMediaSession? SystemCurrent { get; private set; }

    public event EventHandler<MediaSessionsChangedEventArgs>? SessionsChanged;

    public event EventHandler? SystemCurrentChanged;

    public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Replaces the session set and raises <see cref="SessionsChanged"/>.</summary>
    public void SetSessions(params IMediaSession[] sessions)
    {
        _sessions = new List<IMediaSession>(sessions);
        SessionsChanged?.Invoke(this, new MediaSessionsChangedEventArgs(_sessions));
    }

    /// <summary>Sets the platform-current session and raises <see cref="SystemCurrentChanged"/>.</summary>
    public void SetSystemCurrent(IMediaSession? current)
    {
        SystemCurrent = current;
        SystemCurrentChanged?.Invoke(this, EventArgs.Empty);
    }
}
