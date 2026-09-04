namespace WinDots.Core.Visualiser;

/// <summary>
/// Pure placement logic for the visualiser: which region a (style, placement) pair renders in, and the artwork-cell
/// z-order for the art placements. Kept in Core (BCL only) so it is deterministic and unit-tested, and consumed by the
/// App's <c>MediaPage</c> to position the two <c>Visualiser</c> instances. See _docs/04-visual-design.md and
/// _docs/10-enhancement-plan.md (E5).
/// </summary>
/// <remarks>
/// Styles fall into two families: art-area styles (<see cref="VisualiserStyle.Ring"/>, <see cref="VisualiserStyle.Halo"/>,
/// <see cref="VisualiserStyle.Particles"/>) draw around the album blob, while strip styles
/// (<see cref="VisualiserStyle.Bars"/>, <see cref="VisualiserStyle.Waveform"/>) draw as a bottom band.
/// <see cref="VisualiserStyle.BlobPulse"/> draws in neither (the page scales the blob itself). Placement decides where an
/// art-area style lands: the three art placements keep it in the artwork cell (at different depths), while
/// <see cref="VisualiserPlacement.Bottom"/> moves it into the strip band. Strip styles always sit in the strip band
/// regardless of placement.
/// </remarks>
public static class VisualiserLayout
{
    /// <summary>Whether the style draws around the album blob (ring / halo / particles).</summary>
    public static bool IsArtStyle(VisualiserStyle style) =>
        style is VisualiserStyle.Ring or VisualiserStyle.Halo or VisualiserStyle.Particles;

    /// <summary>Whether the style draws as a bottom strip (bars / waveform).</summary>
    public static bool IsStripStyle(VisualiserStyle style) =>
        style is VisualiserStyle.Bars or VisualiserStyle.Waveform;

    /// <summary>Whether the placement keeps the visualiser in the artwork cell (under / over / behind the art).</summary>
    public static bool IsArtPlacement(VisualiserPlacement placement) =>
        placement is VisualiserPlacement.UnderArt or VisualiserPlacement.OverArt or VisualiserPlacement.BehindArt;

    /// <summary>
    /// True when the pair renders in the artwork cell: an art-area style whose placement is one of the art placements.
    /// </summary>
    public static bool ShowsInArtArea(VisualiserStyle style, VisualiserPlacement placement) =>
        IsArtStyle(style) && IsArtPlacement(placement);

    /// <summary>
    /// True when the pair renders in the bottom strip band: any strip style, or an art-area style placed at
    /// <see cref="VisualiserPlacement.Bottom"/>.
    /// </summary>
    public static bool ShowsInStrip(VisualiserStyle style, VisualiserPlacement placement) =>
        IsStripStyle(style) || (IsArtStyle(style) && placement == VisualiserPlacement.Bottom);

    /// <summary>
    /// The artwork-cell z-index for an art-area visualiser, relative to the dotted progress ring (0) and the album blob
    /// (1): over the blob, between blob and ring, or behind everything.
    /// </summary>
    public static int ArtZIndex(VisualiserPlacement placement) => placement switch
    {
        VisualiserPlacement.OverArt => 2,
        VisualiserPlacement.UnderArt => 0,
        VisualiserPlacement.BehindArt => -2,
        _ => 0,
    };
}
