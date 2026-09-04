using WinDots.Core.Drawer;

namespace WinDots.Core.Tests.Drawer;

public class SpringMotionTests
{
    [Fact]
    public void SettlesAtTargetWithinASecond()
    {
        var s = new SpringMotion { PositionTolerance = 0.5, VelocityTolerance = 0.5 };
        s.Start(position: 0, velocity: 0, target: 300);
        var t = TimeSpan.Zero;
        var settled = false;
        while (t < TimeSpan.FromSeconds(1) && !settled)
        {
            settled = s.Step(TimeSpan.FromMilliseconds(16));
            t += TimeSpan.FromMilliseconds(16);
        }

        Assert.True(settled, $"not settled after 1 s; pos={s.Position} v={s.Velocity}");
        Assert.Equal(300, s.Position);
        Assert.Equal(0, s.Velocity);
    }

    [Fact]
    public void ApproachesMonotonicallyForNearCriticalDamping()
    {
        var s = new SpringMotion();
        s.Start(0, 0, 100);
        var last = 0.0;
        var overshoot = 0.0;
        for (var i = 0; i < 120; i++)
        {
            s.Step(TimeSpan.FromMilliseconds(8));
            overshoot = Math.Max(overshoot, s.Position - 100);
            last = s.Position;
        }

        Assert.True(overshoot < 8, $"overshoot {overshoot}");
        Assert.InRange(last, 99.5, 100.5);
    }

    [Fact]
    public void RetargetMidFlightHeadsToNewTarget()
    {
        var s = new SpringMotion();
        s.Start(0, 0, 300);
        for (var i = 0; i < 10; i++) s.Step(TimeSpan.FromMilliseconds(16));
        Assert.True(s.Position > 10);
        s.Retarget(0);
        for (var i = 0; i < 90; i++) s.Step(TimeSpan.FromMilliseconds(16));
        Assert.True(s.IsSettled);
        Assert.Equal(0, s.Position);
    }

    [Fact]
    public void LargeElapsedIsClampedNotExploded()
    {
        var s = new SpringMotion();
        s.Start(0, 0, 300);
        s.Step(TimeSpan.FromSeconds(30));
        Assert.True(double.IsFinite(s.Position));
        Assert.InRange(s.Position, 0, 320);
    }

    [Fact]
    public void InitialVelocityCarriesThrough()
    {
        var slow = new SpringMotion();
        slow.Start(100, 0, 300);
        var fast = new SpringMotion();
        fast.Start(100, 2000, 300);
        slow.Step(TimeSpan.FromMilliseconds(32));
        fast.Step(TimeSpan.FromMilliseconds(32));
        Assert.True(fast.Position > slow.Position);
    }
}
