using System.Runtime.InteropServices;
using Windows.Media.Control;
using WinDots.Core.Contracts;
using WinDots.Windows.Threading;

namespace WinDots.Windows.Media;

/// <summary>
/// Discovers media sessions through <see cref="GlobalSystemMediaTransportControlsSessionManager"/>.
/// Event-driven: the platform raises SessionsChanged / CurrentSessionChanged and this provider reconciles its set
/// on its private <see cref="MediaDispatcher"/> thread, which owns every WinRT object.
/// Session identity is the source AUMID plus an ordinal for duplicates (two browser windows), which is stable
/// while the set is unchanged and may renumber when a duplicate leaves. Milestone 3 revisits identity if needed.
/// </summary>
public sealed class GsmtcSessionProvider : IMediaSessionProvider
{
    private readonly MediaDispatcher _dispatcher = new();
    private readonly Dictionary<string, GsmtcSession> _byId = new(StringComparer.Ordinal);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private IReadOnlyList<IMediaSession> _sessions = Array.Empty<IMediaSession>();
    private string? _systemCurrentId;
    private volatile bool _disposed;

    public IReadOnlyList<IMediaSession> Sessions => Volatile.Read(ref _sessions);

    public IMediaSession? SystemCurrent
    {
        get
        {
            var id = Volatile.Read(ref _systemCurrentId);
            return id is null ? null : Sessions.FirstOrDefault(s => s.Id == id);
        }
    }

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
            manager.SessionsChanged += OnSessionsChanged;
            manager.CurrentSessionChanged += OnCurrentSessionChanged;
            _manager = manager;

            await ReconcileAsync(ct);
            UpdateSystemCurrent();
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
                    _manager.SessionsChanged -= OnSessionsChanged;
                    _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                }

                foreach (var s in _byId.Values)
                {
                    s.Dispose();
                }

                _byId.Clear();
                _sessions = Array.Empty<IMediaSession>();
                return true;
            });
        }
        catch (ObjectDisposedException)
        {
            // Dispatcher already gone.
        }

        _dispatcher.Dispose();
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, global::Windows.Media.Control.SessionsChangedEventArgs args) =>
        _ = ReconcileSafelyAsync();

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        _ = _dispatcher.InvokeAsync(() =>
        {
            UpdateSystemCurrent();
            return true;
        });
    }

    private async Task ReconcileSafelyAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await _dispatcher.InvokeAsync(async () =>
            {
                await ReconcileAsync(CancellationToken.None);
                UpdateSystemCurrent();
            });
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            // Transient platform failure; the next SessionsChanged retries.
        }
    }

    /// <summary>Dispatcher thread only.</summary>
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

        var liveIds = new HashSet<string>(seen.Select(s => s.Id), StringComparer.Ordinal);
        var removed = _byId.Where(kv => !liveIds.Contains(kv.Key)).Select(kv => kv.Value).ToList();
        foreach (var gone in removed)
        {
            _byId.Remove(gone.Id);
            gone.Dispose();
        }

        var added = new List<GsmtcSession>();
        foreach (var (id, session) in seen)
        {
            if (!_byId.ContainsKey(id))
            {
                var wrapper = new GsmtcSession(session, id, _dispatcher);
                _byId[id] = wrapper;
                added.Add(wrapper);
            }
        }

        IReadOnlyList<IMediaSession> snapshot = seen.Select(s => (IMediaSession)_byId[s.Id]).ToArray();
        Volatile.Write(ref _sessions, snapshot);

        foreach (var wrapper in added)
        {
            try
            {
                await wrapper.RefreshAsync(ct);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                // Session may have vanished between enumeration and refresh; it keeps an empty snapshot until reconciled.
            }
        }

        if (added.Count > 0 || removed.Count > 0)
        {
            SessionsChanged?.Invoke(this, new MediaSessionsChangedEventArgs(snapshot));
        }
    }

    /// <summary>Dispatcher thread only.</summary>
    private void UpdateSystemCurrent()
    {
        if (_manager is null || _disposed)
        {
            return;
        }

        string? newId = null;
        try
        {
            var aumid = _manager.GetCurrentSession()?.SourceAppUserModelId;
            if (aumid is not null)
            {
                newId = _byId.Keys.FirstOrDefault(k => k.StartsWith(aumid + "#", StringComparison.OrdinalIgnoreCase));
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
