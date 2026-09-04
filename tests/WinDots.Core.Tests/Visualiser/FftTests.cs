using WinDots.Core.Visualiser;

namespace WinDots.Core.Tests.Visualiser;

public class FftTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(1024, true)]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(1000, false)]
    public void IsPowerOfTwoClassifies(int n, bool expected)
    {
        Assert.Equal(expected, Fft.IsPowerOfTwo(n));
    }

    [Fact]
    public void NonPowerOfTwoThrows()
    {
        double[] real = new double[3];
        double[] imag = new double[3];
        Assert.Throws<ArgumentException>(() => Fft.Forward(real, imag));
    }

    [Fact]
    public void MismatchedLengthsThrow()
    {
        Assert.Throws<ArgumentException>(() => Fft.Forward(new double[4], new double[2]));
    }

    [Fact]
    public void ConstantSignalConcentratesEnergyAtDc()
    {
        const int n = 16;
        double[] real = new double[n];
        double[] imag = new double[n];
        Array.Fill(real, 1.0);

        Fft.Forward(real, imag);

        // Bin 0 equals the sum (n); all other bins are ~0.
        Assert.Equal(n, real[0], 6);
        Assert.Equal(0.0, imag[0], 6);
        for (int k = 1; k < n; k++)
        {
            Assert.True(Math.Abs(real[k]) < 1e-9, $"real[{k}]={real[k]}");
            Assert.True(Math.Abs(imag[k]) < 1e-9, $"imag[{k}]={imag[k]}");
        }
    }

    [Fact]
    public void SingleCycleSineHasPeakAtBinOne()
    {
        const int n = 64;
        double[] real = new double[n];
        double[] imag = new double[n];
        for (int i = 0; i < n; i++)
        {
            real[i] = Math.Sin(2.0 * Math.PI * i / n); // exactly one cycle
        }

        Fft.Forward(real, imag);

        double Power(int k) => (real[k] * real[k]) + (imag[k] * imag[k]);

        double peak = Power(1);
        for (int k = 2; k < n / 2; k++)
        {
            Assert.True(Power(k) < peak * 1e-6, $"bin {k} not negligible vs peak");
        }
    }
}
