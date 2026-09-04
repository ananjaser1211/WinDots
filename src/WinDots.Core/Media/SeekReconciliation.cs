namespace WinDots.Core.Media;

/// <summary>
/// Tracks an in-flight optimistic seek so the UI can show the requested position immediately and ignore stale
/// timeline updates until the player catches up. Pure and UI-free so it can be unit tested; the view-model holds
/// one instance while a seek is pending and discards it once <see cref="ShouldAccept"/> returns true.
/// </summary>
/// <remarks>
/// After a seek the UI displays <see cref="Target"/>. Incoming positions are suppressed (kept at the target) while
/// the hold window is open and the incoming value is farther than <see cref="Tolerance"/> from the target. Once the
/// incoming value lands within tolerance, or the hold window elapses, the real position is accepted again.
/// </remarks>
public readonly record struct SeekReconciliation(TimeSpan Target, DateTimeOffset Deadline, TimeSpan Tolerance)
{
    /// <summary>An incoming position within this distance of the target counts as "landed".</summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromSeconds(3);

    /// <summary>How long the target is held before the real position is accepted regardless of distance.</summary>
    public static readonly TimeSpan DefaultHold = TimeSpan.FromSeconds(2);

    /// <summary>Begins tracking a seek to <paramref name="target"/> issued at <paramref name="now"/>.</summary>
    public static SeekReconciliation Begin(
        TimeSpan target,
        DateTimeOffset now,
        TimeSpan? hold = null,
        TimeSpan? tolerance = null) =>
        new(target, now + (hold ?? DefaultHold), tolerance ?? DefaultTolerance);

    /// <summary>
    /// Decides whether an <paramref name="incoming"/> position observed at <paramref name="now"/> should replace the
    /// displayed target. True once the player has caught up (within <see cref="Tolerance"/>) or the hold window has
    /// elapsed; false while the reported position is still far from the target and the window is open.
    /// </summary>
    public bool ShouldAccept(TimeSpan incoming, DateTimeOffset now)
    {
        if (now >= Deadline)
        {
            return true;
        }

        TimeSpan delta = incoming - Target;
        if (delta < TimeSpan.Zero)
        {
            delta = delta.Negate();
        }

        return delta <= Tolerance;
    }

    /// <summary>True when the hold window has elapsed at <paramref name="now"/>.</summary>
    public bool IsExpired(DateTimeOffset now) => now >= Deadline;
}
