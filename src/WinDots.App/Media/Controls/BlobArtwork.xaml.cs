using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WinDots.Core.Design;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace WinDots.App.Media.Controls;

/// <summary>
/// Renders artwork clipped to the superformula blob outline (<see cref="BlobGeometry"/>). WinUI
/// <c>UIElement.Clip</c> accepts only a <see cref="RectangleGeometry"/>, so the image is instead painted as an
/// <see cref="ImageBrush"/> fill on a <see cref="Path"/> whose data is the blob. Two stacked paths cross-fade
/// between the old and new image over <c>WdMotionBaseMs</c>. When there is no image the blob shows the raised
/// surface with centred image + pause glyphs, matching the static reference.
/// </summary>
public sealed partial class BlobArtwork : UserControl
{
    public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(
        nameof(ImageSource),
        typeof(ImageSource),
        typeof(BlobArtwork),
        new PropertyMetadata(null, OnImageSourceChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size),
        typeof(double),
        typeof(BlobArtwork),
        new PropertyMetadata(200.0, OnGeometryChanged));

    public static readonly DependencyProperty DeformProperty = DependencyProperty.Register(
        nameof(Deform),
        typeof(double),
        typeof(BlobArtwork),
        new PropertyMetadata(1.0, OnGeometryChanged));

    public static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        nameof(Phase),
        typeof(double),
        typeof(BlobArtwork),
        new PropertyMetadata(0.0, OnGeometryChanged));

    public static readonly DependencyProperty HighContrastProperty = DependencyProperty.Register(
        nameof(HighContrast),
        typeof(bool),
        typeof(BlobArtwork),
        new PropertyMetadata(false, OnHighContrastChanged));

    // Idle drift: the phase advances one full cycle (2 pi) per 20 s, regenerating the geometry at ~10 Hz.
    private static readonly TimeSpan DriftInterval = TimeSpan.FromMilliseconds(100);
    private const double DriftPeriodSeconds = 20.0;

    private Path? _front;
    private DispatcherQueueTimer? _driftTimer;
    private bool _idleMotion;

    // Reduced motion turns the artwork cross-fade into an instant swap (_docs/03 Accessibility). Active is the
    // drawer-visibility gate: idle drift must not run while the drawer is hidden in the tray (the XAML tree stays
    // loaded across AppWindow.Hide, so lifecycle events alone would leave the 10 Hz timer allocating forever).
    private bool _reduceMotion;
    private bool _active;

    public BlobArtwork()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RebuildGeometry();
            ApplyImage(ImageSource, animate: false);
            if (_idleMotion)
            {
                StartDrift();
            }
        };
        Unloaded += (_, _) => StopDrift();
    }

    /// <summary>Phase offset (radians) applied to the blob outline for idle drift.</summary>
    public double Phase
    {
        get => (double)GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    /// <summary>When true, the blob is drawn with a 2 px WindowText outline for high contrast.</summary>
    public bool HighContrast
    {
        get => (bool)GetValue(HighContrastProperty);
        set => SetValue(HighContrastProperty, value);
    }

    /// <summary>The artwork to display, or null to show the placeholder.</summary>
    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    /// <summary>Side length of the square blob, in logical pixels.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Blob deformation multiplier; the amplitude is <c>0.06 * Deform</c>.</summary>
    public double Deform
    {
        get => (double)GetValue(DeformProperty);
        set => SetValue(DeformProperty, value);
    }

    private static void OnImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BlobArtwork)d;
        control.ApplyImage(e.NewValue as ImageSource, animate: control.IsLoaded && !control._reduceMotion);
    }

    private static void OnGeometryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BlobArtwork)d).RebuildGeometry();
    }

    private static void OnHighContrastChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BlobArtwork)d).OutlineShape.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Starts or stops the idle phase drift (disabled under reduced motion or high contrast).</summary>
    public void SetIdleMotion(bool enabled)
    {
        _idleMotion = enabled;
        if (enabled)
        {
            StartDrift();
        }
        else
        {
            StopDrift();
            Phase = 0.0;
        }
    }

    /// <summary>When false, the artwork cross-fade is an instant swap (reduced motion / high contrast).</summary>
    public void SetReduceMotion(bool enabled) => _reduceMotion = enabled;

    /// <summary>
    /// Pauses or resumes idle drift with the drawer's visibility. The drawer is hidden via <c>AppWindow.Hide</c>,
    /// which does not unload the XAML tree, so drift must be stopped here or the 10 Hz geometry rebuild runs forever.
    /// </summary>
    public void SetActive(bool active)
    {
        if (_active == active)
        {
            return;
        }

        _active = active;
        if (active && _idleMotion)
        {
            StartDrift();
        }
        else
        {
            StopDrift();
        }
    }

    private void StartDrift()
    {
        if (!IsLoaded || !_active || _driftTimer is not null)
        {
            return;
        }

        DispatcherQueue queue = DispatcherQueue.GetForCurrentThread();
        if (queue is null)
        {
            return;
        }

        _driftTimer = queue.CreateTimer();
        _driftTimer.Interval = DriftInterval;
        _driftTimer.IsRepeating = true;
        _driftTimer.Tick += OnDriftTick;
        _driftTimer.Start();
    }

    private void StopDrift()
    {
        if (_driftTimer is not null)
        {
            _driftTimer.Stop();
            _driftTimer.Tick -= OnDriftTick;
            _driftTimer = null;
        }
    }

    private void OnDriftTick(DispatcherQueueTimer sender, object args)
    {
        double next = Phase + (2.0 * Math.PI * DriftInterval.TotalSeconds / DriftPeriodSeconds);
        if (next >= 2.0 * Math.PI)
        {
            next -= 2.0 * Math.PI;
        }

        Phase = next;
    }

    private void RebuildGeometry()
    {
        double size = Size;
        if (!(size > 0))
        {
            return;
        }

        Width = size;
        Height = size;
        LayoutRoot.Width = size;
        LayoutRoot.Height = size;

        double amplitude = Math.Clamp(0.06 * Deform, 0.0, 0.99);
        BlobPath blob = BlobGeometry.Create(size, 8, amplitude, Phase);
        Geometry geometry = ToGeometry(blob);

        PlaceholderShape.Data = geometry;
        PathA.Data = ToGeometry(blob);
        PathB.Data = ToGeometry(blob);
        OutlineShape.Data = ToGeometry(blob);

        double glyphSize = Math.Round(size * 0.22);
        PlaceholderImageGlyph.FontSize = glyphSize;
        PlaceholderPauseGlyph.FontSize = glyphSize;
    }

    private static Geometry ToGeometry(BlobPath blob)
    {
        var figure = new PathFigure
        {
            StartPoint = new global::Windows.Foundation.Point(blob.Points[0].X, blob.Points[0].Y),
            IsClosed = true,
            IsFilled = true,
        };

        var segment = new PolyLineSegment();
        for (int i = 1; i < blob.Points.Count; i++)
        {
            segment.Points.Add(new global::Windows.Foundation.Point(blob.Points[i].X, blob.Points[i].Y));
        }

        figure.Segments.Add(segment);
        var pathGeometry = new PathGeometry();
        pathGeometry.Figures.Add(figure);
        return pathGeometry;
    }

    private void ApplyImage(ImageSource? source, bool animate)
    {
        if (source is null)
        {
            FadeTo(PlaceholderShape, 1);
            FadeTo(PlaceholderGlyphs, 1);
            if (_front is not null)
            {
                FadeTo(_front, 0);
            }

            return;
        }

        Path back = ReferenceEquals(_front, PathA) ? PathB : PathA;
        back.Fill = new ImageBrush { ImageSource = source, Stretch = Stretch.UniformToFill };

        if (!animate)
        {
            back.Opacity = 1;
            if (_front is not null)
            {
                _front.Opacity = 0;
            }

            PlaceholderShape.Opacity = 0;
            PlaceholderGlyphs.Opacity = 0;
            _front = back;
            return;
        }

        FadeTo(back, 1);
        if (_front is not null)
        {
            FadeTo(_front, 0);
        }

        FadeTo(PlaceholderShape, 0);
        FadeTo(PlaceholderGlyphs, 0);
        _front = back;
    }

    private void FadeTo(UIElement element, double target)
    {
        double durationMs = 250;
        if (Application.Current.Resources.TryGetValue("WdMotionBaseMs", out object? value) && value is double ms)
        {
            durationMs = ms;
        }

        var animation = new DoubleAnimation
        {
            To = target,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
}
