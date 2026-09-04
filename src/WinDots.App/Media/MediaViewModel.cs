using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using WinDots.App.Diagnostics;
using WinDots.App.Media.Controls;
using WinDots.Core.Contracts;
using WinDots.Core.Media;

namespace WinDots.App.Media;

/// <summary>
/// The presentation model for the media drawer. It projects the active <see cref="IMediaSession"/> (chosen by the
/// <see cref="ISessionCoordinator"/>) into bindable, UI-thread-affine properties, runs the timeline interpolation
/// timer while the drawer is open and playing, and turns UI intents into fallible player commands.
/// </summary>
/// <remarks>
/// Thread affinity: every coordinator and session event is marshalled onto the supplied <see cref="DispatcherQueue"/>
/// before any state changes, so all property mutations and <see cref="PropertyChanged"/> notifications happen on the
/// UI thread. Commands are async and observe <see cref="CommandResult"/>; a non-success surfaces a transient
/// <see cref="StatusText"/> and a redacted <see cref="ShellLog"/> line.
/// </remarks>
public sealed partial class MediaViewModel : INotifyPropertyChanged, IDisposable
{
    private const int ArtworkMaxBytes = 8 * 1024 * 1024;
    private const int ArtworkDecodeWidth = 440;
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(2);

    private readonly ISessionCoordinator _coordinator;
    private readonly IMediaSessionProvider _provider;
    private readonly IArtworkCache _artworkCache;
    private readonly MediaOptions _options;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timelineTimer;
    private readonly DispatcherQueueTimer _statusTimer;

    private IMediaSession? _activeSession;
    private Timeline _timeline = Timeline.Empty;
    private PlaybackState _timelineState = PlaybackState.Unknown;
    private SeekReconciliation? _pendingSeek;

    private string? _artworkKey;
    private CancellationTokenSource? _artworkCts;

    private bool _disposed;

    // Backing fields.
    private string _title = "Unknown title";
    private string _artistText = "Unknown artist";
    private string _albumText = "Unknown album";
    private string _sourceLabel = string.Empty;
    private bool _isEmpty = true;
    private PlaybackState _state = PlaybackState.Unknown;
    private Capabilities _capabilities = Capabilities.None;
    private bool _isPlaying;
    private bool? _isShuffleOn;
    private RepeatMode? _repeatMode;
    private TimeSpan _position;
    private TimeSpan? _duration;
    private double? _progress;
    private string _elapsedText = "0:00";
    private string _durationText = "0:00";
    private bool _canSeek;
    private ImageSource? _artwork;
    private IReadOnlyList<PlayerChooserItem> _chooserItems = Array.Empty<PlayerChooserItem>();
    private string _activeChooserLabel = "No player";
    private string _statusText = string.Empty;
    private bool _isDrawerOpen;

    public MediaViewModel(
        ISessionCoordinator coordinator,
        IMediaSessionProvider provider,
        IArtworkCache artworkCache,
        MediaOptions options,
        DispatcherQueue dispatcher)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _artworkCache = artworkCache ?? throw new ArgumentNullException(nameof(artworkCache));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        _timelineTimer = _dispatcher.CreateTimer();
        _timelineTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, _options.TimelineTickMs));
        _timelineTimer.IsRepeating = true;
        _timelineTimer.Tick += OnTimelineTick;

        _statusTimer = _dispatcher.CreateTimer();
        _statusTimer.Interval = StatusDuration;
        _statusTimer.IsRepeating = false;
        _statusTimer.Tick += OnStatusExpired;

        _coordinator.ActiveChanged += OnActiveChanged;
        _coordinator.CandidatesChanged += OnCandidatesChanged;

        _activeSession = _coordinator.Active;
        if (_activeSession is not null)
        {
            _activeSession.Updated += OnSessionUpdated;
        }

        RefreshFromActive();
        RebuildChooser();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get => _title; private set => Set(ref _title, value); }

    public string ArtistText { get => _artistText; private set => Set(ref _artistText, value); }

    public string AlbumText { get => _albumText; private set => Set(ref _albumText, value); }

    public string SourceLabel { get => _sourceLabel; private set => Set(ref _sourceLabel, value); }

    public bool IsEmpty { get => _isEmpty; private set => Set(ref _isEmpty, value); }

    public PlaybackState State { get => _state; private set => Set(ref _state, value); }

    public Capabilities Capabilities { get => _capabilities; private set => Set(ref _capabilities, value); }

    public bool IsPlaying { get => _isPlaying; private set => Set(ref _isPlaying, value); }

    public bool? IsShuffleOn { get => _isShuffleOn; private set => Set(ref _isShuffleOn, value); }

    public RepeatMode? RepeatMode { get => _repeatMode; private set => Set(ref _repeatMode, value); }

    public TimeSpan Position { get => _position; private set => Set(ref _position, value); }

    public TimeSpan? Duration { get => _duration; private set => Set(ref _duration, value); }

    public double? Progress { get => _progress; private set => Set(ref _progress, value); }

    public string ElapsedText { get => _elapsedText; private set => Set(ref _elapsedText, value); }

    public string DurationText { get => _durationText; private set => Set(ref _durationText, value); }

    public bool CanSeek { get => _canSeek; private set => Set(ref _canSeek, value); }

    public ImageSource? Artwork { get => _artwork; private set => Set(ref _artwork, value); }

    public IReadOnlyList<PlayerChooserItem> ChooserItems
    {
        get => _chooserItems;
        private set => Set(ref _chooserItems, value);
    }

    public string ActiveChooserLabel { get => _activeChooserLabel; private set => Set(ref _activeChooserLabel, value); }

    /// <summary>Transient status line (e.g. a rejected command). Cleared after two seconds.</summary>
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    /// <summary>Set by the host. The timeline timer runs only while this is true and playback is active.</summary>
    public bool IsDrawerOpen
    {
        get => _isDrawerOpen;
        set
        {
            if (Set(ref _isDrawerOpen, value) && value)
            {
                // Re-seed the displayed position the moment the drawer opens.
                UpdateTimeline();
            }

            UpdateTimelineTimer();
        }
    }

    // --- Commands ---

    public Task PlayPauseAsync() =>
        InvokeAsync("PlayPause", (session, ct) => session.TryPlayPauseAsync(ct));

    public Task NextAsync() =>
        InvokeAsync("Next", (session, ct) => session.TryNextAsync(ct));

    public Task PreviousAsync() =>
        InvokeAsync("Previous", (session, ct) => session.TryPreviousAsync(ct));

    public Task ToggleShuffleAsync()
    {
        bool enabled = !(IsShuffleOn ?? false);
        return InvokeAsync("Shuffle", (session, ct) => session.TrySetShuffleAsync(enabled, ct));
    }

    public Task CycleRepeatAsync()
    {
        RepeatMode next = (RepeatMode ?? WinDots.Core.Media.RepeatMode.None) switch
        {
            WinDots.Core.Media.RepeatMode.None => WinDots.Core.Media.RepeatMode.List,
            WinDots.Core.Media.RepeatMode.List => WinDots.Core.Media.RepeatMode.Track,
            _ => WinDots.Core.Media.RepeatMode.None,
        };

        return InvokeAsync("Repeat", (session, ct) => session.TrySetRepeatAsync(next, ct));
    }

    public async Task SeekAsync(TimeSpan target)
    {
        IMediaSession? session = _activeSession;
        if (session is null)
        {
            return;
        }

        // Optimistic: show the requested position immediately and suppress far timeline updates for the hold window.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _pendingSeek = SeekReconciliation.Begin(ClampToTrack(target), now);
        UpdateTimeline();

        await ObserveAsync("Seek", session.TrySeekAsync(target, CancellationToken.None)).ConfigureAwait(true);
    }

    /// <summary>Pins the given session, or clears the pin (returns to automatic) when <paramref name="id"/> is null.</summary>
    public void SelectPlayer(string? id)
    {
        if (id is null)
        {
            _coordinator.ClearPin();
        }
        else
        {
            _coordinator.Pin(id);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _coordinator.ActiveChanged -= OnActiveChanged;
        _coordinator.CandidatesChanged -= OnCandidatesChanged;
        if (_activeSession is not null)
        {
            _activeSession.Updated -= OnSessionUpdated;
            _activeSession = null;
        }

        _timelineTimer.Stop();
        _timelineTimer.Tick -= OnTimelineTick;
        _statusTimer.Stop();
        _statusTimer.Tick -= OnStatusExpired;

        CancelArtworkLoad();
    }

    // --- Coordinator / session events (marshalled onto the UI thread) ---

    private void OnActiveChanged(object? sender, EventArgs e) => Marshal(HandleActiveChanged);

    private void OnCandidatesChanged(object? sender, EventArgs e) => Marshal(() =>
    {
        RebuildChooser();
        // The active label may change even when the active session reference does not.
        RefreshChooserLabel();
    });

    private void OnSessionUpdated(object? sender, MediaSnapshot e) => Marshal(RefreshFromActive);

    private void HandleActiveChanged()
    {
        IMediaSession? next = _coordinator.Active;
        if (!ReferenceEquals(next, _activeSession))
        {
            if (_activeSession is not null)
            {
                _activeSession.Updated -= OnSessionUpdated;
            }

            _activeSession = next;
            if (_activeSession is not null)
            {
                _activeSession.Updated += OnSessionUpdated;
            }

            // A different session invalidates any in-flight optimistic seek.
            _pendingSeek = null;
        }

        RefreshFromActive();
        RebuildChooser();
    }

    // --- Projection ---

    private void RefreshFromActive()
    {
        if (_disposed)
        {
            return;
        }

        IMediaSession? session = _activeSession;
        if (session is null)
        {
            ApplyEmptyState();
            UpdateTimelineTimer();
            return;
        }

        MediaSnapshot snapshot = session.Current;

        IsEmpty = false;
        Title = string.IsNullOrWhiteSpace(snapshot.Title) ? "Unknown title" : snapshot.Title!;
        ArtistText = snapshot.Artists.Count > 0 ? string.Join(", ", snapshot.Artists) : "Unknown artist";
        AlbumText = string.IsNullOrWhiteSpace(snapshot.Album) ? "Unknown album" : snapshot.Album!;
        SourceLabel = _options.AliasFor(snapshot.SourceAppId, snapshot.SourceDisplayName);
        State = snapshot.State;
        Capabilities = snapshot.Caps;
        IsPlaying = snapshot.State == PlaybackState.Playing;
        IsShuffleOn = snapshot.Shuffle;
        RepeatMode = snapshot.Repeat;
        CanSeek = snapshot.Can(Capabilities.Seek);

        _timeline = snapshot.Timeline;
        _timelineState = snapshot.State;
        UpdateTimeline();
        UpdateTimelineTimer();

        RefreshChooserLabel();
        UpdateArtwork(session, snapshot.ArtworkKey);
    }

    private void ApplyEmptyState()
    {
        IsEmpty = true;
        Title = "Unknown title";
        ArtistText = "Unknown artist";
        AlbumText = "Unknown album";
        SourceLabel = string.Empty;
        State = PlaybackState.Unknown;
        Capabilities = Capabilities.None;
        IsPlaying = false;
        IsShuffleOn = null;
        RepeatMode = null;
        CanSeek = false;

        _timeline = Timeline.Empty;
        _timelineState = PlaybackState.Unknown;
        _pendingSeek = null;
        Position = TimeSpan.Zero;
        Duration = null;
        Progress = null;
        ElapsedText = "0:00";
        DurationText = "0:00";

        CancelArtworkLoad();
        _artworkKey = null;
        Artwork = null;

        RefreshChooserLabel();
    }

    private void UpdateTimeline()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeSpan displayed = TimelineInterpolator.Displayed(_timeline, _timelineState, now);

        if (_pendingSeek is { } seek)
        {
            if (seek.ShouldAccept(displayed, now))
            {
                _pendingSeek = null;
            }
            else
            {
                displayed = seek.Target;
            }
        }

        TimeSpan? duration = _timeline.HasDuration ? _timeline.Duration : null;
        double? progress = null;
        if (duration is { } d && d > TimeSpan.Zero)
        {
            double fraction = (displayed - _timeline.Start).Ticks / (double)d.Ticks;
            progress = Math.Clamp(fraction, 0.0, 1.0);
        }

        Position = displayed;
        Duration = duration;
        Progress = progress;
        ElapsedText = TimeFormat.Clock(displayed);
        DurationText = TimeFormat.Clock(duration);
    }

    private TimeSpan ClampToTrack(TimeSpan value)
    {
        if (value < _timeline.Start)
        {
            return _timeline.Start;
        }

        if (_timeline.HasDuration && value > _timeline.End)
        {
            return _timeline.End;
        }

        return value;
    }

    private void UpdateTimelineTimer()
    {
        bool shouldRun = _isDrawerOpen && IsPlaying && _activeSession is not null;
        if (shouldRun)
        {
            if (!_timelineTimer.IsRunning)
            {
                _timelineTimer.Start();
            }
        }
        else if (_timelineTimer.IsRunning)
        {
            _timelineTimer.Stop();
        }
    }

    private void OnTimelineTick(DispatcherQueueTimer sender, object args) => UpdateTimeline();

    // --- Chooser ---

    private void RebuildChooser()
    {
        IReadOnlyList<IMediaSession> candidates = _coordinator.Candidates;
        IMediaSession? active = _coordinator.Active;
        var items = new List<PlayerChooserItem>(candidates.Count);
        foreach (IMediaSession candidate in candidates)
        {
            MediaSnapshot snapshot = candidate.Current;
            string label = _options.AliasFor(snapshot.SourceAppId, snapshot.SourceDisplayName);
            items.Add(new PlayerChooserItem(
                candidate.Id,
                label,
                StateText(snapshot.State),
                ReferenceEquals(candidate, active)));
        }

        ChooserItems = items;
    }

    private void RefreshChooserLabel()
    {
        if (_activeSession is not null)
        {
            MediaSnapshot snapshot = _activeSession.Current;
            ActiveChooserLabel = _options.AliasFor(snapshot.SourceAppId, snapshot.SourceDisplayName);
            return;
        }

        IMediaSession? systemCurrent = _provider.SystemCurrent;
        if (systemCurrent is not null)
        {
            MediaSnapshot snapshot = systemCurrent.Current;
            ActiveChooserLabel = _options.AliasFor(snapshot.SourceAppId, snapshot.SourceDisplayName);
        }
        else
        {
            ActiveChooserLabel = "No player";
        }
    }

    private static string StateText(PlaybackState state) => state switch
    {
        PlaybackState.Playing => "Playing",
        PlaybackState.Paused => "Paused",
        PlaybackState.Stopped => "Stopped",
        PlaybackState.Changing => "Changing",
        _ => string.Empty,
    };

    // --- Artwork ---

    private void UpdateArtwork(IMediaSession session, string? key)
    {
        if (string.Equals(key, _artworkKey, StringComparison.Ordinal))
        {
            return;
        }

        CancelArtworkLoad();
        _artworkKey = key;

        if (key is null)
        {
            Artwork = null;
            return;
        }

        var cts = new CancellationTokenSource();
        _artworkCts = cts;
        _ = LoadArtworkAsync(session, key, cts.Token);
    }

    private async Task LoadArtworkAsync(IMediaSession session, string key, CancellationToken ct)
    {
        try
        {
            CachedArtwork? cached = await _artworkCache
                .GetOrAddAsync(key, token => session.LoadArtworkAsync(ArtworkMaxBytes, token), ct)
                .ConfigureAwait(true);

            if (ct.IsCancellationRequested || !string.Equals(key, _artworkKey, StringComparison.Ordinal))
            {
                return;
            }

            if (cached is null || cached.Bytes.IsEmpty)
            {
                Artwork = null;
                return;
            }

            BitmapImage? image = await DecodeAsync(cached.Bytes.ToArray(), ct).ConfigureAwait(true);

            if (ct.IsCancellationRequested || !string.Equals(key, _artworkKey, StringComparison.Ordinal))
            {
                return;
            }

            Artwork = image;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer key; nothing to do.
        }
        catch (Exception ex)
        {
            ShellLog.Write($"artwork load failed: {ex.GetType().Name} (0x{ex.HResult:X8})");
        }
    }

    private static async Task<BitmapImage?> DecodeAsync(byte[] bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer());
        stream.Seek(0);

        var image = new BitmapImage { DecodePixelWidth = ArtworkDecodeWidth };
        await image.SetSourceAsync(stream);
        return image;
    }

    private void CancelArtworkLoad()
    {
        if (_artworkCts is { } cts)
        {
            _artworkCts = null;
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            cts.Dispose();
        }
    }

    // --- Command plumbing ---

    private Task InvokeAsync(string name, Func<IMediaSession, CancellationToken, Task<CommandResult>> command)
    {
        IMediaSession? session = _activeSession;
        if (session is null)
        {
            return Task.CompletedTask;
        }

        return ObserveAsync(name, command(session, CancellationToken.None));
    }

    private async Task ObserveAsync(string name, Task<CommandResult> pending)
    {
        CommandResult result;
        try
        {
            result = await pending.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            result = CommandResult.Faulted(ex);
        }

        if (!result.IsSuccess)
        {
            ShellLog.Write($"command {name} {result.Status}: {result.Message}");
            ShowStatus(result.Message ?? $"{name} failed.");
        }
    }

    private void ShowStatus(string message)
    {
        StatusText = message;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void OnStatusExpired(DispatcherQueueTimer sender, object args)
    {
        _statusTimer.Stop();
        StatusText = string.Empty;
    }

    // --- Infrastructure ---

    private void Marshal(Action action)
    {
        if (_disposed)
        {
            return;
        }

        if (_dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcher.TryEnqueue(() =>
            {
                if (!_disposed)
                {
                    action();
                }
            });
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
