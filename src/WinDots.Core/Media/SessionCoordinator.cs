using WinDots.Core.Contracts;

namespace WinDots.Core.Media;

/// <summary>
/// Picks the active media session by the score table in _docs/05-architecture.md ("Session coordinator scoring").
/// Re-evaluates on <see cref="IMediaSessionProvider.SessionsChanged"/>,
/// <see cref="IMediaSessionProvider.SystemCurrentChanged"/>, and each <see cref="IMediaSession.Updated"/>.
/// </summary>
/// <remarks>
/// Thread-safe: mutable state is guarded by a lock. Events (<see cref="ActiveChanged"/>,
/// <see cref="CandidatesChanged"/>) are raised on the thread that triggered the re-evaluation (the provider's
/// callback thread, or the caller of <see cref="Pin"/> / <see cref="ClearPin"/>), outside the lock. Consumers that
/// need a specific thread must marshal in their handler.
/// </remarks>
public sealed class SessionCoordinator : ISessionCoordinator
{
    private const int PinnedScore = 1000;
    private const int PreferredScore = 400;
    private const int PlayingScore = 300;
    private const int RecentActivityScore = 100;
    private const int SystemCurrentScore = 50;
    private const int PausedScore = 20;

    private readonly IMediaSessionProvider _provider;
    private MediaOptions _options;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private readonly HashSet<IMediaSession> _subscribed = new();

    private IMediaSession? _active;
    private SelectionReason _reason = SelectionReason.None;
    private IReadOnlyList<IMediaSession> _candidates = Array.Empty<IMediaSession>();
    private IReadOnlyDictionary<string, MusicVerdict> _verdicts =
        new Dictionary<string, MusicVerdict>(StringComparer.Ordinal);
    private bool _showAllSources;
    private string? _pinnedId;
    private bool _disposed;

    public SessionCoordinator(IMediaSessionProvider provider, MediaOptions options, Func<DateTimeOffset>? now = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _now = now ?? (() => DateTimeOffset.UtcNow);

        _provider.SessionsChanged += OnSessionsChanged;
        _provider.SystemCurrentChanged += OnSystemCurrentChanged;

        Evaluate();
    }

    /// <summary>
    /// Replaces the selection tunables (preferred player, ignored players, aliases, timeline tick) and re-evaluates
    /// the active session so a live settings change takes effect immediately. See _docs/06-settings-schema.md.
    /// </summary>
    public void UpdateOptions(MediaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _options = options;
        }

        Evaluate();
    }

    public IMediaSession? Active
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    public SelectionReason Reason
    {
        get
        {
            lock (_gate)
            {
                return _reason;
            }
        }
    }

    public IReadOnlyList<IMediaSession> Candidates
    {
        get
        {
            lock (_gate)
            {
                return _candidates;
            }
        }
    }

    public IReadOnlyDictionary<string, MusicVerdict> Verdicts
    {
        get
        {
            lock (_gate)
            {
                return _verdicts;
            }
        }
    }

    public bool ShowAllSources
    {
        get
        {
            lock (_gate)
            {
                return _showAllSources;
            }
        }

        set
        {
            lock (_gate)
            {
                if (_disposed || _showAllSources == value)
                {
                    return;
                }

                _showAllSources = value;
            }

            Evaluate();
            ShowAllSourcesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ActiveChanged;

    public event EventHandler? CandidatesChanged;

    public event EventHandler? ShowAllSourcesChanged;

    public void Pin(string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        lock (_gate)
        {
            _pinnedId = sessionId;
        }

        Evaluate();
    }

    public void ClearPin()
    {
        lock (_gate)
        {
            _pinnedId = null;
        }

        Evaluate();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _provider.SessionsChanged -= OnSessionsChanged;
            _provider.SystemCurrentChanged -= OnSystemCurrentChanged;
            foreach (IMediaSession session in _subscribed)
            {
                session.Updated -= OnSessionUpdated;
            }

            _subscribed.Clear();
        }
    }

    private void OnSessionsChanged(object? sender, MediaSessionsChangedEventArgs e) => Evaluate();

    private void OnSystemCurrentChanged(object? sender, EventArgs e) => Evaluate();

    private void OnSessionUpdated(object? sender, MediaSnapshot e) => Evaluate();

    private void Evaluate()
    {
        bool activeChanged;
        bool candidatesChanged;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            ReconcileSubscriptions();

            IReadOnlyList<IMediaSession> sessions = _provider.Sessions;
            IMediaSession? systemCurrent = _provider.SystemCurrent;
            DateTimeOffset now = _now();
            bool showAll = _showAllSources || _options.SourceMode == SourceMode.All;

            // Ignore filter, source-rule filter (Never excluded, Auto rejected by the detector when tracking), then rank.
            List<Scored> scored = new();
            var newVerdicts = new Dictionary<string, MusicVerdict>(StringComparer.Ordinal);
            foreach (IMediaSession session in sessions)
            {
                if (IsIgnored(session))
                {
                    continue;
                }

                MediaSnapshot snapshot = session.Current;
                SourceRuleMode rule = _options.RuleFor(snapshot.SourceAppId, snapshot.SourceDisplayName);
                if (rule == SourceRuleMode.Never)
                {
                    continue;
                }

                MusicVerdict verdict = MusicDetector.Score(snapshot, rule);

                // Tracked mode drops Auto sources the detector rejects; Always sources are always kept.
                if (!showAll && rule == SourceRuleMode.Auto && !verdict.IsMusic)
                {
                    continue;
                }

                newVerdicts[session.Id] = verdict;
                scored.Add(Score(session, systemCurrent, now));
            }

            scored.Sort(CompareScored);

            IReadOnlyList<IMediaSession> newCandidates = scored.Count == 0
                ? Array.Empty<IMediaSession>()
                : scored.ConvertAll(s => s.Session);

            IMediaSession? newActive;
            SelectionReason newReason;

            // A pin sticks until that session disappears from the (unignored) candidate set.
            int pinnedIndex = _pinnedId is null
                ? -1
                : scored.FindIndex(s => string.Equals(s.Session.Id, _pinnedId, StringComparison.Ordinal));

            if (_pinnedId is not null && pinnedIndex < 0)
            {
                // Pinned session vanished: fall back to automatic.
                _pinnedId = null;
            }

            if (pinnedIndex >= 0)
            {
                newActive = scored[pinnedIndex].Session;
                newReason = SelectionReason.PinnedByUser;
            }
            else if (scored.Count == 0)
            {
                newActive = null;
                newReason = SelectionReason.None;
            }
            else
            {
                Scored winner = scored[0];
                newActive = winner.Session;
                newReason = ReasonFor(winner);
            }

            activeChanged = !ReferenceEquals(_active, newActive) || _reason != newReason;
            candidatesChanged = !SequenceEqual(_candidates, newCandidates) || !VerdictsEqual(_verdicts, newVerdicts);

            _active = newActive;
            _reason = newReason;
            _candidates = newCandidates;
            _verdicts = newVerdicts;
        }

        if (candidatesChanged)
        {
            CandidatesChanged?.Invoke(this, EventArgs.Empty);
        }

        if (activeChanged)
        {
            ActiveChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ReconcileSubscriptions()
    {
        IReadOnlyList<IMediaSession> sessions = _provider.Sessions;
        HashSet<IMediaSession> current = new(sessions);

        // Unsubscribe departed sessions.
        List<IMediaSession> departed = new();
        foreach (IMediaSession session in _subscribed)
        {
            if (!current.Contains(session))
            {
                departed.Add(session);
            }
        }

        foreach (IMediaSession session in departed)
        {
            session.Updated -= OnSessionUpdated;
            _subscribed.Remove(session);
        }

        // Subscribe newcomers.
        foreach (IMediaSession session in sessions)
        {
            if (_subscribed.Add(session))
            {
                session.Updated += OnSessionUpdated;
            }
        }
    }

    private bool IsIgnored(IMediaSession session)
    {
        MediaSnapshot snapshot = session.Current;
        foreach (string pattern in _options.IgnoredPlayers)
        {
            if (MediaOptions.Matches(pattern, snapshot.SourceAppId, snapshot.SourceDisplayName))
            {
                return true;
            }
        }

        return false;
    }

    private Scored Score(IMediaSession session, IMediaSession? systemCurrent, DateTimeOffset now)
    {
        MediaSnapshot snapshot = session.Current;
        bool preferred = _options.PreferredPlayer is { } pref
            && MediaOptions.Matches(pref, snapshot.SourceAppId, snapshot.SourceDisplayName);
        bool playing = snapshot.State == PlaybackState.Playing;
        bool recent = now - snapshot.CapturedAt <= _options.RecentActivityWindow
            && now - snapshot.CapturedAt >= TimeSpan.Zero;
        bool systemCurrentMatch = systemCurrent is not null && ReferenceEquals(systemCurrent, session);
        bool paused = snapshot.State == PlaybackState.Paused;
        bool stale = SessionQuality.IsStale(snapshot);

        int score = 0;
        if (preferred)
        {
            score += PreferredScore;
        }

        if (playing)
        {
            score += PlayingScore;
        }

        if (recent)
        {
            score += RecentActivityScore;
        }

        if (systemCurrentMatch)
        {
            score += SystemCurrentScore;
        }

        if (paused)
        {
            score += PausedScore;
        }

        return new Scored(session, score, stale, snapshot.CapturedAt, preferred, playing, recent, systemCurrentMatch, paused);
    }

    private static SelectionReason ReasonFor(Scored s)
    {
        if (s.Preferred)
        {
            return SelectionReason.PreferredPlayer;
        }

        if (s.Playing)
        {
            return SelectionReason.Playing;
        }

        if (s.Recent)
        {
            return SelectionReason.RecentActivity;
        }

        if (s.SystemCurrent)
        {
            return SelectionReason.SystemCurrent;
        }

        if (s.Paused)
        {
            return SelectionReason.Paused;
        }

        return SelectionReason.OnlyCandidate;
    }

    private static int CompareScored(Scored a, Scored b)
    {
        // Non-stale before stale.
        if (a.Stale != b.Stale)
        {
            return a.Stale ? 1 : -1;
        }

        // Higher score first.
        int byScore = b.Score.CompareTo(a.Score);
        if (byScore != 0)
        {
            return byScore;
        }

        // Latest CapturedAt first.
        int byTime = b.CapturedAt.CompareTo(a.CapturedAt);
        if (byTime != 0)
        {
            return byTime;
        }

        // Stable by session Id.
        return string.CompareOrdinal(a.Session.Id, b.Session.Id);
    }

    private static bool SequenceEqual(IReadOnlyList<IMediaSession> a, IReadOnlyList<IMediaSession> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!ReferenceEquals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool VerdictsEqual(
        IReadOnlyDictionary<string, MusicVerdict> a,
        IReadOnlyDictionary<string, MusicVerdict> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, MusicVerdict> pair in a)
        {
            if (!b.TryGetValue(pair.Key, out MusicVerdict other) || other != pair.Value)
            {
                return false;
            }
        }

        return true;
    }

    private readonly record struct Scored(
        IMediaSession Session,
        int Score,
        bool Stale,
        DateTimeOffset CapturedAt,
        bool Preferred,
        bool Playing,
        bool Recent,
        bool SystemCurrent,
        bool Paused);
}
