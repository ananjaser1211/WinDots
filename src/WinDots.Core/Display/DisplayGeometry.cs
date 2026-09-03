using WinDots.Core.Contracts;

namespace WinDots.Core.Display;

/// <summary>
/// Converts between physical pixels (what the monitor service reports) and logical, DPI-independent units.
/// <see cref="MonitorInfo.Scale"/> is the monitor's DPI divided by 96, so a 150% monitor has a scale of 1.5.
/// </summary>
public static class DisplayGeometry
{
    public const double BaselineDpi = 96.0;

    public static double ScaleFromDpi(double dpi) => dpi <= 0 ? 1.0 : dpi / BaselineDpi;

    public static Rect ToLogical(Rect physical, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        return new Rect(physical.X / scale, physical.Y / scale, physical.Width / scale, physical.Height / scale);
    }

    public static Rect ToPhysical(Rect logical, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        return new Rect(logical.X * scale, logical.Y * scale, logical.Width * scale, logical.Height * scale);
    }

    /// <summary>Logical bounds of a monitor, i.e. its physical bounds divided by its scale.</summary>
    public static Rect LogicalBounds(MonitorInfo monitor) => ToLogical(monitor.Bounds, monitor.Scale);

    /// <summary>Logical work area of a monitor, i.e. its physical work area divided by its scale.</summary>
    public static Rect LogicalWorkArea(MonitorInfo monitor) => ToLogical(monitor.WorkArea, monitor.Scale);

    /// <summary>True when <paramref name="inner"/> lies entirely within <paramref name="outer"/>.</summary>
    public static bool Contains(Rect outer, Rect inner) =>
        inner.X >= outer.X
        && inner.Y >= outer.Y
        && inner.X + inner.Width <= outer.X + outer.Width
        && inner.Y + inner.Height <= outer.Y + outer.Height;
}
