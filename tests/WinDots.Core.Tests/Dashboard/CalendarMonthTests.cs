using WinDots.Core.Dashboard;

namespace WinDots.Core.Tests.Dashboard;

public class CalendarMonthTests
{
    [Fact]
    public void ProducesFortyTwoCellsInSixRows()
    {
        var month = CalendarMonth.Create(2026, 9, new DateOnly(2026, 9, 4));
        Assert.Equal(CalendarMonth.Rows * CalendarMonth.Columns, month.Cells.Count);
        Assert.Equal(42, month.Cells.Count);
    }

    [Fact]
    public void TitleIsMonthNameAndYear()
    {
        var month = CalendarMonth.Create(2026, 9, new DateOnly(2026, 9, 4));
        Assert.Equal("September 2026", month.Title);
    }

    [Fact]
    public void WeekdayHeadersStartSundayByDefault()
    {
        var month = CalendarMonth.Create(2026, 9, new DateOnly(2026, 9, 4));
        Assert.Equal(new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" }, month.WeekdayHeaders);
    }

    [Fact]
    public void September2026StartsWithLeadingAugustDays()
    {
        // 1 Sep 2026 is a Tuesday, so with a Sunday start the row leads with Sun 30 Aug, Mon 31 Aug.
        var month = CalendarMonth.Create(2026, 9, new DateOnly(2026, 9, 4));
        Assert.Equal(new DateOnly(2026, 8, 30), month.CellAt(0, 0).Date);
        Assert.False(month.CellAt(0, 0).IsInMonth);
        Assert.Equal(new DateOnly(2026, 8, 31), month.CellAt(0, 1).Date);
        Assert.False(month.CellAt(0, 1).IsInMonth);
        Assert.Equal(new DateOnly(2026, 9, 1), month.CellAt(0, 2).Date);
        Assert.True(month.CellAt(0, 2).IsInMonth);
    }

    [Fact]
    public void TrailingDaysBelongToNextMonth()
    {
        var month = CalendarMonth.Create(2026, 9, new DateOnly(2026, 9, 4));
        CalendarCell last = month.CellAt(5, 6);
        Assert.False(last.IsInMonth);
        Assert.Equal(10, last.Date.Month);
    }

    [Fact]
    public void TodayIsFlaggedOnMatchingCellOnly()
    {
        var today = new DateOnly(2026, 9, 4);
        var month = CalendarMonth.Create(2026, 9, today);
        int flagged = month.Cells.Count(c => c.IsToday);
        Assert.Equal(1, flagged);
        Assert.True(month.Cells.Single(c => c.IsToday).Date == today);
    }

    [Fact]
    public void TodayNotFlaggedWhenOutsideDisplayedMonth()
    {
        var month = CalendarMonth.Create(2026, 9, new DateOnly(2026, 12, 25));
        Assert.DoesNotContain(month.Cells, c => c.IsToday);
    }

    [Fact]
    public void MondayStartShiftsHeadersAndLead()
    {
        var month = CalendarMonth.Create(2026, 9, new DateOnly(2026, 9, 4), DayOfWeek.Monday);
        Assert.Equal("Mon", month.WeekdayHeaders[0]);
        Assert.Equal("Sun", month.WeekdayHeaders[6]);
        // 1 Sep 2026 is Tuesday -> one leading day (Mon 31 Aug).
        Assert.Equal(new DateOnly(2026, 8, 31), month.CellAt(0, 0).Date);
        Assert.Equal(new DateOnly(2026, 9, 1), month.CellAt(0, 1).Date);
    }

    [Fact]
    public void FirstOfMonthOnWeekStartHasNoLeadingDays()
    {
        // 1 Feb 2026 is a Sunday.
        var month = CalendarMonth.Create(2026, 2, new DateOnly(2026, 2, 1));
        Assert.Equal(new DateOnly(2026, 2, 1), month.CellAt(0, 0).Date);
        Assert.True(month.CellAt(0, 0).IsInMonth);
    }

    [Fact]
    public void PreviousWrapsAcrossYearBoundary()
    {
        var jan = CalendarMonth.Create(2026, 1, new DateOnly(2026, 1, 1));
        var prev = jan.Previous(new DateOnly(2026, 1, 1));
        Assert.Equal(2025, prev.Year);
        Assert.Equal(12, prev.Month);
        Assert.Equal("December 2025", prev.Title);
    }

    [Fact]
    public void NextWrapsAcrossYearBoundary()
    {
        var dec = CalendarMonth.Create(2026, 12, new DateOnly(2026, 12, 1));
        var next = dec.Next(new DateOnly(2026, 12, 1));
        Assert.Equal(2027, next.Year);
        Assert.Equal(1, next.Month);
    }

    [Fact]
    public void NavigationPreservesWeekStart()
    {
        var month = CalendarMonth.Create(2026, 9, new DateOnly(2026, 9, 4), DayOfWeek.Monday);
        Assert.Equal(DayOfWeek.Monday, month.Next(new DateOnly(2026, 9, 4)).FirstDayOfWeek);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void RejectsInvalidMonth(int badMonth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CalendarMonth.Create(2026, badMonth, new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void CellAtRejectsOutOfRange()
    {
        var month = CalendarMonth.Create(2026, 9, new DateOnly(2026, 9, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => month.CellAt(6, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => month.CellAt(0, 7));
    }
}
