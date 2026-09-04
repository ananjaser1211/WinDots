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
public interface ISessionCoordinator : IDisposable
{
    IMediaSession? Active { get; }

    SelectionReason Reason { get; }

    /// <summary>Sessions after the ignore filter, in ranked order, for the player chooser.</summary>
    IReadOnlyList<IMediaSession> Candidates { get; }

    void Pin(string sessionId);

    void ClearPin();

    event EventHandler? ActiveChanged;

    event EventHandler? CandidatesChanged;
}
