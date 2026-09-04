using System.Buffers.Binary;
using Windows.Storage.Streams;

namespace WinDots.TestPlayer;

/// <summary>Builds a tiny silent PCM WAV in memory so the fake player can open a real audio render session.</summary>
public static class WavFactory
{
    public static IRandomAccessStream SilentWav(TimeSpan duration)
    {
        const int sampleRate = 44100;
        const short channels = 1;
        const short bitsPerSample = 16;
        var blockAlign = channels * bitsPerSample / 8;
        var byteRate = sampleRate * blockAlign;
        var frames = (int)Math.Max(1, duration.TotalSeconds * sampleRate);
        var dataBytes = frames * blockAlign;
        var fileSize = 44 + dataBytes;

        var buffer = new byte[fileSize];
        var span = buffer.AsSpan();

        span[0] = (byte)'R';
        span[1] = (byte)'I';
        span[2] = (byte)'F';
        span[3] = (byte)'F';
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], fileSize - 8);
        span[8] = (byte)'W';
        span[9] = (byte)'A';
        span[10] = (byte)'V';
        span[11] = (byte)'E';

        span[12] = (byte)'f';
        span[13] = (byte)'m';
        span[14] = (byte)'t';
        span[15] = (byte)' ';
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], (short)blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], bitsPerSample);

        span[36] = (byte)'d';
        span[37] = (byte)'a';
        span[38] = (byte)'t';
        span[39] = (byte)'a';
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataBytes);
        // The sample data stays zero: silence.

        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(buffer);
            writer.StoreAsync().AsTask().GetAwaiter().GetResult();
            writer.FlushAsync().AsTask().GetAwaiter().GetResult();
            writer.DetachStream();
        }

        stream.Seek(0);
        return stream;
    }
}
