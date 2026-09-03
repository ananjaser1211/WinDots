using WinDots.Core.Media;

namespace WinDots.Core.Tests.Media;

public class TimeFormatTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(18, "0:18")]
    [InlineData(246, "4:06")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3725, "1:02:05")]
    public void FormatsClock(int seconds, string expected)
    {
        Assert.Equal(expected, TimeFormat.Clock(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void NullAndNegativeRenderAsZero()
    {
        Assert.Equal("0:00", TimeFormat.Clock(null));
        Assert.Equal("0:00", TimeFormat.Clock(TimeSpan.FromSeconds(-3)));
    }

    [Fact]
    public void FractionalSecondsFloor()
    {
        Assert.Equal("0:05", TimeFormat.Clock(TimeSpan.FromSeconds(5.9)));
    }
}
