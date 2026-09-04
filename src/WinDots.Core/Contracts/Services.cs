using WinDots.Core.Media;

namespace WinDots.Core.Contracts;

/// <summary>Loads and persists <see cref="global::WinDots.Core.Settings.Settings"/>. See _docs/06-settings-schema.md.</summary>
public interface ISettingsStore
{
    /// <summary>The current in-memory settings. Defaults until <see cref="LoadAsync"/> completes.</summary>
    global::WinDots.Core.Settings.Settings Current { get; }

    /// <summary>Raised after <see cref="Current"/> changes (load or save).</summary>
    event EventHandler<global::WinDots.Core.Settings.Settings>? Changed;

    Task LoadAsync(CancellationToken ct);

    Task SaveAsync(global::WinDots.Core.Settings.Settings settings, CancellationToken ct);
}

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
