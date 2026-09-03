using WinDots.Core.Media;

namespace WinDots.Core.Tests.Media;

public class MediaSnapshotTests
{
    [Fact]
    public void EmptyHasNoMetadataAndNoCapabilities()
    {
        var s = MediaSnapshot.Empty("id", "app", "App", DateTimeOffset.UnixEpoch);
        Assert.False(s.HasMetadata);
        Assert.False(s.Can(Capabilities.PlayPause));
        Assert.Equal(PlaybackState.Unknown, s.State);
    }

    [Fact]
    public void CanRequiresEveryFlag()
    {
        var s = MediaSnapshot.Empty("id", "app", "App", DateTimeOffset.UnixEpoch) with { Caps = Capabilities.Next | Capabilities.Previous };
        Assert.True(s.Can(Capabilities.Next));
        Assert.True(s.Can(Capabilities.Next | Capabilities.Previous));
        Assert.False(s.Can(Capabilities.Next | Capabilities.Seek));
    }
}
