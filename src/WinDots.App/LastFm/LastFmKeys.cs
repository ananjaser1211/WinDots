using System.Reflection;

namespace WinDots.App.LastFm;

/// <summary>
/// The Last.fm application key and shared secret. Official builds embed them at build time via the
/// <c>WinDotsLastFmApiKey</c> / <c>WinDotsLastFmSecret</c> assembly metadata (fed from environment variables; see
/// _docs/09-dev-environment.md). Source checkouts have neither, so the settings page offers a "Create a key" helper that
/// stores a user-provided key/secret in the secret store instead. Keys are never committed. See _docs/10-enhancement-plan.md (E4).
/// </summary>
public static class LastFmKeys
{
    /// <summary>The build-time API key, or an empty string when the build had none.</summary>
    public static string BuildApiKey { get; } = ReadMetadata("WinDotsLastFmApiKey");

    /// <summary>The build-time shared secret, or an empty string when the build had none.</summary>
    public static string BuildSecret { get; } = ReadMetadata("WinDotsLastFmSecret");

    /// <summary>True when the build embedded both a key and a secret.</summary>
    public static bool HasBuildKey => BuildApiKey.Length > 0 && BuildSecret.Length > 0;

    private static string ReadMetadata(string key)
    {
        foreach (AssemblyMetadataAttribute attribute in typeof(LastFmKeys).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attribute.Key, key, StringComparison.Ordinal))
            {
                return attribute.Value ?? string.Empty;
            }
        }

        return string.Empty;
    }
}
