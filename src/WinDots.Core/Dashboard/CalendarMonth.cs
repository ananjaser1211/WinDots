using System.Globalization;

namespace WinDots.Core.Dashboard;

/// <summary>A single cell of the calendar grid: a date plus whether it falls in the displayed month.</summary>
public readonly record struct CalendarCell(DateOnly Date, bool IsInMonth, bool IsToday)
{
    /// <summary>The day-of-month number, e.g. <c>1</c>..<c>31</c>.</summary>
    public int Day => Date.Day;
}

/// <summary>
/// Pure model for the dashboard month calendar. Produces a fixed 6x7 grid (6 weeks, 7 days) of
/// <see cref="CalendarCell"/>s with leading days from the previous month and trailing days from the next,
/// the month title, the weekday headers, and previous/next navigation. No ambient clock: "today" is passed in.
/// </summary>
public sealed class CalendarMonth
{
    /// <summary>Number of week rows always rendered, matching the Widgets.png layout.</summary>
    public const int Rows = 6;

    /// <summary>Number of day columns (a full week).</summary>
    public const int Columns = 7;

    private readonly IReadOnlyList<CalendarCell> _cells;

    private CalendarMonth(
        int year,
        int month,
        DayOfWeek firstDayOfWeek,
        IReadOnlyList<CalendarCell> cells,
        IReadOnlyList<string> weekdayHeaders,
        string title)
    {
        Year = year;
        Month = month;
        FirstDayOfWeek = firstDayOfWeek;
        _cells = cells;
        WeekdayHeaders = weekdayHeaders;
        Title = title;
    }

    /// <summary>The displayed year.</summary>
    public int Year { get; }

    /// <summary>The displayed month, <c>1</c>..<c>12</c>.</summary>
    public int Month { get; }

    /// <summary>The day the week starts on (a column-order origin).</summary>
    public DayOfWeek FirstDayOfWeek { get; }

    /// <summary>Month title such as <c>"September 2026"</c> (invariant culture).</summary>
    public string Title { get; }

    /// <summary>The 7 weekday abbreviations in column order (e.g. Sun..Sat), invariant culture.</summary>
    public IReadOnlyList<string> WeekdayHeaders { get; }

    /// <summary>The 42 cells in reading order (row-major, top-left first).</summary>
    public IReadOnlyList<CalendarCell> Cells => _cells;

    /// <summary>
    /// Builds the grid for <paramref name="year"/>/<paramref name="month"/>.
    /// </summary>
    /// <param name="year">Calendar year.</param>
    /// <param name="month">Month <c>1</c>..<c>12</c>.</param>
    /// <param name="today">The date to flag as today; a cell matches only when its date equals this exactly.</param>
    /// <param name="firstDayOfWeek">Week-start column origin; defaults to Sunday to match the reference layout.</param>
    public static CalendarMonth Create(
        int year,
        int month,
        DateOnly today,
        DayOfWeek firstDayOfWeek = DayOfWeek.Sunday)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be 1..12.");
        }

        if (year is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year must be 1..9999.");
        }

        var first = new DateOnly(year, month, 1);

        // How many leading days from the previous month fill the first row.
        int lead = ((int)first.DayOfWeek - (int)firstDayOfWeek + Columns) % Columns;
        DateOnly start = first.AddDays(-lead);

        var cells = new CalendarCell[Rows * Columns];
        for (int i = 0; i < cells.Length; i++)
        {
            DateOnly date = start.AddDays(i);
            bool inMonth = date.Month == month && date.Year == year;
            cells[i] = new CalendarCell(date, inMonth, date == today);
        }

        var headers = new string[Columns];
        string[] abbreviated = DateTimeFormatInfo.InvariantInfo.AbbreviatedDayNames;
        for (int c = 0; c < Columns; c++)
        {
            headers[c] = abbreviated[((int)firstDayOfWeek + c) % Columns];
        }

        string title = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeFormatInfo.InvariantInfo.GetMonthName(month)} {year}");

        return new CalendarMonth(year, month, firstDayOfWeek, cells, headers, title);
    }

    /// <summary>Reads a cell by row (0..5) and column (0..6).</summary>
    public CalendarCell CellAt(int row, int column)
    {
        if (row is < 0 or >= Rows)
        {
            throw new ArgumentOutOfRangeException(nameof(row), row, "Row must be 0..5.");
        }

        if (column is < 0 or >= Columns)
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "Column must be 0..6.");
        }

        return _cells[row * Columns + column];
    }

    /// <summary>The calendar for the month before this one, same week-start and today flag.</summary>
    public CalendarMonth Previous(DateOnly today)
    {
        (int y, int m) = Month == 1 ? (Year - 1, 12) : (Year, Month - 1);
        return Create(y, m, today, FirstDayOfWeek);
    }

    /// <summary>The calendar for the month after this one, same week-start and today flag.</summary>
    public CalendarMonth Next(DateOnly today)
    {
        (int y, int m) = Month == 12 ? (Year + 1, 1) : (Year, Month + 1);
        return Create(y, m, today, FirstDayOfWeek);
    }
}
