using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using Windows.UI;
using WinDots.Core.Contracts;

namespace WinDots.App.Shell;

/// <summary>
/// One per enabled monitor: a tiny non-activating Win32 popup at the top-centre of the work area that draws the
/// collapsed handle and forwards pointer samples to the shared <see cref="DrawerController"/> through
/// <see cref="DrawerHost"/>. It never takes focus (<c>WS_EX_NOACTIVATE</c>) and never appears in Alt+Tab or the
/// taskbar (<c>WS_EX_TOOLWINDOW</c>).
///
/// The window IS the pill: it is sized to the visual and DWM rounds its corners (anti-aliased, composited), so the
/// desktop shows transparently around it - no aliased <c>SetWindowRgn</c>. The pill grows smoothly on hover via an
/// exponentially-smoothed bounds tween, its fill brightens, and at rest it breathes with a gentle colour pulse.
/// </summary>
public sealed partial class HandleWindow : Window
{
    // Logical geometry: a small, thin iPhone-home-indicator-style pill. The window IS the pill (DWM rounds its
    // corners, anti-aliased); it is NOT region-clipped, because a Win32 window region stops WinUI routing pointer
    // input to the content. Its height is therefore floored by what WinUI will render and hit-test. Hover grows it
    // and blooms the accent; it breathes at rest.
    private const double VisualWidth = 112;
    private const double VisualHeight = 12;
    private const double HoverWidth = 150;
    private const double HoverHeight = 16;

    // Bounds tween: exponential smoothing toward the target size at ~80 fps; snaps when within this many logical px.
    private const double TweenAlpha = 0.28;
    private const double TweenSnapPx = 0.3;

    private readonly DrawerHost _host;
    private readonly bool _reducedMotion;
    private readonly nint _hwnd;
    private readonly DispatcherQueueTimer _tween;

    private MonitorInfo _monitor;
    private double _originYLogical;
    private int _offsetPercent;
    private double _scale;

    private Grid _root = null!;
    private bool _capturing;
    private bool _hovering;

    // Previous physical window rect, so a shrink can invalidate the strip it used to cover (clears the DWM residue).
    private int _prevX, _prevY, _prevW, _prevH;

    // Live (fractional) logical size, eased toward the target each tween tick.
    private double _currentW = VisualWidth;
    private double _currentH = VisualHeight;
    private double _targetW = VisualWidth;
    private double _targetH = VisualHeight;

    private Color _restColor;
    private Color _hoverColor;
    private Color _breatheColor;
    private Storyboard? _breath;

    public HandleWindow(MonitorInfo monitor, DrawerHost host, int offsetPercent = 50, bool reducedMotion = false)
    {
        _monitor = monitor;
        _host = host;
        _offsetPercent = Math.Clamp(offsetPercent, 0, 100);
        _reducedMotion = reducedMotion;
        _scale = monitor.Scale;
        InitializeComponent();

        _originYLogical = (monitor.WorkArea.Y - monitor.Bounds.Y) / monitor.Scale;

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ConfigurePresenter();
        // Non-activating, topmost tool window; never a taskbar or Alt+Tab entry. Only extended styles are touched:
        // the presenter already removed the frame, and editing WS_* by hand breaks WinUI's swap chain (black window).
        NativeInterop.ApplyStyles(
            _hwnd,
            styleSet: 0,
            styleClear: 0,
            exSet: NativeInterop.WS_EX_TOOLWINDOW | NativeInterop.WS_EX_NOACTIVATE | NativeInterop.WS_EX_TOPMOST,
            exClear: NativeInterop.WS_EX_APPWINDOW);
        // DWM rounds the pill's corners (anti-aliased, composited); persists across resizes.
        NativeInterop.SetRoundedCorners(_hwnd);
        ResolveColors();
        FillBrush.Color = _restColor;

        _root = (Grid)Content;
        // Designate a zero-height draggable region so the whole capsule is content (no caption interception).
        SetTitleBar(TitleBarRegion);
        _root.PointerPressed += OnPointerPressed;
        _root.PointerMoved += OnPointerMoved;
        _root.PointerReleased += OnPointerReleased;
        _root.PointerCanceled += OnPointerLost;
        _root.PointerCaptureLost += OnPointerLost;
        _root.ActualThemeChanged += (_, _) => { ResolveColors(); if (!_hovering) { StartBreathing(); } };

        _tween = DispatcherQueue.CreateTimer();
        _tween.Interval = TimeSpan.FromMilliseconds(12);
        _tween.IsRepeating = true;
        _tween.Tick += OnTweenTick;

        SizeToPill(VisualWidth, VisualHeight);
        AppWindow.Show(activateWindow: false);
        StartBreathing();
    }

    public MonitorInfo Monitor => _monitor;

    /// <summary>Places the handle at the top-centre of the monitor's physical work area at its current size.</summary>
    public void Reposition(MonitorInfo monitor)
    {
        _monitor = monitor;
        _scale = monitor.Scale;
        _originYLogical = (monitor.WorkArea.Y - monitor.Bounds.Y) / monitor.Scale;
        SizeToPill(_currentW, _currentH);
    }

    // Positions and sizes the window to the given logical pill size, re-centred on the monitor's top edge, and clips
    // it to a thin stadium region. The region (not DWM rounding) defines the silhouette because a plain WinUI window
    // has a minimum height and an opaque background that would otherwise show a chunky rounded box with a light band;
    // the region clips both away so only the thin pill shows. At ~5-8 px tall the ~3 px corner radius makes GDI
    // region aliasing imperceptible, and the smooth grow/brighten/breathing animation carries the polish.
    private void SizeToPill(double logicalWidth, double logicalHeight)
    {
        _currentW = logicalWidth;
        _currentH = logicalHeight;
        var w = (int)Math.Round(logicalWidth * _scale);
        var h = (int)Math.Round(logicalHeight * _scale);
        var x = (int)Math.Round(_monitor.WorkArea.X + ((_monitor.WorkArea.Width - w) * (_offsetPercent / 100.0)));
        var y = (int)Math.Round(_monitor.WorkArea.Y);
        // The window is the pill; DWM (set once) keeps the corners rounded across resizes. The Border also carries a
        // matching CornerRadius so the fill itself is rounded even before DWM composites.
        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
        Bar.CornerRadius = new CornerRadius(logicalHeight / 2.0);

        // If the window shrank, invalidate the strip it used to cover so DWM/underlying windows repaint it now
        // instead of leaving a ~1 s edge residue.
        if (_prevW > 0 && (w < _prevW || h < _prevH))
        {
            int left = Math.Min(x, _prevX);
            int top = Math.Min(y, _prevY);
            int right = Math.Max(x + w, _prevX + _prevW);
            int bottom = Math.Max(y + h, _prevY + _prevH);
            NativeInterop.InvalidateScreenRect(left, top, right, bottom);
        }

        _prevX = x;
        _prevY = y;
        _prevW = w;
        _prevH = h;
    }

    private void ConfigurePresenter()
    {
        // Content must reach the very top edge (no reserved title-bar band). A zero-height title bar (SetTitleBar in
        // the ctor) then leaves no caption region, so pointer input reaches the pill instead of being eaten as a
        // window-drag caption hit.
        ExtendsContentIntoTitleBar = true;
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
    }

    private PointerSample ToSample(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_root);
        var y = _originYLogical + point.Position.Y;
        // PointerPoint.Timestamp is monotonic microseconds since boot.
        return new PointerSample(point.Position.X, y, TimeSpan.FromMicroseconds(point.Timestamp));
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // If the drawer is open on another monitor this press means "bring it here": the host closes it there and
        // reopens it on this monitor once the close finishes. The controller is settling meanwhile, so feeding it
        // this press would be dropped anyway.
        if (_host.TryRequestMonitorSwitch(_monitor))
        {
            e.Handled = true;
            return;
        }

        _host.SetActiveMonitor(_monitor);
        _capturing = _root.CapturePointer(e.Pointer);
        _host.Controller.PointerDown(ToSample(e));
        _host.OnDragSampleFed();
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_capturing)
        {
            return;
        }

        _host.Controller.PointerMove(ToSample(e));
        _host.OnDragSampleFed();
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_capturing)
        {
            return;
        }

        _capturing = false;
        _host.Controller.PointerUp(ToSample(e));
        _root.ReleasePointerCaptures();
        _host.OnDragSampleFed();
        e.Handled = true;
    }

    private void OnPointerLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_capturing)
        {
            return;
        }

        _capturing = false;
        _host.Controller.PointerUp(ToSample(e));
        _host.OnDragSampleFed();
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => SetHover(true);

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => SetHover(false);

    private void SetHover(bool hovering)
    {
        if (_hovering == hovering)
        {
            return;
        }

        _hovering = hovering;
        _targetW = hovering ? HoverWidth : VisualWidth;
        _targetH = hovering ? HoverHeight : VisualHeight;

        if (_reducedMotion)
        {
            StopBreathing();
            FillBrush.Color = hovering ? _hoverColor : _restColor;
            SizeToPill(_targetW, _targetH);
            return;
        }

        if (hovering)
        {
            StopBreathing();
            AnimateFill(_hoverColor, 140, resumeBreathing: false);
        }
        else
        {
            AnimateFill(_restColor, 160, resumeBreathing: true);
        }

        _tween.Start();
    }

    // Exponentially eases the live size toward the target, re-centring each tick; stops once within the snap radius.
    private void OnTweenTick(DispatcherQueueTimer sender, object args)
    {
        var w = _currentW + ((_targetW - _currentW) * TweenAlpha);
        var h = _currentH + ((_targetH - _currentH) * TweenAlpha);

        if (Math.Abs(_targetW - w) < TweenSnapPx && Math.Abs(_targetH - h) < TweenSnapPx)
        {
            w = _targetW;
            h = _targetH;
            _tween.Stop();
        }

        SizeToPill(w, h);
    }

    private void ResolveColors()
    {
        // Rest: a dim neutral bar. Hover: blooms to the brand accent teal (matches the drawer), so hovering reads
        // instantly. Breathe: a faint accent-tinged shimmer at rest, never a flash.
        bool dark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        _restColor = dark ? Color.FromArgb(0xFF, 0x6B, 0x74, 0x78) : Color.FromArgb(0xFF, 0x9A, 0xA3, 0xA7);
        var accent = dark ? Color.FromArgb(0xFF, 0x8F, 0xD3, 0xC8) : Color.FromArgb(0xFF, 0x1F, 0x7A, 0x6E);
        _hoverColor = accent;
        _breatheColor = Lerp(_restColor, accent, 0.30);
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + ((b.A - a.A) * t)),
        (byte)Math.Round(a.R + ((b.R - a.R) * t)),
        (byte)Math.Round(a.G + ((b.G - a.G) * t)),
        (byte)Math.Round(a.B + ((b.B - a.B) * t)));

    private void AnimateFill(Color to, int durationMs, bool resumeBreathing)
    {
        var animation = new ColorAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, FillBrush);
        Storyboard.SetTargetProperty(animation, "Color");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        if (resumeBreathing)
        {
            storyboard.Completed += (_, _) => { if (!_hovering) { StartBreathing(); } };
        }

        storyboard.Begin();
    }

    // A slow, autoreversing colour pulse between the rest tone and a slightly brighter one, so the collapsed handle
    // reads as "alive" without drawing attention. Disabled under reduced motion.
    private void StartBreathing()
    {
        if (_reducedMotion || _hovering)
        {
            return;
        }

        StopBreathing();
        FillBrush.Color = _restColor;
        var animation = new ColorAnimation
        {
            From = _restColor,
            To = _breatheColor,
            Duration = new Duration(TimeSpan.FromMilliseconds(2600)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(animation, FillBrush);
        Storyboard.SetTargetProperty(animation, "Color");
        _breath = new Storyboard();
        _breath.Children.Add(animation);
        _breath.Begin();
    }

    private void StopBreathing()
    {
        _breath?.Stop();
        _breath = null;
    }
}
