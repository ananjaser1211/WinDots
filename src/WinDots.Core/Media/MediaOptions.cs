namespace WinDots.Core.Media;

/// <summary>
/// Tunables for media session selection and timeline interpolation. Immutable; a new instance is created when
/// settings change. See _docs/05-architecture.md ("Session coordinator scoring") and _docs/06-settings-schema.md.
/// </summary>
public sealed record MediaOptions
{
    /// <summary>Player pattern that scores +400 when matched. Null disables the preference.</summary>
    public string? PreferredPlayer { get; init; }

    /// <summary>Player patterns excluded from selection entirely.</summary>
    public IReadOnlyList<string> IgnoredPlayers { get; init; } = Array.Empty<string>();

    /// <summary>Maps a player pattern (exact AUMID or case-insensitive substring) to a display alias.</summary>
    public IReadOnlyDictionary<string, string> PlayerAliases { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Timeline interpolation tick interval in milliseconds.</summary>
    public int TimelineTickMs { get; init; } = 500;

    /// <summary>A snapshot captured within this window of "now" scores +100.</summary>
    public TimeSpan RecentActivityWindow { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Resolves the display name for a player. Returns the matching alias when a key matches the AUMID exactly
    /// or is a case-insensitive substring of the AUMID or display name; otherwise returns <paramref name="displayName"/>.
    /// </summary>
    public string AliasFor(string sourceAppId, string displayName)
    {
        foreach (KeyValuePair<string, string> alias in PlayerAliases)
        {
            if (Matches(alias.Key, sourceAppId, displayName))
            {
                return alias.Value;
            }
        }

        return displayName;
    }

    /// <summary>
    /// True when <paramref name="pattern"/> equals <paramref name="sourceAppId"/> (an exact AUMID) or is a
    /// case-insensitive substring of the source app ID or display name.
    /// </summary>
    internal static bool Matches(string pattern, string sourceAppId, string displayName)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        if (string.Equals(pattern, sourceAppId, StringComparison.Ordinal))
        {
            return true;
        }

        return sourceAppId.Contains(pattern, StringComparison.OrdinalIgnoreCase)
            || displayName.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
