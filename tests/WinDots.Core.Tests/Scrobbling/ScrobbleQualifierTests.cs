using WinDots.Core.Scrobbling;

namespace WinDots.Core.Tests.Scrobbling;

public sealed class ScrobbleQualifierTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static TrackIdentity Track(string title = "Song") => new("Artist", title, "Album");

    [Fact]
    public void ShortTrack_NeverQualifies()
    {
        var q = new ScrobbleQualifier();
        var id = Track();
        var dur = TimeSpan.FromSeconds(25);

        Scrobble? result = null;
        for (int s = 0; s <= 25; s++)
        {
            result ??= q.Update(id, dur, TimeSpan.FromSeconds(s), playing: true, T0.AddSeconds(s));
        }

        Assert.Null(result);
        Assert.False(q.HasQualified);
    }

    [Fact]
    public void QualifiesAtHalf_ForShortEnoughTrack()
    {
        var q = new ScrobbleQualifier();
        var id = Track();
        var dur = TimeSpan.FromSeconds(180); // half = 90s, below the 4-minute cap

        Scrobble? emitted = null;
        int emittedAt = -1;
        for (int s = 0; s <= 120; s++)
        {
            Scrobble? r = q.Update(id, dur, TimeSpan.FromSeconds(s), playing: true, T0.AddSeconds(s));
            if (r is not null && emitted is null)
            {
                emitted = r;
                emittedAt = s;
            }
        }

        Assert.NotNull(emitted);
        Assert.Equal(90, emittedAt);
        Assert.Equal(id, emitted!.Identity);
    }

    [Fact]
    public void QualifiesAtFourMinutes_ForLongTrack()
    {
        var q = new ScrobbleQualifier();
        var id = Track();
        var dur = TimeSpan.FromMinutes(20); // half = 10min, but the 4-minute cap wins

        int emittedAt = -1;
        for (int s = 0; s <= 300; s++)
        {
            Scrobble? r = q.Update(id, dur, TimeSpan.FromSeconds(s), playing: true, T0.AddSeconds(s));
            if (r is not null && emittedAt < 0)
            {
                emittedAt = s;
            }
        }

        Assert.Equal(240, emittedAt);
    }

    [Fact]
    public void EmitsOnce_PerPlay()
    {
        var q = new ScrobbleQualifier();
        var id = Track();
        var dur = TimeSpan.FromSeconds(180);

        int emitted = 0;
        for (int s = 0; s <= 179; s++)
        {
            if (q.Update(id, dur, TimeSpan.FromSeconds(s), playing: true, T0.AddSeconds(s)) is not null)
            {
                emitted++;
            }
        }

        Assert.Equal(1, emitted);
    }

    [Fact]
    public void PauseDoesNotAccumulate()
    {
        var q = new ScrobbleQualifier();
        var id = Track();
        var dur = TimeSpan.FromSeconds(180); // needs 90s of listening

        // Play 60s.
        for (int s = 0; s <= 60; s++)
        {
            Assert.Null(q.Update(id, dur, TimeSpan.FromSeconds(s), playing: true, T0.AddSeconds(s)));
        }

        // Pause at 60s for 10 minutes (position frozen, not playing).
        Scrobble? duringPause = q.Update(id, dur, TimeSpan.FromSeconds(60), playing: false, T0.AddSeconds(660));
        Assert.Null(duringPause);
        Assert.True(q.Accumulated < TimeSpan.FromSeconds(65));

        // Resume; needs ~30 more seconds of real playing to reach 90s.
        Scrobble? result = null;
        for (int s = 1; s <= 40 && result is null; s++)
        {
            result = q.Update(id, dur, TimeSpan.FromSeconds(60 + s), playing: true, T0.AddSeconds(660 + s));
        }

        Assert.NotNull(result);
    }

    [Fact]
    public void RestartBeginsNewPlay_AndCanQualifyAgain()
    {
        var q = new ScrobbleQualifier();
        var id = Track();
        var dur = TimeSpan.FromSeconds(180);

        int emitted = 0;
        DateTimeOffset now = T0;
        // First full play to qualification.
        for (int s = 0; s <= 95; s++)
        {
            if (q.Update(id, dur, TimeSpan.FromSeconds(s), playing: true, now.AddSeconds(s)) is not null)
            {
                emitted++;
            }
        }

        Assert.Equal(1, emitted);

        // Restart: position jumps back to 0 (same track replayed).
        now = now.AddSeconds(100);
        q.Update(id, dur, TimeSpan.FromSeconds(0), playing: true, now);
        Assert.False(q.HasQualified);

        for (int s = 1; s <= 95; s++)
        {
            if (q.Update(id, dur, TimeSpan.FromSeconds(s), playing: true, now.AddSeconds(s)) is not null)
            {
                emitted++;
            }
        }

        Assert.Equal(2, emitted);
    }

    [Fact]
    public void TrackChange_ResetsAndDedupesByIdentity()
    {
        var q = new ScrobbleQualifier();
        var dur = TimeSpan.FromSeconds(180);

        int emitted = 0;
        for (int s = 0; s <= 95; s++)
        {
            if (q.Update(Track("A"), dur, TimeSpan.FromSeconds(s), playing: true, T0.AddSeconds(s)) is not null)
            {
                emitted++;
            }
        }

        DateTimeOffset t = T0.AddSeconds(200);
        for (int s = 0; s <= 95; s++)
        {
            if (q.Update(Track("B"), dur, TimeSpan.FromSeconds(s), playing: true, t.AddSeconds(s)) is not null)
            {
                emitted++;
            }
        }

        Assert.Equal(2, emitted);
    }

    [Fact]
    public void NullOrUnusableIdentity_Resets()
    {
        var q = new ScrobbleQualifier();
        q.Update(Track(), TimeSpan.FromSeconds(180), TimeSpan.FromSeconds(50), playing: true, T0);
        Assert.NotNull(q.Current);

        q.Update(null, TimeSpan.Zero, TimeSpan.Zero, playing: false, T0.AddSeconds(1));
        Assert.Null(q.Current);
    }
}
