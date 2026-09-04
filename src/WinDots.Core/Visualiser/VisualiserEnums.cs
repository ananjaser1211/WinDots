namespace WinDots.Core.Visualiser;

/// <summary>
/// How the audio visualiser is rendered. Serialised camelCase in settings (<c>visualiser.style</c>).
/// See _docs/10-enhancement-plan.md (E5) and _docs/04-visual-design.md.
/// </summary>
public enum VisualiserStyle
{
    /// <summary>Vertical bars (a bottom strip by default).</summary>
    Bars,

    /// <summary>A thin min/max waveform line.</summary>
    Waveform,

    /// <summary>Radial bars around the album blob, replacing the dotted ring while audio is active.</summary>
    Ring,

    /// <summary>A soft glow behind the blob that follows energy.</summary>
    Halo,

    /// <summary>The blob amplitude follows energy.</summary>
    BlobPulse,

    /// <summary>Sparse dots orbiting the blob at beat peaks.</summary>
    Particles,
}

/// <summary>
/// Where the visualiser sits relative to the album artwork. Serialised camelCase (<c>visualiser.placement</c>).
/// </summary>
public enum VisualiserPlacement
{
    /// <summary>Under the artwork.</summary>
    UnderArt,

    /// <summary>Overlaid on the artwork.</summary>
    OverArt,

    /// <summary>Behind the artwork.</summary>
    BehindArt,

    /// <summary>Along the bottom of the drawer.</summary>
    Bottom,
}
