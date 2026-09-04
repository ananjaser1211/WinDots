namespace WinDots.Core.Visualiser;

/// <summary>
/// Converts mono float sample frames into N log-spaced frequency band magnitudes in 0..1. Applies a Hann window,
/// a self-contained radix-2 FFT, a power spectrum, per-band normalisation with configurable reference/gain,
/// per-band fast-attack / slow-decay smoothing held across frames, optional peak-hold, and silence detection.
/// Pure math, stateful per instance, deterministic. BCL only. See _docs/10-enhancement-plan.md (E5).
/// </summary>
public sealed class AudioSpectrum
{
    private readonly AudioSpectrumConfig _config;
    private readonly int _bandCount;
    private readonly int _fftSize;
    private readonly double[] _window;
    private readonly double[] _real;
    private readonly double[] _imag;
    private readonly float[] _bands;
    private readonly float[] _peaks;

    // Cached bin-to-band mapping, recomputed when the sample rate changes.
    private readonly int[] _bandStart;
    private readonly int[] _bandEnd;
    private int _mappedSampleRate;

    /// <summary>Creates a spectrum analyser. A null config uses defaults (60 bands, 2048-point FFT).</summary>
    public AudioSpectrum(AudioSpectrumConfig? config = null)
    {
        _config = config ?? new AudioSpectrumConfig();
        _bandCount = Math.Clamp(_config.Bands, VisualiserOptions.MinBars, VisualiserOptions.MaxBars);
        _fftSize = _config.FftSize;

        if (!Fft.IsPowerOfTwo(_fftSize) || _fftSize < 8)
        {
            throw new ArgumentException($"FftSize must be a power of two >= 8, was {_fftSize}.", nameof(config));
        }

        _window = BuildHannWindow(_fftSize);
        _real = new double[_fftSize];
        _imag = new double[_fftSize];
        _bands = new float[_bandCount];
        _peaks = new float[_bandCount];
        _bandStart = new int[_bandCount];
        _bandEnd = new int[_bandCount];
    }

    /// <summary>The number of output bands (the clamped configured value).</summary>
    public int BandCount => _bandCount;

    /// <summary>The current smoothed band magnitudes, each in 0..1. The same backing array is reused each frame.</summary>
    public IReadOnlyList<float> Bands => _bands;

    /// <summary>The current peak-hold values, each in 0..1. All zero unless <see cref="AudioSpectrumConfig.PeakHold"/> is set.</summary>
    public IReadOnlyList<float> Peaks => _peaks;

    /// <summary>True when the most recent processed frame was below the silence RMS threshold.</summary>
    public bool IsSilent { get; private set; } = true;

    /// <summary>Clears all smoothing and peak state back to silence.</summary>
    public void Reset()
    {
        Array.Clear(_bands);
        Array.Clear(_peaks);
        IsSilent = true;
    }

    /// <summary>
    /// Processes one mono frame at the given sample rate, updating <see cref="Bands"/>, <see cref="Peaks"/>, and
    /// <see cref="IsSilent"/>. The frame is windowed over the first <see cref="AudioSpectrumConfig.FftSize"/>
    /// samples (zero-padded if shorter). Advances smoothing by exactly one step.
    /// </summary>
    public void Process(ReadOnlySpan<float> monoFrame, int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        }

        EnsureBandMapping(sampleRate);

        // RMS over the available samples (before windowing) for silence detection.
        int available = Math.Min(monoFrame.Length, _fftSize);
        double sumSquares = 0.0;
        for (int i = 0; i < available; i++)
        {
            double s = monoFrame[i];
            sumSquares += s * s;
        }

        double rms = available > 0 ? Math.Sqrt(sumSquares / available) : 0.0;
        IsSilent = rms <= _config.SilenceRms;

        if (IsSilent)
        {
            DecayToSilence();
            return;
        }

        // Window into the FFT buffers, zero-padding any remainder.
        for (int i = 0; i < _fftSize; i++)
        {
            double sample = i < available ? monoFrame[i] : 0.0;
            _real[i] = sample * _window[i];
            _imag[i] = 0.0;
        }

        Fft.Forward(_real, _imag);

        // Group bin power into bands, then normalise and smooth.
        for (int band = 0; band < _bandCount; band++)
        {
            int start = _bandStart[band];
            int end = _bandEnd[band];
            double powerSum = 0.0;
            int count = 0;

            for (int bin = start; bin <= end; bin++)
            {
                double re = _real[bin];
                double im = _imag[bin];
                powerSum += (re * re) + (im * im);
                count++;
            }

            double meanPower = count > 0 ? powerSum / count : 0.0;

            // Scale by the window/size so full-scale content lands near the configured dB ceiling.
            double normalisedPower = meanPower / ((double)_fftSize * _fftSize);
            double db = 10.0 * Math.Log10(normalisedPower + 1e-12);
            double t = (db - _config.MinDecibels) / (_config.MaxDecibels - _config.MinDecibels);
            double target = Math.Clamp(t * _config.Gain, 0.0, 1.0);

            ApplySmoothing(band, (float)target);
        }
    }

    private void ApplySmoothing(int band, float target)
    {
        float current = _bands[band];
        double coefficient = target >= current ? _config.Attack : _config.Decay;
        current += (float)((target - current) * coefficient);
        _bands[band] = current;

        if (_config.PeakHold)
        {
            if (current >= _peaks[band])
            {
                _peaks[band] = current;
            }
            else
            {
                _peaks[band] = (float)Math.Max(current, _peaks[band] - _config.PeakDecay);
            }
        }
    }

    private void DecayToSilence()
    {
        for (int band = 0; band < _bandCount; band++)
        {
            ApplySmoothing(band, 0f);
        }
    }

    private void EnsureBandMapping(int sampleRate)
    {
        if (sampleRate == _mappedSampleRate)
        {
            return;
        }

        _mappedSampleRate = sampleRate;

        int halfBins = _fftSize / 2; // usable bins 0.._fftSize/2
        double binHz = (double)sampleRate / _fftSize;
        double minHz = _config.MinFrequencyHz;
        double maxHz = Math.Min(_config.MaxFrequencyHz, sampleRate / 2.0);
        double logMin = Math.Log(minHz);
        double logMax = Math.Log(maxHz);

        // First pass: assign each bin to a band by log frequency; fold out-of-range bins to the edges.
        int[] binBand = new int[halfBins + 1];
        for (int bin = 0; bin <= halfBins; bin++)
        {
            double freq = bin * binHz;
            int band;
            if (freq <= minHz)
            {
                band = 0;
            }
            else if (freq >= maxHz)
            {
                band = _bandCount - 1;
            }
            else
            {
                double frac = (Math.Log(freq) - logMin) / (logMax - logMin);
                band = Math.Clamp((int)(frac * _bandCount), 0, _bandCount - 1);
            }

            binBand[bin] = band;
        }

        // Second pass: contiguous [start,end] bin range per band. Empty bands borrow the nearest bin below.
        for (int band = 0; band < _bandCount; band++)
        {
            _bandStart[band] = -1;
            _bandEnd[band] = -1;
        }

        for (int bin = 0; bin <= halfBins; bin++)
        {
            int band = binBand[bin];
            if (_bandStart[band] < 0)
            {
                _bandStart[band] = bin;
            }

            _bandEnd[band] = bin;
        }

        int lastBin = 0;
        for (int band = 0; band < _bandCount; band++)
        {
            if (_bandStart[band] < 0)
            {
                // No bin fell in this band (bands finer than bin spacing): reuse the previous bin.
                _bandStart[band] = lastBin;
                _bandEnd[band] = lastBin;
            }
            else
            {
                lastBin = _bandEnd[band];
            }
        }
    }

    private static double[] BuildHannWindow(int size)
    {
        double[] window = new double[size];
        for (int i = 0; i < size; i++)
        {
            window[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (size - 1)));
        }

        return window;
    }
}
