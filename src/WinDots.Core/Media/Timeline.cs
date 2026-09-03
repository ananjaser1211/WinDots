namespace WinDots.Core.Media;

/// <summary>
/// A timestamped timeline sample as reported by a media session. Position is valid at <see cref="LastUpdated"/>;
/// use <see cref="TimelineInterpolator"/> to project it forward.
/// </summary>
public readonly record struct Timeline(
    TimeSpan Start,
    TimeSpan End,
    TimeSpan Position,
    DateTimeOffset LastUpdated,
    double Rate)
{
    public static Timeline Empty => new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, DateTimeOffset.MinValue, 1.0);

    public bool HasDuration => End > Start;

    public TimeSpan Duration => HasDuration ? End - Start : TimeSpan.Zero;
}
