using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Windows.ApplicationModel;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;
using WinDots.Core.Contracts;
using WinDots.Core.Media;
using WinDots.Windows.Threading;

namespace WinDots.Windows.Media;

/// <summary>
/// Adapts one <see cref="GlobalSystemMediaTransportControlsSession"/> to <see cref="IMediaSession"/>.
/// Every platform call runs on the owning <see cref="MediaDispatcher"/> thread. A vanished session yields an
/// empty snapshot or a faulted command, never an exception. <see cref="Updated"/> is raised on the dispatcher
/// thread; consumers marshal to their own context.
/// </summary>
public sealed class GsmtcSession : IMediaSession, IDisposable
{
    private const int ArtworkChunkBytes = 64 * 1024;

    private readonly GlobalSystemMediaTransportControlsSession _session;
    private readonly MediaDispatcher _dispatcher;
    private MediaSnapshot _current;
    private volatile bool _disposed;

    /// <summary>Must be constructed on the dispatcher thread.</summary>
    internal GsmtcSession(GlobalSystemMediaTransportControlsSession session, string id, MediaDispatcher dispatcher)
    {
        _session = session;
        _dispatcher = dispatcher;
        Id = id;
        SourceAppId = SafeGet(() => session.SourceAppUserModelId) ?? string.Empty;
        SourceDisplayName = ResolveDisplayName(SourceAppId);
        _current = MediaSnapshot.Empty(Id, SourceAppId, SourceDisplayName, DateTimeOffset.UtcNow);

        _session.MediaPropertiesChanged += OnPlatformChanged;
        _session.PlaybackInfoChanged += OnPlatformChanged;
        _session.TimelinePropertiesChanged += OnPlatformChanged;
    }

    public string Id { get; }

    public string SourceAppId { get; }

    public string SourceDisplayName { get; }

    public MediaSnapshot Current => Volatile.Read(ref _current);

    /// <summary>True once <see cref="Dispose"/> ran; the provider drops disposed wrappers on the next reconcile.</summary>
    internal bool IsDisposed => _disposed;

    /// <summary>
    /// Dispatcher thread only. Reads the state the platform session reports right now so the provider can tell
    /// whether an enumerated session object is this wrapper's session; null when the object no longer answers.
    /// See <see cref="SessionFingerprint"/> for why identity has to be inferred this way.
    /// </summary>
    internal SessionFingerprint? Fingerprint() => _disposed ? null : SessionFingerprint.Read(_session);

    public event EventHandler<MediaSnapshot>? Updated;

    public Task<CommandResult> TryPlayPauseAsync(CancellationToken ct) =>
        RunCommandAsync(Capabilities.PlayPause, () => _session.TryTogglePlayPauseAsync(), ct);

    public Task<CommandResult> TryNextAsync(CancellationToken ct) =>
        RunCommandAsync(Capabilities.Next, () => _session.TrySkipNextAsync(), ct);

    public Task<CommandResult> TryPreviousAsync(CancellationToken ct) =>
        RunCommandAsync(Capabilities.Previous, () => _session.TrySkipPreviousAsync(), ct);

    public Task<CommandResult> TrySeekAsync(TimeSpan position, CancellationToken ct) =>
        RunCommandAsync(Capabilities.Seek, () => _session.TryChangePlaybackPositionAsync(position.Ticks), ct);

    public Task<CommandResult> TrySetShuffleAsync(bool enabled, CancellationToken ct) =>
        RunCommandAsync(Capabilities.Shuffle, () => _session.TryChangeShuffleActiveAsync(enabled), ct);

    public Task<CommandResult> TrySetRepeatAsync(RepeatMode mode, CancellationToken ct) =>
        RunCommandAsync(Capabilities.Repeat, () => _session.TryChangeAutoRepeatModeAsync(ToPlatformRepeat(mode)), ct);

    public Task<ArtworkResult> LoadArtworkAsync(int maxBytes, CancellationToken ct)
    {
        if (_disposed || _dispatcher.IsDisposed)
        {
            return Task.FromResult(ArtworkResult.Failed("Session is gone."));
        }

        if (maxBytes <= 0)
        {
            return Task.FromResult(ArtworkResult.Failed("Artwork byte limit must be positive."));
        }

        try
        {
            return _dispatcher.InvokeAsync(() => LoadArtworkOnDispatcherAsync(maxBytes, ct));
        }
        catch (ObjectDisposedException)
        {
            return Task.FromResult(ArtworkResult.Failed("Session is gone."));
        }
    }

    /// <summary>Re-reads every property from the platform and publishes a new snapshot. Runs on the dispatcher.</summary>
    internal Task RefreshAsync(CancellationToken ct) => _dispatcher.InvokeAsync(() => RefreshOnDispatcherAsync(ct));

    /// <summary>Dispatcher thread only: the platform event removal is a WinRT call.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _session.MediaPropertiesChanged -= OnPlatformChanged;
            _session.PlaybackInfoChanged -= OnPlatformChanged;
            _session.TimelinePropertiesChanged -= OnPlatformChanged;
        }
        catch (Exception ex) when (IsPlatformFailure(ex))
        {
            // The platform object is already dead; its event sources went with it.
        }
    }

    private static bool IsPlatformFailure(Exception ex) =>
        ex is COMException or InvalidOperationException or UnauthorizedAccessException or FileNotFoundException
            or ArgumentException or NotSupportedException or ObjectDisposedException;

    private async Task<ArtworkResult> LoadArtworkOnDispatcherAsync(int maxBytes, CancellationToken ct)
    {
        if (_disposed)
        {
            return ArtworkResult.Failed("Session is gone.");
        }

        try
        {
            var props = await _session.TryGetMediaPropertiesAsync().AsTask(ct);
            var reference = props?.Thumbnail;
            if (reference is null)
            {
                return ArtworkResult.None;
            }

            using var stream = await reference.OpenReadAsync().AsTask(ct);

            // Size is advisory: some streams report 0 for an unknown length, and a known size lets us refuse a
            // multi-megabyte frame grab (Windows Media Player) before touching a single byte.
            var declared = stream.Size;
            if (declared > (ulong)maxBytes)
            {
                return ArtworkResult.Failed($"Artwork of {declared} bytes exceeds the {maxBytes} byte limit.");
            }

            using var reader = new DataReader(stream) { InputStreamOptions = InputStreamOptions.Partial };
            var buffer = new MemoryStream(declared == 0 ? ArtworkChunkBytes : (int)declared);
            var chunk = new byte[ArtworkChunkBytes];
            while (true)
            {
                // LoadAsync may deliver fewer bytes than requested; keep pulling until the stream reports end-of-data.
                var loaded = await reader.LoadAsync(ArtworkChunkBytes).AsTask(ct);
                if (loaded == 0)
                {
                    break;
                }

                if ((ulong)buffer.Length + loaded > (ulong)maxBytes)
                {
                    return ArtworkResult.Failed($"Artwork exceeds the {maxBytes} byte limit.");
                }

                // ReadBytes fills the whole array it is given, so hand it exactly the bytes that were loaded.
                var part = loaded == ArtworkChunkBytes ? chunk : new byte[loaded];
                reader.ReadBytes(part);
                buffer.Write(part, 0, (int)loaded);
            }

            if (buffer.Length == 0)
            {
                return ArtworkResult.None;
            }

            return ArtworkResult.Loaded(buffer.ToArray(), Normalize(stream.ContentType));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsPlatformFailure(ex))
        {
            return ArtworkResult.Failed(ex.Message);
        }
    }

    private void OnPlatformChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        if (_disposed)
        {
            return;
        }

        _ = RefreshSafelyAsync();
    }

    private async Task RefreshSafelyAsync()
    {
        try
        {
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex) when (IsPlatformFailure(ex))
        {
            // The session vanished mid-refresh. The provider drops it on the next SessionsChanged.
        }
    }

    private async Task RefreshOnDispatcherAsync(CancellationToken ct)
    {
        if (_disposed)
        {
            return;
        }

        var snapshot = await BuildSnapshotAsync(ct);
        if (_disposed)
        {
            // Disposed while the platform reads were in flight; do not publish for a dead session.
            return;
        }

        Volatile.Write(ref _current, snapshot);
        Updated?.Invoke(this, snapshot);
    }

    private async Task<MediaSnapshot> BuildSnapshotAsync(CancellationToken ct)
    {
        GlobalSystemMediaTransportControlsSessionMediaProperties? props = null;
        try
        {
            props = await _session.TryGetMediaPropertiesAsync().AsTask(ct);
        }
        catch (Exception ex) when (IsPlatformFailure(ex))
        {
            // Metadata unavailable; keep going with playback and timeline.
        }

        var playback = SafeGet(_session.GetPlaybackInfo);
        var timeline = SafeGet(_session.GetTimelineProperties);

        // Captured after the reads so that CapturedAt is never earlier than a LastUpdated the platform stamped
        // during them; SessionQuality then clamps any LastUpdated that is still in the future.
        var now = DateTimeOffset.UtcNow;

        var artists = new List<string>(2);
        var artist = Normalize(props?.Artist);
        if (artist is not null)
        {
            artists.Add(artist);
        }

        return new MediaSnapshot(
            Id,
            SourceAppId,
            SourceDisplayName,
            Title: Normalize(props?.Title),
            Artists: artists,
            Album: Normalize(props?.AlbumTitle),
            Kind: ToKind(props?.PlaybackType),
            State: ToState(playback?.PlaybackStatus),
            Caps: ToCapabilities(playback?.Controls),
            Timeline: timeline is null
                ? Timeline.Empty
                : new Timeline(
                    timeline.StartTime,
                    timeline.EndTime,
                    timeline.Position,
                    SessionQuality.NormalizeLastUpdated(timeline.LastUpdatedTime, now),
                    SessionQuality.NormalizeRate(playback?.PlaybackRate)),
            Shuffle: playback?.IsShuffleActive,
            Repeat: ToRepeat(playback?.AutoRepeatMode),
            ArtworkKey: props?.Thumbnail is null ? null : ArtworkKeyFor(props),
            CapturedAt: now);
    }

    private Task<CommandResult> RunCommandAsync(Capabilities required, Func<global::Windows.Foundation.IAsyncOperation<bool>> command, CancellationToken ct)
    {
        if (_disposed || _dispatcher.IsDisposed)
        {
            return Task.FromResult(CommandResult.Rejected("Session is gone."));
        }

        if (!Current.Can(required))
        {
            return Task.FromResult(CommandResult.Unsupported(required.ToString()));
        }

        try
        {
            return _dispatcher.InvokeAsync(() => RunCommandOnDispatcherAsync(command, ct));
        }
        catch (ObjectDisposedException)
        {
            return Task.FromResult(CommandResult.Rejected("Session is gone."));
        }
    }

    private async Task<CommandResult> RunCommandOnDispatcherAsync(Func<global::Windows.Foundation.IAsyncOperation<bool>> command, CancellationToken ct)
    {
        if (_disposed)
        {
            return CommandResult.Rejected("Session is gone.");
        }

        try
        {
            var accepted = await command().AsTask(ct);
            if (!accepted)
            {
                return CommandResult.Rejected("The player declined the command.");
            }

            _ = RefreshSafelyAsync();
            return CommandResult.Succeeded;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsPlatformFailure(ex))
        {
            return CommandResult.Faulted(ex);
        }
    }

    private static T? SafeGet<T>(Func<T?> getter)
        where T : class
    {
        try
        {
            return getter();
        }
        catch (Exception ex) when (IsPlatformFailure(ex))
        {
            return null;
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ArtworkKeyFor(GlobalSystemMediaTransportControlsSessionMediaProperties props)
    {
        var seed = $"{props.Title}|{props.Artist}|{props.AlbumTitle}|{props.AlbumArtist}|{props.TrackNumber}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }

    private static string ResolveDisplayName(string aumid)
    {
        if (string.IsNullOrEmpty(aumid))
        {
            return "Unknown player";
        }

        try
        {
            // Throws ArgumentException / FileNotFoundException / COMException for anything that is not a packaged app
            // (browsers, classic desktop players); those fall through to the executable name.
            var info = AppInfo.GetFromAppUserModelId(aumid);
            var name = info?.DisplayInfo?.DisplayName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch (Exception ex) when (IsPlatformFailure(ex))
        {
            // Not a packaged app.
        }

        var name2 = aumid;
        var separator = name2.LastIndexOfAny(['!', '\\', '/']);
        if (separator >= 0 && separator < name2.Length - 1)
        {
            name2 = name2[(separator + 1)..];
        }

        if (name2.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name2 = name2[..^4];
        }

        return string.IsNullOrWhiteSpace(name2) ? aumid : name2;
    }

    private static MediaKind ToKind(MediaPlaybackType? type) => type switch
    {
        MediaPlaybackType.Music => MediaKind.Music,
        MediaPlaybackType.Video => MediaKind.Video,
        MediaPlaybackType.Image => MediaKind.Image,
        _ => MediaKind.Unknown,
    };

    private static PlaybackState ToState(GlobalSystemMediaTransportControlsSessionPlaybackStatus? status) => status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackState.Playing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackState.Paused,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => PlaybackState.Stopped,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => PlaybackState.Changing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => PlaybackState.Paused,
        _ => PlaybackState.Unknown,
    };

    private static Capabilities ToCapabilities(GlobalSystemMediaTransportControlsSessionPlaybackControls? c)
    {
        if (c is null)
        {
            return Capabilities.None;
        }

        var caps = Capabilities.None;
        if (c.IsPlayEnabled) caps |= Capabilities.Play;
        if (c.IsPauseEnabled) caps |= Capabilities.Pause;

        // TryTogglePlayPauseAsync works whenever either direction is enabled; players advertise only the direction
        // that currently applies (Pause while playing, Play while paused) and not always the toggle flag.
        if (c.IsPlayPauseToggleEnabled || c.IsPlayEnabled || c.IsPauseEnabled) caps |= Capabilities.PlayPause;
        if (c.IsNextEnabled) caps |= Capabilities.Next;
        if (c.IsPreviousEnabled) caps |= Capabilities.Previous;
        if (c.IsPlaybackPositionEnabled) caps |= Capabilities.Seek;
        if (c.IsShuffleEnabled) caps |= Capabilities.Shuffle;
        if (c.IsRepeatEnabled) caps |= Capabilities.Repeat;
        return caps;
    }

    private static RepeatMode? ToRepeat(MediaPlaybackAutoRepeatMode? mode) => mode switch
    {
        MediaPlaybackAutoRepeatMode.None => RepeatMode.None,
        MediaPlaybackAutoRepeatMode.Track => RepeatMode.Track,
        MediaPlaybackAutoRepeatMode.List => RepeatMode.List,
        _ => null,
    };

    private static MediaPlaybackAutoRepeatMode ToPlatformRepeat(RepeatMode mode) => mode switch
    {
        RepeatMode.Track => MediaPlaybackAutoRepeatMode.Track,
        RepeatMode.List => MediaPlaybackAutoRepeatMode.List,
        _ => MediaPlaybackAutoRepeatMode.None,
    };
}
