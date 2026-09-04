using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using WinDots.Core.Media;

namespace WinDots.App.Media.Controls;

/// <summary>
/// A seek row: elapsed time on the left, a thin slider, and the duration on the right. While the user drags the
/// thumb the elapsed label follows it and no <see cref="SeekRequested"/> fires; the request is raised once on
/// release. When <see cref="CanSeek"/> is false the slider is read-only.
/// </summary>
public sealed partial class SeekBar : UserControl
{
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position),
        typeof(TimeSpan),
        typeof(SeekBar),
        new PropertyMetadata(TimeSpan.Zero, OnPositionChanged));

    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration),
        typeof(TimeSpan?),
        typeof(SeekBar),
        new PropertyMetadata(null, OnDurationChanged));

    public static readonly DependencyProperty CanSeekProperty = DependencyProperty.Register(
        nameof(CanSeek),
        typeof(bool),
        typeof(SeekBar),
        new PropertyMetadata(false, OnCanSeekChanged));

    private static readonly TimeSpan SmallSeek = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LargeSeek = TimeSpan.FromSeconds(30);

    private bool _programmatic;
    private bool _dragging;

    public SeekBar()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ApplyDuration();
            ApplyPosition();
            Track.IsEnabled = CanSeek;
        };
    }

    /// <summary>Raised once, on drag or keyboard release, with the requested seek target.</summary>
    public event EventHandler<TimeSpan>? SeekRequested;

    /// <summary>Current playback position.</summary>
    public TimeSpan Position
    {
        get => (TimeSpan)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    /// <summary>Track duration, or null when unknown (shows "0:00").</summary>
    public TimeSpan? Duration
    {
        get => (TimeSpan?)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    /// <summary>Whether seeking is allowed; false gives a read-only look.</summary>
    public bool CanSeek
    {
        get => (bool)GetValue(CanSeekProperty);
        set => SetValue(CanSeekProperty, value);
    }

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SeekBar)d).ApplyPosition();
    }

    private static void OnDurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SeekBar)d).ApplyDuration();
    }

    private static void OnCanSeekChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SeekBar)d).Track.IsEnabled = (bool)e.NewValue;
    }

    private void ApplyDuration()
    {
        double seconds = Duration is { } duration && duration > TimeSpan.Zero ? duration.TotalSeconds : 0;
        _programmatic = true;
        Track.Maximum = Math.Max(seconds, 0);
        _programmatic = false;
        DurationText.Text = TimeFormat.Clock(Duration);
        ApplyPosition();
    }

    private void ApplyPosition()
    {
        if (_dragging)
        {
            return;
        }

        _programmatic = true;
        Track.Value = Math.Clamp(Position.TotalSeconds, Track.Minimum, Track.Maximum);
        _programmatic = false;
        ElapsedText.Text = TimeFormat.Clock(Position);
    }

    private void OnValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_programmatic)
        {
            return;
        }

        _dragging = true;
        ElapsedText.Text = TimeFormat.Clock(TimeSpan.FromSeconds(e.NewValue));
    }

    // The keyboard map (_docs/03) puts seek nudges on the slider itself: Left/Right = +/-5 s, Shift = +/-30 s.
    // We handle them here (marking the event handled) so the Slider's native SmallChange step never runs, and so
    // the page-level handler is not shadowed by the focused Slider consuming Left/Right first.
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        TimeSpan delta;
        switch (e.Key)
        {
            case global::Windows.System.VirtualKey.Left:
                delta = ShiftDown ? -LargeSeek : -SmallSeek;
                break;
            case global::Windows.System.VirtualKey.Right:
                delta = ShiftDown ? LargeSeek : SmallSeek;
                break;
            default:
                return;
        }

        e.Handled = true;
        if (!CanSeek)
        {
            return;
        }

        TimeSpan target = ClampToTrack(Position + delta);

        // Reflect the new target immediately; the committed position flows back through Position.
        _programmatic = true;
        Track.Value = Math.Clamp(target.TotalSeconds, Track.Minimum, Track.Maximum);
        _programmatic = false;
        ElapsedText.Text = TimeFormat.Clock(target);

        SeekRequested?.Invoke(this, target);
    }

    private static bool ShiftDown =>
        (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
            global::Windows.System.VirtualKey.Shift) & global::Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

    private TimeSpan ClampToTrack(TimeSpan target)
    {
        if (target < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (Duration is { } duration && target > duration)
        {
            return duration;
        }

        return target;
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e) => Commit();

    private void OnCommit(object sender, PointerRoutedEventArgs e) => Commit();

    private void Commit()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        if (CanSeek)
        {
            SeekRequested?.Invoke(this, TimeSpan.FromSeconds(Track.Value));
        }
    }
}
