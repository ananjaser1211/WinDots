using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinDots.App.Diagnostics;
using WinDots.App.Media.Controls;
using WinDots.Core.Contracts;
using WinDots.Core.Design;
using WinDots.Core.Lyrics;
using WinDots.Core.Media;
using WinDots.Core.Settings;
using CoreLyricsProvider = WinDots.Core.Settings.LyricsProvider;

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

    // Palette extraction decodes a fixed 64x64 BGRA copy (see _docs/04-visual-design.md); WdMotionSlowMs is the
    // 400 ms colour cross-fade for the palette transition.
    private const int PaletteDim = 64;
    private const int PaletteMotionMs = 400;

    // Reduced motion maps every token to a 100 ms linear transition (_docs/04-visual-design.md).
    private const int ReducedPaletteMotionMs = 100;
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan VolumeDebounce = TimeSpan.FromMilliseconds(50);

    private readonly ISessionCoordinator _coordinator;
    private readonly IMediaSessionProvider _provider;
    private readonly IArtworkCache _artworkCache;
    private readonly IAudioSessionProvider? _audio;
    private MediaOptions _options;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timelineTimer;
    private readonly DispatcherQueueTimer _statusTimer;
    private readonly DispatcherQueueTimer _volumeTimer;

    // Per-application volume (Milestone 5). Only a High match (or Medium with media.allowSharedVolume) may be
    // acted on; with no match the provider is never called, so no unrelated application can be touched.
    private AudioMatch? _audioMatch;
    private CancellationTokenSource? _audioCts;
    private int? _pendingVolume;
    private bool _volumeAvailable;
    private bool _volumeShared;
    private string _volumeExplanation = "No player is active.";
    private int _volumeLevel;
    private bool _isMuted;

    // --- Lyrics (E3) ---
    private readonly ILyricsProvider? _lyricsProvider;
    private readonly LyricsCache? _lyricsCache;
    private readonly LyricsOffsetStore? _offsetStore;
    private CoreLyricsProvider _lyricsMode = CoreLyricsProvider.Off;
    private int _defaultOffsetMs;
    private string? _lyricsTrackKey;
    private LyricsQuery? _lyricsQuery;
    private CancellationTokenSource? _lyricsCts;
    private IReadOnlyList<LyricsLine> _lyricsModelLines = Array.Empty<LyricsLine>();
    private int _lyricsOffsetMs;

    private IMediaSession? _activeSession;
    private Timeline _timeline = Timeline.Empty;
    private PlaybackState _timelineState = PlaybackState.Unknown;
    private SeekReconciliation? _pendingSeek;

    private string? _artworkKey;
    private CancellationTokenSource? _artworkCts;

    // --- Artwork palette (Milestone 4) ---
    // The four brushes are created once on the UI thread and mutated in place by a 400 ms ColorAnimation, so the
    // bound controls animate rather than snap. _paletteBgra holds the last decoded 64x64 copy so a theme or
    // settings change can recompute without re-fetching artwork.
    private readonly IPaletteService _paletteService = new PaletteService();
    private readonly SolidColorBrush _accentBrush = new();
    private readonly SolidColorBrush _onAccentBrush = new();
    private readonly SolidColorBrush _accentContainerBrush = new();
    private readonly SolidColorBrush _blobTintBrush = new();
    private Func<bool>? _isDarkTheme;
    private PaletteSource _paletteSource = PaletteSource.Artwork;
    private uint _fixedAccent;
    private bool _fixedAccentValid;
    private byte[]? _paletteBgra;
    private Palette? _currentPalette;
    private bool _reduceMotion;
    private bool _highContrast;

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

    // Lyrics backing fields.
    private IReadOnlyList<string> _lyricsLines = Array.Empty<string>();
    private int _lyricsCurrentIndex = -1;
    private bool _lyricsSynced;
    private string _lyricsAttribution = string.Empty;
    private LyricsState _lyricsState = LyricsState.Off;

    public MediaViewModel(
        ISessionCoordinator coordinator,
        IMediaSessionProvider provider,
        IArtworkCache artworkCache,
        MediaOptions options,
        DispatcherQueue dispatcher,
        IAudioSessionProvider? audio = null,
        ILyricsProvider? lyricsProvider = null,
        LyricsCache? lyricsCache = null,
        LyricsOffsetStore? offsetStore = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _artworkCache = artworkCache ?? throw new ArgumentNullException(nameof(artworkCache));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _audio = audio;
        _lyricsProvider = lyricsProvider;
        _lyricsCache = lyricsCache;
        _offsetStore = offsetStore;

        _volumeTimer = _dispatcher.CreateTimer();
        _volumeTimer.Interval = VolumeDebounce;
        _volumeTimer.IsRepeating = false;
        _volumeTimer.Tick += OnVolumeDebounceElapsed;
        if (_audio is not null)
        {
            _audio.Changed += OnAudioChanged;
        }

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
        _coordinator.ShowAllSourcesChanged += OnShowAllSourcesChanged;

        _activeSession = _coordinator.Active;
        if (_activeSession is not null)
        {
            _activeSession.Updated += OnSessionUpdated;
        }

        // Seed the palette brushes with the static fallback (identical to the WdAccentBrush token) so bound
        // controls have colour before any artwork or settings arrive. Created here on the UI thread.
        Palette initial = _paletteService.Fallback(darkTheme: true);
        SetBrushColors(initial);
        _currentPalette = initial;

        RefreshFromActive();
        RebuildChooser();
        RefreshAudioMatch();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // --- Volume (confidence-gated) ---

    /// <summary>True when the active player's audio session was matched confidently enough to show controls.</summary>
    public bool VolumeAvailable { get => _volumeAvailable; private set => Set(ref _volumeAvailable, value); }

    /// <summary>True for a Medium match: the volume applies to several windows of the same app (browser tabs).</summary>
    public bool VolumeShared { get => _volumeShared; private set => Set(ref _volumeShared, value); }

    /// <summary>Why volume is or is not available, from the matcher. Never contains media titles.</summary>
    public string VolumeExplanation { get => _volumeExplanation; private set => Set(ref _volumeExplanation, value); }

    /// <summary>0-100.</summary>
    public int VolumeLevel { get => _volumeLevel; private set => Set(ref _volumeLevel, value); }

    public bool IsMuted { get => _isMuted; private set => Set(ref _isMuted, value); }

    /// <summary>Requests a volume change (0-100). Debounced so slider drags coalesce; optimistic in the UI.</summary>
    public void SetVolume(int percent)
    {
        if (!VolumeAvailable)
        {
            return;
        }

        percent = Math.Clamp(percent, 0, 100);
        VolumeLevel = percent;
        _pendingVolume = percent;
        _volumeTimer.Stop();
        _volumeTimer.Start();
    }

    /// <summary>Nudges by <see cref="MediaOptions.VolumeStepPercent"/> in the given direction (+1 / -1).</summary>
    public void NudgeVolume(int direction) => SetVolume(VolumeLevel + (Math.Sign(direction) * Math.Max(1, _options.VolumeStepPercent)));

    public async Task ToggleMuteAsync()
    {
        if (!VolumeAvailable || _audio is null || _audioMatch is null)
        {
            return;
        }

        bool target = !IsMuted;
        IsMuted = target;
        bool ok;
        try
        {
            ok = await _audio.TrySetMuteAsync(_audioMatch, target, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            ok = false;
            ShellLog.Write($"volume: mute faulted {ex.GetType().Name}");
        }

        if (!ok)
        {
            IsMuted = !target;
            ShowStatus("The player's mute state could not be changed.");
        }
    }

    private async void OnVolumeDebounceElapsed(DispatcherQueueTimer sender, object args)
    {
        if (_pendingVolume is not { } percent || _audio is null || _audioMatch is null || !VolumeAvailable)
        {
            return;
        }

        _pendingVolume = null;
        bool ok;
        try
        {
            ok = await _audio.TrySetVolumeAsync(_audioMatch, percent / 100f, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            ok = false;
            ShellLog.Write($"volume: set faulted {ex.GetType().Name}");
        }

        if (!ok)
        {
            ShowStatus("The player's volume could not be changed.");
        }
    }

    private void OnAudioChanged(object? sender, EventArgs e) => Marshal(RefreshAudioMatch);

    /// <summary>Re-matches the active session's app to Core Audio sessions off the UI thread; results are gated.</summary>
    private void RefreshAudioMatch()
    {
        _audioCts?.Cancel();
        _audioCts = null;

        IMediaSession? session = _activeSession;
        if (_audio is null || session is null || _disposed)
        {
            ClearVolume(session is null ? "No player is active." : "Per-app volume is unavailable.");
            return;
        }

        var cts = new CancellationTokenSource();
        _audioCts = cts;
        string sourceAppId = session.SourceAppId;
        IAudioSessionProvider audio = _audio;
        bool allowShared = _options.AllowSharedVolume;

        _ = Task.Run(async () =>
        {
            AudioMatch match;
            float? level = null;
            bool? muted = null;
            try
            {
                match = await audio.MatchAsync(sourceAppId, cts.Token).ConfigureAwait(false);
                bool usable = match.Confidence == AudioMatchConfidence.High ||
                              (match.Confidence == AudioMatchConfidence.Medium && allowShared);
                if (usable)
                {
                    level = await audio.GetVolumeAsync(match, cts.Token).ConfigureAwait(false);
                    muted = await audio.GetMuteAsync(match, cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                match = AudioMatch.NoMatch($"Audio session lookup failed ({ex.GetType().Name}).");
            }

            if (cts.IsCancellationRequested)
            {
                return;
            }

            Marshal(() =>
            {
                if (cts.IsCancellationRequested || !ReferenceEquals(_audioCts, cts))
                {
                    return;
                }

                ApplyAudioMatch(match, level, muted, allowShared);
            });
        });
    }

    private void ApplyAudioMatch(AudioMatch match, float? level, bool? muted, bool allowShared)
    {
        bool usable = match.Confidence == AudioMatchConfidence.High ||
                      (match.Confidence == AudioMatchConfidence.Medium && allowShared);
        _audioMatch = usable ? match : null;
        _pendingVolume = null;
        VolumeShared = usable && match.Confidence == AudioMatchConfidence.Medium;
        VolumeExplanation = match.Explanation;
        if (usable)
        {
            VolumeLevel = (int)Math.Round(Math.Clamp(level ?? 0f, 0f, 1f) * 100);
            IsMuted = muted ?? false;
        }

        VolumeAvailable = usable;
        ShellLog.Write($"volume: match={match.Confidence} usable={usable} sessions={match.AudioSessionIds.Count} why=\"{match.Explanation}\"");
    }

    private void ClearVolume(string explanation)
    {
        _audioMatch = null;
        _pendingVolume = null;
        VolumeAvailable = false;
        VolumeShared = false;
        VolumeExplanation = explanation;
    }

    /// <summary>
    /// Raised on the UI thread after the user invokes a transport or seek command from the drawer. The host uses it
    /// to honour <c>drawer.hideAfterCommand</c>.
    /// </summary>
    public event EventHandler? CommandInvoked;

    /// <summary>
    /// Swaps the media tunables (aliases, timeline tick) after a live settings change. Alias resolution reads the
    /// current instance on every use; the timeline timer interval is updated here.
    /// </summary>
    public void UpdateOptions(MediaOptions options)
    {
        bool sharedChanged = _options.AllowSharedVolume != options.AllowSharedVolume;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timelineTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, _options.TimelineTickMs));
        if (sharedChanged)
        {
            RefreshAudioMatch();
        }
    }

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

    /// <summary>Dynamic accent brush from the artwork palette. Same instance for life; its colour is animated.</summary>
    public SolidColorBrush AccentBrush => _accentBrush;

    /// <summary>Readable colour to place on top of <see cref="AccentBrush"/> (e.g. the play pill glyph).</summary>
    public SolidColorBrush OnAccentBrush => _onAccentBrush;

    /// <summary>Accent at 18 % over the surface, for tonal containers.</summary>
    public SolidColorBrush AccentContainerBrush => _accentContainerBrush;

    /// <summary>Accent at 8 %, the background-blob tint.</summary>
    public SolidColorBrush BlobTintBrush => _blobTintBrush;

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

    // --- Lyrics (E3) ---

    /// <summary>The current lyric lines' text (synced or plain). Empty when there are none.</summary>
    public IReadOnlyList<string> LyricsLines { get => _lyricsLines; private set => Set(ref _lyricsLines, value); }

    /// <summary>The index into <see cref="LyricsLines"/> of the current synced line, or -1 (before the first / unsynced).</summary>
    public int LyricsCurrentIndex { get => _lyricsCurrentIndex; private set => Set(ref _lyricsCurrentIndex, value); }

    /// <summary>True when the current lyrics carry timestamps (auto-scroll applies); false for plain lyrics.</summary>
    public bool LyricsSynced { get => _lyricsSynced; private set => Set(ref _lyricsSynced, value); }

    /// <summary>The provider attribution line shown under the lyrics (e.g. "Lyrics from LRCLIB").</summary>
    public string LyricsAttribution { get => _lyricsAttribution; private set => Set(ref _lyricsAttribution, value); }

    /// <summary>The lyrics slot's state (off / loading / found / not-found).</summary>
    public LyricsState LyricsState { get => _lyricsState; private set => Set(ref _lyricsState, value); }

    /// <summary>Raised when the user asks to enable lyrics from the panel; the host flips <c>lyrics.provider</c>.</summary>
    public event EventHandler? LyricsEnableRequested;

    /// <summary>Panel action: request that lyrics be enabled (persisted by the host).</summary>
    public void RequestEnableLyrics() => LyricsEnableRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Panel action: nudge the current track's lyrics offset by the given milliseconds and re-sync.</summary>
    public void AdjustLyricsOffset(int deltaMs)
    {
        _lyricsOffsetMs += deltaMs;
        PersistOffset();
        UpdateTimeline();
    }

    /// <summary>Panel action: clear the current track's offset back to the global default.</summary>
    public void ResetLyricsOffset()
    {
        _lyricsOffsetMs = _defaultOffsetMs;
        if (_lyricsTrackKey is not null)
        {
            _offsetStore?.Set(_lyricsTrackKey, 0);
        }

        UpdateTimeline();
    }

    private void PersistOffset()
    {
        if (_lyricsTrackKey is not null)
        {
            _offsetStore?.Set(_lyricsTrackKey, _lyricsOffsetMs);
        }
    }

    /// <summary>Applies a live change to <c>lyrics.provider</c> / <c>lyrics.offsetMs</c>. Re-evaluates the current track.</summary>
    public void UpdateLyricsSettings(CoreLyricsProvider mode, int defaultOffsetMs)
    {
        bool modeChanged = _lyricsMode != mode;
        _defaultOffsetMs = defaultOffsetMs;
        _lyricsMode = mode;

        // Force a re-evaluation of the active track under the new mode.
        _lyricsTrackKey = null;
        RefreshLyrics();
    }

    private void RefreshLyrics()
    {
        IMediaSession? session = _activeSession;
        if (session is null)
        {
            ClearLyrics(LyricsState.Off);
            return;
        }

        EvaluateLyrics(session.Current);
    }

    private void EvaluateLyrics(MediaSnapshot snapshot)
    {
        if (_lyricsMode != CoreLyricsProvider.Lrclib || _lyricsProvider is null)
        {
            _lyricsTrackKey = null;
            ClearLyrics(LyricsState.Off);
            return;
        }

        TimeSpan? duration = snapshot.Timeline.HasDuration ? snapshot.Timeline.Duration : null;
        var query = new LyricsQuery(snapshot.Title ?? string.Empty, snapshot.Artists, snapshot.Album, duration);
        if (!query.IsUsable)
        {
            _lyricsTrackKey = null;
            ClearLyrics(LyricsState.NotFound);
            return;
        }

        string key = LyricsCache.NormalizeKey(query);
        if (string.Equals(key, _lyricsTrackKey, StringComparison.Ordinal))
        {
            return;
        }

        // New track identity: cancel any in-flight lookup and reset the slot.
        _lyricsCts?.Cancel();
        _lyricsTrackKey = key;
        _lyricsQuery = query;
        _lyricsOffsetMs = _offsetStore?.Get(key) ?? _defaultOffsetMs;
        _lyricsModelLines = Array.Empty<LyricsLine>();
        LyricsLines = Array.Empty<string>();
        LyricsCurrentIndex = -1;
        LyricsSynced = false;
        LyricsAttribution = string.Empty;
        LyricsState = LyricsState.Loading;

        var cts = new CancellationTokenSource();
        _lyricsCts = cts;
        _ = LookupLyricsAsync(query, key, cts.Token);
    }

    private async Task LookupLyricsAsync(LyricsQuery query, string key, CancellationToken ct)
    {
        try
        {
            LyricsResult? result;
            if (_lyricsCache is not null && _lyricsCache.TryGet(query, out LyricsResult? cached))
            {
                result = cached;
            }
            else
            {
                result = await _lyricsProvider!.LookupAsync(query, ct).ConfigureAwait(true);
                _lyricsCache?.Set(query, result);
            }

            if (ct.IsCancellationRequested || !string.Equals(key, _lyricsTrackKey, StringComparison.Ordinal))
            {
                return;
            }

            ApplyLyricsResult(result);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a track change.
        }
        catch (Exception ex)
        {
            ShellLog.Write($"lyrics: lookup failed {ex.GetType().Name}");
            if (!ct.IsCancellationRequested && string.Equals(key, _lyricsTrackKey, StringComparison.Ordinal))
            {
                ClearLyrics(LyricsState.NotFound);
            }
        }
    }

    private void ApplyLyricsResult(LyricsResult? result)
    {
        if (result is null || result.Lines.Count == 0)
        {
            ClearLyrics(LyricsState.NotFound);
            return;
        }

        _lyricsModelLines = result.Lines;
        var text = new string[result.Lines.Count];
        for (int i = 0; i < result.Lines.Count; i++)
        {
            text[i] = result.Lines[i].Text;
        }

        LyricsLines = text;
        LyricsSynced = result.IsSynced;
        // The lyrics source is not surfaced in the UI (user preference); attribution stays empty so the panel hides it.
        LyricsAttribution = string.Empty;
        LyricsState = LyricsState.Found;
        UpdateLyricsIndex();
    }

    private void ClearLyrics(LyricsState state)
    {
        _lyricsModelLines = Array.Empty<LyricsLine>();
        LyricsLines = Array.Empty<string>();
        LyricsCurrentIndex = -1;
        LyricsSynced = false;
        LyricsAttribution = string.Empty;
        LyricsState = state;
    }

    private void UpdateLyricsIndex()
    {
        if (!_lyricsSynced || _lyricsModelLines.Count == 0)
        {
            return;
        }

        LyricsCurrentIndex = LyricsSync.CurrentIndex(_lyricsModelLines, Position, TimeSpan.FromMilliseconds(_lyricsOffsetMs));
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
        CommandInvoked?.Invoke(this, EventArgs.Empty);

        await ObserveAsync("Seek", session.TrySeekAsync(target, CancellationToken.None)).ConfigureAwait(true);
    }

    /// <summary>
    /// The runtime "show every source" override, forwarded to the coordinator. Two-way bound to the chooser toggle;
    /// turning it on reveals sources the tracked/music filter would hide (Never sources stay hidden).
    /// </summary>
    public bool ShowAllSources
    {
        get => _coordinator.ShowAllSources;
        set => _coordinator.ShowAllSources = value;
    }

    private void OnShowAllSourcesChanged(object? sender, EventArgs e) =>
        Marshal(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowAllSources))));

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
        _coordinator.ShowAllSourcesChanged -= OnShowAllSourcesChanged;
        if (_activeSession is not null)
        {
            _activeSession.Updated -= OnSessionUpdated;
            _activeSession = null;
        }

        _timelineTimer.Stop();
        _timelineTimer.Tick -= OnTimelineTick;
        _statusTimer.Stop();
        _statusTimer.Tick -= OnStatusExpired;
        _volumeTimer.Stop();
        _volumeTimer.Tick -= OnVolumeDebounceElapsed;
        if (_audio is not null)
        {
            _audio.Changed -= OnAudioChanged;
        }

        _audioCts?.Cancel();
        _audioCts = null;

        _lyricsCts?.Cancel();
        _lyricsCts = null;

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

            // A different session invalidates any in-flight optimistic seek and the audio match.
            _pendingSeek = null;
            RefreshAudioMatch();
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
        EvaluateLyrics(snapshot);
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
        _paletteBgra = null;
        ApplyPalette();

        _lyricsCts?.Cancel();
        _lyricsTrackKey = null;
        ClearLyrics(_lyricsMode == CoreLyricsProvider.Lrclib ? LyricsState.NotFound : LyricsState.Off);

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
        UpdateLyricsIndex();
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
        IReadOnlyDictionary<string, MusicVerdict> verdicts = _coordinator.Verdicts;
        foreach (IMediaSession candidate in candidates)
        {
            MediaSnapshot snapshot = candidate.Current;
            string label = _options.AliasFor(snapshot.SourceAppId, snapshot.SourceDisplayName);
            string verdict = verdicts.TryGetValue(candidate.Id, out MusicVerdict v) ? v.Reason : string.Empty;
            items.Add(new PlayerChooserItem(
                candidate.Id,
                label,
                StateText(snapshot.State),
                ReferenceEquals(candidate, active),
                verdict));
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
            _paletteBgra = null;
            ApplyPalette();
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
                _paletteBgra = null;
                ApplyPalette();
                return;
            }

            byte[] raw = cached.Bytes.ToArray();
            BitmapImage? image = await DecodeAsync(raw, ct).ConfigureAwait(true);

            if (ct.IsCancellationRequested || !string.Equals(key, _artworkKey, StringComparison.Ordinal))
            {
                return;
            }

            Artwork = image;

            // A fixed accent ignores artwork entirely; only extract when the palette follows the artwork.
            if (_paletteSource != PaletteSource.Fixed)
            {
                byte[]? bgra = await DecodePaletteBgraAsync(raw, ct).ConfigureAwait(true);

                if (ct.IsCancellationRequested || !string.Equals(key, _artworkKey, StringComparison.Ordinal))
                {
                    return;
                }

                _paletteBgra = bgra;
                ApplyPalette();
            }
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

    // --- Artwork palette ---

    /// <summary>
    /// Supplies the theme accessor and the palette settings. The <paramref name="isDarkTheme"/> callback is invoked
    /// on the UI thread and typically reads the page's <c>ActualTheme</c>. Applies the palette immediately.
    /// </summary>
    public void ConfigurePalette(Func<bool> isDarkTheme, PaletteSource source, string fixedAccent)
    {
        _isDarkTheme = isDarkTheme ?? throw new ArgumentNullException(nameof(isDarkTheme));
        SetPaletteSettings(source, fixedAccent);
    }

    /// <summary>Applies a live change to <c>appearance.paletteSource</c> / <c>appearance.fixedAccent</c>.</summary>
    public void SetPaletteSettings(PaletteSource source, string fixedAccent)
    {
        _paletteSource = source;
        _fixedAccentValid = TryParseAccent(fixedAccent, out _fixedAccent);
        ApplyPalette();
    }

    /// <summary>Recomputes the palette for the current theme (call when <c>ActualTheme</c> changes).</summary>
    public void RefreshPalette() => ApplyPalette();

    /// <summary>
    /// Applies the accessibility state to the dynamic accent brushes: reduced motion collapses the cross-fade to a
    /// 100 ms linear transition, and high contrast remaps every dynamic brush to the <c>SystemColor*</c> brushes so
    /// they honour the user's high-contrast scheme rather than the artwork accent (_docs/04-visual-design.md).
    /// </summary>
    public void SetAccessibility(bool reduceMotion, bool highContrast)
    {
        _reduceMotion = reduceMotion;
        _highContrast = highContrast;
        ApplyPalette();
    }

    private bool IsDarkTheme() => _isDarkTheme?.Invoke() ?? true;

    private static bool TryParseAccent(string? value, out uint color)
    {
        color = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string hex = value.Trim();
        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }

        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            return false;
        }

        color = 0xFF000000u | rgb;
        return true;
    }

    private void ApplyPalette()
    {
        if (_disposed)
        {
            return;
        }

        if (_highContrast)
        {
            // High contrast: bind the dynamic brushes to the SystemColor* brushes (the HighContrast ThemeDictionary
            // in Tokens.xaml). Snap, and force a recompute when high contrast turns back off.
            ApplyHighContrastBrushes();
            _currentPalette = null;
            return;
        }

        bool dark = IsDarkTheme();
        Palette palette;
        if (_paletteSource == PaletteSource.Fixed)
        {
            palette = _fixedAccentValid
                ? _paletteService.FromAccent(_fixedAccent, dark)
                : _paletteService.Fallback(dark);
        }
        else if (_paletteBgra is { } bgra)
        {
            palette = _paletteService.FromArtwork(bgra, PaletteDim, PaletteDim, dark);
        }
        else
        {
            palette = _paletteService.Fallback(dark);
        }

        if (_currentPalette == palette)
        {
            return;
        }

        _currentPalette = palette;
        ShellLog.Write($"palette: fallback={palette.IsFallback} accent=#{palette.Accent & 0x00FFFFFF:X6}");
        AnimatePalette(palette);
    }

    private void AnimatePalette(Palette palette)
    {
        // Reduced motion collapses the cross-fade to 100 ms linear (_docs/04-visual-design.md); otherwise 400 ms.
        int durationMs = _reduceMotion ? ReducedPaletteMotionMs : PaletteMotionMs;
        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        AddColorAnimation(storyboard, _accentBrush, ToColor(palette.Accent), durationMs);
        AddColorAnimation(storyboard, _onAccentBrush, ToColor(palette.OnAccent), durationMs);
        AddColorAnimation(storyboard, _accentContainerBrush, ToColor(palette.AccentContainer), durationMs);
        AddColorAnimation(storyboard, _blobTintBrush, ToColor(palette.BlobTint), durationMs);
        storyboard.Begin();
    }

    private static void AddColorAnimation(Microsoft.UI.Xaml.Media.Animation.Storyboard storyboard, SolidColorBrush brush, global::Windows.UI.Color to, int durationMs)
    {
        var animation = new Microsoft.UI.Xaml.Media.Animation.ColorAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EnableDependentAnimation = true,
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, brush);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Color");
        storyboard.Children.Add(animation);
    }

    /// <summary>
    /// Snaps the dynamic accent brushes to the high-contrast SystemColor* values from the HighContrast
    /// ThemeDictionary in Tokens.xaml (WdAccentBrush = SystemColorHighlight, WdOnAccentBrush = SystemColorHighlightText).
    /// The container and blob tint follow the accent so no artwork-derived colour leaks into a high-contrast scheme.
    /// </summary>
    private void ApplyHighContrastBrushes()
    {
        if (TryGetThemeBrushColor("HighContrast", "WdAccentBrush", out global::Windows.UI.Color accent))
        {
            _accentBrush.Color = accent;
            _accentContainerBrush.Color = accent;
            _blobTintBrush.Color = accent;
        }

        if (TryGetThemeBrushColor("HighContrast", "WdOnAccentBrush", out global::Windows.UI.Color onAccent))
        {
            _onAccentBrush.Color = onAccent;
        }
    }

    private static bool TryGetThemeBrushColor(string themeKey, string brushKey, out global::Windows.UI.Color color)
    {
        foreach (ResourceDictionary md in Application.Current.Resources.MergedDictionaries)
        {
            if (md.ThemeDictionaries.TryGetValue(themeKey, out object? themed) &&
                themed is ResourceDictionary dict &&
                dict.TryGetValue(brushKey, out object? brush) &&
                brush is SolidColorBrush scb)
            {
                color = scb.Color;
                return true;
            }
        }

        color = default;
        return false;
    }

    private void SetBrushColors(Palette palette)
    {
        _accentBrush.Color = ToColor(palette.Accent);
        _onAccentBrush.Color = ToColor(palette.OnAccent);
        _accentContainerBrush.Color = ToColor(palette.AccentContainer);
        _blobTintBrush.Color = ToColor(palette.BlobTint);
    }

    private static global::Windows.UI.Color ToColor(uint argb) => global::Windows.UI.Color.FromArgb(
        (byte)((argb >> 24) & 0xFF),
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF));

    /// <summary>
    /// Decodes a 64x64 BGRA (straight alpha, no premultiply) copy of the artwork for palette extraction, using the
    /// same cancellation token as the artwork load. The WinRT decode runs off the UI thread.
    /// </summary>
    private static async Task<byte[]?> DecodePaletteBgraAsync(byte[] bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer());
        stream.Seek(0);

        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);
        var transform = new BitmapTransform { ScaledWidth = PaletteDim, ScaledHeight = PaletteDim };
        PixelDataProvider pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(false);

        return pixels.DetachPixelData();
    }

    // --- Command plumbing ---

    private Task InvokeAsync(string name, Func<IMediaSession, CancellationToken, Task<CommandResult>> command)
    {
        IMediaSession? session = _activeSession;
        if (session is null)
        {
            return Task.CompletedTask;
        }

        CommandInvoked?.Invoke(this, EventArgs.Empty);
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
