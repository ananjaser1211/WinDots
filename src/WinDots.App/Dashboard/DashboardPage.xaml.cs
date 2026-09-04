using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using WinDots.App.Diagnostics;
using WinDots.App.Media;
using WinDots.Core.Contracts;
using WinDots.Core.Dashboard;
using WinDots.Core.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace WinDots.App.Dashboard;

/// <summary>
/// The Dashboard tab surface (Widgets.png): weather placeholder, user card, stacked clock, month calendar, three
/// resource rings and a compact now-playing card. All widget arithmetic comes from the committed
/// <see cref="WinDots.Core.Dashboard"/> models; live data from <see cref="ISystemMetricsProvider"/> and the shared
/// <see cref="MediaViewModel"/>. Timers (a 1 s clock/uptime tick and a metrics sampler) run only while this page is the
/// active tab and the drawer is open — the shell drives that through <see cref="SetActive"/> — and are stopped on unload.
/// </summary>
public sealed partial class DashboardPage : UserControl
{
    // Ring geometry: a 270-degree gauge with the gap at the top (12 o'clock), matching Widgets.png. Angles are measured
    // clockwise from the positive x-axis (screen y grows downward, so 90deg is 6 o'clock and 270deg is 12 o'clock). The
    // arc runs 315deg..585deg (315 -> 3 -> 6 -> 9 -> 225), leaving the missing 90deg centred on 270deg = straight up;
    // the progress fill starts at 315deg (1 o'clock) and sweeps clockwise down the right side.
    private const double RingSize = 62;
    private const double RingStroke = 5;
    private const double RingStartAngle = 315;
    private const double RingArcDegrees = ResourceGauge.DefaultSweepDegrees;

    private readonly List<RingVisual> _rings = new(3);
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DispatcherTimer? _metricsTimer;

    // No 12/24-hour setting exists in the schema yet, so the clock is 12-hour with AM/PM per Widgets.png. Wire this to a
    // settings flag when one is added.
    private const bool Use24Hour = false;

    private CalendarMonth? _calendar;
    private DateOnly _today;

    private int _sampleIntervalMs = 1000;
    private bool _active;
    private bool _initialized;
    private bool _snapshotInFlight;
    private CancellationTokenSource? _metricsCts;

    public DashboardPage()
    {
        InitializeComponent();
        _clockTimer.Tick += OnClockTick;
        Unloaded += OnUnloaded;
        ActualThemeChanged += OnActualThemeChanged;
        BuildRings();
    }

    /// <summary>Live system metrics for the rings, user card and uptime. Null until <see cref="Initialize"/> runs.</summary>
    public ISystemMetricsProvider? Metrics { get; private set; }

    /// <summary>The shared media presentation model, so the dashboard's mini media card mirrors the Media tab.</summary>
    public MediaViewModel? Media { get; private set; }

    /// <summary>Raised when the user asks to enable weather from the placeholder card; the host flips consent in settings.</summary>
    public event EventHandler? WeatherEnableRequested;

    /// <summary>
    /// Wires the page to the shell's metrics provider, the shared media view-model and the metrics sample interval.
    /// Idempotent: repeated calls only refresh the stored values.
    /// </summary>
    public void Initialize(ISystemMetricsProvider metrics, MediaViewModel media, int sampleIntervalMs)
    {
        Metrics = metrics;
        Media = media;
        _sampleIntervalMs = Math.Clamp(sampleIntervalMs, 250, 10000);

        if (!_initialized)
        {
            _initialized = true;
            Media.PropertyChanged += OnMediaPropertyChanged;
            ApplyUserInfo();
        }

        // Re-point every x:Bind now that Media is non-null (they were evaluated against null at construction).
        Bindings.Update();
        UpdateMiniTransport();
    }

    /// <summary>Applies the current metrics sample interval (live settings change). Restarts the sampler if running.</summary>
    public void UpdateSampleInterval(int sampleIntervalMs)
    {
        _sampleIntervalMs = Math.Clamp(sampleIntervalMs, 250, 10000);
        if (_active && _metricsTimer is not null)
        {
            _metricsTimer.Interval = TimeSpan.FromMilliseconds(_sampleIntervalMs);
        }
    }

    /// <summary>Reflects weather consent in the placeholder card. No network access; the live provider is a TODO.</summary>
    public void SetWeatherConsent(bool consentGranted)
    {
        // TODO: when a weather provider lands, replace this placeholder with the live forecast (temperature, condition,
        // icon) fetched only after consent. Until then the card only reports the consent state.
        if (consentGranted)
        {
            WeatherPrimary.Text = "Weather on";
            WeatherSecondary.Text = "Forecast coming soon";
            WeatherEnableButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            WeatherPrimary.Text = "Weather off";
            WeatherSecondary.Text = "Location forecast is off";
            WeatherEnableButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Starts or stops the page's timers. The shell calls this so the clock/uptime tick and the metrics sampler run
    /// only while the Dashboard tab is the active tab and the drawer is open, and never churn in the background.
    /// </summary>
    public void SetActive(bool active)
    {
        if (active == _active)
        {
            return;
        }

        _active = active;
        if (active)
        {
            EnsureCalendar();
            RefreshClock();
            RefreshUptime();
            _clockTimer.Start();
            StartMetrics();
        }
        else
        {
            StopMetrics();
            _clockTimer.Stop();
        }
    }

    // --- Clock / uptime ---

    private void OnClockTick(object? sender, object e)
    {
        RefreshClock();
        RefreshUptime();

        // Roll the calendar highlight over at midnight without disturbing any month the user navigated to.
        DateOnly nowDate = DateOnly.FromDateTime(DateTime.Now);
        if (nowDate != _today && _calendar is not null)
        {
            _today = nowDate;
            RebuildCalendar();
        }
    }

    private void RefreshClock()
    {
        ClockModel clock = ClockModel.Create(DateTimeOffset.Now, Use24Hour);
        ClockHour.Text = clock.Hour;
        ClockMinute.Text = clock.Minute;
        ClockMeridiem.Text = clock.Meridiem;
        ClockMeridiem.Visibility = clock.Meridiem.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshUptime()
    {
        if (Metrics is not null)
        {
            UptimeText.Text = UptimeFormatter.Format(Metrics.GetUptime());
        }
    }

    private void ApplyUserInfo()
    {
        if (Metrics is null)
        {
            return;
        }

        UserInfo user = Metrics.GetUserInfo();
        UserName.Text = user.DisplayName;

        if (user.AccountPicturePath is { } path && File.Exists(path))
        {
            try
            {
                AvatarBrush.ImageSource = new BitmapImage(new Uri(path));
                AvatarEllipse.Visibility = Visibility.Visible;
            }
            catch (Exception ex) when (ex is UriFormatException or FileNotFoundException)
            {
                AvatarEllipse.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            AvatarEllipse.Visibility = Visibility.Collapsed;
        }
    }

    // --- Calendar ---

    private void EnsureCalendar()
    {
        _today = DateOnly.FromDateTime(DateTime.Now);
        if (_calendar is null || _calendar.Year != _today.Year || _calendar.Month != _today.Month)
        {
            _calendar = CalendarMonth.Create(_today.Year, _today.Month, _today);
        }

        RebuildCalendar();
    }

    private void OnPreviousMonthClick(object sender, RoutedEventArgs e)
    {
        _calendar = (_calendar ?? CalendarMonth.Create(_today.Year, _today.Month, _today)).Previous(_today);
        RebuildCalendar();
    }

    private void OnNextMonthClick(object sender, RoutedEventArgs e)
    {
        _calendar = (_calendar ?? CalendarMonth.Create(_today.Year, _today.Month, _today)).Next(_today);
        RebuildCalendar();
    }

    private void RebuildCalendar()
    {
        if (_calendar is null)
        {
            return;
        }

        MonthTitle.Text = _calendar.Title;

        CalendarGrid.Children.Clear();
        CalendarGrid.RowDefinitions.Clear();
        CalendarGrid.ColumnDefinitions.Clear();

        for (int c = 0; c < CalendarMonth.Columns; c++)
        {
            CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        // One header row plus six week rows.
        CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int r = 0; r < CalendarMonth.Rows; r++)
        {
            CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        Brush muted = GetThemeBrush("WdOnSurfaceMutedBrush");
        Brush onSurface = GetThemeBrush("WdOnSurfaceBrush");
        Brush accent = Media?.AccentBrush ?? GetThemeBrush("WdAccentBrush");
        Brush onAccent = Media?.OnAccentBrush ?? GetThemeBrush("WdOnAccentBrush");

        for (int c = 0; c < CalendarMonth.Columns; c++)
        {
            var header = new TextBlock
            {
                Text = _calendar.WeekdayHeaders[c],
                FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
                FontSize = 11,
                Foreground = muted,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            Grid.SetRow(header, 0);
            Grid.SetColumn(header, c);
            CalendarGrid.Children.Add(header);
        }

        for (int i = 0; i < _calendar.Cells.Count; i++)
        {
            CalendarCell cell = _calendar.Cells[i];
            int row = 1 + (i / CalendarMonth.Columns);
            int col = i % CalendarMonth.Columns;

            var container = new Grid();
            var number = new TextBlock
            {
                Text = cell.Day.ToString(CultureInfo.InvariantCulture),
                FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            if (cell.IsToday)
            {
                container.Children.Add(new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(13),
                    Background = accent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                number.Foreground = onAccent;
                number.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            }
            else
            {
                number.Foreground = cell.IsInMonth ? onSurface : muted;
                number.Opacity = cell.IsInMonth ? 1.0 : 0.5;
            }

            container.Children.Add(number);
            Grid.SetRow(container, row);
            Grid.SetColumn(container, col);
            CalendarGrid.Children.Add(container);
        }
    }

    // --- Resource rings ---

    private void BuildRings()
    {
        _rings.Clear();
        RingsPanel.Children.Clear();
        _rings.Add(CreateRing("")); // CPU (processor)
        _rings.Add(CreateRing("")); // Memory
        _rings.Add(CreateRing("")); // Disk (hard drive)
        foreach (RingVisual ring in _rings)
        {
            RingsPanel.Children.Add(ring.Root);
        }
    }

    private RingVisual CreateRing(string glyph)
    {
        double centre = RingSize / 2.0;
        double radius = centre - (RingStroke / 2.0);

        var root = new Grid { Width = RingSize, Height = RingSize };

        var track = new Path
        {
            Stroke = GetThemeBrush("WdRingTrackBrush"),
            StrokeThickness = RingStroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = BuildArc(centre, radius, RingStartAngle, RingStartAngle + RingArcDegrees),
        };
        root.Children.Add(track);

        var progressFigure = new PathFigure { StartPoint = PointOnCircle(centre, radius, RingStartAngle) };
        var progressSegment = new ArcSegment
        {
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = false,
            Point = PointOnCircle(centre, radius, RingStartAngle),
        };
        progressFigure.Segments.Add(progressSegment);
        var progressGeometry = new PathGeometry();
        progressGeometry.Figures.Add(progressFigure);
        var progress = new Path
        {
            Stroke = Media?.AccentBrush ?? GetThemeBrush("WdAccentBrush"),
            StrokeThickness = RingStroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = progressGeometry,
            Visibility = Visibility.Collapsed,
        };
        root.Children.Add(progress);

        var glyphIcon = new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 14,
            Foreground = GetThemeBrush("WdOnSurfaceMutedBrush"),
            Glyph = glyph,
            Margin = new Thickness(0, 0, 0, 10),
        };
        root.Children.Add(glyphIcon);

        var percent = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = GetThemeBrush("WdOnSurfaceBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
            Text = "0%",
        };
        root.Children.Add(percent);

        return new RingVisual(root, progress, progressSegment, percent);
    }

    private void UpdateRing(RingVisual ring, double fraction)
    {
        ResourceGauge gauge = ResourceGauge.FromFraction(fraction, RingArcDegrees);
        ring.Percent.Text = gauge.Display;

        if (gauge.SweepDegrees < 0.5)
        {
            ring.ProgressPath.Visibility = Visibility.Collapsed;
            return;
        }

        double centre = RingSize / 2.0;
        double radius = centre - (RingStroke / 2.0);
        double endAngle = RingStartAngle + gauge.SweepDegrees;
        ring.Segment.Point = PointOnCircle(centre, radius, endAngle);
        ring.Segment.IsLargeArc = gauge.SweepDegrees > 180;
        ring.ProgressPath.Stroke = Media?.AccentBrush ?? GetThemeBrush("WdAccentBrush");
        ring.ProgressPath.Visibility = Visibility.Visible;
    }

    private static PathGeometry BuildArc(double centre, double radius, double startAngle, double endAngle)
    {
        var figure = new PathFigure { StartPoint = PointOnCircle(centre, radius, startAngle) };
        figure.Segments.Add(new ArcSegment
        {
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = (endAngle - startAngle) > 180,
            Point = PointOnCircle(centre, radius, endAngle),
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Point PointOnCircle(double centre, double radius, double angleDegrees)
    {
        double a = angleDegrees * Math.PI / 180.0;
        return new Point(centre + (radius * Math.Cos(a)), centre + (radius * Math.Sin(a)));
    }

    // --- Metrics sampling ---

    private void StartMetrics()
    {
        _metricsTimer ??= CreateMetricsTimer();
        _metricsTimer.Interval = TimeSpan.FromMilliseconds(_sampleIntervalMs);

        // Prime the CPU counter and paint memory/disk immediately; the first CPU reading may be zero (expected).
        _ = RefreshMetricsAsync();
        _metricsTimer.Start();
    }

    private void StopMetrics()
    {
        _metricsTimer?.Stop();
        _metricsCts?.Cancel();
        _metricsCts?.Dispose();
        _metricsCts = null;
    }

    private DispatcherTimer CreateMetricsTimer()
    {
        var timer = new DispatcherTimer();
        timer.Tick += (_, _) => _ = RefreshMetricsAsync();
        return timer;
    }

    private async Task RefreshMetricsAsync()
    {
        if (Metrics is null || _snapshotInFlight || !_active)
        {
            return;
        }

        _snapshotInFlight = true;
        _metricsCts?.Dispose();
        var cts = new CancellationTokenSource();
        _metricsCts = cts;
        try
        {
            SystemMetrics metrics = await Metrics.GetSnapshotAsync(cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested || !_active)
            {
                return;
            }

            UpdateRing(_rings[0], metrics.CpuFraction);
            UpdateRing(_rings[1], metrics.MemoryFraction);
            UpdateRing(_rings[2], metrics.DiskFraction);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a stop; nothing to do.
        }
        catch (Exception ex)
        {
            ShellLog.Write($"dashboard metrics: snapshot failed {ex.GetType().Name}");
        }
        finally
        {
            _snapshotInFlight = false;
        }
    }

    // --- Mini media card ---

    private void OnMediaPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MediaViewModel.IsPlaying) or nameof(MediaViewModel.Capabilities) or null)
        {
            UpdateMiniTransport();
        }
    }

    private void UpdateMiniTransport()
    {
        if (Media is null)
        {
            return;
        }

        MiniPlayGlyph.Glyph = Media.IsPlaying ? "" : "";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MiniPlayButton, Media.IsPlaying ? "Pause" : "Play");

        Capabilities caps = Media.Capabilities;
        bool canPlayPause = caps.HasFlag(Capabilities.PlayPause) || caps.HasFlag(Capabilities.Play) || caps.HasFlag(Capabilities.Pause);
        SetEnabled(MiniPrevButton, caps.HasFlag(Capabilities.Previous));
        SetEnabled(MiniPlayButton, canPlayPause);
        SetEnabled(MiniNextButton, caps.HasFlag(Capabilities.Next));
    }

    private static void SetEnabled(Control button, bool enabled)
    {
        button.IsEnabled = enabled;
        button.Opacity = enabled ? 1.0 : 0.4;
    }

    private void OnMiniPreviousClick(object sender, RoutedEventArgs e) => _ = Media?.PreviousAsync();

    private void OnMiniPlayPauseClick(object sender, RoutedEventArgs e) => _ = Media?.PlayPauseAsync();

    private void OnMiniNextClick(object sender, RoutedEventArgs e) => _ = Media?.NextAsync();

    private void OnEnableWeatherClick(object sender, RoutedEventArgs e) => WeatherEnableRequested?.Invoke(this, EventArgs.Empty);

    // --- Theme ---

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        // Code-created elements do not resolve ThemeResource on their own, so re-tint them when the theme flips.
        RebuildCalendar();
        foreach (RingVisual ring in _rings)
        {
            if (ring.Root.Children[0] is Path track)
            {
                track.Stroke = GetThemeBrush("WdRingTrackBrush");
            }
        }
    }

    private Brush GetThemeBrush(string key)
    {
        string themeKey = ActualTheme == ElementTheme.Light ? "Light" : "Default";
        foreach (ResourceDictionary md in Application.Current.Resources.MergedDictionaries)
        {
            if (md.ThemeDictionaries.TryGetValue(themeKey, out object? themed) &&
                themed is ResourceDictionary dict &&
                dict.TryGetValue(key, out object? brush) &&
                brush is Brush b)
            {
                return b;
            }
        }

        return new SolidColorBrush(Colors.Gray);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _active = false;
        StopMetrics();
        _clockTimer.Stop();
    }

    /// <summary>Holds the mutable pieces of one resource ring so a sample only updates the arc and percent text.</summary>
    private sealed record RingVisual(Grid Root, Path ProgressPath, ArcSegment Segment, TextBlock Percent);
}
