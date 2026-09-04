using System.Globalization;

namespace WinDots.Core.Dashboard;

/// <summary>
/// Pure clock read-out for the stacked dashboard clock ("12" / "10" / "AM"). Given an instant and a
/// 12h/24h preference it yields the hour, zero-padded minute, and meridiem strings. No ambient clock:
/// the instant is passed in so tests are deterministic.
/// </summary>
public readonly record struct ClockModel
{
    private ClockModel(string hour, string minute, string meridiem, bool use24Hour)
    {
        Hour = hour;
        Minute = minute;
        Meridiem = meridiem;
        Use24Hour = use24Hour;
    }

    /// <summary>Hour text. In 24h mode zero-padded <c>"00".."23"</c>; in 12h mode <c>"12","1".."11"</c> (not padded).</summary>
    public string Hour { get; }

    /// <summary>Zero-padded minute text, <c>"00".."59"</c>.</summary>
    public string Minute { get; }

    /// <summary>Meridiem: <c>"AM"</c>/<c>"PM"</c> in 12h mode, empty string in 24h mode.</summary>
    public string Meridiem { get; }

    /// <summary>Whether this read-out is in 24-hour mode.</summary>
    public bool Use24Hour { get; }

    /// <summary>Builds the read-out from the local clock components of <paramref name="now"/>.</summary>
    /// <param name="now">The instant to display; its own offset defines the wall-clock time shown.</param>
    /// <param name="use24Hour">True for 24-hour time (no meridiem); false for 12-hour with AM/PM.</param>
    public static ClockModel Create(DateTimeOffset now, bool use24Hour)
    {
        int h24 = now.Hour;
        int minute = now.Minute;

        if (use24Hour)
        {
            return new ClockModel(
                h24.ToString("D2", CultureInfo.InvariantCulture),
                minute.ToString("D2", CultureInfo.InvariantCulture),
                string.Empty,
                true);
        }

        int h12 = h24 % 12;
        if (h12 == 0)
        {
            h12 = 12;
        }

        string meridiem = h24 < 12 ? "AM" : "PM";
        return new ClockModel(
            h12.ToString(CultureInfo.InvariantCulture),
            minute.ToString("D2", CultureInfo.InvariantCulture),
            meridiem,
            false);
    }
}
