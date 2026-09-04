using WinDots.Core.Dashboard;

namespace WinDots.Core.Tests.Dashboard;

public class ResourceGaugeTests
{
    [Fact]
    public void FractionRoundTripsAndFormats()
    {
        var gauge = ResourceGauge.FromFraction(0.73);
        Assert.Equal(0.73, gauge.Fraction, 1e-9);
        Assert.Equal(73, gauge.Percent);
        Assert.Equal("73%", gauge.Display);
    }

    [Fact]
    public void SweepScalesWithArc()
    {
        var gauge = ResourceGauge.FromFraction(0.5);
        Assert.Equal(135.0, gauge.SweepDegrees, 1e-9); // half of the default 270
    }

    [Fact]
    public void FullFractionSweepsWholeArc()
    {
        var gauge = ResourceGauge.FromFraction(1.0, arcDegrees: 360);
        Assert.Equal(360.0, gauge.SweepDegrees, 1e-9);
        Assert.Equal(100, gauge.Percent);
        Assert.Equal("100%", gauge.Display);
    }

    [Theory]
    [InlineData(1.4, 1.0, 100)]
    [InlineData(-0.2, 0.0, 0)]
    public void FractionIsClamped(double input, double expectedFraction, int expectedPercent)
    {
        var gauge = ResourceGauge.FromFraction(input);
        Assert.Equal(expectedFraction, gauge.Fraction, 1e-9);
        Assert.Equal(expectedPercent, gauge.Percent);
    }

    [Fact]
    public void NaNFractionYieldsZero()
    {
        var gauge = ResourceGauge.FromFraction(double.NaN);
        Assert.Equal(0.0, gauge.Fraction, 1e-9);
        Assert.Equal(0, gauge.Percent);
    }

    [Fact]
    public void FromPercentMatchesFromFraction()
    {
        var byPercent = ResourceGauge.FromPercent(42);
        var byFraction = ResourceGauge.FromFraction(0.42);
        Assert.Equal(byFraction.Fraction, byPercent.Fraction, 1e-9);
        Assert.Equal(42, byPercent.Percent);
    }

    [Fact]
    public void FromPercentClampsAboveHundred()
    {
        var gauge = ResourceGauge.FromPercent(150);
        Assert.Equal(100, gauge.Percent);
        Assert.Equal(1.0, gauge.Fraction, 1e-9);
    }

    [Fact]
    public void PercentRoundsToNearest()
    {
        Assert.Equal(74, ResourceGauge.FromFraction(0.735).Percent);
        Assert.Equal(50, ResourceGauge.FromFraction(0.495).Percent);
    }

    [Fact]
    public void NegativeArcRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ResourceGauge.FromFraction(0.5, -10));
    }
}
