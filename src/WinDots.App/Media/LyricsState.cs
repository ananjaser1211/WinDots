namespace WinDots.App.Media;

/// <summary>The lyrics slot's state, driving the panel between the disabled prompt, spinner, lines, and placeholder.</summary>
public enum LyricsState
{
    /// <summary>Lyrics are turned off (<c>lyrics.provider == Off</c>); the panel offers to enable them.</summary>
    Off,

    /// <summary>A lookup is in flight for the current track.</summary>
    Loading,

    /// <summary>Lyrics were found and are shown.</summary>
    Found,

    /// <summary>The lookup completed with no lyrics.</summary>
    NotFound,
}
