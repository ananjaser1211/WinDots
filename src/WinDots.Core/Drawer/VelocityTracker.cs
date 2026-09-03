using WinDots.Core.Contracts;

namespace WinDots.Core.Drawer;

/// <summary>
/// Vertical pointer velocity as a windowed average over the most recent samples.
/// Time comes only from sample timestamps, so the tracker is deterministic and timer-free.
/// </summary>
public sealed class VelocityTracker
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMilliseconds(60);

    private readonly List<PointerSample> samples = new(16);
    private readonly TimeSpan window;

    public VelocityTracker()
        : this(DefaultWindow)
    {
    }

    public VelocityTracker(TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), window, "Window must be positive.");
        }

        this.window = window;
    }

    public TimeSpan Window => window;

    public int SampleCount => samples.Count;

    /// <summary>
    /// Average vertical velocity in px/s across the window; positive means downward (Y increasing).
    /// Zero until two samples with distinct timestamps are present.
    /// </summary>
    public double VelocityPxPerSecond
    {
        get
        {
            if (samples.Count < 2)
            {
                return 0;
            }

            var first = samples[0];
            var last = samples[^1];
            var seconds = (last.Timestamp - first.Timestamp).TotalSeconds;
            if (seconds <= 0)
            {
                return 0;
            }

            return (last.Y - first.Y) / seconds;
        }
    }

    /// <summary>Records a sample and evicts every sample older than <see cref="Window"/> relative to it.</summary>
    public void Add(PointerSample sample)
    {
        // A sample that goes backwards in time restarts the window; the view's clock is monotonic so this is defensive.
        if (samples.Count > 0 && sample.Timestamp < samples[^1].Timestamp)
        {
            samples.Clear();
        }

        samples.Add(sample);
        Evict(sample.Timestamp);
    }

    public void Clear() => samples.Clear();

    private void Evict(TimeSpan now)
    {
        var cutoff = now - window;
        var stale = 0;
        while (stale < samples.Count - 1 && samples[stale].Timestamp < cutoff)
        {
            stale++;
        }

        if (stale > 0)
        {
            samples.RemoveRange(0, stale);
        }
    }
}
