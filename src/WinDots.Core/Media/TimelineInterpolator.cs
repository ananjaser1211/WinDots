namespace WinDots.Core.Media;

/// <summary>
/// Projects a timestamped <see cref="Timeline"/> forward so the UI can show smooth progress between session events.
/// Pure function; see _docs/05-architecture.md "Timeline interpolation".
/// </summary>
public static class TimelineInterpolator
{
    public static TimeSpan Displayed(in Timeline timeline, PlaybackState state, DateTimeOffset now)
    {
        var position = timeline.Position;

        if (state == PlaybackState.Playing && timeline.LastUpdated != DateTimeOffset.MinValue)
        {
            var elapsed = now - timeline.LastUpdated;
            if (elapsed > TimeSpan.Zero)
            {
                var rate = double.IsFinite(timeline.Rate) && timeline.Rate > 0 ? timeline.Rate : 1.0;
                position += TimeSpan.FromTicks((long)(elapsed.Ticks * rate));
            }
        }

        if (position < timeline.Start)
        {
            position = timeline.Start;
        }

        if (timeline.HasDuration && position > timeline.End)
        {
            position = timeline.End;
        }

        return position;
    }

    /// <summary>Fraction of the track elapsed in [0, 1], or null when the duration is unknown.</summary>
    public static double? Progress(in Timeline timeline, PlaybackState state, DateTimeOffset now)
    {
        if (!timeline.HasDuration)
        {
            return null;
        }

        var displayed = Displayed(timeline, state, now) - timeline.Start;
        return Math.Clamp(displayed.Ticks / (double)timeline.Duration.Ticks, 0.0, 1.0);
    }
}
