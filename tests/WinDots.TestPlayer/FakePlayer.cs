using System.Globalization;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace WinDots.TestPlayer;

/// <summary>
/// Publishes a manually controlled SMTC session through a <see cref="MediaPlayer"/> with its command manager disabled.
/// Position advances on a timer while playing and is republished once per second.
/// </summary>
public sealed class FakePlayer : IDisposable
{
    public const string AppUserModelId = "WinDots.TestPlayer";

    private static readonly (string Title, string Artist, string Album, TimeSpan Duration, byte R, byte G, byte B)[] Tracks =
    {
        ("Test Track 1", "WinDots QA", "Fixtures", TimeSpan.FromMinutes(3), 143, 211, 200),
        ("Test Track 2", "WinDots QA", "Fixtures", TimeSpan.FromSeconds(245), 211, 143, 160),
        ("Test Track 3", "Another Artist", "Second Album", TimeSpan.FromMinutes(1), 200, 200, 120),
    };

    private readonly object _gate = new();
    private readonly MediaPlayer _player = new();
    private readonly SystemMediaTransportControls _smtc;
    private readonly Timer _tick;
    private int _index;
    private TimeSpan _position;
    private DateTimeOffset _positionAt = DateTimeOffset.UtcNow;
    private bool _playing;
    private string? _titleOverride;

    public FakePlayer()
    {
        _player.CommandManager.IsEnabled = false;
        _smtc = _player.SystemMediaTransportControls;
        _smtc.IsEnabled = true;
        _smtc.IsPlayEnabled = true;
        _smtc.IsPauseEnabled = true;
        _smtc.IsNextEnabled = true;
        _smtc.IsPreviousEnabled = true;
        _smtc.ShuffleEnabled = false;
        _smtc.AutoRepeatMode = MediaPlaybackAutoRepeatMode.None;

        _smtc.ButtonPressed += OnButtonPressed;
        _smtc.PlaybackPositionChangeRequested += OnSeekRequested;
        _smtc.ShuffleEnabledChangeRequested += OnShuffleRequested;
        _smtc.AutoRepeatModeChangeRequested += OnRepeatRequested;

        _tick = new Timer(_ => PublishTimeline(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        PublishTrack();
        Play();
    }

    public void Play()
    {
        lock (_gate)
        {
            if (!_playing)
            {
                _playing = true;
                _positionAt = DateTimeOffset.UtcNow;
            }

            _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
        }

        PublishTimeline();
        _tick.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        Console.WriteLine("[state] Playing");
    }

    public void Pause()
    {
        lock (_gate)
        {
            _position = CurrentPosition();
            _positionAt = DateTimeOffset.UtcNow;
            _playing = false;
            _smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
        }

        _tick.Change(Timeout.Infinite, Timeout.Infinite);
        PublishTimeline();
        Console.WriteLine("[state] Paused");
    }

    public void Next() => Jump(+1);

    public void Previous() => Jump(-1);

    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            var duration = Tracks[_index].Duration;
            _position = position < TimeSpan.Zero ? TimeSpan.Zero : position > duration ? duration : position;
            _positionAt = DateTimeOffset.UtcNow;
        }

        PublishTimeline();
        Console.WriteLine($"[state] Position {_position.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}");
    }

    public void SetTitle(string title)
    {
        lock (_gate)
        {
            _titleOverride = title;
        }

        PublishTrack();
    }

    public void Dispose()
    {
        _tick.Dispose();
        _smtc.ButtonPressed -= OnButtonPressed;
        _smtc.PlaybackPositionChangeRequested -= OnSeekRequested;
        _smtc.ShuffleEnabledChangeRequested -= OnShuffleRequested;
        _smtc.AutoRepeatModeChangeRequested -= OnRepeatRequested;
        _smtc.IsEnabled = false;
        _player.Dispose();
    }

    private void Jump(int delta)
    {
        lock (_gate)
        {
            _index = ((_index + delta) % Tracks.Length + Tracks.Length) % Tracks.Length;
            _position = TimeSpan.Zero;
            _positionAt = DateTimeOffset.UtcNow;
            _titleOverride = null;
        }

        PublishTrack();
        PublishTimeline();
        Console.WriteLine($"[state] Track {_index + 1}");
    }

    private TimeSpan CurrentPosition()
    {
        if (!_playing)
        {
            return _position;
        }

        var p = _position + (DateTimeOffset.UtcNow - _positionAt);
        var duration = Tracks[_index].Duration;
        return p > duration ? duration : p;
    }

    private void PublishTrack()
    {
        (string title, string artist, string album, TimeSpan _, byte r, byte g, byte b) track;
        string? titleOverride;
        lock (_gate)
        {
            var t = Tracks[_index];
            track = (t.Title, t.Artist, t.Album, t.Duration, t.R, t.G, t.B);
            titleOverride = _titleOverride;
        }

        var updater = _smtc.DisplayUpdater;
        updater.ClearAll();
        updater.Type = MediaPlaybackType.Music;
        updater.MusicProperties.Title = titleOverride ?? track.title;
        updater.MusicProperties.Artist = track.artist;
        updater.MusicProperties.AlbumTitle = track.album;
        updater.MusicProperties.TrackNumber = (uint)(_index + 1);
        updater.Thumbnail = RandomAccessStreamReference.CreateFromStream(BitmapFactory.SolidColourBmp(64, 64, track.r, track.g, track.b));
        updater.Update();
    }

    private void PublishTimeline()
    {
        TimeSpan position, duration;
        lock (_gate)
        {
            position = CurrentPosition();
            duration = Tracks[_index].Duration;
        }

        _smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero,
            MinSeekTime = TimeSpan.Zero,
            Position = position,
            MaxSeekTime = duration,
            EndTime = duration,
        });
    }

    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        Console.WriteLine($"[event] ButtonPressed {args.Button}");
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
                Play();
                break;
            case SystemMediaTransportControlsButton.Pause:
                Pause();
                break;
            case SystemMediaTransportControlsButton.Next:
                Next();
                break;
            case SystemMediaTransportControlsButton.Previous:
                Previous();
                break;
        }
    }

    private void OnSeekRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args)
    {
        Console.WriteLine($"[event] SeekRequested {args.RequestedPlaybackPosition.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}");
        Seek(args.RequestedPlaybackPosition);
    }

    private void OnShuffleRequested(SystemMediaTransportControls sender, ShuffleEnabledChangeRequestedEventArgs args)
    {
        Console.WriteLine($"[event] ShuffleRequested {args.RequestedShuffleEnabled}");
        _smtc.ShuffleEnabled = args.RequestedShuffleEnabled;
    }

    private void OnRepeatRequested(SystemMediaTransportControls sender, AutoRepeatModeChangeRequestedEventArgs args)
    {
        Console.WriteLine($"[event] RepeatRequested {args.RequestedAutoRepeatMode}");
        _smtc.AutoRepeatMode = args.RequestedAutoRepeatMode;
    }
}
