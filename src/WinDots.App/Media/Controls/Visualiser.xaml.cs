using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WinDots.Core.Design;
using WinDots.Core.Visualiser;

namespace WinDots.App.Media.Controls;

/// <summary>
/// A composition-rendered audio visualiser. The owner pushes a fresh <see cref="Bands"/> (and, for the waveform
/// style, <see cref="Waveform"/>) list each frame; the control updates only the composition transforms of a fixed
/// set of shapes, so a frame allocates nothing and forces no layout (the same discipline as
/// <see cref="DottedProgressRing"/> and the background blobs). All colour comes from <see cref="AccentBrush"/>.
/// Styles per <see cref="VisualiserStyle"/>; the placement relative to the artwork is decided by the host page.
/// The <see cref="VisualiserStyle.BlobPulse"/> style renders nothing here — the page scales the album blob itself.
/// See _docs/10-enhancement-plan.md (E5) and _docs/04-visual-design.md.
/// </summary>
public sealed partial class Visualiser : UserControl
{
    private const float MinBarScale = 0.02f;
    private const int ParticleCount = 7;

    public static readonly DependencyProperty RenderStyleProperty = DependencyProperty.Register(
        nameof(RenderStyle),
        typeof(VisualiserStyle),
        typeof(Visualiser),
        new PropertyMetadata(VisualiserStyle.Ring, OnStructureChanged));

    public static readonly DependencyProperty BandsProperty = DependencyProperty.Register(
        nameof(Bands),
        typeof(IReadOnlyList<double>),
        typeof(Visualiser),
        new PropertyMetadata(null, OnFrameChanged));

    public static readonly DependencyProperty WaveformProperty = DependencyProperty.Register(
        nameof(Waveform),
        typeof(IReadOnlyList<double>),
        typeof(Visualiser),
        new PropertyMetadata(null, OnFrameChanged));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush),
        typeof(Brush),
        typeof(Visualiser),
        new PropertyMetadata(null, OnStructureChanged));

    public static readonly DependencyProperty BarCountProperty = DependencyProperty.Register(
        nameof(BarCount),
        typeof(int),
        typeof(Visualiser),
        new PropertyMetadata(60, OnStructureChanged));

    public static readonly DependencyProperty MirroredProperty = DependencyProperty.Register(
        nameof(Mirrored),
        typeof(bool),
        typeof(Visualiser),
        new PropertyMetadata(false, OnFrameChanged));

    // Per-style shape caches; only the collection for the current style is populated.
    private Rectangle[] _bars = Array.Empty<Rectangle>();
    private Visual[] _barVisuals = Array.Empty<Visual>();
    private Line[] _ringBars = Array.Empty<Line>();
    private (double Ix, double Iy, double Dx, double Dy)[] _ringGeometry =
        Array.Empty<(double, double, double, double)>();
    private Polygon? _waveShape;
    private Ellipse? _halo;
    private Visual? _haloVisual;
    private Ellipse[] _particles = Array.Empty<Ellipse>();
    private Visual[] _particleVisuals = Array.Empty<Visual>();
    private double[] _particleAngles = Array.Empty<double>();

    private bool _built;

    public Visualiser()
    {
        InitializeComponent();
        Loaded += (_, _) => Rebuild();
        SizeChanged += (_, _) => Rebuild();
    }

    /// <summary>The render style.</summary>
    public VisualiserStyle RenderStyle
    {
        get => (VisualiserStyle)GetValue(RenderStyleProperty);
        set => SetValue(RenderStyleProperty, value);
    }

    /// <summary>Latest band magnitudes in <c>0..1</c>, pushed each frame. A fresh list per frame drives the update.</summary>
    public IReadOnlyList<double>? Bands
    {
        get => (IReadOnlyList<double>?)GetValue(BandsProperty);
        set => SetValue(BandsProperty, value);
    }

    /// <summary>Latest waveform amplitude points in <c>0..1</c> (half-height envelope), for the waveform style.</summary>
    public IReadOnlyList<double>? Waveform
    {
        get => (IReadOnlyList<double>?)GetValue(WaveformProperty);
        set => SetValue(WaveformProperty, value);
    }

    /// <summary>The brush every mark is painted with (the artwork accent, or the fixed accent).</summary>
    public Brush? AccentBrush
    {
        get => (Brush?)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    /// <summary>Number of bars / radial spokes to draw (clamped to the supported band range).</summary>
    public int BarCount
    {
        get => (int)GetValue(BarCountProperty);
        set => SetValue(BarCountProperty, value);
    }

    /// <summary>Mirror the bars about the horizontal centre (bars style only).</summary>
    public bool Mirrored
    {
        get => (bool)GetValue(MirroredProperty);
        set => SetValue(MirroredProperty, value);
    }

    private static void OnStructureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((Visualiser)d).Rebuild();

    private static void OnFrameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((Visualiser)d).UpdateFrame();

    private double Width_ => ActualWidth;

    private double Height_ => ActualHeight;

    private int ClampedBars =>
        Math.Clamp(BarCount, VisualiserOptions.MinBars, VisualiserOptions.MaxBars);

    private void Rebuild()
    {
        _built = false;
        Host.Children.Clear();
        _bars = Array.Empty<Rectangle>();
        _barVisuals = Array.Empty<Visual>();
        _ringBars = Array.Empty<Line>();
        _ringGeometry = Array.Empty<(double, double, double, double)>();
        _waveShape = null;
        _halo = null;
        _haloVisual = null;
        _particles = Array.Empty<Ellipse>();
        _particleVisuals = Array.Empty<Visual>();
        _particleAngles = Array.Empty<double>();

        if (!IsLoaded || !(Width_ > 0) || !(Height_ > 0))
        {
            return;
        }

        Host.Width = Width_;
        Host.Height = Height_;

        switch (RenderStyle)
        {
            case VisualiserStyle.Bars:
                BuildBars();
                break;
            case VisualiserStyle.Waveform:
                BuildWaveform();
                break;
            case VisualiserStyle.Ring:
                BuildRing();
                break;
            case VisualiserStyle.Halo:
                BuildHalo();
                break;
            case VisualiserStyle.Particles:
                BuildParticles();
                break;
            case VisualiserStyle.BlobPulse:
            default:
                // Rendered by the page (it scales the album blob); nothing to draw here.
                break;
        }

        _built = true;
        UpdateFrame();
    }

    private void BuildBars()
    {
        int n = ClampedBars;
        double w = Width_;
        double h = Height_;
        double slot = w / n;
        double barW = Math.Max(2.0, slot * 0.62);
        _bars = new Rectangle[n];
        _barVisuals = new Visual[n];
        for (int i = 0; i < n; i++)
        {
            var bar = new Rectangle
            {
                Width = barW,
                Height = h,
                RadiusX = barW / 2,
                RadiusY = barW / 2,
                Fill = AccentBrush,
            };
            Canvas.SetLeft(bar, (i * slot) + ((slot - barW) / 2));
            Canvas.SetTop(bar, 0);
            Host.Children.Add(bar);
            Visual visual = ElementCompositionPreview.GetElementVisual(bar);
            visual.CenterPoint = new Vector3((float)(barW / 2), (float)h, 0f);
            _bars[i] = bar;
            _barVisuals[i] = visual;
        }
    }

    private void BuildWaveform()
    {
        // A single filled polygon: the top edge follows +amplitude, the returning bottom edge follows -amplitude,
        // giving a thin symmetric waveform centred vertically. Point count is fixed so a frame only mutates values.
        int points = Math.Clamp(ClampedBars, 24, 96);
        _waveShape = new Polygon { Fill = AccentBrush, Opacity = 0.9 };
        var collection = new global::Microsoft.UI.Xaml.Media.PointCollection();
        for (int i = 0; i < points * 2; i++)
        {
            collection.Add(new global::Windows.Foundation.Point(0, Height_ / 2));
        }

        _waveShape.Points = collection;
        Host.Children.Add(_waveShape);
    }

    private void BuildRing()
    {
        int n = ClampedBars;
        double w = Width_;
        double h = Height_;
        double cx = w / 2;
        double cy = h / 2;
        double inner = Math.Min(w, h) / 2 * 0.80;
        double barW = Math.Max(2.0, (2 * Math.PI * inner / n) * 0.45);

        _ringBars = new Line[n];
        _ringGeometry = new (double, double, double, double)[n];
        IReadOnlyList<(double X, double Y)> centres = DottedRingLayout.Centres(cx, cy, inner, n, -90);
        for (int i = 0; i < n; i++)
        {
            double ix = centres[i].X;
            double iy = centres[i].Y;
            double dx = (ix - cx) / inner;
            double dy = (iy - cy) / inner;
            _ringGeometry[i] = (ix, iy, dx, dy);

            var line = new Line
            {
                X1 = ix,
                Y1 = iy,
                X2 = ix,
                Y2 = iy,
                Stroke = AccentBrush,
                StrokeThickness = barW,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            Host.Children.Add(line);
            _ringBars[i] = line;
        }
    }

    private void BuildHalo()
    {
        double size = Math.Min(Width_, Height_);
        var brush = new RadialGradientBrush
        {
            Center = new global::Windows.Foundation.Point(0.5, 0.5),
            GradientOrigin = new global::Windows.Foundation.Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };
        global::Windows.UI.Color accent = AccentColor();
        brush.GradientStops.Add(new GradientStop { Color = WithAlpha(accent, 0.85), Offset = 0.0 });
        brush.GradientStops.Add(new GradientStop { Color = WithAlpha(accent, 0.35), Offset = 0.55 });
        brush.GradientStops.Add(new GradientStop { Color = WithAlpha(accent, 0.0), Offset = 1.0 });

        _halo = new Ellipse { Width = size, Height = size, Fill = brush, Opacity = 0 };
        Canvas.SetLeft(_halo, (Width_ - size) / 2);
        Canvas.SetTop(_halo, (Height_ - size) / 2);
        Host.Children.Add(_halo);
        _haloVisual = ElementCompositionPreview.GetElementVisual(_halo);
        _haloVisual.CenterPoint = new Vector3((float)(size / 2), (float)(size / 2), 0f);
    }

    private void BuildParticles()
    {
        double dot = Math.Max(4.0, Math.Min(Width_, Height_) * 0.03);
        _particles = new Ellipse[ParticleCount];
        _particleVisuals = new Visual[ParticleCount];
        _particleAngles = new double[ParticleCount];
        for (int i = 0; i < ParticleCount; i++)
        {
            var e = new Ellipse { Width = dot, Height = dot, Fill = AccentBrush, Opacity = 0.4 };
            Host.Children.Add(e);
            Visual visual = ElementCompositionPreview.GetElementVisual(e);
            visual.CenterPoint = new Vector3((float)(dot / 2), (float)(dot / 2), 0f);
            _particles[i] = e;
            _particleVisuals[i] = visual;
            _particleAngles[i] = 2 * Math.PI * i / ParticleCount;
        }
    }

    private void UpdateFrame()
    {
        if (!_built)
        {
            return;
        }

        switch (RenderStyle)
        {
            case VisualiserStyle.Bars:
                UpdateBars();
                break;
            case VisualiserStyle.Waveform:
                UpdateWaveform();
                break;
            case VisualiserStyle.Ring:
                UpdateRing();
                break;
            case VisualiserStyle.Halo:
                UpdateHalo();
                break;
            case VisualiserStyle.Particles:
                UpdateParticles();
                break;
            default:
                break;
        }
    }

    private void UpdateBars()
    {
        IReadOnlyList<double>? bands = Bands;
        int n = _bars.Length;
        if (n == 0)
        {
            return;
        }

        int count = bands?.Count ?? 0;
        for (int i = 0; i < n; i++)
        {
            double value = 0;
            if (count > 0)
            {
                int idx = Mirrored ? MirrorIndex(i, n, count) : Map(i, n, count);
                value = bands![idx];
            }

            float scale = MinBarScale + (float)(Math.Clamp(value, 0, 1) * (1 - MinBarScale));
            _barVisuals[i].Scale = new Vector3(1f, scale, 1f);
        }
    }

    private void UpdateWaveform()
    {
        if (_waveShape is null)
        {
            return;
        }

        IReadOnlyList<double>? wave = Waveform;
        global::Microsoft.UI.Xaml.Media.PointCollection points = _waveShape.Points;
        int half = points.Count / 2;
        if (half == 0)
        {
            return;
        }

        double w = Width_;
        double centre = Height_ / 2;
        double halfHeight = (Height_ / 2) - 1;
        int count = wave?.Count ?? 0;
        for (int i = 0; i < half; i++)
        {
            double x = half == 1 ? 0 : (double)i / (half - 1) * w;
            double amp = count > 0 ? Math.Clamp(wave![Map(i, half, count)], 0, 1) : 0;
            double dy = amp * halfHeight;
            points[i] = new global::Windows.Foundation.Point(x, centre - dy);
            points[points.Count - 1 - i] = new global::Windows.Foundation.Point(x, centre + dy);
        }
    }

    private void UpdateRing()
    {
        IReadOnlyList<double>? bands = Bands;
        int n = _ringBars.Length;
        if (n == 0)
        {
            return;
        }

        double maxLen = Math.Min(Width_, Height_) / 2 * 0.18;
        double minLen = 2.0;
        int count = bands?.Count ?? 0;
        for (int i = 0; i < n; i++)
        {
            double value = count > 0 ? Math.Clamp(bands![Map(i, n, count)], 0, 1) : 0;
            double len = minLen + (value * maxLen);
            (double ix, double iy, double dx, double dy) = _ringGeometry[i];
            Line line = _ringBars[i];
            line.X2 = ix + (dx * len);
            line.Y2 = iy + (dy * len);
        }
    }

    private void UpdateHalo()
    {
        if (_haloVisual is null)
        {
            return;
        }

        double energy = Energy();
        float scale = 0.65f + (float)(energy * 0.45);
        _haloVisual.Scale = new Vector3(scale, scale, 1f);
        _haloVisual.Opacity = 0.12f + (float)(energy * 0.6);
    }

    private void UpdateParticles()
    {
        int n = _particles.Length;
        if (n == 0)
        {
            return;
        }

        double energy = Energy();
        double cx = Width_ / 2;
        double cy = Height_ / 2;
        double baseR = Math.Min(Width_, Height_) / 2 * 0.86;
        double amp = Math.Min(Width_, Height_) / 2 * 0.10;
        double t = Environment.TickCount64 / 1000.0;
        for (int i = 0; i < n; i++)
        {
            double angle = _particleAngles[i] + (t * 0.6);
            double radius = baseR + (energy * amp);
            double dot = _particles[i].Width;
            double x = cx + (radius * Math.Cos(angle)) - (dot / 2);
            double y = cy + (radius * Math.Sin(angle)) - (dot / 2);
            _particleVisuals[i].Offset = new Vector3((float)x, (float)y, 0f);
            _particleVisuals[i].Opacity = 0.3f + (float)(Math.Clamp(energy, 0, 1) * 0.7);
        }
    }

    private double Energy()
    {
        IReadOnlyList<double>? bands = Bands;
        if (bands is null || bands.Count == 0)
        {
            return 0;
        }

        double sum = 0;
        for (int i = 0; i < bands.Count; i++)
        {
            sum += bands[i];
        }

        return Math.Clamp(sum / bands.Count, 0, 1);
    }

    // Maps display index i (of n) into a source list of the given count.
    private static int Map(int i, int n, int count) =>
        n <= 1 ? 0 : Math.Clamp((int)((double)i / (n - 1) * (count - 1)), 0, count - 1);

    // Mirrors n display bars around the centre onto count source bands.
    private static int MirrorIndex(int i, int n, int count)
    {
        int half = (n + 1) / 2;
        int folded = i < half ? (half - 1 - i) : (i - half);
        return Map(folded, Math.Max(1, half), count);
    }

    private global::Windows.UI.Color AccentColor() =>
        AccentBrush is SolidColorBrush scb ? scb.Color : global::Windows.UI.Color.FromArgb(0xFF, 0x8F, 0xD3, 0xC8);

    private static global::Windows.UI.Color WithAlpha(global::Windows.UI.Color color, double alpha) =>
        global::Windows.UI.Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), color.R, color.G, color.B);
}
