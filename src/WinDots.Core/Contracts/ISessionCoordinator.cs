namespace WinDots.Core.Contracts;

public enum SelectionReason
{
    None,
    PinnedByUser,
    PreferredPlayer,
    Playing,
    RecentActivity,
    SystemCurrent,
    Paused,
    OnlyCandidate,
}

/// <summary>Chooses the active session by the policy in _docs/05-architecture.md. Implemented in Milestone 3.</summary>
public interface ISessionCoordinator
{
    IMediaSession? Active { get; }

    SelectionReason Reason { get; }

    void Pin(string sessionId);

    void ClearPin();

    event EventHandler? ActiveChanged;
}
