namespace WinDots.Core.Media;

public enum PlaybackState
{
    Unknown,
    Playing,
    Paused,
    Stopped,
    Changing,
}

public enum RepeatMode
{
    None,
    Track,
    List,
}

public enum MediaKind
{
    Unknown,
    Music,
    Video,
    Image,
}

[Flags]
public enum Capabilities
{
    None = 0,
    Play = 1 << 0,
    Pause = 1 << 1,
    PlayPause = 1 << 2,
    Next = 1 << 3,
    Previous = 1 << 4,
    Seek = 1 << 5,
    Shuffle = 1 << 6,
    Repeat = 1 << 7,
}

public enum CommandStatus
{
    Succeeded,
    Rejected,
    Unsupported,
    Faulted,
}

/// <summary>How the coordinator decides which sources to surface. See _docs/10-enhancement-plan.md (E1).</summary>
public enum SourceMode
{
    /// <summary>Only music sources (Always rules, and Auto rules the detector accepts) are shown.</summary>
    Tracked,

    /// <summary>Every source except <see cref="SourceRuleMode.Never"/> is shown.</summary>
    All,
}

/// <summary>A per-source rule outcome. See _docs/10-enhancement-plan.md (E1).</summary>
public enum SourceRuleMode
{
    /// <summary>Always treated as music and kept regardless of the detector.</summary>
    Always,

    /// <summary>The <see cref="MusicDetector"/> decides per session.</summary>
    Auto,

    /// <summary>Never shown; excluded from selection entirely.</summary>
    Never,
}
