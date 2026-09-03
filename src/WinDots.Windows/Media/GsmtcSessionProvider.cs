using System.Runtime.InteropServices;
using Windows.Media.Control;
using WinDots.Core.Contracts;
using WinDots.Windows.Threading;

namespace WinDots.Windows.Media;

/// <summary>
/// Discovers media sessions through <see cref="GlobalSystemMediaTransportControlsSessionManager"/>.
/// Event-driven: the platform raises SessionsChanged / CurrentSessionChanged and this provider reconciles its set
/// on its private <see cref="MediaDispatcher"/> thread, which owns every WinRT object.
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity rule.</b> A session ID is <c>&lt;AUMID&gt;#&lt;ordinal&gt;</c>. A <see cref="GsmtcSession"/> wrapper is
/// kept, with its ID, for as long as its session is still enumerated. The platform offers no identity (every
/// enumeration returns a new object, see <see cref="SessionFingerprint"/>), so "still enumerated" means an enumerated
/// object of the same AUMID reports the same state as the wrapper's object at the same instant. A new platform
/// session takes the lowest ordinal not held by a surviving wrapper of the same AUMID. Consequently the ID of a
/// surviving session never changes when a duplicate (a second browser window) leaves, and an ID is only reused
/// after its previous holder has gone. A wrapper that matches nothing is dropped even if its object still answers,
/// because a session whose player exited keeps answering for a while.
/// </para>
/// <para>
/// <b>Ordering.</b> <see cref="Sessions"/> keeps surviving sessions in their existing order and appends new ones,
/// so the list changes exactly when a session is added or removed, which is also exactly when
/// <see cref="SessionsChanged"/> is raised. Platform enumeration order is not meaningful and is not exposed.
/// </para>
/// <para>
/// <b>Serialisation.</b> Reconciles run one at a time on the dispatcher; a SessionsChanged that arrives during a
/// reconcile queues one more pass instead of interleaving with it.
/// </para>
/// </remarks>
public sealed class GsmtcSessionProvider : IMediaSessionProvider
{
    private readonly MediaDispatcher _dispatcher = new();
    private readonly List<GsmtcSession> _ordered = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private IReadOnlyList<IMediaSession> _sessions = Array.Empty<IMediaSession>();
    private IMediaSession? _systemCurrent;
    private bool _reconciling;
    private bool _reconcileRequested;
    private volatile bool _disposed;

    public IReadOnlyList<IMediaSession> Sessions => Volatile.Read(ref _sessions);

    public IMediaSession? SystemCurrent => Volatile.Read(ref _systemCurrent);

    public event EventHandler<MediaSessionsChangedEventArgs>? SessionsChanged;

    public event EventHandler? SystemCurrentChanged;

    public Task InitializeAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _dispatcher.InvokeAsync(async () =>
        {
            if (_manager is not null)
            {
                return;
            }

            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(ct);
            if (_disposed)
            {
                return;
            }

            manager.SessionsChanged += OnSessionsChanged;
            manager.CurrentSessionChanged += OnCurrentSessionChanged;
            _manager = manager;

            await ReconcileSerializedAsync(ct);
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await _dispatcher.InvokeAsync(() =>
            {
                if (_manager is not null)
                {
                    try
                    {
                        _manager.SessionsChanged -= OnSessionsChanged;
                        _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                    }
                    catch (Exception ex) when (IsPlatformFailure(ex))
                    {
                        // Manager already gone.
                    }
                }

                foreach (var s in _ordered)
                {
                    s.Dispose();
                }

                _ordered.Clear();
                Volatile.Write(ref _sessions, Array.Empty<IMediaSession>());
                Volatile.Write(ref _systemCurrent, null);
                return true;
            });
        }
        catch (ObjectDisposedException)
        {
            // Dispatcher already gone.
        }

        _dispatcher.Dispose();
    }

    private static bool IsPlatformFailure(Exception ex) =>
        ex is COMException or InvalidOperationException or ObjectDisposedException or UnauthorizedAccessException
            or FileNotFoundException or ArgumentException or NotSupportedException;

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, global::Windows.Media.Control.SessionsChangedEventArgs args) =>
        _ = ReconcileSafelyAsync();

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        _ = UpdateSystemCurrentSafelyAsync();

    private async Task ReconcileSafelyAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await _dispatcher.InvokeAsync(() => ReconcileSerializedAsync(CancellationToken.None));
        }
        catch (Exception ex) when (IsPlatformFailure(ex))
        {
            // Transient platform failure; the next SessionsChanged retries.
        }
    }

    private async Task UpdateSystemCurrentSafelyAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await _dispatcher.InvokeAsync(() =>
            {
                UpdateSystemCurrent();
                return true;
            });
        }
        catch (Exception ex) when (IsPlatformFailure(ex))
        {
            // Transient platform failure; the next CurrentSessionChanged retries.
        }
    }

    /// <summary>Dispatcher thread only. Runs one reconcile at a time and re-runs once if asked meanwhile.</summary>
    private async Task ReconcileSerializedAsync(CancellationToken ct)
    {
        if (_reconciling)
        {
            _reconcileRequested = true;
            return;
        }

        _reconciling = true;
        try
        {
            do
            {
                _reconcileRequested = false;
                await ReconcileAsync(ct);
                UpdateSystemCurrent();
            }
            while (_reconcileRequested && !_disposed);
        }
        finally
        {
            _reconciling = false;
        }
    }

    /// <summary>Dispatcher thread only; call through <see cref="ReconcileSerializedAsync"/>.</summary>
    private async Task ReconcileAsync(CancellationToken ct)
    {
        if (_manager is null || _disposed)
        {
            return;
        }

        var platformSessions = _manager.GetSessions();
        var kept = new HashSet<GsmtcSession>(ReferenceEqualityComparer.Instance);
        var added = new List<GsmtcSession>();

        foreach (var group in platformSessions.GroupBy(SafeAumid, StringComparer.OrdinalIgnoreCase))
        {
            var candidates = _ordered
                .Where(w => !w.IsDisposed && string.Equals(w.SourceAppId, group.Key, StringComparison.OrdinalIgnoreCase))
                .Select(w => (Wrapper: w, Fingerprint: w.Fingerprint()))
                .Where(c => c.Fingerprint is not null)
                .ToList();

            // Pass 1: match every enumerated session before any ordinal is chosen. Enumeration order is not
            // guaranteed, so a newcomer listed before a survivor must not be numbered until that survivor is known.
            var newcomers = new List<GlobalSystemMediaTransportControlsSession>();
            foreach (var session in group)
            {
                var match = MatchWrapper(session, candidates, kept);
                if (match is not null)
                {
                    kept.Add(match);
                }
                else
                {
                    newcomers.Add(session);
                }
            }

            // Pass 2: number the newcomers against the complete survivor set of this AUMID.
            var taken = candidates.Where(c => kept.Contains(c.Wrapper)).Select(c => c.Wrapper.Id).ToList();
            foreach (var session in newcomers)
            {
                var id = $"{group.Key}#{NextOrdinal(taken)}";
                taken.Add(id);
                added.Add(new GsmtcSession(session, id, _dispatcher));
            }
        }

        var removed = _ordered.Where(w => !kept.Contains(w)).ToList();
        foreach (var gone in removed)
        {
            gone.Dispose();
        }

        _ordered.RemoveAll(w => !kept.Contains(w));
        _ordered.AddRange(added);

        IReadOnlyList<IMediaSession> snapshot = _ordered.ToArray();
        Volatile.Write(ref _sessions, snapshot);

        foreach (var wrapper in added)
        {
            try
            {
                await wrapper.RefreshAsync(ct);
            }
            catch (Exception ex) when (IsPlatformFailure(ex))
            {
                // Session may have vanished between enumeration and refresh; it keeps an empty snapshot until reconciled.
            }
        }

        if (!_disposed && (added.Count > 0 || removed.Count > 0))
        {
            SessionsChanged?.Invoke(this, new MediaSessionsChangedEventArgs(snapshot));
        }
    }

    private static string SafeAumid(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.SourceAppUserModelId ?? string.Empty;
        }
        catch (Exception ex) when (IsPlatformFailure(ex))
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Dispatcher thread only. Finds the wrapper whose session reports the same state as <paramref name="session"/>
    /// right now. On a mismatch the candidates are re-read once, because a playing session may have published an
    /// update between the two reads. Identical fingerprints (two idle sessions with no timeline) resolve positionally.
    /// </summary>
    private static GsmtcSession? MatchWrapper(
        GlobalSystemMediaTransportControlsSession session,
        List<(GsmtcSession Wrapper, SessionFingerprint? Fingerprint)> candidates,
        HashSet<GsmtcSession> kept)
    {
        var fingerprint = SessionFingerprint.Read(session);
        if (fingerprint is null)
        {
            return null;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                var (wrapper, candidate) = candidates[i];
                if (kept.Contains(wrapper))
                {
                    continue;
                }

                if (attempt == 1)
                {
                    candidate = wrapper.Fingerprint();
                    candidates[i] = (wrapper, candidate);
                }

                if (candidate == fingerprint)
                {
                    return wrapper;
                }
            }

            fingerprint = SessionFingerprint.Read(session);
            if (fingerprint is null)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// The lowest ordinal not used by any of <paramref name="takenIds"/> (IDs of the form <c>&lt;AUMID&gt;#&lt;ordinal&gt;</c>,
    /// all of one AUMID). Pure; exposed so the numbering rule is unit-testable without a platform session.
    /// </summary>
    public static int NextOrdinal(IEnumerable<string> takenIds)
    {
        ArgumentNullException.ThrowIfNull(takenIds);
        var taken = takenIds
            .Select(id => int.TryParse(id.AsSpan(id.LastIndexOf('#') + 1), out var n) ? n : -1)
            .ToHashSet();
        var ordinal = 0;
        while (taken.Contains(ordinal))
        {
            ordinal++;
        }

        return ordinal;
    }

    /// <summary>Dispatcher thread only.</summary>
    private void UpdateSystemCurrent()
    {
        if (_manager is null || _disposed)
        {
            return;
        }

        GsmtcSession? current = null;
        try
        {
            var platformCurrent = _manager.GetCurrentSession();
            if (platformCurrent is not null)
            {
                // Same identity rule as reconcile; the AUMID fallback only decides among duplicates when identity fails.
                var aumid = SafeAumid(platformCurrent);
                var candidates = _ordered
                    .Where(w => !w.IsDisposed && string.Equals(w.SourceAppId, aumid, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                current = candidates.Count == 1
                    ? candidates[0]
                    : MatchWrapper(platformCurrent, candidates.Select(w => (w, w.Fingerprint())).ToList(), new HashSet<GsmtcSession>(ReferenceEqualityComparer.Instance))
                        ?? candidates.FirstOrDefault(w => w.Current.State == Core.Media.PlaybackState.Playing)
                        ?? candidates.FirstOrDefault();
            }
        }
        catch (Exception ex) when (IsPlatformFailure(ex))
        {
            current = null;
        }

        var old = Interlocked.Exchange(ref _systemCurrent, current);
        if (!ReferenceEquals(old, current))
        {
            SystemCurrentChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
