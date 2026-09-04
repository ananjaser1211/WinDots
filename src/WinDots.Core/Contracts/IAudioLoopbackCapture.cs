namespace WinDots.Core.Contracts;

/// <summary>
/// One captured buffer of audio. <see cref="Samples"/> is interleaved when <see cref="Channels"/> &gt; 1.
/// Never persisted to disk. Down-mix with <see cref="WinDots.Core.Visualiser.AudioMixer.DownmixToMono"/>.
/// </summary>
public sealed record AudioFrame(float[] Samples, int SampleRate, int Channels);

/// <summary>
/// Abstraction over a WASAPI-style loopback capture of the default render endpoint. Implemented in
/// <c>WinDots.Windows</c>; faked in tests so Core and App code consume the interface only. See E5.
/// </summary>
public interface IAudioLoopbackCapture : IDisposable
{
    /// <summary>Raised on each captured buffer. May fire on a capture thread; consumers must marshal as needed.</summary>
    event EventHandler<AudioFrame>? FrameAvailable;

    /// <summary>True between a successful <see cref="Start"/> and the next <see cref="Stop"/>.</summary>
    bool IsCapturing { get; }

    /// <summary>Begins capture. <paramref name="ct"/> cancellation stops it. Safe to call when already capturing.</summary>
    void Start(CancellationToken ct);

    /// <summary>Stops capture and releases the capture resources. Safe to call when not capturing.</summary>
    void Stop();
}
