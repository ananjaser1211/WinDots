using System.Collections.Concurrent;
using System.Diagnostics;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using WinDots.Core.Contracts;
using WinDots.Core.Media;

namespace WinDots.Windows.AppIcons;

/// <summary>
/// Resolves the real per-application icon for a media session's source application over the WinRT package / Shell
/// APIs, returning encoded PNG bytes. Packaged players (Spotify, Windows Media Player, Groove) resolve their store
/// logo from <see cref="AppInfo"/>; unpackaged players (Chrome, foobar2000) resolve the running executable's icon via
/// the Shell thumbnail of the process's main module.
/// </summary>
/// <remarks>
/// All resolution runs off the UI thread (callers await from a background continuation) and never throws into the
/// caller: any failure -- an unknown AUMID, a Shell error, a race with an exiting process -- resolves to
/// <see langword="null"/>. Results (including negative results) are cached in memory by the normalized app id, so a
/// player is probed at most once per app-id per process. No disk writes, no network.
/// </remarks>
public sealed class AppIconProvider : IAppIconProvider
{
    private const uint LogoSize = 64;

    private readonly ConcurrentDictionary<string, byte[]?> _cache = new(StringComparer.Ordinal);

    public async Task<byte[]?> GetIconAsync(string appId, CancellationToken ct)
    {
        string key = AppIconKey.Normalize(appId);
        if (key.Length == 0)
        {
            return null;
        }

        if (_cache.TryGetValue(key, out byte[]? cached))
        {
            return cached;
        }

        byte[]? bytes = await ResolveAsync(appId, ct).ConfigureAwait(false);
        _cache[key] = bytes;
        return bytes;
    }

    private static async Task<byte[]?> ResolveAsync(string appId, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            // Preferred: the app's registered logo. Works for any AUMID the catalogue knows (all packaged players and
            // some registered unpackaged ones).
            byte[]? logo = await TryGetPackageLogoAsync(appId, ct).ConfigureAwait(false);
            if (logo is not null)
            {
                return logo;
            }

            // Unpackaged fallback: the running executable's Shell icon.
            if (!AppIconKey.IsPackaged(appId))
            {
                return await TryGetExecutableIconAsync(appId, ct).ConfigureAwait(false);
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            // Never throw into callers; an unresolved icon is a null.
            return null;
        }
    }

    private static async Task<byte[]?> TryGetPackageLogoAsync(string appId, CancellationToken ct)
    {
        try
        {
            AppInfo? info = AppInfo.GetFromAppUserModelId(appId);
            AppDisplayInfo? display = info?.DisplayInfo;
            if (display is null)
            {
                return null;
            }

            RandomAccessStreamReference logo = display.GetLogo(new global::Windows.Foundation.Size(LogoSize, LogoSize));
            using IRandomAccessStreamWithContentType stream = await logo.OpenReadAsync().AsTask(ct).ConfigureAwait(false);
            return await ReadAllAsync(stream, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<byte[]?> TryGetExecutableIconAsync(string appId, CancellationToken ct)
    {
        string? exePath = ResolveExecutablePath(appId);
        if (exePath is null)
        {
            return null;
        }

        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(exePath).AsTask(ct).ConfigureAwait(false);
            using StorageItemThumbnail thumb = await file
                .GetThumbnailAsync(ThumbnailMode.SingleItem, LogoSize, ThumbnailOptions.UseCurrentScale)
                .AsTask(ct)
                .ConfigureAwait(false);
            if (thumb is null || thumb.Size == 0)
            {
                return null;
            }

            return await ReadAllAsync(thumb, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static string? ResolveExecutablePath(string appId)
    {
        string? name = AppIconKey.ExecutableName(appId);
        if (name is null)
        {
            return null;
        }

        try
        {
            foreach (Process p in Process.GetProcessesByName(name))
            {
                try
                {
                    string? path = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path))
                    {
                        return path;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // A protected or exited process; try the next one.
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Process table changed underneath us.
        }

        return null;
    }

    private static async Task<byte[]?> ReadAllAsync(IRandomAccessStream stream, CancellationToken ct)
    {
        uint size = (uint)stream.Size;
        if (size == 0)
        {
            return null;
        }

        var buffer = new global::Windows.Storage.Streams.Buffer(size);
        IBuffer read = await stream.ReadAsync(buffer, size, InputStreamOptions.None).AsTask(ct).ConfigureAwait(false);
        if (read.Length == 0)
        {
            return null;
        }

        var bytes = new byte[read.Length];
        using (DataReader reader = DataReader.FromBuffer(read))
        {
            reader.ReadBytes(bytes);
        }

        return bytes;
    }
}
