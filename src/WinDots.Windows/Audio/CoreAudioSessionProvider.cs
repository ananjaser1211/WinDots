using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.System.Com;
using WinDots.Core.Audio;
using WinDots.Core.Contracts;
using WinDots.Windows.Threading;

namespace WinDots.Windows.Audio;

/// <summary>
/// Maps a media session's source application to Core Audio (WASAPI) render sessions and controls their per-application
/// volume, never the endpoint/master volume. Confidence tiers follow <see cref="AudioMatchPolicy"/> and
/// <c>_docs/05-architecture.md</c> "Core Audio matching".
/// </summary>
/// <remarks>
/// <para>
/// Every COM object is created and used on a single dedicated <see cref="MediaDispatcher"/> thread; interface pointers
/// are never shared across threads. <see cref="MatchAsync"/> resolves candidate process ids off that thread (plain
/// <see cref="Process"/> / package queries), then enumerates the render endpoint's sessions on the dispatcher and
/// feeds pure facts to <see cref="AudioMatchPolicy"/>. Volume/mute re-resolve the match's sessions by their session
/// identifier and apply to all of them together.
/// </para>
/// <para>
/// <see cref="Changed"/> fires when a new audio session is created (<see cref="IAudioSessionNotification"/>) and when
/// the default render device changes (<see cref="IMMNotificationClient"/>), so consumers can re-run
/// <see cref="MatchAsync"/>. COM objects are released deterministically in <see cref="DisposeAsync"/>.
/// </para>
/// </remarks>
public sealed class CoreAudioSessionProvider : IAudioSessionProvider, IAsyncDisposable
{
    private readonly MediaDispatcher _dispatcher;
    private readonly bool _ownsDispatcher;
    private readonly SessionNotificationSink _sessionSink;
    private readonly DeviceNotificationSink _deviceSink;

    private IMMDeviceEnumerator? _deviceEnumerator;
    private IMMDevice? _endpoint;
    private IAudioSessionManager2? _manager;
    private bool _initialized;
    private int _endpointGeneration;
    private volatile bool _disposed;

    public CoreAudioSessionProvider()
        : this(new MediaDispatcher(), ownsDispatcher: true)
    {
    }

    /// <summary>Uses a caller-owned dispatcher; the caller keeps responsibility for disposing it.</summary>
    public CoreAudioSessionProvider(MediaDispatcher dispatcher)
        : this(dispatcher, ownsDispatcher: false)
    {
    }

    private CoreAudioSessionProvider(MediaDispatcher dispatcher, bool ownsDispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
        _ownsDispatcher = ownsDispatcher;
        _sessionSink = new SessionNotificationSink(() => RaiseChanged());
        _deviceSink = new DeviceNotificationSink(OnDefaultRenderDeviceChanged);
    }

    public event EventHandler? Changed;

    /// <summary>
    /// Incremented on every successful endpoint activation (initial attach and each re-attach after a default render
    /// device change). Test hook only; lets a platform test assert that a device change actually re-bound the manager.
    /// </summary>
    internal int EndpointGeneration => Volatile.Read(ref _endpointGeneration);

    /// <summary>Test hook: runs the same re-attach path the default-render-device notification triggers, and awaits it.</summary>
    internal Task ForceDefaultRenderDeviceChangedForTestsAsync() => ReattachAndNotifyAsync();

    public async Task<AudioMatch> MatchAsync(string sourceAppId, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(sourceAppId))
        {
            return AudioMatch.NoMatch("None: no source application id.");
        }

        var (kind, pids) = await Task.Run(() => ResolveCandidates(sourceAppId), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var sessions = await _dispatcher.InvokeAsync(() => EnumerateSessions()).ConfigureAwait(false);

        var result = AudioMatchPolicy.Evaluate(kind, pids, sessions);
        return new AudioMatch(result.Confidence, result.SessionIdentifiers, result.Explanation);
    }

    public Task<float?> GetVolumeAsync(AudioMatch match, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(match);
        if (match.AudioSessionIds.Count == 0)
        {
            return Task.FromResult<float?>(null);
        }

        return _dispatcher.InvokeAsync(() =>
        {
            float? level = null;
            ForEachMatchingVolume(match, v =>
            {
                v.GetMasterVolume(out var l);
                level ??= l;
            });
            return level;
        });
    }

    public Task<bool?> GetMuteAsync(AudioMatch match, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(match);
        if (match.AudioSessionIds.Count == 0)
        {
            return Task.FromResult<bool?>(null);
        }

        return _dispatcher.InvokeAsync(() =>
        {
            bool? mute = null;
            ForEachMatchingVolume(match, v =>
            {
                unsafe
                {
                    BOOL m;
                    v.GetMute(&m);
                    mute ??= (bool)m;
                }
            });
            return mute;
        });
    }

    public Task<bool> TrySetVolumeAsync(AudioMatch match, float level, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(match);
        if (match.AudioSessionIds.Count == 0)
        {
            return Task.FromResult(false);
        }

        var clamped = Math.Clamp(level, 0f, 1f);
        return _dispatcher.InvokeAsync(() =>
        {
            var any = false;
            ForEachMatchingVolume(match, v =>
            {
                unsafe
                {
                    v.SetMasterVolume(clamped, null);
                }

                any = true;
            });
            return any;
        });
    }

    public Task<bool> TrySetMuteAsync(AudioMatch match, bool mute, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(match);
        if (match.AudioSessionIds.Count == 0)
        {
            return Task.FromResult(false);
        }

        return _dispatcher.InvokeAsync(() =>
        {
            var any = false;
            ForEachMatchingVolume(match, v =>
            {
                unsafe
                {
                    v.SetMute(mute, null);
                }

                any = true;
            });
            return any;
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_dispatcher.IsDisposed)
        {
            try
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    ReleaseCom();
                    return true;
                }).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Dispatcher torn down; nothing more to release on it.
            }
        }

        if (_ownsDispatcher)
        {
            _dispatcher.Dispose();
        }
    }

    // --- Dispatcher-thread work (COM only touched here) ---

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        PInvoke.CoCreateInstance<IMMDeviceEnumerator>(
            typeof(MMDeviceEnumerator).GUID,
            null,
            CLSCTX.CLSCTX_ALL,
            out var enumerator).ThrowOnFailure();
        _deviceEnumerator = enumerator;

        enumerator.RegisterEndpointNotificationCallback(_deviceSink);

        AttachEndpoint();
        _initialized = true;
    }

    private void AttachEndpoint()
    {
        if (_deviceEnumerator is null)
        {
            return;
        }

        _deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var endpoint);
        _endpoint = endpoint;

        unsafe
        {
            var iid = typeof(IAudioSessionManager2).GUID;
            endpoint.Activate(&iid, CLSCTX.CLSCTX_ALL, null, out var managerObj);
            _manager = (IAudioSessionManager2)managerObj;
        }

        _manager.RegisterSessionNotification(_sessionSink);

        // Enumerating once primes the notification callback (Windows only fires OnSessionCreated after the first
        // enumeration on the manager).
        var primer = _manager.GetSessionEnumerator();
        Marshal.FinalReleaseComObject(primer);

        Interlocked.Increment(ref _endpointGeneration);
    }

    /// <summary>
    /// Handles a default-render-device change from the endpoint notification callback. The callback runs on an
    /// arbitrary COM thread, so the actual COM re-attach is marshalled onto the dispatcher; this returns immediately.
    /// </summary>
    private void OnDefaultRenderDeviceChanged()
    {
        if (_disposed)
        {
            return;
        }

        // Fire-and-forget: the notification callback must not block, and any exception is swallowed inside the task.
        _ = ReattachAndNotifyAsync();
    }

    private async Task ReattachAndNotifyAsync()
    {
        try
        {
            await _dispatcher.InvokeAsync(() =>
            {
                ReattachEndpoint();
                return true;
            }).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Dispatcher torn down between the notification and the re-attach; nothing to do.
            return;
        }

        // Notify consumers only after the manager is re-bound, so their follow-up MatchAsync enumerates the new device.
        RaiseChanged();
    }

    /// <summary>
    /// Releases the manager/endpoint bound to the previous default render device and re-activates against the current
    /// one. <see cref="IAudioSessionManager2"/> is bound to the <see cref="IMMDevice"/> it was activated on, so without
    /// this every subsequent enumeration and volume/mute call would keep operating on the stale endpoint's sessions.
    /// Runs on the dispatcher thread.
    /// </summary>
    private void ReattachEndpoint()
    {
        if (_disposed || !_initialized || _deviceEnumerator is null)
        {
            // Not yet initialized: the first EnumerateSessions/ForEachMatchingVolume will attach against the current device.
            return;
        }

        ReleaseEndpoint();

        try
        {
            AttachEndpoint();
        }
        catch (COMException)
        {
            // No usable default render endpoint right now (e.g. device removed with no replacement yet). Leave the
            // manager null; enumeration returns empty until the next default-device change re-attaches successfully.
        }
    }

    private List<AudioSessionInfo> EnumerateSessions()
    {
        EnsureInitialized();
        var infos = new List<AudioSessionInfo>();
        if (_manager is null)
        {
            return infos;
        }

        var processNames = BuildProcessNameMap();

        IAudioSessionEnumerator? sessionEnum = null;
        try
        {
            sessionEnum = _manager.GetSessionEnumerator();
            sessionEnum.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                IAudioSessionControl? control = null;
                try
                {
                    sessionEnum.GetSession(i, out control);
                    var control2 = (IAudioSessionControl2)control;

                    control2.GetProcessId(out var pid);

                    string identifier;
                    unsafe
                    {
                        PWSTR raw;
                        control2.GetSessionIdentifier(&raw);
                        identifier = raw.Value is null ? string.Empty : raw.ToString();
                        if (raw.Value is not null)
                        {
                            PInvoke.CoTaskMemFree(raw.Value);
                        }
                    }

                    processNames.TryGetValue(pid, out var name);
                    var isSharedHost = AudioMatchPolicy.IsSharedHost(name);
                    infos.Add(new AudioSessionInfo(pid, identifier, isSharedHost));
                }
                catch (COMException)
                {
                    // A session that vanished mid-enumeration; skip it.
                }
                finally
                {
                    if (control is not null)
                    {
                        Marshal.FinalReleaseComObject(control);
                    }
                }
            }
        }
        finally
        {
            if (sessionEnum is not null)
            {
                Marshal.FinalReleaseComObject(sessionEnum);
            }
        }

        return infos;
    }

    private void ForEachMatchingVolume(AudioMatch match, Action<ISimpleAudioVolume> apply)
    {
        EnsureInitialized();
        if (_manager is null)
        {
            return;
        }

        var wanted = new HashSet<string>(match.AudioSessionIds, StringComparer.OrdinalIgnoreCase);

        IAudioSessionEnumerator? sessionEnum = null;
        try
        {
            sessionEnum = _manager.GetSessionEnumerator();
            sessionEnum.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                IAudioSessionControl? control = null;
                try
                {
                    sessionEnum.GetSession(i, out control);
                    var control2 = (IAudioSessionControl2)control;

                    string identifier;
                    unsafe
                    {
                        PWSTR raw;
                        control2.GetSessionIdentifier(&raw);
                        identifier = raw.Value is null ? string.Empty : raw.ToString();
                        if (raw.Value is not null)
                        {
                            PInvoke.CoTaskMemFree(raw.Value);
                        }
                    }

                    if (identifier.Length == 0 || !wanted.Contains(identifier))
                    {
                        continue;
                    }

                    var volume = (ISimpleAudioVolume)control;
                    apply(volume);
                }
                catch (COMException)
                {
                    // Session vanished; skip.
                }
                finally
                {
                    if (control is not null)
                    {
                        Marshal.FinalReleaseComObject(control);
                    }
                }
            }
        }
        finally
        {
            if (sessionEnum is not null)
            {
                Marshal.FinalReleaseComObject(sessionEnum);
            }
        }
    }

    /// <summary>Releases the manager and endpoint bound to a specific render device; keeps the device enumerator.</summary>
    private void ReleaseEndpoint()
    {
        if (_manager is not null)
        {
            try
            {
                _manager.UnregisterSessionNotification(_sessionSink);
            }
            catch (COMException)
            {
                // Endpoint already gone.
            }

            Marshal.FinalReleaseComObject(_manager);
            _manager = null;
        }

        if (_endpoint is not null)
        {
            Marshal.FinalReleaseComObject(_endpoint);
            _endpoint = null;
        }
    }

    private void ReleaseCom()
    {
        ReleaseEndpoint();

        if (_deviceEnumerator is not null)
        {
            try
            {
                _deviceEnumerator.UnregisterEndpointNotificationCallback(_deviceSink);
            }
            catch (COMException)
            {
                // Already unregistered.
            }

            Marshal.FinalReleaseComObject(_deviceEnumerator);
            _deviceEnumerator = null;
        }

        _initialized = false;
    }

    private void RaiseChanged()
    {
        if (_disposed)
        {
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // --- Candidate resolution (no COM; runs off the dispatcher) ---

    private static (AudioSourceKind Kind, IReadOnlyCollection<uint> Pids) ResolveCandidates(string sourceAppId)
    {
        return sourceAppId.Contains('!', StringComparison.Ordinal)
            ? (AudioSourceKind.Package, ResolvePackagePids(sourceAppId))
            : (AudioSourceKind.Executable, ResolveExecutablePids(sourceAppId));
    }

    private static IReadOnlyCollection<uint> ResolveExecutablePids(string sourceAppId)
    {
        var name = sourceAppId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? sourceAppId[..^4]
            : sourceAppId;

        var pids = new HashSet<uint>();
        try
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    pids.Add((uint)p.Id);
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Process table changed underneath us.
        }

        return pids;
    }

    private static IReadOnlyCollection<uint> ResolvePackagePids(string aumid)
    {
        var familyName = ResolvePackageFamilyName(aumid);
        var pids = new HashSet<uint>();
        if (familyName is null)
        {
            return pids;
        }

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (string.Equals(GetPackageFamilyForProcess((uint)p.Id), familyName, StringComparison.OrdinalIgnoreCase))
                {
                    pids.Add((uint)p.Id);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Process exited or is inaccessible; ignore it.
            }
            finally
            {
                p.Dispose();
            }
        }

        return pids;
    }

    private static string? ResolvePackageFamilyName(string aumid)
    {
        // Preferred: the WinRT projection, which validates the AUMID against the installed catalogue.
        try
        {
            var info = global::Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(aumid);
            if (!string.IsNullOrEmpty(info?.PackageFamilyName))
            {
                return info.PackageFamilyName;
            }
        }
        catch (Exception)
        {
            // AppInfo may be unavailable (unpackaged host) or the AUMID may not resolve; fall back to parsing.
        }

        // Fallback: a packaged AUMID is "<PackageFamilyName>!<AppId>".
        var bang = aumid.IndexOf('!', StringComparison.Ordinal);
        return bang > 0 ? aumid[..bang] : null;
    }

    private static string? GetPackageFamilyForProcess(uint pid)
    {
        SafeHandle? handle = null;
        try
        {
            handle = PInvoke.OpenProcess_SafeHandle(
                global::Windows.Win32.System.Threading.PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
                false,
                pid);
            if (handle.IsInvalid)
            {
                return null;
            }

            uint length = 0;
            var status = PInvoke.GetPackageFamilyName(handle, ref length);
            if (length == 0)
            {
                return null;
            }

            var buffer = new char[length];
            status = PInvoke.GetPackageFamilyName(handle, ref length, buffer);
            if (status != WIN32_ERROR.ERROR_SUCCESS)
            {
                return null;
            }

            var end = Array.IndexOf(buffer, '\0');
            return new string(buffer, 0, end < 0 ? (int)length : end);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            handle?.Dispose();
        }
    }

    private static Dictionary<uint, string> BuildProcessNameMap()
    {
        var map = new Dictionary<uint, string>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                map[(uint)p.Id] = p.ProcessName;
            }
            catch (InvalidOperationException)
            {
                // Exited before we read its name.
            }
            finally
            {
                p.Dispose();
            }
        }

        return map;
    }

    /// <summary>Managed CCW for session-creation notifications. Re-match happens in the consumer via <c>Changed</c>.</summary>
    private sealed class SessionNotificationSink : IAudioSessionNotification
    {
        private readonly Action _onChanged;

        public SessionNotificationSink(Action onChanged) => _onChanged = onChanged;

        public void OnSessionCreated(IAudioSessionControl NewSession) => _onChanged();
    }

    /// <summary>Managed CCW for endpoint changes; only the default render device change is of interest.</summary>
    private sealed class DeviceNotificationSink : IMMNotificationClient
    {
        private readonly Action _onChanged;

        public DeviceNotificationSink(Action onChanged) => _onChanged = onChanged;

        public void OnDeviceStateChanged(PCWSTR pwstrDeviceId, DEVICE_STATE dwNewState)
        {
        }

        public void OnDeviceAdded(PCWSTR pwstrDeviceId)
        {
        }

        public void OnDeviceRemoved(PCWSTR pwstrDeviceId)
        {
        }

        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, PCWSTR pwstrDefaultDeviceId)
        {
            if (flow == EDataFlow.eRender && role == ERole.eMultimedia)
            {
                _onChanged();
            }
        }

        public void OnPropertyValueChanged(PCWSTR pwstrDeviceId, PROPERTYKEY key)
        {
        }
    }
}
