using WinDots.Core.Scrobbling;

namespace WinDots.Core.Tests.Scrobbling;

public sealed class ScrobbleQueueTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Scrobble Make(string track, int tsOffsetSeconds = 0) =>
        new(new TrackIdentity("Artist", track, "Album"), T0.AddSeconds(tsOffsetSeconds), TimeSpan.FromSeconds(200));

    [Fact]
    public void Enqueue_IsIdempotent_ByIdentityAndTimestamp()
    {
        var q = new ScrobbleQueue(null);
        var s = Make("Song");
        q.Enqueue(s);
        q.Enqueue(s);
        q.Enqueue(Make("Song")); // same identity + timestamp

        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void DueBatch_ReturnsPendingImmediately_OldestFirst()
    {
        var q = new ScrobbleQueue(null);
        q.Enqueue(Make("B", 50));
        q.Enqueue(Make("A", 10));

        IReadOnlyList<Scrobble> due = q.DueBatch(T0.AddSeconds(100));
        Assert.Equal(2, due.Count);
        Assert.Equal("A", due[0].Identity.Track);
        Assert.Equal("B", due[1].Identity.Track);
    }

    [Fact]
    public void MarkFailure_BacksOff_ThenBecomesDueAgain()
    {
        var q = new ScrobbleQueue(null);
        var s = Make("Song");
        q.Enqueue(s);

        var now = T0.AddSeconds(300);
        IReadOnlyList<Scrobble> due = q.DueBatch(now);
        Assert.Single(due);

        q.MarkFailure(due, now);

        // Immediately after failure it is not due (first backoff is 30s).
        Assert.Empty(q.DueBatch(now.AddSeconds(5)));
        // After the backoff elapses it is due again.
        Assert.Single(q.DueBatch(now + ScrobbleQueue.BackoffFor(1) + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void BackoffFor_IsExponential_AndCapped()
    {
        Assert.Equal(TimeSpan.Zero, ScrobbleQueue.BackoffFor(0));
        Assert.True(ScrobbleQueue.BackoffFor(2) > ScrobbleQueue.BackoffFor(1));
        Assert.True(ScrobbleQueue.BackoffFor(3) > ScrobbleQueue.BackoffFor(2));
        Assert.Equal(ScrobbleQueue.BackoffFor(100), ScrobbleQueue.BackoffFor(200)); // capped
    }

    [Fact]
    public void MarkSuccess_RemovesEntries()
    {
        var q = new ScrobbleQueue(null);
        q.Enqueue(Make("A", 1));
        q.Enqueue(Make("B", 2));

        q.MarkSuccess(new[] { Make("A", 1) });
        Assert.Equal(1, q.Count);
        Assert.Equal("B", q.DueBatch(T0.AddSeconds(100))[0].Identity.Track);
    }

    [Fact]
    public void Bound_DropsOldestBeyondMax()
    {
        var q = new ScrobbleQueue(null);
        for (int i = 0; i < ScrobbleQueue.MaxEntries + 10; i++)
        {
            q.Enqueue(Make("Song", i));
        }

        Assert.Equal(ScrobbleQueue.MaxEntries, q.Count);
        // The oldest (offset 0..9) should have been dropped; offset 10 is now the earliest.
        Assert.Equal(T0.AddSeconds(10), q.DueBatch(T0.AddSeconds(10000), 1)[0].Timestamp);
    }

    [Fact]
    public void Persistence_RoundTrips_AndToleratesCorruption()
    {
        string path = Path.Combine(Path.GetTempPath(), "windots-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var q = new ScrobbleQueue(path);
            q.Enqueue(Make("A", 1));
            q.Enqueue(Make("B", 2));

            var reloaded = new ScrobbleQueue(path);
            Assert.Equal(2, reloaded.Count);

            File.WriteAllText(path, "{ not json");
            var corrupt = new ScrobbleQueue(path);
            Assert.Equal(0, corrupt.Count);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
