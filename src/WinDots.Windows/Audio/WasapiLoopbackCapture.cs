using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.System.Com;
using WinDots.Core.Contracts;
using WinDots.Core.Visualiser;

namespace WinDots.Windows.Audio;

/// <summary>
/// Captures the system's mixed render output via WASAPI loopback on the default render endpoint and raises it as
/// interleaved <see cref="AudioFrame"/>s for the visualiser (E5). Audio is analysed in memory only and never written
/// to disk or transmitted.
/// </summary>
/// <remarks>
/// <para>
/// Every COM object (device enumerator, endpoint, <see cref="IAudioClient"/>, <see cref="IAudioCaptureClient"/>) is
/// created and used on a single dedicated MTA capture thread, mirroring the single-thread rule in
/// <see cref="Threading.MediaDispatcher"/> and <see cref="CoreAudioSessionProvider"/>: WASAPI interface pointers must
/// not be marshalled across threads. The thread initialises the client in shared loopback mode, then polls
/// <see cref="IAudioCaptureClient.GetNextPacketSize"/> on a short interval (event-driven capture is unreliable for
/// loopback during silence), converts each packet to <c>float</c> via <see cref="PcmConverter"/>, and raises
/// <see cref="FrameAvailable"/>. It releases every COM object deterministically in its <c>finally</c> before exiting.
/// </para>
/// <para>
/// A default-render-device change is not re-attached automatically; capture continues on the original endpoint until
/// the next <see cref="Stop"/>/<see cref="Start(CancellationToken)"/>. Re-attach on device change is a documented
/// follow-up (see <c>_docs/10-enhancement-plan.md</c> E5).
/// </para>
/// </remarks>
public sealed class WasapiLoopbackCapture : IAudioLoopbackCapture
{
    // 200 ms endpoint buffer; comfortably larger than the poll interval so no packet is dropped.
    private const long BufferDurationHns = 200 * 10_000;

    // Poll cadence. ~10 ms yields ~480-sample frames at 48 kHz, plenty for a 2048-point FFT accumulator.
    private const int PollIntervalMs = 10;

    // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT (mmreg.h): {00000003-0000-0010-8000-00aa00389b71}.
    private static readonly Guid KsSubtypeIeeeFloat =
        new(0x00000003, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71);

    private readonly object _gate = new();

    private Thread? _thread;
    private volatile bool _stopRequested;
    private CancellationTokenRegistration _ctRegistration;
    private volatile bool _capturing;
    private bool _disposed;

    public event EventHandler<AudioFrame>? FrameAvailable;

    public bool IsCapturing => _capturing;

    public void Start(CancellationToken ct)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is not null)
            {
                return;
            }

            _stopRequested = false;
            _ctRegistration = ct.CanBeCanceled ? ct.Register(Stop) : default;

            _thread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Name = "WinDots.Visualiser.Capture",
            };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        CancellationTokenRegistration registration;
        lock (_gate)
        {
            thread = _thread;
            registration = _ctRegistration;
            _ctRegistration = default;
            _thread = null;
            _stopRequested = true;
        }

        registration.Dispose();

        if (thread is not null && thread != Thread.CurrentThread)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }

        _capturing = false;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
        FrameAvailable = null;
    }

    // --- Capture thread (all COM lives here) ---

    private void CaptureLoop()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? endpoint = null;
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        var started = false;

        try
        {
            PInvoke.CoCreateInstance<IMMDeviceEnumerator>(
                typeof(MMDeviceEnumerator).GUID,
                null,
                CLSCTX.CLSCTX_ALL,
                out enumerator).ThrowOnFailure();

            enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out endpoint);

            object clientObj;
            unsafe
            {
                var iid = typeof(IAudioClient).GUID;
                endpoint.Activate(&iid, CLSCTX.CLSCTX_ALL, null, out clientObj);
            }

            client = (IAudioClient)clientObj;

            int channels;
            int sampleRate;
            PcmSampleFormat format;
            unsafe
            {
                WAVEFORMATEX* mix;
                client.GetMixFormat(&mix);
                try
                {
                    if (!TryResolveFormat(mix, out channels, out sampleRate, out format))
                    {
                        return; // Unsupported mix format; decline rather than emit garbage.
                    }

                    client.Initialize(
                        AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED,
                        PInvoke.AUDCLNT_STREAMFLAGS_LOOPBACK,
                        BufferDurationHns,
                        0,
                        mix,
                        null);
                }
                finally
                {
                    PInvoke.CoTaskMemFree(mix);
                }
            }

            capture = GetCaptureClient(client);

            client.Start();
            started = true;
            _capturing = true;

            int bytesPerSample = PcmConverter.BytesPerSample(format);
            int blockAlign = bytesPerSample * channels;

            while (!_stopRequested)
            {
                DrainPackets(capture, channels, sampleRate, format, blockAlign);
                if (_stopRequested)
                {
                    break;
                }

                Thread.Sleep(PollIntervalMs);
            }
        }
        catch (COMException)
        {
            // No usable render endpoint, activation refused, or the device vanished mid-capture. Stop quietly;
            // the consumer sees IsCapturing flip to false.
        }
        finally
        {
            _capturing = false;

            // If the loop exited on its own (unsupported mix format, or a COMException such as the default render device
            // changing or the endpoint vanishing) rather than via Stop(), clear the thread field so a later Start() can
            // spin up a fresh capture thread. Stop() nulls _thread before joining, so when it drove the exit this guard
            // sees a different (or null) reference and leaves its bookkeeping alone. Guarded by _gate against a racing
            // Start()/Stop().
            lock (_gate)
            {
                if (_thread == Thread.CurrentThread)
                {
                    _thread = null;
                    _stopRequested = false;
                    _ctRegistration.Dispose();
                    _ctRegistration = default;
                }
            }

            if (started && client is not null)
            {
                try
                {
                    client.Stop();
                }
                catch (COMException)
                {
                    // Endpoint already gone.
                }
            }

            if (capture is not null)
            {
                Marshal.FinalReleaseComObject(capture);
            }

            if (client is not null)
            {
                Marshal.FinalReleaseComObject(client);
            }

            if (endpoint is not null)
            {
                Marshal.FinalReleaseComObject(endpoint);
            }

            if (enumerator is not null)
            {
                Marshal.FinalReleaseComObject(enumerator);
            }
        }
    }

    private void DrainPackets(
        IAudioCaptureClient capture,
        int channels,
        int sampleRate,
        PcmSampleFormat format,
        int blockAlign)
    {
        while (!_stopRequested)
        {
            uint packetFrames;
            capture.GetNextPacketSize(out packetFrames);
            if (packetFrames == 0)
            {
                return; // AUDCLNT_S_BUFFER_EMPTY: nothing queued right now.
            }

            uint framesRead;
            float[]? samples = null;
            unsafe
            {
                byte* data;
                capture.GetBuffer(&data, out framesRead, out uint flags, null, null);
                try
                {
                    if (framesRead > 0)
                    {
                        int byteCount = (int)framesRead * blockAlign;
                        if ((flags & (uint)_AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT) != 0)
                        {
                            // Silent packet: the buffer contents are undefined, so emit zeros of the right shape.
                            samples = new float[(int)framesRead * channels];
                        }
                        else
                        {
                            var span = new ReadOnlySpan<byte>(data, byteCount);
                            samples = PcmConverter.Convert(span, format);
                        }
                    }
                }
                finally
                {
                    capture.ReleaseBuffer(framesRead);
                }
            }

            if (samples is { Length: > 0 })
            {
                FrameAvailable?.Invoke(this, new AudioFrame(samples, sampleRate, channels));
            }
        }
    }

    private static unsafe bool TryResolveFormat(
        WAVEFORMATEX* mix,
        out int channels,
        out int sampleRate,
        out PcmSampleFormat format)
    {
        channels = mix->nChannels;
        sampleRate = (int)mix->nSamplesPerSec;
        int bits = mix->wBitsPerSample;
        int tag = mix->wFormatTag;

        bool subtypeIsFloat = false;
        if (tag == PcmConverter.WaveFormatExtensible)
        {
            // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT == {00000003-0000-0010-8000-00aa00389b71}; the first field is the wave
            // format tag. Comparing against the built GUID avoids depending on a constant CsWin32 does not project.
            var extensible = (WAVEFORMATEXTENSIBLE*)mix;
            subtypeIsFloat = extensible->SubFormat == KsSubtypeIeeeFloat;
        }

        PcmSampleFormat? resolved = PcmConverter.ResolveFormat(tag, bits, subtypeIsFloat);
        if (resolved is null || channels < 1)
        {
            format = default;
            return false;
        }

        format = resolved.Value;
        return true;
    }

    private static IAudioCaptureClient GetCaptureClient(IAudioClient client)
    {
        object service;
        unsafe
        {
            var iid = typeof(IAudioCaptureClient).GUID;
            client.GetService(&iid, out service);
        }

        return (IAudioCaptureClient)service;
    }
}
