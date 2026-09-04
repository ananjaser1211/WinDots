using System.Globalization;
using System.Text;

namespace WinDots.Core.Design;

/// <summary>
/// An immutable closed blob outline: the sampled polygon points and an equivalent
/// SVG/XAML path string. Produced by <see cref="BlobGeometry"/>.
/// </summary>
public sealed record BlobPath(IReadOnlyList<(double X, double Y)> Points, string PathData);

/// <summary>
/// Pure geometry for the artwork blob: a superformula-style closed curve
/// <c>r(theta) = R * (1 + amplitude * sin(lobes * theta + phase))</c> centred in a
/// square of the given size. Deterministic and free of any platform dependency.
/// </summary>
public static class BlobGeometry
{
    private const int Steps = 256;

    /// <summary>
    /// Builds the blob outline for a square of the given <paramref name="size"/>.
    /// </summary>
    /// <param name="size">Side length of the containing square, in pixels. Must be positive.</param>
    /// <param name="lobes">Number of lobes around the curve. Must be at least one.</param>
    /// <param name="amplitude">Deformation amount in <c>[0, 1)</c>. Zero yields a circle.</param>
    /// <param name="phase">Phase offset in radians, used for idle drift.</param>
    /// <returns>The sampled points (closed: first equals last) and the path string.</returns>
    public static BlobPath Create(double size, int lobes = 8, double amplitude = 0.06, double phase = 0)
    {
        if (!(size > 0) || double.IsNaN(size))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Size must be positive.");
        }

        if (lobes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(lobes), lobes, "Lobes must be at least one.");
        }

        if (double.IsNaN(amplitude) || amplitude < 0 || amplitude >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(amplitude), amplitude, "Amplitude must be in [0, 1).");
        }

        if (double.IsNaN(phase) || double.IsInfinity(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase, "Phase must be a finite number.");
        }

        double centre = size / 2.0;
        double radius = centre * (1.0 - amplitude);

        // Steps segments -> Steps + 1 points, the last coinciding with the first (closed).
        var points = new (double X, double Y)[Steps + 1];
        for (int i = 0; i <= Steps; i++)
        {
            double theta = 2.0 * Math.PI * i / Steps;
            double r = radius * (1.0 + amplitude * Math.Sin(lobes * theta + phase));
            points[i] = (centre + r * Math.Cos(theta), centre + r * Math.Sin(theta));
        }

        return new BlobPath(points, BuildPathData(points));
    }

    private static string BuildPathData(IReadOnlyList<(double X, double Y)> points)
    {
        var sb = new StringBuilder();
        AppendPoint(sb, 'M', points[0]);

        // The final point repeats the first; connect through the distinct interior points and close with Z.
        for (int i = 1; i < points.Count - 1; i++)
        {
            AppendPoint(sb, 'L', points[i]);
        }

        sb.Append('Z');
        return sb.ToString();
    }

    private static void AppendPoint(StringBuilder sb, char command, (double X, double Y) point)
    {
        if (sb.Length > 0)
        {
            sb.Append(' ');
        }

        sb.Append(command)
          .Append(point.X.ToString("0.00", CultureInfo.InvariantCulture))
          .Append(',')
          .Append(point.Y.ToString("0.00", CultureInfo.InvariantCulture));
    }
}
