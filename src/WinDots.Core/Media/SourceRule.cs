namespace WinDots.Core.Media;

/// <summary>
/// A source rule: a match pattern (an exact AUMID or a case-insensitive substring of the AUMID or display name)
/// and the <see cref="SourceRuleMode"/> to apply. See _docs/10-enhancement-plan.md (E1) and _docs/06-settings-schema.md.
/// </summary>
public sealed record SourceRule(string Match, SourceRuleMode Mode)
{
    /// <summary>Parameterless-friendly default so serialisers can round-trip the record.</summary>
    public SourceRule()
        : this(string.Empty, SourceRuleMode.Auto)
    {
    }

    /// <summary>
    /// The built-in defaults: dedicated music players are Always; browsers and the Windows media player are Auto
    /// (the detector decides per session); communication, generic video, and game apps are Never.
    /// </summary>
    public static IReadOnlyList<SourceRule> Defaults { get; } = new SourceRule[]
    {
        // Dedicated music players -> Always.
        new("Spotify", SourceRuleMode.Always),
        new("AppleMusic", SourceRuleMode.Always),
        new("Apple Music", SourceRuleMode.Always),
        new("AmazonMusic", SourceRuleMode.Always),
        new("Amazon Music", SourceRuleMode.Always),
        new("Tidal", SourceRuleMode.Always),
        new("Deezer", SourceRuleMode.Always),
        new("MusicBee", SourceRuleMode.Always),
        new("foobar2000", SourceRuleMode.Always),
        new("YouTube Music", SourceRuleMode.Always),

        // Browsers and the Windows media player -> Auto.
        new("Chrome", SourceRuleMode.Auto),
        new("msedge", SourceRuleMode.Auto),
        new("Microsoft Edge", SourceRuleMode.Auto),
        new("Firefox", SourceRuleMode.Auto),
        new("Brave", SourceRuleMode.Auto),
        new("ZuneMusic", SourceRuleMode.Auto),
        new("wmplayer", SourceRuleMode.Auto),

        // Communication, generic video, and games -> Never.
        new("Teams", SourceRuleMode.Never),
        new("Zoom", SourceRuleMode.Never),
        new("Discord", SourceRuleMode.Never),
        new("VLC", SourceRuleMode.Never),
        new("mpv", SourceRuleMode.Never),
        new("Steam", SourceRuleMode.Never),
    };
}
