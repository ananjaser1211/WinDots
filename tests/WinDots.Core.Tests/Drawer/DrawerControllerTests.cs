using WinDots.Core.Contracts;
using WinDots.Core.Drawer;

namespace WinDots.Core.Tests.Drawer;

public class DrawerControllerTests
{
    private const double Height = 300;

    private static PointerSample At(double y, double ms, double x = 0) => new(x, y, TimeSpan.FromMilliseconds(ms));

    private static DrawerController Create(bool reducedMotion = false) =>
        new(new DrawerOptions(Height, ReducedMotion: reducedMotion));

    private static List<DrawerTransition> Record(DrawerController c)
    {
        var log = new List<DrawerTransition>();
        c.Transition += (_, t) => log.Add(t);
        return log;
    }

    /// <summary>Drags from y=0 to <paramref name="y"/> over <paramref name="ms"/> milliseconds in ten steps and releases.</summary>
    private static void Drag(DrawerController c, double y, double ms)
    {
        c.PointerDown(At(0, 0));
        for (var i = 1; i <= 10; i++)
        {
            c.PointerMove(At(y * i / 10, ms * i / 10));
        }

        c.PointerUp(At(y, ms));
    }

    private static DrawerController Opened()
    {
        var c = Create();
        c.Toggle();
        c.AnimationCompleted();
        Assert.Equal(DrawerState.Open, c.State);
        return c;
    }

    [Fact]
    public void StartsClosedWithZeroProgress()
    {
        var c = Create();
        Assert.Equal(DrawerState.Closed, c.State);
        Assert.Equal(0, c.Progress);
        Assert.Equal(0, c.Target);
    }

    [Fact]
    public void ClickBelowThresholdToggles()
    {
        var c = Create();
        var log = Record(c);
        c.PointerDown(At(0, 0));
        c.PointerMove(At(20, 30));
        c.PointerUp(At(20, 60));

        Assert.Equal(DrawerState.SettlingOpen, c.State);
        Assert.Equal(1, c.Target);
        var t = Assert.Single(log);
        Assert.Equal(DrawerState.Closed, t.From);
        Assert.Equal(DrawerState.SettlingOpen, t.To);
    }

    [Fact]
    public void ReleaseWithNoMoveIsAClick()
    {
        var c = Create();
        c.PointerDown(At(0, 0));
        c.PointerUp(At(0, 0));
        Assert.Equal(DrawerState.SettlingOpen, c.State);
    }

    [Fact]
    public void PressBelowThresholdDoesNotMoveDrawer()
    {
        var c = Create();
        c.PointerDown(At(0, 0));
        c.PointerMove(At(49, 100));
        Assert.Equal(DrawerState.Closed, c.State);
        Assert.Equal(0, c.Progress);
    }

    [Fact]
    public void CrossingThresholdEntersDragging()
    {
        var c = Create();
        var log = Record(c);
        c.PointerDown(At(0, 0));
        c.PointerMove(At(50, 100));
        Assert.Equal(DrawerState.Dragging, c.State);
        Assert.Equal(50 / Height, c.Progress, 9);
        var t = Assert.Single(log);
        Assert.Equal(DrawerState.Closed, t.From);
        Assert.Equal(DrawerState.Dragging, t.To);
    }

    [Fact]
    public void ProgressFollowsPointerWhileDragging()
    {
        var c = Create();
        c.PointerDown(At(0, 0));
        c.PointerMove(At(150, 100));
        Assert.Equal(0.5, c.Progress, 9);
        c.PointerMove(At(90, 200));
        Assert.Equal(0.3, c.Progress, 9);
        c.PointerMove(At(-20, 300));
        Assert.Equal(0, c.Progress);
    }

    [Fact]
    public void SlowDragAboveOpenThresholdOpens()
    {
        var c = Create();
        var log = Record(c);
        Drag(c, 120, 2000);

        Assert.Equal(DrawerState.SettlingOpen, c.State);
        Assert.Equal(0.4, c.Progress, 9);
        Assert.Equal(1, c.Target);
        var release = log[^1];
        Assert.Equal(DrawerState.Dragging, release.From);
        Assert.Equal(DrawerState.SettlingOpen, release.To);
        Assert.Equal(0.4, release.Progress, 9);
        Assert.True(Math.Abs(release.VelocityPxPerSecond) < 600);
    }

    [Fact]
    public void SlowDragBelowOpenThresholdCloses()
    {
        var c = Create();
        Drag(c, 90, 2000);
        Assert.Equal(DrawerState.SettlingClosed, c.State);
        Assert.Equal(0, c.Target);
    }

    [Fact]
    public void FlickOpens()
    {
        var c = Create();
        var log = Record(c);
        Drag(c, 80, 60);

        Assert.Equal(DrawerState.SettlingOpen, c.State);
        Assert.True(c.Progress < 0.35);
        Assert.True(log[^1].VelocityPxPerSecond >= 600);
    }

    [Fact]
    public void UpwardFlickClosesEvenAboveOpenThreshold()
    {
        var c = Create();
        c.PointerDown(At(0, 0));
        for (var i = 1; i <= 10; i++)
        {
            c.PointerMove(At(25 * i, 100 * i));
        }

        Assert.Equal(DrawerState.Dragging, c.State);
        c.PointerMove(At(200, 1010));
        c.PointerUp(At(150, 1040));

        Assert.Equal(DrawerState.SettlingClosed, c.State);
        Assert.Equal(0.5, c.Progress, 9);
    }

    [Fact]
    public void RubberBandsPastFullyOpen()
    {
        var c = Create();
        c.PointerDown(At(0, 0));
        c.PointerMove(At(Height, 100));
        Assert.Equal(1, c.Progress, 9);

        c.PointerMove(At(Height + 150, 200));
        var expected = 1 + (0.15 * Math.Tanh(0.5));
        Assert.Equal(expected, c.Progress, 9);
        Assert.True(c.Progress > 1);

        c.PointerMove(At(Height * 50, 300));
        Assert.True(c.Progress <= 1.15);
    }

    [Fact]
    public void HorizontalJitterIsIgnored()
    {
        var c = Create();
        c.PointerDown(At(0, 0, x: 100));
        c.PointerMove(At(0, 30, x: 400));
        Assert.Equal(DrawerState.Closed, c.State);

        c.PointerMove(At(150, 100, x: -300));
        Assert.Equal(0.5, c.Progress, 9);

        c.PointerUp(At(150, 2000, x: 900));
        Assert.Equal(DrawerState.SettlingOpen, c.State);
    }

    [Fact]
    public void ReducedMotionSkipsSettling()
    {
        var c = Create(reducedMotion: true);
        var log = Record(c);
        Drag(c, 200, 2000);

        Assert.Equal(DrawerState.Open, c.State);
        Assert.Equal(1, c.Progress);
        Assert.DoesNotContain(log, t => t.To is DrawerState.SettlingOpen or DrawerState.SettlingClosed);
        Assert.Equal(DrawerState.Open, log[^1].To);

        c.Dismiss(DismissReason.Escape);
        Assert.Equal(DrawerState.Closed, c.State);
        Assert.Equal(0, c.Progress);
        Assert.DoesNotContain(log, t => t.To is DrawerState.SettlingOpen or DrawerState.SettlingClosed);
    }

    [Fact]
    public void ToggleFromClosedSettlesOpenThenAnimationCompletes()
    {
        var c = Create();
        var log = Record(c);
        c.Toggle();
        Assert.Equal(DrawerState.SettlingOpen, c.State);
        Assert.Equal(1, c.Target);

        c.AnimationCompleted();
        Assert.Equal(DrawerState.Open, c.State);
        Assert.Equal(1, c.Progress);
        Assert.Equal(2, log.Count);
        Assert.Equal(DrawerState.SettlingOpen, log[1].From);
        Assert.Equal(DrawerState.Open, log[1].To);
    }

    [Fact]
    public void ToggleFromOpenSettlesClosed()
    {
        var c = Opened();
        c.Toggle();
        Assert.Equal(DrawerState.SettlingClosed, c.State);
        Assert.Equal(0, c.Target);
        c.AnimationCompleted();
        Assert.Equal(DrawerState.Closed, c.State);
        Assert.Equal(0, c.Progress);
    }

    [Fact]
    public void ToggleWhileSettlingReversesTarget()
    {
        var c = Create();
        c.Toggle();
        c.Toggle();
        Assert.Equal(DrawerState.SettlingClosed, c.State);
        c.Toggle();
        Assert.Equal(DrawerState.SettlingOpen, c.State);
    }

    [Theory]
    [InlineData(DismissReason.Escape)]
    [InlineData(DismissReason.ClickOutside)]
    [InlineData(DismissReason.Inactivity)]
    [InlineData(DismissReason.AfterCommand)]
    [InlineData(DismissReason.FullScreenApp)]
    [InlineData(DismissReason.MonitorChange)]
    public void DismissFromOpenSettlesClosed(DismissReason reason)
    {
        var c = Opened();
        var log = Record(c);
        c.Dismiss(reason);
        Assert.Equal(DrawerState.SettlingClosed, c.State);
        Assert.Equal(0, c.Target);
        var t = Assert.Single(log);
        Assert.Equal(DrawerState.Open, t.From);
    }

    [Fact]
    public void DismissWhileClosedIsNoOp()
    {
        var c = Create();
        var log = Record(c);
        c.Dismiss(DismissReason.Escape);
        Assert.Equal(DrawerState.Closed, c.State);
        Assert.Empty(log);

        c.Toggle();
        c.Toggle();
        log.Clear();
        c.Dismiss(DismissReason.ClickOutside);
        Assert.Equal(DrawerState.SettlingClosed, c.State);
        Assert.Empty(log);
    }

    [Fact]
    public void DismissWhileDraggingClosesAndCancelsGesture()
    {
        var c = Create();
        c.PointerDown(At(0, 0));
        c.PointerMove(At(200, 100));
        Assert.Equal(DrawerState.Dragging, c.State);

        c.Dismiss(DismissReason.MonitorChange);
        Assert.Equal(DrawerState.SettlingClosed, c.State);

        // The stale pointer stream no longer drives the drawer.
        c.PointerMove(At(250, 200));
        c.PointerUp(At(250, 300));
        Assert.Equal(DrawerState.SettlingClosed, c.State);
        Assert.Equal(200 / Height, c.Progress, 9);
    }

    [Fact]
    public void DismissWhileSettlingOpenRedirectsToClosed()
    {
        var c = Create();
        c.Toggle();
        c.Dismiss(DismissReason.FullScreenApp);
        Assert.Equal(DrawerState.SettlingClosed, c.State);
    }

    [Fact]
    public void AnimationCompletedInWrongStateIsNoOp()
    {
        var c = Create();
        var log = Record(c);
        c.AnimationCompleted();
        Assert.Equal(DrawerState.Closed, c.State);

        c.PointerDown(At(0, 0));
        c.PointerMove(At(100, 100));
        log.Clear();
        c.AnimationCompleted();
        Assert.Equal(DrawerState.Dragging, c.State);
        Assert.Equal(100 / Height, c.Progress, 9);
        Assert.Empty(log);

        c.PointerUp(At(100, 2000));
        c.AnimationCompleted();
        c.AnimationCompleted();
        Assert.Equal(DrawerState.Closed, c.State);
    }

    [Fact]
    public void DoublePressIsIgnored()
    {
        var c = Create();
        c.PointerDown(At(0, 0));
        c.PointerMove(At(150, 100));
        c.PointerDown(At(150, 110));
        c.PointerMove(At(180, 200));

        Assert.Equal(DrawerState.Dragging, c.State);
        Assert.Equal(0.6, c.Progress, 9);
    }

    [Fact]
    public void PointerEventsWhileSettlingAreIgnored()
    {
        var c = Create();
        c.Toggle();
        c.PointerDown(At(0, 0));
        c.PointerMove(At(200, 100));
        c.PointerUp(At(200, 200));
        Assert.Equal(DrawerState.SettlingOpen, c.State);
        Assert.Equal(0, c.Progress);
    }

    [Fact]
    public void MoveAndUpWithoutPressAreIgnored()
    {
        var c = Create();
        var log = Record(c);
        c.PointerMove(At(200, 100));
        c.PointerUp(At(200, 200));
        Assert.Equal(DrawerState.Closed, c.State);
        Assert.Empty(log);
    }

    [Fact]
    public void OpenDrawerUpwardDragPastThresholdCloses()
    {
        var c = Opened();
        var log = Record(c);
        c.PointerDown(At(20, 0));
        c.PointerMove(At(-40, 500));
        Assert.Equal(DrawerState.Dragging, c.State);
        Assert.Equal(1 - (60 / Height), c.Progress, 9);

        c.PointerUp(At(-40, 2000));
        Assert.Equal(DrawerState.SettlingClosed, c.State);
        Assert.Equal(0, c.Target);
        Assert.Equal(DrawerState.Open, log[0].From);
        Assert.Equal(DrawerState.Dragging, log[0].To);
        Assert.Equal(DrawerState.SettlingClosed, log[1].To);
    }

    [Fact]
    public void OpenDrawerClickInTopBandStaysOpen()
    {
        var c = Opened();
        var log = Record(c);
        c.PointerDown(At(10, 0));
        c.PointerMove(At(-20, 50));
        c.PointerUp(At(-20, 100));
        Assert.Equal(DrawerState.Open, c.State);
        Assert.Equal(1, c.Progress);
        Assert.Empty(log);
    }

    [Fact]
    public void OpenDrawerDownwardDragRubberBandsAndStaysOpen()
    {
        var c = Opened();
        c.PointerDown(At(10, 0));
        c.PointerMove(At(110, 500));
        Assert.Equal(DrawerState.Dragging, c.State);
        Assert.True(c.Progress > 1 && c.Progress < 1.15);

        c.PointerUp(At(110, 2000));
        Assert.Equal(DrawerState.SettlingOpen, c.State);
        c.AnimationCompleted();
        Assert.Equal(1, c.Progress);
    }

    [Fact]
    public void OpenDrawerDownwardFlickAfterUpwardTravelStaysOpen()
    {
        var c = Opened();
        c.PointerDown(At(0, 0));
        c.PointerMove(At(-100, 500));
        c.PointerMove(At(-90, 510));
        c.PointerUp(At(-60, 540));
        Assert.Equal(DrawerState.SettlingOpen, c.State);
    }

    [Fact]
    public void TransitionsCarryVelocity()
    {
        var c = Create();
        var log = Record(c);
        Drag(c, 300, 300);
        Assert.Equal(1000, log[^1].VelocityPxPerSecond, 6);
    }

    [Fact]
    public void OptionsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DrawerController(new DrawerOptions(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DrawerController(new DrawerOptions(300, OpenThreshold: 1.5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DrawerController(new DrawerOptions(300, VelocityThresholdPxPerS: 0)));
        Assert.Throws<ArgumentNullException>(() => new DrawerController(null!));
    }

    [Fact]
    public void DefaultOptionsMatchSpec()
    {
        var o = new DrawerOptions(300);
        Assert.Equal(50, o.DragThresholdPx);
        Assert.Equal(0.35, o.OpenThreshold);
        Assert.Equal(600, o.VelocityThresholdPxPerS);
        Assert.Equal(0.15, o.RubberBandFactor);
        Assert.False(o.ReducedMotion);
    }
}
