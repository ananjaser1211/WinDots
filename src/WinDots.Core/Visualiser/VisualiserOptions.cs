namespace WinDots.Core.Visualiser;

/// <summary>
/// Runtime tunables for the visualiser, bridged from the settings <c>visualiser</c> section. Immutable; a new
/// instance is created when settings change. See _docs/10-enhancement-plan.md (E5).
/// </summary>
public sealed record VisualiserOptions
{
    /// <summary>Minimum number of frequency bands.</summary>
    public const int MinBars = 24;

    /// <summary>Maximum number of frequency bands.</summary>
    public const int MaxBars = 96;

    /// <summary>Whether the visualiser is active. Off by default.</summary>
    public bool Enabled { get; init; }

    /// <summary>The render style.</summary>
    public VisualiserStyle Style { get; init; } = VisualiserStyle.Ring;

    /// <summary>Where the visualiser sits relative to the artwork.</summary>
    public VisualiserPlacement Placement { get; init; } = VisualiserPlacement.BehindArt;

    /// <summary>Number of frequency bands (clamped to <see cref="MinBars"/>..<see cref="MaxBars"/> by <see cref="ClampedBars"/>).</summary>
    public int Bars { get; init; } = 60;

    /// <summary>Decay smoothing factor in 0..1; higher is smoother (slower to fall). Attack is always faster.</summary>
    public double Smoothing { get; init; } = 0.6;

    /// <summary>Mirror the bands about the centre.</summary>
    public bool Mirrored { get; init; }

    /// <summary><see cref="Bars"/> clamped to the supported range.</summary>
    public int ClampedBars => Math.Clamp(Bars, MinBars, MaxBars);
}
