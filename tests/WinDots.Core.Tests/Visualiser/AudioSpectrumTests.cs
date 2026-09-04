using WinDots.Core.Visualiser;

namespace WinDots.Core.Tests.Visualiser;

public class AudioSpectrumTests
{
    private const int SampleRate = 48000;
    private const int FftSize = 2048;

    private static float[] Tone(double freq, double amplitude = 1.0, int length = FftSize, int sampleRate = SampleRate)
    {
        float[] frame = new float[length];
        for (int i = 0; i < length; i++)
        {
            frame[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * freq * i / sampleRate));
        }

        return frame;
    }

    private static int BandFor(double freq, int bands = 60, double minHz = 40, double maxHz = 16000)
    {
        if (freq <= minHz)
        {
            return 0;
        }

        if (freq >= maxHz)
        {
            return bands - 1;
        }

        double frac = (Math.Log(freq) - Math.Log(minHz)) / (Math.Log(maxHz) - Math.Log(minHz));
        return Math.Clamp((int)(frac * bands), 0, bands - 1);
    }

    private static int ArgMax(IReadOnlyList<float> values)
    {
        int best = 0;
        for (int i = 1; i < values.Count; i++)
        {
            if (values[i] > values[best])
            {
                best = i;
            }
        }

        return best;
    }

    [Fact]
    public void PureSinePeaksInBandContainingItsFrequency()
    {
        var spectrum = new AudioSpectrum();
        float[] tone = Tone(1000.0);
        for (int i = 0; i < 20; i++)
        {
            spectrum.Process(tone, SampleRate);
        }

        int peak = ArgMax(spectrum.Bands);
        int expected = BandFor(1000.0);

        Assert.InRange(peak, expected - 1, expected + 1);
        Assert.False(spectrum.IsSilent);

        // Neighbours are much lower than the peak.
        Assert.True(spectrum.Bands[peak] > 0.2f, $"peak too small: {spectrum.Bands[peak]}");
        Assert.True(spectrum.Bands[peak - 2] < spectrum.Bands[peak] * 0.3f, "lower neighbour not much smaller");
        Assert.True(spectrum.Bands[peak + 2] < spectrum.Bands[peak] * 0.3f, "upper neighbour not much smaller");
    }

    [Fact]
    public void ConstantSignalExcitesOnlyLowestBand()
    {
        var spectrum = new AudioSpectrum();
        float[] dc = new float[FftSize];
        Array.Fill(dc, 1.0f);

        for (int i = 0; i < 5; i++)
        {
            spectrum.Process(dc, SampleRate);
        }

        Assert.Equal(0, ArgMax(spectrum.Bands));
        Assert.False(spectrum.IsSilent);

        // Mid and high bands are essentially silent.
        for (int b = 3; b < spectrum.BandCount; b++)
        {
            Assert.True(spectrum.Bands[b] < spectrum.Bands[0] * 0.1f, $"band {b} not negligible vs band 0");
        }
    }

    [Fact]
    public void SilenceLeavesAllBandsNearZero()
    {
        var spectrum = new AudioSpectrum();
        float[] silence = new float[FftSize];

        for (int i = 0; i < 5; i++)
        {
            spectrum.Process(silence, SampleRate);
        }

        Assert.True(spectrum.IsSilent);
        foreach (float value in spectrum.Bands)
        {
            Assert.True(value < 1e-4f, $"band not near zero: {value}");
        }
    }

    [Fact]
    public void SilenceDetectedBelowThreshold()
    {
        var spectrum = new AudioSpectrum();
        float[] loud = Tone(1000.0, amplitude: 0.5);
        spectrum.Process(loud, SampleRate);
        Assert.False(spectrum.IsSilent);

        float[] tiny = Tone(1000.0, amplitude: 1e-6);
        spectrum.Process(tiny, SampleRate);
        Assert.True(spectrum.IsSilent);
    }

    [Fact]
    public void AttackRisesOverSeveralFramesNotInstantly()
    {
        var spectrum = new AudioSpectrum();
        float[] tone = Tone(1000.0);

        spectrum.Process(tone, SampleRate);
        int band = ArgMax(spectrum.Bands);
        float f1 = spectrum.Bands[band];

        spectrum.Process(tone, SampleRate);
        float f2 = spectrum.Bands[band];

        spectrum.Process(tone, SampleRate);
        float f3 = spectrum.Bands[band];

        Assert.True(f1 > 0f, "no rise on first frame");
        Assert.True(f2 > f1, "did not keep rising on frame 2");
        Assert.True(f3 > f2, "did not keep rising on frame 3");

        // Settled value is clearly higher than the first frame (not instant).
        for (int i = 0; i < 30; i++)
        {
            spectrum.Process(tone, SampleRate);
        }

        Assert.True(spectrum.Bands[band] > f1 * 1.2f, "reached target too quickly");
    }

    [Fact]
    public void DecayIsSlowerThanAttack()
    {
        // Settle a spectrum, then measure the drop from one silence frame.
        var settled = new AudioSpectrum();
        float[] tone = Tone(1000.0);
        for (int i = 0; i < 40; i++)
        {
            settled.Process(tone, SampleRate);
        }

        int band = ArgMax(settled.Bands);
        float peak = settled.Bands[band];

        float[] silence = new float[FftSize];
        settled.Process(silence, SampleRate);
        float drop = peak - settled.Bands[band];

        // Measure the rise from a single attack frame on a fresh spectrum.
        var fresh = new AudioSpectrum();
        fresh.Process(tone, SampleRate);
        float rise = fresh.Bands[band];

        Assert.True(rise > drop, $"attack ({rise}) should exceed decay ({drop})");
    }

    [Fact]
    public void PeakHoldStaysAtOrAboveBandAndDecays()
    {
        var spectrum = new AudioSpectrum(new AudioSpectrumConfig { PeakHold = true });
        float[] tone = Tone(1000.0);
        for (int i = 0; i < 40; i++)
        {
            spectrum.Process(tone, SampleRate);
        }

        int band = ArgMax(spectrum.Bands);
        float peakBefore = spectrum.Peaks[band];
        Assert.True(peakBefore >= spectrum.Bands[band], "peak below band");
        Assert.True(peakBefore > 0f);

        // Silence: the band collapses faster than the held peak.
        float[] silence = new float[FftSize];
        spectrum.Process(silence, SampleRate);
        Assert.True(spectrum.Peaks[band] >= spectrum.Bands[band], "peak fell below band on decay");
        Assert.True(spectrum.Peaks[band] < peakBefore, "peak did not decay");
    }

    [Fact]
    public void ResetClearsState()
    {
        var spectrum = new AudioSpectrum();
        float[] tone = Tone(1000.0);
        spectrum.Process(tone, SampleRate);
        Assert.False(spectrum.IsSilent);

        spectrum.Reset();
        Assert.True(spectrum.IsSilent);
        foreach (float value in spectrum.Bands)
        {
            Assert.Equal(0f, value);
        }
    }

    [Fact]
    public void AllBandsStayInUnitRange()
    {
        var spectrum = new AudioSpectrum();
        float[] tone = Tone(1000.0, amplitude: 1.0);
        for (int i = 0; i < 40; i++)
        {
            spectrum.Process(tone, SampleRate);
        }

        foreach (float value in spectrum.Bands)
        {
            Assert.InRange(value, 0f, 1f);
        }
    }

    [Theory]
    [InlineData(10, 24)]
    [InlineData(24, 24)]
    [InlineData(60, 60)]
    [InlineData(96, 96)]
    [InlineData(200, 96)]
    public void BandCountClampsToSupportedRange(int requested, int expected)
    {
        var spectrum = new AudioSpectrum(new AudioSpectrumConfig { Bands = requested });
        Assert.Equal(expected, spectrum.BandCount);
    }

    [Fact]
    public void NonPowerOfTwoFftSizeThrows()
    {
        Assert.Throws<ArgumentException>(() => new AudioSpectrum(new AudioSpectrumConfig { FftSize = 1000 }));
    }

    [Fact]
    public void ZeroSampleRateThrows()
    {
        var spectrum = new AudioSpectrum();
        Assert.Throws<ArgumentOutOfRangeException>(() => spectrum.Process(new float[FftSize], 0));
    }

    [Fact]
    public void HandlesShortFrameByZeroPadding()
    {
        var spectrum = new AudioSpectrum();
        float[] tone = Tone(1000.0, length: 512); // shorter than FFT size
        spectrum.Process(tone, SampleRate);
        // Should not throw and should register non-silence.
        Assert.False(spectrum.IsSilent);
    }

    [Fact]
    public void DifferentSampleRateRemapsBands()
    {
        var spectrum = new AudioSpectrum();
        spectrum.Process(Tone(1000.0, sampleRate: 48000), 48000);
        int peak48 = ArgMax(spectrum.Bands);

        var spectrum2 = new AudioSpectrum();
        for (int i = 0; i < 10; i++)
        {
            spectrum2.Process(Tone(1000.0, sampleRate: 44100), 44100);
        }

        int peak44 = ArgMax(spectrum2.Bands);

        // 1 kHz maps to roughly the same log band regardless of sample rate.
        Assert.InRange(peak44, peak48 - 2, peak48 + 2);
    }
}
