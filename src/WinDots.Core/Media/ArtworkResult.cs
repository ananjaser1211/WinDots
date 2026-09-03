namespace WinDots.Core.Media;

/// <summary>Raw, bounded artwork bytes. Decoding happens in the presentation layer.</summary>
public sealed record ArtworkResult(bool Success, ReadOnlyMemory<byte> Bytes, string? ContentType, string? Error)
{
    public static ArtworkResult None { get; } = new(false, ReadOnlyMemory<byte>.Empty, null, "No artwork available.");

    public static ArtworkResult Failed(string error) => new(false, ReadOnlyMemory<byte>.Empty, null, error);

    public static ArtworkResult Loaded(ReadOnlyMemory<byte> bytes, string? contentType) => new(true, bytes, contentType, null);
}
