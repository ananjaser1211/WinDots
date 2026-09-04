using WinDots.Core.Design;

namespace WinDots.Core.Tests.Design;

public class DottedRingLayoutTests
{
    [Fact]
    public void DefaultProducesSeventyTwoCentres()
    {
        var centres = DottedRingLayout.Centres(100, 100, 50);
        Assert.Equal(72, centres.Count);
    }

    [Fact]
    public void FirstCentreIsAtTwelveOClock()
    {
        var centres = DottedRingLayout.Centres(100, 100, 50);
        Assert.Equal(100, centres[0].X, 1e-9);
        Assert.Equal(50, centres[0].Y, 1e-9); // directly above the centre
    }

    [Fact]
    public void SecondCentreIsClockwiseOfTheFirst()
    {
        var centres = DottedRingLayout.Centres(100, 100, 50);
        // Moving clockwise from 12 o'clock in screen coordinates goes right and down.
        Assert.True(centres[1].X > centres[0].X);
        Assert.True(centres[1].Y > centres[0].Y);
    }

    [Fact]
    public void CentresAreEvenlySpaced()
    {
        const int count = 72;
        double cx = 0, cy = 0, r = 40;
        var centres = DottedRingLayout.Centres(cx, cy, r, count);
        double expectedStep = 2.0 * Math.PI / count;

        for (int i = 0; i < count; i++)
        {
            var next = centres[(i + 1) % count];
            double a0 = Math.Atan2(centres[i].Y - cy, centres[i].X - cx);
            double a1 = Math.Atan2(next.Y - cy, next.X - cx);
            double delta = a1 - a0;
            while (delta <= 0)
            {
                delta += 2.0 * Math.PI;
            }

            Assert.Equal(expectedStep, delta, 1e-9);
        }
    }

    [Fact]
    public void CentresLieOnTheCircle()
    {
        double cx = 30, cy = 70, r = 55;
        foreach (var (x, y) in DottedRingLayout.Centres(cx, cy, r))
        {
            double distance = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            Assert.Equal(r, distance, 1e-9);
        }
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(0.0, 0)]
    [InlineData(0.5, 36)]
    [InlineData(1.0, 72)]
    [InlineData(1.5, 72)]
    [InlineData(-0.3, 0)]
    public void ElapsedDotsHonoursBoundaries(double? progress, int expected)
    {
        Assert.Equal(expected, DottedRingLayout.ElapsedDots(progress, 72));
    }

    [Fact]
    public void ElapsedDotsRoundsDown()
    {
        // 0.51 * 72 = 36.72 -> 36
        Assert.Equal(36, DottedRingLayout.ElapsedDots(0.51, 72));
    }

    [Fact]
    public void CentresRejectsNonPositiveCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DottedRingLayout.Centres(0, 0, 10, 0));
    }

    [Fact]
    public void CentresRejectsNegativeRadius()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DottedRingLayout.Centres(0, 0, -1));
    }

    [Fact]
    public void ElapsedDotsRejectsNonPositiveCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DottedRingLayout.ElapsedDots(0.5, 0));
    }
}
