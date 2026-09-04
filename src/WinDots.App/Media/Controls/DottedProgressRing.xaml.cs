using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WinDots.Core.Design;

namespace WinDots.App.Media.Controls;

/// <summary>
/// A ring of evenly spaced dots showing playback progress. Dot centres come from
/// <see cref="DottedRingLayout.Centres"/>; the elapsed count from <see cref="DottedRingLayout.ElapsedDots"/>.
/// A layout (size/count/diameter) change rebuilds the ellipses; a progress change only re-fills them.
/// </summary>
public sealed partial class DottedProgressRing : UserControl
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress),
        typeof(double?),
        typeof(DottedProgressRing),
        new PropertyMetadata(null, OnFillChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size),
        typeof(double),
        typeof(DottedProgressRing),
        new PropertyMetadata(220.0, OnLayoutChanged));

    public static readonly DependencyProperty DotCountProperty = DependencyProperty.Register(
        nameof(DotCount),
        typeof(int),
        typeof(DottedProgressRing),
        new PropertyMetadata(72, OnLayoutChanged));

    public static readonly DependencyProperty DotDiameterProperty = DependencyProperty.Register(
        nameof(DotDiameter),
        typeof(double),
        typeof(DottedProgressRing),
        new PropertyMetadata(3.0, OnLayoutChanged));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush),
        typeof(Brush),
        typeof(DottedProgressRing),
        new PropertyMetadata(null, OnFillChanged));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(DottedProgressRing),
        new PropertyMetadata(null, OnFillChanged));

    private Ellipse[] _dots = Array.Empty<Ellipse>();

    public DottedProgressRing()
    {
        InitializeComponent();
        Loaded += (_, _) => Rebuild();
    }

    /// <summary>Playback fraction in <c>[0, 1]</c>; null hides the elapsed arc (all track colour).</summary>
    public double? Progress
    {
        get => (double?)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>Side length of the ring's bounding square, in logical pixels.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Number of dots around the ring.</summary>
    public int DotCount
    {
        get => (int)GetValue(DotCountProperty);
        set => SetValue(DotCountProperty, value);
    }

    /// <summary>Diameter of each dot, in logical pixels.</summary>
    public double DotDiameter
    {
        get => (double)GetValue(DotDiameterProperty);
        set => SetValue(DotDiameterProperty, value);
    }

    /// <summary>Brush for elapsed dots.</summary>
    public Brush? AccentBrush
    {
        get => (Brush?)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    /// <summary>Brush for remaining dots.</summary>
    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((DottedProgressRing)d).Rebuild();
    }

    private static void OnFillChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((DottedProgressRing)d).UpdateFills();
    }

    private void Rebuild()
    {
        double size = Size;
        int count = DotCount;
        double diameter = DotDiameter;
        if (!(size > 0) || count < 1 || !(diameter > 0))
        {
            return;
        }

        Width = size;
        Height = size;
        DotCanvas.Width = size;
        DotCanvas.Height = size;
        DotCanvas.Children.Clear();

        double centre = size / 2.0;
        double radius = centre - (diameter / 2.0);
        var centres = DottedRingLayout.Centres(centre, centre, radius, count, -90);

        _dots = new Ellipse[count];
        for (int i = 0; i < count; i++)
        {
            var dot = new Ellipse { Width = diameter, Height = diameter };
            Canvas.SetLeft(dot, centres[i].X - (diameter / 2.0));
            Canvas.SetTop(dot, centres[i].Y - (diameter / 2.0));
            _dots[i] = dot;
            DotCanvas.Children.Add(dot);
        }

        UpdateFills();
    }

    private void UpdateFills()
    {
        if (_dots.Length == 0)
        {
            return;
        }

        int elapsed = DottedRingLayout.ElapsedDots(Progress, _dots.Length);
        for (int i = 0; i < _dots.Length; i++)
        {
            _dots[i].Fill = i < elapsed ? AccentBrush : TrackBrush;
        }
    }
}
