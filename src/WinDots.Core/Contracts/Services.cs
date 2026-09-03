using WinDots.Core.Media;

namespace WinDots.Core.Contracts;

/// <summary>Accessible colour tokens derived from artwork. See _docs/04-visual-design.md.</summary>
public sealed record Palette(uint Accent, uint OnAccent, uint AccentContainer, uint BlobTint, bool IsFallback);

public interface IPaletteService
{
    Palette FromArtwork(ReadOnlySpan<byte> bgra, int width, int height, bool darkTheme);

    Palette Fallback(bool darkTheme);
}

public sealed record CachedArtwork(string Key, ReadOnlyMemory<byte> Bytes, string? ContentType);

public interface IArtworkCache
{
    Task<CachedArtwork?> GetOrAddAsync(string key, Func<CancellationToken, Task<ArtworkResult>> loader, CancellationToken ct);
}

public interface ISecretStore
{
    Task<string?> GetAsync(string key, CancellationToken ct);

    Task SetAsync(string key, string value, CancellationToken ct);

    Task DeleteAsync(string key, CancellationToken ct);
}
