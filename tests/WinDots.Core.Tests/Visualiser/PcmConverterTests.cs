using System.Buffers.Binary;
using WinDots.Core.Visualiser;

namespace WinDots.Core.Tests.Visualiser;

public class PcmConverterTests
{
    [Theory]
    [InlineData(PcmConverter.WaveFormatIeeeFloat, 32, false, PcmSampleFormat.Float32)]
    [InlineData(PcmConverter.WaveFormatPcm, 16, false, PcmSampleFormat.Int16)]
    [InlineData(PcmConverter.WaveFormatPcm, 24, false, PcmSampleFormat.Int24)]
    [InlineData(PcmConverter.WaveFormatPcm, 32, false, PcmSampleFormat.Int32)]
    [InlineData(PcmConverter.WaveFormatExtensible, 32, true, PcmSampleFormat.Float32)]
    [InlineData(PcmConverter.WaveFormatExtensible, 16, false, PcmSampleFormat.Int16)]
    public void ResolvesSupportedFormats(int tag, int bits, bool subFloat, PcmSampleFormat expected)
    {
        Assert.Equal(expected, PcmConverter.ResolveFormat(tag, bits, subFloat));
    }

    [Theory]
    [InlineData(PcmConverter.WaveFormatIeeeFloat, 64, false)] // 64-bit float unsupported
    [InlineData(PcmConverter.WaveFormatPcm, 8, false)] // 8-bit PCM unsupported
    [InlineData(0x1234, 16, false)] // unknown tag
    [InlineData(PcmConverter.WaveFormatExtensible, 20, false)] // odd PCM depth
    public void RejectsUnsupportedFormats(int tag, int bits, bool subFloat)
    {
        Assert.Null(PcmConverter.ResolveFormat(tag, bits, subFloat));
    }

    [Theory]
    [InlineData(PcmSampleFormat.Float32, 4)]
    [InlineData(PcmSampleFormat.Int16, 2)]
    [InlineData(PcmSampleFormat.Int24, 3)]
    [InlineData(PcmSampleFormat.Int32, 4)]
    public void BytesPerSampleMatchesEncoding(PcmSampleFormat format, int expected)
    {
        Assert.Equal(expected, PcmConverter.BytesPerSample(format));
    }

    [Fact]
    public void ConvertsFloat32Verbatim()
    {
        float[] source = { 0.25f, -0.5f, 1f, -1f };
        byte[] bytes = new byte[source.Length * 4];
        for (int i = 0; i < source.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4, 4), source[i]);
        }

        float[] result = PcmConverter.Convert(bytes, PcmSampleFormat.Float32);
        Assert.Equal(source, result);
    }

    [Fact]
    public void ConvertsInt16ToNormalisedRange()
    {
        byte[] bytes = new byte[6];
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0, 2), 0);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2, 2), short.MaxValue);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4, 2), short.MinValue);

        float[] result = PcmConverter.Convert(bytes, PcmSampleFormat.Int16);

        Assert.Equal(0f, result[0]);
        Assert.InRange(result[1], 0.999f, 1f);
        Assert.Equal(-1f, result[2]);
    }

    [Fact]
    public void ConvertsInt24SignExtendsNegatives()
    {
        // Full-scale negative is 0x800000 (LE 0x00 0x00 0x80); zero is 0x000000; near-full positive is 0x7FFFFF.
        byte[] bytes = { 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x7F };
        float[] result = PcmConverter.Convert(bytes, PcmSampleFormat.Int24);

        Assert.Equal(-1f, result[0]);
        Assert.Equal(0f, result[1]);
        Assert.InRange(result[2], 0.999f, 1f);
    }

    [Fact]
    public void ConvertsInt32ToNormalisedRange()
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), int.MinValue);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), 0);

        float[] result = PcmConverter.Convert(bytes, PcmSampleFormat.Int32);

        Assert.Equal(-1f, result[0]);
        Assert.Equal(0f, result[1]);
    }

    [Fact]
    public void IgnoresTrailingPartialSample()
    {
        // Five bytes of Int16 data: two whole samples plus a dangling byte.
        byte[] bytes = { 0x00, 0x00, 0x00, 0x40, 0x11 };
        float[] result = PcmConverter.Convert(bytes, PcmSampleFormat.Int16);
        Assert.Equal(2, result.Length);
    }
}
