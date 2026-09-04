using WinDots.Core.Dashboard;

namespace WinDots.Core.Tests.Dashboard;

public class UptimeFormatterTests
{
    [Fact]
    public void HoursAndMinutes()
    {
        Assert.Equal(
            "up 7 hours, 28 minutes",
            UptimeFormatter.Format(new TimeSpan(7, 28, 0)));
    }

    [Fact]
    public void SingularUnits()
    {
        Assert.Equal(
            "up 1 hour, 1 minute",
            UptimeFormatter.Format(new TimeSpan(1, 1, 0)));
    }

    [Fact]
    public void IncludesDays()
    {
        Assert.Equal(
            "up 2 days, 3 hours, 4 minutes",
            UptimeFormatter.Format(new TimeSpan(2, 3, 4, 0)));
    }

    [Fact]
    public void SingularDay()
    {
        Assert.Equal(
            "up 1 day, 5 minutes",
            UptimeFormatter.Format(new TimeSpan(1, 0, 5, 0)));
    }

    [Fact]
    public void OmitsZeroComponents()
    {
        Assert.Equal("up 3 hours", UptimeFormatter.Format(new TimeSpan(3, 0, 0)));
        Assert.Equal("up 45 minutes", UptimeFormatter.Format(new TimeSpan(0, 45, 0)));
    }

    [Fact]
    public void SubMinuteAndNegativeReadLessThanAMinute()
    {
        Assert.Equal("up less than a minute", UptimeFormatter.Format(TimeSpan.FromSeconds(30)));
        Assert.Equal("up less than a minute", UptimeFormatter.Format(TimeSpan.Zero));
        Assert.Equal("up less than a minute", UptimeFormatter.Format(TimeSpan.FromMinutes(-5)));
    }

    [Fact]
    public void CompactForm()
    {
        Assert.Equal("7h 28m", UptimeFormatter.FormatCompact(new TimeSpan(7, 28, 0)));
        Assert.Equal("2d 3h 4m", UptimeFormatter.FormatCompact(new TimeSpan(2, 3, 4, 0)));
    }

    [Fact]
    public void CompactOmitsHoursWhenNoneAndNoDays()
    {
        Assert.Equal("45m", UptimeFormatter.FormatCompact(new TimeSpan(0, 45, 0)));
    }

    [Fact]
    public void CompactKeepsHoursWhenDaysPresent()
    {
        Assert.Equal("1d 0h 5m", UptimeFormatter.FormatCompact(new TimeSpan(1, 0, 5, 0)));
    }

    [Fact]
    public void CompactSubMinute()
    {
        Assert.Equal("<1m", UptimeFormatter.FormatCompact(TimeSpan.FromSeconds(10)));
    }
}
