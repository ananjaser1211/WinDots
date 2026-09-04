using WinDots.Core.Dashboard;

namespace WinDots.Core.Tests.Dashboard;

public class ClockModelTests
{
    private static DateTimeOffset At(int hour, int minute) =>
        new(2026, 9, 4, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void TwelveHourMorning()
    {
        var clock = ClockModel.Create(At(10, 8), use24Hour: false);
        Assert.Equal("10", clock.Hour);
        Assert.Equal("08", clock.Minute);
        Assert.Equal("AM", clock.Meridiem);
        Assert.False(clock.Use24Hour);
    }

    [Fact]
    public void TwelveHourAfternoon()
    {
        var clock = ClockModel.Create(At(22, 30), use24Hour: false);
        Assert.Equal("10", clock.Hour);
        Assert.Equal("30", clock.Minute);
        Assert.Equal("PM", clock.Meridiem);
    }

    [Fact]
    public void MidnightIsTwelveAm()
    {
        var clock = ClockModel.Create(At(0, 0), use24Hour: false);
        Assert.Equal("12", clock.Hour);
        Assert.Equal("00", clock.Minute);
        Assert.Equal("AM", clock.Meridiem);
    }

    [Fact]
    public void NoonIsTwelvePm()
    {
        var clock = ClockModel.Create(At(12, 0), use24Hour: false);
        Assert.Equal("12", clock.Hour);
        Assert.Equal("PM", clock.Meridiem);
    }

    [Fact]
    public void TwentyFourHourZeroPadsHourAndHasNoMeridiem()
    {
        var clock = ClockModel.Create(At(9, 5), use24Hour: true);
        Assert.Equal("09", clock.Hour);
        Assert.Equal("05", clock.Minute);
        Assert.Equal(string.Empty, clock.Meridiem);
        Assert.True(clock.Use24Hour);
    }

    [Fact]
    public void TwentyFourHourEvening()
    {
        var clock = ClockModel.Create(At(23, 59), use24Hour: true);
        Assert.Equal("23", clock.Hour);
        Assert.Equal("59", clock.Minute);
    }

    [Fact]
    public void UsesOffsetWallClockNotUtc()
    {
        var instant = new DateTimeOffset(2026, 9, 4, 8, 0, 0, TimeSpan.FromHours(3));
        var clock = ClockModel.Create(instant, use24Hour: true);
        Assert.Equal("08", clock.Hour);
    }
}
