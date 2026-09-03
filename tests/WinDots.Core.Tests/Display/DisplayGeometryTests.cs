using WinDots.Core.Contracts;
using WinDots.Core.Display;

namespace WinDots.Core.Tests.Display;

public class DisplayGeometryTests
{
    [Fact]
    public void ScaleFromDpiIsRelativeTo96()
    {
        Assert.Equal(1.0, DisplayGeometry.ScaleFromDpi(96));
        Assert.Equal(1.5, DisplayGeometry.ScaleFromDpi(144));
        Assert.Equal(1.0, DisplayGeometry.ScaleFromDpi(0));
    }

    [Fact]
    public void LogicalAndPhysicalRoundTrip()
    {
        var physical = new Rect(-3840, 0, 3840, 2160);
        var logical = DisplayGeometry.ToLogical(physical, 2.0);
        Assert.Equal(new Rect(-1920, 0, 1920, 1080), logical);
        Assert.Equal(physical, DisplayGeometry.ToPhysical(logical, 2.0));
    }

    [Fact]
    public void ContainsChecksAllEdges()
    {
        var outer = new Rect(0, 0, 100, 100);
        Assert.True(DisplayGeometry.Contains(outer, new Rect(0, 0, 100, 60)));
        Assert.False(DisplayGeometry.Contains(outer, new Rect(0, 0, 101, 60)));
        Assert.False(DisplayGeometry.Contains(outer, new Rect(-1, 0, 50, 50)));
    }

    [Fact]
    public void RejectsNonPositiveScale()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayGeometry.ToLogical(new Rect(0, 0, 1, 1), 0));
    }
}
