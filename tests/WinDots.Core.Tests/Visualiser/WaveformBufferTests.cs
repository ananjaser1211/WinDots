using WinDots.Core.Visualiser;

namespace WinDots.Core.Tests.Visualiser;

public class WaveformBufferTests
{
    [Fact]
    public void EnvelopeStaysWithinRange()
    {
        var buffer = new WaveformBuffer(32);
        float[] frame = new float[2048];
        for (int i = 0; i < frame.Length; i++)
        {
            frame[i] = (float)Math.Sin(2.0 * Math.PI * i / 64.0);
        }

        buffer.Process(frame);

        for (int p = 0; p < buffer.Points; p++)
        {
            Assert.InRange(buffer.Min[p], -1f, 1f);
            Assert.InRange(buffer.Max[p], -1f, 1f);
            Assert.True(buffer.Min[p] <= buffer.Max[p], $"min>max at {p}");
        }
    }

    [Fact]
    public void OutOfRangeSamplesAreClamped()
    {
        var buffer = new WaveformBuffer(4);
        float[] frame = { 5f, -5f, 100f, -100f, 2f, -2f, 3f, -3f };
        buffer.Process(frame);

        for (int p = 0; p < buffer.Points; p++)
        {
            Assert.InRange(buffer.Min[p], -1f, 1f);
            Assert.InRange(buffer.Max[p], -1f, 1f);
        }
    }

    [Fact]
    public void EmptyFrameYieldsZeroEnvelope()
    {
        var buffer = new WaveformBuffer(8);
        buffer.Process(ReadOnlySpan<float>.Empty);
        for (int p = 0; p < buffer.Points; p++)
        {
            Assert.Equal(0f, buffer.Min[p]);
            Assert.Equal(0f, buffer.Max[p]);
        }
    }

    [Fact]
    public void CapturesPerBucketExtremes()
    {
        var buffer = new WaveformBuffer(2);
        // Bucket 0: [0.2, -0.4]; bucket 1: [0.9, -0.1]
        float[] frame = { 0.2f, -0.4f, 0.9f, -0.1f };
        buffer.Process(frame);

        Assert.Equal(-0.4f, buffer.Min[0], 6);
        Assert.Equal(0.2f, buffer.Max[0], 6);
        Assert.Equal(-0.1f, buffer.Min[1], 6);
        Assert.Equal(0.9f, buffer.Max[1], 6);
    }

    [Fact]
    public void InvalidPointCountThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WaveformBuffer(0));
    }
}
