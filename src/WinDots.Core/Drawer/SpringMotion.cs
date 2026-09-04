namespace WinDots.Core.Drawer;

/// <summary>
/// A damped spring integrated in fixed sub-steps, used to settle the drawer when the platform cannot animate the
/// window itself. Defaults match _docs/04-visual-design.md Motion.Spring (stiffness 320, damping 28, mass 1).
/// Pure and deterministic: callers supply the elapsed time.
/// </summary>
public sealed class SpringMotion
{
    private const double SubStepSeconds = 1.0 / 240.0;

    public SpringMotion(double stiffness = 320, double damping = 28, double mass = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stiffness);
        ArgumentOutOfRangeException.ThrowIfNegative(damping);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mass);
        Stiffness = stiffness;
        Damping = damping;
        Mass = mass;
    }

    public double Stiffness { get; }

    public double Damping { get; }

    public double Mass { get; }

    public double Position { get; private set; }

    public double Velocity { get; private set; }

    public double Target { get; private set; }

    /// <summary>Settle tolerance in the caller's units (drawer progress uses 1/height px).</summary>
    public double PositionTolerance { get; init; } = 0.5;

    public double VelocityTolerance { get; init; } = 0.5;

    public bool IsSettled => Math.Abs(Velocity) < VelocityTolerance && Math.Abs(Position - Target) < PositionTolerance;

    public void Start(double position, double velocity, double target)
    {
        Position = position;
        Velocity = velocity;
        Target = target;
    }

    public void Retarget(double target) => Target = target;

    /// <summary>Advances by <paramref name="elapsed"/>; returns true once settled (position snapped to the target).</summary>
    public bool Step(TimeSpan elapsed)
    {
        var remaining = Math.Clamp(elapsed.TotalSeconds, 0, 0.25);
        while (remaining > 0)
        {
            var dt = Math.Min(SubStepSeconds, remaining);
            remaining -= dt;
            var force = (-Stiffness * (Position - Target)) - (Damping * Velocity);
            Velocity += force / Mass * dt;
            Position += Velocity * dt;
        }

        if (IsSettled)
        {
            Position = Target;
            Velocity = 0;
            return true;
        }

        return false;
    }
}
