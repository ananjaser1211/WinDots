using System.Runtime.InteropServices;
using Windows.Media.Control;
using WinDots.Core.Contracts;

namespace WinDots.Windows.Media;

/// <summary>
/// Discovers media sessions through <see cref="GlobalSystemMediaTransportControlsSessionManager"/>.
/// Event-driven: the platform raises SessionsChanged / CurrentSessionChanged and this provider reconciles its set.
/// Session identity is the source AUMID plus an ordinal for duplicates (two browser windows), which is stable
/// while the set is unchanged and may renumber when a duplicate leaves. Milestone 3 revisits identity if needed.
/// </summary>
public sealed class GsmtcSessionProvider : IMediaSessionProvider
{
    private readonly object _gate = new();
    private readonly Dictionary<string, GsmtcSession> _byId = new(StringComparer.Ordinal);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private IReadOnlyList<IMediaSession> _sessions = Array.Empty<IMediaSession>();
    private string? _systemCurrentId;
    private bool _disposed;

    public IReadOnlyList<IMediaSession> Sessions => Volatile.Read(ref _sessions);

    public IMediaSession? SystemCurrent
    {
        get
        {
            var id = Volatile.Read(ref _systemCurrentId);
            if (id is null)
            {
                return null;
            }

            lock (_gate)
            {
                return _byId.GetValueOrDefault(id);
            }
        }
    }

    public event EventHandler<MediaSessionsChangedEventArgs>? SessionsChanged;

    public event EventHandler? SystemCurrentChanged;

    public async Task InitializeAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_manager is not null)
        {
            return;
        }

        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(ct).ConfigureAwait(false);
        manager.SessionsChanged += OnSessionsChanged;
        manager.CurrentSessionChanged += OnCurrentSessionChanged;
        _manager = manager;

        await ReconcileAsync(ct).ConfigureAwait(false);
        UpdateSystemCurrent();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_manager is not null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }

        GsmtcSession[] toDispose;
        lock (_gate)
        {
            toDispose = _byId.Values.ToArray();
            _byId.Clear();
            _sessions = Array.Empty<IMediaSession>();
        }

        foreach (var s in toDispose)
        {
            s.Dispose();
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, global::Windows.Media.Control.SessionsChangedEventArgs args)
    {
        _ = ReconcileSafelyAsync();
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        UpdateSystemCurrent();
    }

    private async Task ReconcileSafelyAsync()
    {
        try
        {
            await ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            UpdateSystemCurrent();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            // Transient platform failure; the next SessionsChanged will retry.
        }
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        if (_manager is null || _disposed)
        {
            return;
        }

        var platformSessions = _manager.GetSessions();
        var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seen = new List<(string Id, GlobalSystemMediaTransportControlsSession Session)>(platformSessions.Count);

        foreach (var session in platformSessions)
        {
            var aumid = session.SourceAppUserModelId ?? "unknown";
            var ordinal = ordinals.GetValueOrDefault(aumid);
            ordinals[aumid] = ordinal + 1;
            seen.Add(($"{aumid}#{ordinal}", session));
        }

        var added = new List<GsmtcSession>();
        var removed = new List<GsmtcSession>();
        IReadOnlyList<IMediaSession> snapshot;

        lock (_gate)
        {
            var liveIds = new HashSet<string>(seen.Select(s => s.Id), StringComparer.Ordinal);
            foreach (var (id, existing) in _byId.ToArray())
            {
                if (!liveIds.Contains(id))
                {
                    _byId.Remove(id);
                    removed.Add(existing);
                }
            }

            foreach (var (id, session) in seen)
            {
                if (!_byId.ContainsKey(id))
                {
                    var wrapper = new GsmtcSession(session, id);
                    _byId[id] = wrapper;
                    added.Add(wrapper);
                }
            }

            snapshot = seen.Select(s => (IMediaSession)_byId[s.Id]).ToArray();
            _sessions = snapshot;
        }

        foreach (var gone in removed)
        {
            gone.Dispose();
        }

        foreach (var wrapper in added)
        {
            try
            {
                await wrapper.RefreshAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                // Session may have vanished between enumeration and refresh; it stays with an empty snapshot until reconciled.
            }
        }

        if (added.Count > 0 || removed.Count > 0)
        {
            SessionsChanged?.Invoke(this, new MediaSessionsChangedEventArgs(snapshot));
        }
    }

    private void UpdateSystemCurrent()
    {
        if (_manager is null || _disposed)
        {
            return;
        }

        string? newId = null;
        try
        {
            var current = _manager.GetCurrentSession();
            var aumid = current?.SourceAppUserModelId;
            if (aumid is not null)
            {
                lock (_gate)
                {
                    newId = _byId.Keys.FirstOrDefault(k => k.StartsWith(aumid + "#", StringComparison.OrdinalIgnoreCase));
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            newId = null;
        }

        var old = Interlocked.Exchange(ref _systemCurrentId, newId);
        if (!string.Equals(old, newId, StringComparison.Ordinal))
        {
            SystemCurrentChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
