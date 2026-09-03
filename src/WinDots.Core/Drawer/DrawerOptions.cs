namespace WinDots.Core.Drawer;

/// <summary>
/// Tunables for the drawer gesture. Defaults mirror _docs/03-ux-interaction-spec.md and the <c>drawer.*</c> settings.
/// </summary>
/// <param name="DrawerHeight">Drawer height in logical pixels; the full travel of the gesture.</param>
/// <param name="DragThresholdPx">Minimum travel before a press becomes a drag; below it a release on the handle is a click.</param>
/// <param name="OpenThreshold">Progress at or above which a release opens the drawer.</param>
/// <param name="VelocityThresholdPxPerS">Flick speed at or above which a release opens (downward) or closes (upward) regardless of progress.</param>
/// <param name="RubberBandFactor">Resistance applied to travel past the fully open position.</param>
/// <param name="ReducedMotion">When true the controller skips the settling states and lands directly on Open/Closed so the view fades instead of springing.</param>
public sealed record DrawerOptions(
    double DrawerHeight,
    double DragThresholdPx = 50,
    double OpenThreshold = 0.35,
    double VelocityThresholdPxPerS = 600,
    double RubberBandFactor = 0.15,
    bool ReducedMotion = false)
{
    /// <summary>Throws when a value is outside its usable range; returns this instance otherwise.</summary>
    public DrawerOptions Validate()
    {
        if (!double.IsFinite(DrawerHeight) || DrawerHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DrawerHeight), DrawerHeight, "Drawer height must be a positive finite number.");
        }

        if (!double.IsFinite(DragThresholdPx) || DragThresholdPx < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DragThresholdPx), DragThresholdPx, "Drag threshold must be non-negative.");
        }

        if (!double.IsFinite(OpenThreshold) || OpenThreshold < 0 || OpenThreshold > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(OpenThreshold), OpenThreshold, "Open threshold must be in [0, 1].");
        }

        if (!double.IsFinite(VelocityThresholdPxPerS) || VelocityThresholdPxPerS <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(VelocityThresholdPxPerS), VelocityThresholdPxPerS, "Velocity threshold must be positive.");
        }

        if (!double.IsFinite(RubberBandFactor) || RubberBandFactor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RubberBandFactor), RubberBandFactor, "Rubber-band factor must be non-negative.");
        }

        return this;
    }
}
