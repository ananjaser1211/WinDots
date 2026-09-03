using System.Globalization;

namespace WinDots.Core.Media;

public static class TimeFormat
{
    /// <summary>Formats as m:ss, or h:mm:ss at one hour and above. Negative or null values render as 0:00.</summary>
    public static string Clock(TimeSpan? value)
    {
        if (value is not { } t || t < TimeSpan.Zero)
        {
            return "0:00";
        }

        var totalSeconds = (long)Math.Floor(t.TotalSeconds);
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;

        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:00}");
    }
}
