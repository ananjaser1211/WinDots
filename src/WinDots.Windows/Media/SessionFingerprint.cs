using Windows.Media.Control;

namespace WinDots.Windows.Media;

/// <summary>
/// The state a <see cref="GlobalSystemMediaTransportControlsSession"/> reports at one instant, used to recognise the
/// same underlying session across enumerations. <c>GetSessions()</c> and <c>GetCurrentSession()</c> hand back a new
/// COM object every time (verified on Windows 11 26200: neither managed reference identity nor the IUnknown pointer
/// repeats), and a session whose player has exited keeps answering queries for a short while, so neither identity
/// nor liveness can be observed directly. Two objects that mirror the same session report the same timestamps and
/// positions, so comparing what they say at the same moment identifies them; two live sessions of the same app
/// practically never share a <see cref="LastUpdated"/> tick.
/// </summary>
internal readonly record struct SessionFingerprint(
    TimeSpan Start,
    TimeSpan End,
    TimeSpan Position,
    DateTimeOffset LastUpdated,
    GlobalSystemMediaTransportControlsSessionPlaybackStatus? Status,
    bool? Shuffle,
    global::Windows.Media.MediaPlaybackAutoRepeatMode? Repeat)
{
    /// <summary>Dispatcher thread only. Null when the object answers neither query, which is how a dead session looks.</summary>
    public static SessionFingerprint? Read(GlobalSystemMediaTransportControlsSession session)
    {
        GlobalSystemMediaTransportControlsSessionTimelineProperties? timeline = null;
        GlobalSystemMediaTransportControlsSessionPlaybackInfo? playback = null;
        try
        {
            timeline = session.GetTimelineProperties();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Object is gone or faulted; fall through.
        }

        try
        {
            playback = session.GetPlaybackInfo();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Object is gone or faulted; fall through.
        }

        if (timeline is null && playback is null)
        {
            return null;
        }

        return new SessionFingerprint(
            timeline?.StartTime ?? TimeSpan.Zero,
            timeline?.EndTime ?? TimeSpan.Zero,
            timeline?.Position ?? TimeSpan.Zero,
            timeline?.LastUpdatedTime ?? default,
            playback?.PlaybackStatus,
            playback?.IsShuffleActive,
            playback?.AutoRepeatMode);
    }
}
