using System;
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

    private Path? _front;

    public BlobArtwork()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RebuildGeometry();
            ApplyImage(ImageSource, animate: false);
        };
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
        control.ApplyImage(e.NewValue as ImageSource, animate: control.IsLoaded);
    }

    private static void OnGeometryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BlobArtwork)d).RebuildGeometry();
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
        BlobPath blob = BlobGeometry.Create(size, 8, amplitude);
        Geometry geometry = ToGeometry(blob);

        PlaceholderShape.Data = geometry;
        PathA.Data = ToGeometry(blob);
        PathB.Data = ToGeometry(blob);

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
