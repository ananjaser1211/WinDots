using System.Buffers.Binary;
using Windows.Storage.Streams;

namespace WinDots.TestPlayer;

/// <summary>Builds tiny solid-colour BMP images in memory so the fake player can publish artwork without asset files.</summary>
public static class BitmapFactory
{
    public static IRandomAccessStream SolidColourBmp(int width, int height, byte r, byte g, byte b)
    {
        var rowBytes = ((width * 3 + 3) / 4) * 4;
        var pixelBytes = rowBytes * height;
        var fileSize = 54 + pixelBytes;
        var buffer = new byte[fileSize];
        var span = buffer.AsSpan();

        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span[2..], fileSize);
        BinaryPrimitives.WriteInt32LittleEndian(span[10..], 54);
        BinaryPrimitives.WriteInt32LittleEndian(span[14..], 40);
        BinaryPrimitives.WriteInt32LittleEndian(span[18..], width);
        BinaryPrimitives.WriteInt32LittleEndian(span[22..], height);
        BinaryPrimitives.WriteInt16LittleEndian(span[26..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[28..], 24);
        BinaryPrimitives.WriteInt32LittleEndian(span[34..], pixelBytes);
        BinaryPrimitives.WriteInt32LittleEndian(span[38..], 2835);
        BinaryPrimitives.WriteInt32LittleEndian(span[42..], 2835);

        for (var y = 0; y < height; y++)
        {
            var row = 54 + y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                var i = row + x * 3;
                buffer[i] = b;
                buffer[i + 1] = g;
                buffer[i + 2] = r;
            }
        }

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
