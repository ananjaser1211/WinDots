using WinDots.Core.Media;

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

    /// <summary>Sessions after the ignore, source-rule, and music filters, in ranked order, for the player chooser.</summary>
    IReadOnlyList<IMediaSession> Candidates { get; }

    /// <summary>The <see cref="MusicVerdict"/> for each candidate, keyed by session id, for the chooser and diagnostics.</summary>
    IReadOnlyDictionary<string, MusicVerdict> Verdicts { get; }

    /// <summary>
    /// Runtime override for a one-off look at every source: when true, <see cref="SourceMode.Tracked"/> filtering and
    /// the music detector are bypassed (only <see cref="SourceRuleMode.Never"/> sources stay excluded). Drives the
    /// chooser's "Show all sources" toggle. Setting it re-evaluates and raises <see cref="ShowAllSourcesChanged"/>.
    /// </summary>
    bool ShowAllSources { get; set; }

    void Pin(string sessionId);

    void ClearPin();

    event EventHandler? ActiveChanged;

    event EventHandler? CandidatesChanged;

    event EventHandler? ShowAllSourcesChanged;
}
