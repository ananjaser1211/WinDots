using System.Globalization;
using WinDots.Core.Design;

namespace WinDots.Core.Tests.Design;

public class BlobGeometryTests
{
    [Fact]
    public void IsDeterministic()
    {
        var a = BlobGeometry.Create(200, 8, 0.06, 0.3);
        var b = BlobGeometry.Create(200, 8, 0.06, 0.3);

        Assert.Equal(a.PathData, b.PathData);
        Assert.Equal(a.Points, b.Points);
    }

    [Fact]
    public void PathIsClosed()
    {
        var blob = BlobGeometry.Create(256, 8, 0.06, 1.1);

        var first = blob.Points[0];
        var last = blob.Points[^1];
        Assert.Equal(first.X, last.X, 1e-9);
        Assert.Equal(first.Y, last.Y, 1e-9);
    }

    [Fact]
    public void ZeroAmplitudeYieldsCircle()
    {
        const double size = 300;
        var blob = BlobGeometry.Create(size, 8, 0.0);
        double centre = size / 2.0;
        double expected = centre; // R = size/2 * (1 - 0)

        foreach (var (x, y) in blob.Points)
        {
            double distance = Math.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
            Assert.Equal(expected, distance, 1e-9);
        }
    }

    [Fact]
    public void AllPointsInsideTheSquare()
    {
        const double size = 128;
        var blob = BlobGeometry.Create(size, 8, 0.06, 0.7);

        foreach (var (x, y) in blob.Points)
        {
            Assert.InRange(x, 0.0, size);
            Assert.InRange(y, 0.0, size);
        }
    }

    [Fact]
    public void PathDataStartsWithMoveAndEndsWithClose()
    {
        var blob = BlobGeometry.Create(200);

        Assert.StartsWith("M", blob.PathData, StringComparison.Ordinal);
        Assert.EndsWith("Z", blob.PathData, StringComparison.Ordinal);
    }

    [Fact]
    public void PathDataIsCultureInvariant()
    {
        var original = CultureInfo.CurrentCulture;

        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var control = BlobGeometry.Create(200, 8, 0.06, 0.5);
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // comma decimal separator
            var blob = BlobGeometry.Create(200, 8, 0.06, 0.5);

            // Coordinates use a dot as the decimal separator regardless of the ambient culture.
            Assert.Contains(".", blob.PathData, StringComparison.Ordinal);
            Assert.Equal(control.PathData, blob.PathData);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void RejectsNonPositiveSize(double size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BlobGeometry.Create(size));
    }

    [Fact]
    public void RejectsFewerThanOneLobe()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BlobGeometry.Create(200, 0));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void RejectsAmplitudeOutOfRange(double amplitude)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BlobGeometry.Create(200, 8, amplitude));
    }
}
