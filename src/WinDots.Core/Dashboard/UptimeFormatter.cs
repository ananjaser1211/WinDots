using System.Globalization;
using System.Text;

namespace WinDots.Core.Dashboard;

/// <summary>
/// Formats an uptime <see cref="TimeSpan"/> into the "up 7 hours, 28 minutes" phrasing shown on the
/// dashboard user card, plus a compact "7h 28m" variant. Pure: no ambient clock is read.
/// </summary>
public static class UptimeFormatter
{
    /// <summary>
    /// Long form such as <c>"up 7 hours, 28 minutes"</c>. Days, hours, and minutes are shown when non-zero,
    /// with correct singular/plural. A sub-minute or negative span reads <c>"up less than a minute"</c>.
    /// </summary>
    public static string Format(TimeSpan uptime)
    {
        if (uptime.Ticks <= 0 || uptime.TotalMinutes < 1)
        {
            return "up less than a minute";
        }

        int days = (int)uptime.TotalDays;
        int hours = uptime.Hours;
        int minutes = uptime.Minutes;

        var parts = new List<string>(3);
        if (days > 0)
        {
            parts.Add(Unit(days, "day"));
        }

        if (hours > 0)
        {
            parts.Add(Unit(hours, "hour"));
        }

        if (minutes > 0)
        {
            parts.Add(Unit(minutes, "minute"));
        }

        return "up " + string.Join(", ", parts);
    }

    /// <summary>Compact form such as <c>"7h 28m"</c> (or <c>"2d 3h 28m"</c>); a sub-minute span reads <c>"&lt;1m"</c>.</summary>
    public static string FormatCompact(TimeSpan uptime)
    {
        if (uptime.Ticks <= 0 || uptime.TotalMinutes < 1)
        {
            return "<1m";
        }

        int days = (int)uptime.TotalDays;
        int hours = uptime.Hours;
        int minutes = uptime.Minutes;

        var builder = new StringBuilder();
        if (days > 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{days}d ");
        }

        if (hours > 0 || days > 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{hours}h ");
        }

        builder.Append(CultureInfo.InvariantCulture, $"{minutes}m");
        return builder.ToString();
    }

    private static string Unit(int value, string noun) =>
        value == 1
            ? string.Create(CultureInfo.InvariantCulture, $"1 {noun}")
            : string.Create(CultureInfo.InvariantCulture, $"{value} {noun}s");
}
