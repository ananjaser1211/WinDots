namespace WinDots.Core.Visualiser;

/// <summary>
/// Tunables for <see cref="AudioSpectrum"/>. Immutable; deterministic given a fixed configuration and input.
/// </summary>
public sealed record AudioSpectrumConfig
{
    /// <summary>Number of log-spaced output bands. Clamped to <see cref="VisualiserOptions.MinBars"/>..<see cref="VisualiserOptions.MaxBars"/>.</summary>
    public int Bands { get; init; } = 60;

    /// <summary>FFT window size in samples. Must be a power of two. Frames are truncated or zero-padded to this length.</summary>
    public int FftSize { get; init; } = 2048;

    /// <summary>Lowest band edge in Hz. Bins at or below this fold into the first band.</summary>
    public double MinFrequencyHz { get; init; } = 40.0;

    /// <summary>Highest band edge in Hz. Bins at or above this fold into the last band.</summary>
    public double MaxFrequencyHz { get; init; } = 16000.0;

    /// <summary>Band power at or below this many dB maps to 0.</summary>
    public double MinDecibels { get; init; } = -70.0;

    /// <summary>Band power at or above this many dB maps to 1 (before <see cref="Gain"/>).</summary>
    public double MaxDecibels { get; init; } = -12.0;

    /// <summary>Linear multiplier applied to the normalised value before clamping to 0..1.</summary>
    public double Gain { get; init; } = 1.0;

    /// <summary>Fraction (0..1) a band moves toward a higher target each frame. Larger is a faster attack.</summary>
    public double Attack { get; init; } = 0.6;

    /// <summary>Fraction (0..1) a band moves toward a lower target each frame. Smaller is a slower decay.</summary>
    public double Decay { get; init; } = 0.18;

    /// <summary>Track a decaying per-band peak alongside the smoothed value.</summary>
    public bool PeakHold { get; init; }

    /// <summary>Fraction (0..1) a held peak falls each frame when the band is below it.</summary>
    public double PeakDecay { get; init; } = 0.05;

    /// <summary>Input RMS at or below this is treated as silence; bands then decay toward zero.</summary>
    public double SilenceRms { get; init; } = 1e-4;

    /// <summary>Builds a config from the user-facing <see cref="VisualiserOptions"/> (bands and decay smoothing).</summary>
    public static AudioSpectrumConfig FromOptions(VisualiserOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        double smoothing = Math.Clamp(options.Smoothing, 0.0, 0.98);

        // Smoothing drives the decay (fall speed): higher smoothing => slower fall. Cap the decay well below 1 so it
        // never falls as fast as - or faster than - the attack. Attack (rise speed) is always kept faster than the
        // decay, including at smoothing 0, so bars snap up and ease down (never the reverse).
        double decay = Math.Clamp((1.0 - smoothing) * 0.5, 0.03, 0.5);
        double attack = Math.Clamp(decay * 3.0, decay + 0.1, 0.95);

        return new AudioSpectrumConfig
        {
            Bands = options.ClampedBars,
            Decay = decay,
            Attack = attack,
        };
    }
}
