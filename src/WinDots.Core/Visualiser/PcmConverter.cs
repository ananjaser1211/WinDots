using System.Buffers.Binary;

namespace WinDots.Core.Visualiser;

/// <summary>
/// The interleaved sample encodings a WASAPI capture buffer can carry. Resolved once from the endpoint mix
/// format, then used to reinterpret each raw buffer as normalised <c>float</c> samples in roughly -1..1.
/// </summary>
public enum PcmSampleFormat
{
    /// <summary>32-bit IEEE little-endian float (the usual shared-mode mix format).</summary>
    Float32,

    /// <summary>Signed 16-bit little-endian PCM.</summary>
    Int16,

    /// <summary>Packed signed 24-bit little-endian PCM (3 bytes per sample).</summary>
    Int24,

    /// <summary>Signed 32-bit little-endian PCM.</summary>
    Int32,
}

/// <summary>
/// Pure, BCL-only helpers that decide how to read a capture buffer and turn its raw bytes into normalised
/// <c>float</c> samples. Kept out of the platform layer so the branch-heavy conversion logic is unit-tested in
/// <c>WinDots.Core.Tests</c> without opening an audio device. See <c>_docs/05-architecture.md</c> (visualiser capture).
/// </summary>
public static class PcmConverter
{
    /// <summary><c>WAVE_FORMAT_PCM</c> tag.</summary>
    public const int WaveFormatPcm = 0x0001;

    /// <summary><c>WAVE_FORMAT_IEEE_FLOAT</c> tag.</summary>
    public const int WaveFormatIeeeFloat = 0x0003;

    /// <summary><c>WAVE_FORMAT_EXTENSIBLE</c> tag; the true encoding is then read from the sub-format GUID.</summary>
    public const int WaveFormatExtensible = 0xFFFE;

    /// <summary>
    /// Resolves the sample encoding from a <c>WAVEFORMATEX</c>'s tag and bit depth. For
    /// <see cref="WaveFormatExtensible"/>, <paramref name="subFormatIsFloat"/> comes from the sub-format GUID
    /// (<c>KSDATAFORMAT_SUBTYPE_IEEE_FLOAT</c> vs <c>_PCM</c>). Returns <see langword="null"/> for an unsupported
    /// combination so the caller can decline to capture rather than emit garbage.
    /// </summary>
    public static PcmSampleFormat? ResolveFormat(int formatTag, int bitsPerSample, bool subFormatIsFloat)
    {
        bool isFloat = formatTag == WaveFormatIeeeFloat
            || (formatTag == WaveFormatExtensible && subFormatIsFloat);
        bool isPcm = formatTag == WaveFormatPcm
            || (formatTag == WaveFormatExtensible && !subFormatIsFloat);

        if (isFloat)
        {
            return bitsPerSample == 32 ? PcmSampleFormat.Float32 : null;
        }

        if (isPcm)
        {
            return bitsPerSample switch
            {
                16 => PcmSampleFormat.Int16,
                24 => PcmSampleFormat.Int24,
                32 => PcmSampleFormat.Int32,
                _ => null,
            };
        }

        return null;
    }

    /// <summary>Bytes occupied by one sample (one channel of one frame) in the given encoding.</summary>
    public static int BytesPerSample(PcmSampleFormat format) => format switch
    {
        PcmSampleFormat.Float32 => 4,
        PcmSampleFormat.Int16 => 2,
        PcmSampleFormat.Int24 => 3,
        PcmSampleFormat.Int32 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown sample format."),
    };

    /// <summary>
    /// Converts a raw interleaved capture buffer to normalised <c>float</c> samples in roughly -1..1. A trailing
    /// partial sample is ignored. The result stays interleaved; down-mix with <see cref="AudioMixer.DownmixToMono"/>.
    /// </summary>
    public static float[] Convert(ReadOnlySpan<byte> data, PcmSampleFormat format)
    {
        int stride = BytesPerSample(format);
        int count = data.Length / stride;
        float[] samples = new float[count];

        switch (format)
        {
            case PcmSampleFormat.Float32:
                for (int i = 0; i < count; i++)
                {
                    samples[i] = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(i * 4, 4));
                }

                break;

            case PcmSampleFormat.Int16:
                for (int i = 0; i < count; i++)
                {
                    short v = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(i * 2, 2));
                    samples[i] = v / 32768f;
                }

                break;

            case PcmSampleFormat.Int24:
                for (int i = 0; i < count; i++)
                {
                    int b0 = data[(i * 3) + 0];
                    int b1 = data[(i * 3) + 1];
                    int b2 = data[(i * 3) + 2];
                    int v = b0 | (b1 << 8) | (b2 << 16);
                    if ((v & 0x0080_0000) != 0)
                    {
                        v |= unchecked((int)0xFF00_0000); // sign-extend the 24-bit value.
                    }

                    samples[i] = v / 8388608f;
                }

                break;

            case PcmSampleFormat.Int32:
                for (int i = 0; i < count; i++)
                {
                    int v = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(i * 4, 4));
                    samples[i] = (float)(v / 2147483648.0);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown sample format.");
        }

        return samples;
    }
}
