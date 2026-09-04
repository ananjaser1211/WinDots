namespace WinDots.Core.Visualiser;

/// <summary>
/// Downsamples a mono float frame to a fixed number of points, keeping a min/max envelope per bucket, for the
/// waveform render style. Values are clamped to -1..1. Stateful per instance (reuses its buffers); deterministic.
/// </summary>
public sealed class WaveformBuffer
{
    private readonly int _points;
    private readonly float[] _min;
    private readonly float[] _max;

    /// <summary>Creates a buffer producing <paramref name="points"/> envelope points (default 128).</summary>
    public WaveformBuffer(int points = 128)
    {
        if (points < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(points), points, "Point count must be at least 1.");
        }

        _points = points;
        _min = new float[points];
        _max = new float[points];
    }

    /// <summary>The number of envelope points produced.</summary>
    public int Points => _points;

    /// <summary>The per-bucket minimum sample, each in -1..1. The backing array is reused each frame.</summary>
    public IReadOnlyList<float> Min => _min;

    /// <summary>The per-bucket maximum sample, each in -1..1. The backing array is reused each frame.</summary>
    public IReadOnlyList<float> Max => _max;

    /// <summary>
    /// Downsamples <paramref name="monoFrame"/> into <see cref="Points"/> min/max buckets. An empty frame yields
    /// all-zero envelopes. Samples are clamped to -1..1 so the envelope always stays in range.
    /// </summary>
    public void Process(ReadOnlySpan<float> monoFrame)
    {
        if (monoFrame.Length == 0)
        {
            Array.Clear(_min);
            Array.Clear(_max);
            return;
        }

        for (int p = 0; p < _points; p++)
        {
            long start = (long)p * monoFrame.Length / _points;
            long end = (long)(p + 1) * monoFrame.Length / _points;
            if (end <= start)
            {
                end = start + 1; // guarantee at least one sample per bucket
            }

            float lo = float.PositiveInfinity;
            float hi = float.NegativeInfinity;
            for (long i = start; i < end && i < monoFrame.Length; i++)
            {
                float s = Math.Clamp(monoFrame[(int)i], -1f, 1f);
                if (s < lo)
                {
                    lo = s;
                }

                if (s > hi)
                {
                    hi = s;
                }
            }

            if (float.IsInfinity(lo))
            {
                lo = 0f;
                hi = 0f;
            }

            _min[p] = lo;
            _max[p] = hi;
        }
    }
}
