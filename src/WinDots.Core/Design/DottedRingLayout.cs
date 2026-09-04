namespace WinDots.Core.Design;

/// <summary>
/// Pure geometry for the progress ring: the centres of the ring dots and the count of
/// dots that should be highlighted for a given playback progress. No platform dependency.
/// </summary>
public static class DottedRingLayout
{
    /// <summary>
    /// Computes the dot centres evenly spaced around a circle, ordered clockwise starting
    /// from the 12 o'clock position.
    /// </summary>
    /// <param name="centreX">Circle centre X.</param>
    /// <param name="centreY">Circle centre Y.</param>
    /// <param name="radius">Circle radius. Must not be negative.</param>
    /// <param name="count">Number of dots. Must be at least one.</param>
    /// <param name="startAngleDeg">Angle of the first dot in degrees; -90 is 12 o'clock.</param>
    public static IReadOnlyList<(double X, double Y)> Centres(
        double centreX,
        double centreY,
        double radius,
        int count = 72,
        double startAngleDeg = -90)
    {
        if (double.IsNaN(radius) || radius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must not be negative.");
        }

        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be at least one.");
        }

        var centres = new (double X, double Y)[count];
        double step = 360.0 / count;
        for (int i = 0; i < count; i++)
        {
            // Screen coordinates have Y pointing down, so increasing angle sweeps clockwise.
            double radians = (startAngleDeg + i * step) * Math.PI / 180.0;
            centres[i] = (centreX + radius * Math.Cos(radians), centreY + radius * Math.Sin(radians));
        }

        return centres;
    }

    /// <summary>
    /// Number of dots to highlight for a given playback progress: null yields zero,
    /// progress is clamped to <c>[0, 1]</c>, and the fraction is rounded down.
    /// </summary>
    public static int ElapsedDots(double? progress, int count)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be at least one.");
        }

        if (progress is not double value || double.IsNaN(value))
        {
            return 0;
        }

        double clamped = Math.Clamp(value, 0.0, 1.0);
        return (int)Math.Floor(clamped * count);
    }
}
