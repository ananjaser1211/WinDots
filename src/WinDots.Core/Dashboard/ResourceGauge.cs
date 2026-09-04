using System.Globalization;

namespace WinDots.Core.Dashboard;

/// <summary>
/// Pure math for a resource ring (CPU/RAM/disk). Clamps a load value to <c>[0,1]</c> and exposes the
/// sweep angle for an arc gauge and the display string the ring binds to. No platform dependency.
/// </summary>
public readonly record struct ResourceGauge
{
    /// <summary>The default arc extent in degrees for a three-quarter ("270 degree") gauge.</summary>
    public const double DefaultSweepDegrees = 270.0;

    private ResourceGauge(double fraction, double sweepDegrees, int percent)
    {
        Fraction = fraction;
        SweepDegrees = sweepDegrees;
        Percent = percent;
    }

    /// <summary>Load clamped to <c>[0,1]</c>.</summary>
    public double Fraction { get; }

    /// <summary>Filled arc length in degrees: <c>Fraction * arcDegrees</c>.</summary>
    public double SweepDegrees { get; }

    /// <summary>Whole-number percentage, <c>0..100</c> (rounded to nearest).</summary>
    public int Percent { get; }

    /// <summary>Display string such as <c>"73%"</c>.</summary>
    public string Display => string.Create(CultureInfo.InvariantCulture, $"{Percent}%");

    /// <summary>Builds a gauge from a fraction in <c>[0,1]</c> (values outside are clamped; NaN yields 0).</summary>
    /// <param name="fraction">Load in <c>[0,1]</c>.</param>
    /// <param name="arcDegrees">Total arc extent the full ring represents; defaults to <see cref="DefaultSweepDegrees"/>.</param>
    public static ResourceGauge FromFraction(double fraction, double arcDegrees = DefaultSweepDegrees)
    {
        if (double.IsNaN(arcDegrees) || arcDegrees < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(arcDegrees), arcDegrees, "Arc must not be negative.");
        }

        double clamped = double.IsNaN(fraction) ? 0.0 : Math.Clamp(fraction, 0.0, 1.0);
        int percent = (int)Math.Round(clamped * 100.0, MidpointRounding.AwayFromZero);
        return new ResourceGauge(clamped, clamped * arcDegrees, percent);
    }

    /// <summary>Builds a gauge from a percentage in <c>[0,100]</c> (values outside are clamped; NaN yields 0).</summary>
    public static ResourceGauge FromPercent(double percent, double arcDegrees = DefaultSweepDegrees) =>
        FromFraction(double.IsNaN(percent) ? 0.0 : percent / 100.0, arcDegrees);
}
