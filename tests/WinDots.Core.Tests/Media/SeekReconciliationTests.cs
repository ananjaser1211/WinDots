using WinDots.Core.Media;

namespace WinDots.Core.Tests.Media;

public class SeekReconciliationTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SuppressesFarUpdatesWithinHoldWindow()
    {
        SeekReconciliation seek = SeekReconciliation.Begin(TimeSpan.FromSeconds(120), T0);

        // A stale report far from the target, still inside the 2s hold window, is not accepted.
        Assert.False(seek.ShouldAccept(TimeSpan.FromSeconds(30), T0 + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void AcceptsUpdatesThatLandNearTarget()
    {
        SeekReconciliation seek = SeekReconciliation.Begin(TimeSpan.FromSeconds(120), T0);

        // Within 3s of the target: the player caught up, so accept.
        Assert.True(seek.ShouldAccept(TimeSpan.FromSeconds(121), T0 + TimeSpan.FromSeconds(0.5)));
        Assert.True(seek.ShouldAccept(TimeSpan.FromSeconds(118), T0 + TimeSpan.FromSeconds(0.5)));
    }

    [Fact]
    public void AcceptsAnyUpdateOnceHoldWindowElapses()
    {
        SeekReconciliation seek = SeekReconciliation.Begin(TimeSpan.FromSeconds(120), T0);

        // After the 2s window, even a far report is accepted (reconcile with reality).
        Assert.True(seek.ShouldAccept(TimeSpan.FromSeconds(30), T0 + TimeSpan.FromSeconds(2)));
        Assert.True(seek.ShouldAccept(TimeSpan.FromSeconds(30), T0 + TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ToleranceBoundaryIsInclusive()
    {
        SeekReconciliation seek = SeekReconciliation.Begin(TimeSpan.FromSeconds(120), T0);

        Assert.True(seek.ShouldAccept(TimeSpan.FromSeconds(123), T0 + TimeSpan.FromSeconds(1)));
        Assert.False(seek.ShouldAccept(TimeSpan.FromSeconds(123.5), T0 + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void CustomHoldAndToleranceAreHonored()
    {
        SeekReconciliation seek = SeekReconciliation.Begin(
            TimeSpan.FromSeconds(60),
            T0,
            hold: TimeSpan.FromSeconds(5),
            tolerance: TimeSpan.FromSeconds(1));

        Assert.False(seek.IsExpired(T0 + TimeSpan.FromSeconds(4)));
        Assert.True(seek.IsExpired(T0 + TimeSpan.FromSeconds(5)));
        Assert.False(seek.ShouldAccept(TimeSpan.FromSeconds(58), T0 + TimeSpan.FromSeconds(4)));
        Assert.True(seek.ShouldAccept(TimeSpan.FromSeconds(60.5), T0 + TimeSpan.FromSeconds(4)));
    }
}
