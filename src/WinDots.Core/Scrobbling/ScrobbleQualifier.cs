namespace WinDots.Core.Scrobbling;

/// <summary>
/// Decides when a track has been played enough to scrobble. Per the Last.fm scrobbling rules a track qualifies once it
/// has been listened to for at least half its length or four minutes (whichever is lower), and only tracks longer than
/// 30 seconds are eligible. Listening time accumulates from wall-clock while playing (so pauses do not count), the play
/// is de-duplicated (a track qualifies once per play), and a restart to the beginning begins a fresh play. Pure and
/// deterministic: the caller supplies the clock via <c>now</c> on every <see cref="Update"/>. See _docs/10-enhancement-plan.md (E4).
/// </summary>
public sealed class ScrobbleQualifier
{
    private const double QualifyFraction = 0.5;
    private static readonly TimeSpan QualifyCap = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan MinTrackLength = TimeSpan.FromSeconds(30);

    // A backward jump to within this of the start, after real progress, is treated as a restart (a new play).
    private static readonly TimeSpan RestartThreshold = TimeSpan.FromSeconds(3);

    private string? _key;
    private TimeSpan _duration;
    private DateTimeOffset _startedAt;
    private TimeSpan _accumulated;
    private TimeSpan _lastPosition;
    private DateTimeOffset _lastUpdate;
    private bool _qualified;

    /// <summary>The identity of the track currently being tracked, or null when idle.</summary>
    public TrackIdentity? Current { get; private set; }

    /// <summary>Listening time accumulated for the current play (playing time only).</summary>
    public TimeSpan Accumulated => _accumulated;

    /// <summary>True once the current play has qualified and been reported.</summary>
    public bool HasQualified => _qualified;

    /// <summary>
    /// Feeds one observation. Returns a <see cref="Scrobble"/> exactly once, at the moment the current play crosses the
    /// qualification threshold; otherwise null.
    /// </summary>
    public Scrobble? Update(TrackIdentity? identity, TimeSpan duration, TimeSpan position, bool playing, DateTimeOffset now)
    {
        if (identity is null || !identity.IsUsable)
        {
            Reset();
            return null;
        }

        string key = identity.Key;
        if (!string.Equals(key, _key, StringComparison.Ordinal))
        {
            StartPlay(identity, key, duration, position, now);
            return CheckQualified(identity);
        }

        Current = identity;
        if (duration > TimeSpan.Zero)
        {
            _duration = duration;
        }

        bool restarted = _lastPosition > RestartThreshold &&
                         position < _lastPosition &&
                         position <= RestartThreshold;
        if (restarted)
        {
            _accumulated = TimeSpan.Zero;
            _qualified = false;
            _startedAt = now - position;
        }
        else if (playing)
        {
            // Count real playback progress: the lesser of wall-clock elapsed and position advanced. A frozen position
            // (pause) contributes nothing; a forward seek is bounded by the wall clock so it cannot inflate the total.
            TimeSpan wallDelta = now - _lastUpdate;
            TimeSpan positionDelta = position - _lastPosition;
            if (wallDelta > TimeSpan.Zero && positionDelta > TimeSpan.Zero)
            {
                _accumulated += positionDelta < wallDelta ? positionDelta : wallDelta;
            }
        }

        _lastPosition = position;
        _lastUpdate = now;
        return CheckQualified(identity);
    }

    /// <summary>Clears all state (call on sign-out or when scrobbling is disabled).</summary>
    public void Reset()
    {
        _key = null;
        Current = null;
        _duration = TimeSpan.Zero;
        _accumulated = TimeSpan.Zero;
        _lastPosition = TimeSpan.Zero;
        _qualified = false;
    }

    private void StartPlay(TrackIdentity identity, string key, TimeSpan duration, TimeSpan position, DateTimeOffset now)
    {
        _key = key;
        Current = identity;
        _duration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
        _startedAt = now - (position > TimeSpan.Zero ? position : TimeSpan.Zero);
        _accumulated = TimeSpan.Zero;
        _lastPosition = position;
        _lastUpdate = now;
        _qualified = false;
    }

    private Scrobble? CheckQualified(TrackIdentity identity)
    {
        if (_qualified || _duration <= MinTrackLength)
        {
            return null;
        }

        TimeSpan threshold = _duration * QualifyFraction;
        if (threshold > QualifyCap)
        {
            threshold = QualifyCap;
        }

        if (_accumulated < threshold)
        {
            return null;
        }

        _qualified = true;
        return new Scrobble(identity, _startedAt, _duration > TimeSpan.Zero ? _duration : null);
    }
}
