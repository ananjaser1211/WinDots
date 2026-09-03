using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Windows.ApplicationModel;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;
using WinDots.Core.Contracts;
using WinDots.Core.Media;

namespace WinDots.Windows.Media;

/// <summary>
/// Adapts one <see cref="GlobalSystemMediaTransportControlsSession"/> to <see cref="IMediaSession"/>.
/// Every platform call is wrapped: a vanished session yields an empty snapshot or a faulted command, never an exception.
/// Events are raised on the platform's callback thread; consumers marshal to their own context.
/// </summary>
public sealed class GsmtcSession : IMediaSession, IDisposable
{
    private readonly GlobalSystemMediaTransportControlsSession _session;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private MediaSnapshot _current;
    private bool _disposed;

    internal GsmtcSession(GlobalSystemMediaTransportControlsSession session, string id)
    {
        _session = session;
        Id = id;
        SourceAppId = session.SourceAppUserModelId ?? string.Empty;
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

    public async Task<ArtworkResult> LoadArtworkAsync(int maxBytes, CancellationToken ct)
    {
        if (_disposed)
        {
            return ArtworkResult.Failed("Session is gone.");
        }

        try
        {
            var props = await _session.TryGetMediaPropertiesAsync().AsTask(ct).ConfigureAwait(false);
            var reference = props?.Thumbnail;
            if (reference is null)
            {
                return ArtworkResult.None;
            }

            using var stream = await reference.OpenReadAsync().AsTask(ct).ConfigureAwait(false);
            if (stream.Size == 0)
            {
                return ArtworkResult.None;
            }

            if (stream.Size > (ulong)maxBytes)
            {
                return ArtworkResult.Failed($"Artwork of {stream.Size} bytes exceeds the {maxBytes} byte limit.");
            }

            using var reader = new DataReader(stream);
            var size = (uint)stream.Size;
            var loaded = await reader.LoadAsync(size).AsTask(ct).ConfigureAwait(false);
            var bytes = new byte[loaded];
            reader.ReadBytes(bytes);
            return ArtworkResult.Loaded(bytes, stream.ContentType);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            return ArtworkResult.Failed(ex.Message);
        }
    }

    /// <summary>Re-reads every property from the platform and publishes a new snapshot.</summary>
    internal async Task RefreshAsync(CancellationToken ct)
    {
        if (_disposed)
        {
            return;
        }

        await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await BuildSnapshotAsync(ct).ConfigureAwait(false);
            Volatile.Write(ref _current, snapshot);
            Updated?.Invoke(this, snapshot);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.MediaPropertiesChanged -= OnPlatformChanged;
        _session.PlaybackInfoChanged -= OnPlatformChanged;
        _session.TimelinePropertiesChanged -= OnPlatformChanged;
        _refreshGate.Dispose();
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
            await RefreshAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            // The session vanished mid-refresh. The provider will drop it on the next SessionsChanged.
        }
    }

    private async Task<MediaSnapshot> BuildSnapshotAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        GlobalSystemMediaTransportControlsSessionMediaProperties? props = null;
        try
        {
            props = await _session.TryGetMediaPropertiesAsync().AsTask(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // Metadata unavailable; keep going with playback and timeline.
        }

        var playback = SafeGet(_session.GetPlaybackInfo);
        var timeline = SafeGet(_session.GetTimelineProperties);

        var artists = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(props?.Artist))
        {
            artists.Add(props!.Artist.Trim());
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
                : new Timeline(timeline.StartTime, timeline.EndTime, timeline.Position, timeline.LastUpdatedTime, playback?.PlaybackRate ?? 1.0),
            Shuffle: playback?.IsShuffleActive,
            Repeat: ToRepeat(playback?.AutoRepeatMode),
            ArtworkKey: props?.Thumbnail is null ? null : ArtworkKeyFor(props),
            CapturedAt: now);
    }

    private async Task<CommandResult> RunCommandAsync(Capabilities required, Func<global::Windows.Foundation.IAsyncOperation<bool>> command, CancellationToken ct)
    {
        if (_disposed)
        {
            return CommandResult.Rejected("Session is gone.");
        }

        if (!Current.Can(required))
        {
            return CommandResult.Unsupported(required.ToString());
        }

        try
        {
            var accepted = await command().AsTask(ct).ConfigureAwait(false);
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
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return CommandResult.Faulted(ex);
        }
    }

    private static T? SafeGet<T>(Func<T> getter)
        where T : class
    {
        try
        {
            return getter();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
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
            var info = AppInfo.GetFromAppUserModelId(aumid);
            var name = info?.DisplayInfo?.DisplayName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or FileNotFoundException)
        {
            // Not a packaged app; fall through to the executable name.
        }

        var name2 = aumid;
        var bang = name2.LastIndexOf('!');
        if (bang >= 0 && bang < name2.Length - 1)
        {
            name2 = name2[(bang + 1)..];
        }

        if (name2.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name2 = name2[..^4];
        }

        return name2;
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
        if (c.IsPlayPauseToggleEnabled || (c.IsPlayEnabled && c.IsPauseEnabled)) caps |= Capabilities.PlayPause;
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
