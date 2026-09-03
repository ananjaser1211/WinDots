namespace WinDots.Core.Media;

public enum PlaybackState
{
    Unknown,
    Playing,
    Paused,
    Stopped,
    Changing,
}

public enum RepeatMode
{
    None,
    Track,
    List,
}

public enum MediaKind
{
    Unknown,
    Music,
    Video,
    Image,
}

[Flags]
public enum Capabilities
{
    None = 0,
    Play = 1 << 0,
    Pause = 1 << 1,
    PlayPause = 1 << 2,
    Next = 1 << 3,
    Previous = 1 << 4,
    Seek = 1 << 5,
    Shuffle = 1 << 6,
    Repeat = 1 << 7,
}

public enum CommandStatus
{
    Succeeded,
    Rejected,
    Unsupported,
    Faulted,
}
