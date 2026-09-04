using System.Globalization;

namespace WinDots.Core.Media;

/// <summary>
/// Pure helpers for interpreting a media session's source application id (the AUMID for packaged players, an
/// executable name for unpackaged ones) when resolving its icon: a stable cache key and a split into package family
/// name vs executable name. BCL-only so it can be unit-tested in Core; the platform <c>IAppIconProvider</c> uses it.
/// </summary>
/// <remarks>
/// A packaged AUMID is <c>&lt;PackageFamilyName&gt;!&lt;AppId&gt;</c> (for example
/// <c>Spotify.SpotifyMusic_zpdnekdrzrea0!Spotify</c>); an unpackaged id is a bare executable name (for example
/// <c>chrome.exe</c> or <c>foobar2000</c>). The <c>'!'</c> separator is the discriminator.
/// </remarks>
public static class AppIconKey
{
    /// <summary>
    /// A stable, case-insensitive cache key for an app id: trimmed and lower-cased with the invariant culture. Returns
    /// the empty string for a null or whitespace id.
    /// </summary>
    public static string Normalize(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return string.Empty;
        }

        return appId.Trim().ToLowerInvariant();
    }

    /// <summary>True when the id is a packaged AUMID (contains the <c>'!'</c> app-id separator with a family name before it).</summary>
    public static bool IsPackaged(string? appId) => PackageFamilyName(appId) is not null;

    /// <summary>
    /// The package family name from a packaged AUMID (the part before <c>'!'</c>), or <see langword="null"/> for an
    /// unpackaged id or when the family part is empty.
    /// </summary>
    public static string? PackageFamilyName(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return null;
        }

        int bang = appId.IndexOf('!', StringComparison.Ordinal);
        if (bang <= 0)
        {
            return null;
        }

        string family = appId[..bang].Trim();
        return family.Length == 0 ? null : family;
    }

    /// <summary>
    /// The executable name for an unpackaged id, without any trailing <c>.exe</c>. Returns <see langword="null"/> for a
    /// packaged AUMID or a null/whitespace id.
    /// </summary>
    public static string? ExecutableName(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId) || IsPackaged(appId))
        {
            return null;
        }

        string name = appId.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.Length == 0 ? null : name;
    }
}
