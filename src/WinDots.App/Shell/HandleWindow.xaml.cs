using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using WinDots.Core.Contracts;

namespace WinDots.App.Shell;

/// <summary>
/// One per enabled monitor: a tiny non-activating Win32 popup at the top-centre of the work area that draws the
/// collapsed handle and forwards pointer samples to the shared <see cref="DrawerController"/> through
/// <see cref="DrawerHost"/>. It never takes focus (<c>WS_EX_NOACTIVATE</c>) and never appears in Alt+Tab or the
/// taskbar (<c>WS_EX_TOOLWINDOW</c>).
/// </summary>
public sealed partial class HandleWindow : Window
{
    // Logical geometry from _docs/03-ux-interaction-spec.md.
    private const double HitWidth = 200;
    private const double HitHeight = 12;
    private const double VisualWidth = 160;
    private const double VisualHeight = 6;
    private const double HoverWidth = 200;
    private const double HoverHeight = 8;

    private readonly DrawerHost _host;
    private readonly MonitorInfo _monitor;
    private readonly double _originYLogical;

    private Grid _root = null!;
    private bool _capturing;

    public HandleWindow(MonitorInfo monitor, DrawerHost host)
    {
        _monitor = monitor;
        _host = host;
        InitializeComponent();

        _originYLogical = (monitor.WorkArea.Y - monitor.Bounds.Y) / monitor.Scale;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ConfigurePresenter();
        // Non-activating, topmost tool window; never a taskbar or Alt+Tab entry. Only extended styles are touched:
        // the presenter already removed the frame, and editing WS_* by hand breaks WinUI's swap chain (black window).
        NativeInterop.ApplyStyles(
            hwnd,
            styleSet: 0,
            styleClear: 0,
            exSet: NativeInterop.WS_EX_TOOLWINDOW | NativeInterop.WS_EX_NOACTIVATE | NativeInterop.WS_EX_TOPMOST,
            exClear: NativeInterop.WS_EX_APPWINDOW);

        _root = (Grid)Content;
        _root.PointerPressed += OnPointerPressed;
        _root.PointerMoved += OnPointerMoved;
        _root.PointerReleased += OnPointerReleased;
        _root.PointerCanceled += OnPointerLost;
        _root.PointerCaptureLost += OnPointerLost;

        Reposition(monitor);
        AppWindow.Show(activateWindow: false);
    }

    public MonitorInfo Monitor => _monitor;

    /// <summary>Places the handle at the top-centre of the monitor's physical work area.</summary>
    public void Reposition(MonitorInfo monitor)
    {
        var scale = monitor.Scale;
        var w = (int)Math.Round(HitWidth * scale);
        var h = (int)Math.Round(HitHeight * scale);
        var x = (int)Math.Round(monitor.WorkArea.X + ((monitor.WorkArea.Width - w) / 2));
        var y = (int)Math.Round(monitor.WorkArea.Y);
        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
    }

    private void ConfigurePresenter()
    {
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

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => AnimateBar(HoverWidth, HoverHeight, brighten: true);

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => AnimateBar(VisualWidth, VisualHeight, brighten: false);

    private void AnimateBar(double width, double height, bool brighten)
    {
        Bar.Background = (Brush)Application.Current.Resources[brighten ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"];

        var duration = new Duration(TimeSpan.FromMilliseconds(120));
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var storyboard = new Storyboard();

        var widthAnim = new DoubleAnimation { To = width, Duration = duration, EnableDependentAnimation = true, EasingFunction = ease };
        Storyboard.SetTarget(widthAnim, Bar);
        Storyboard.SetTargetProperty(widthAnim, "Width");
        storyboard.Children.Add(widthAnim);

        var heightAnim = new DoubleAnimation { To = height, Duration = duration, EnableDependentAnimation = true, EasingFunction = ease };
        Storyboard.SetTarget(heightAnim, Bar);
        Storyboard.SetTargetProperty(heightAnim, "Height");
        storyboard.Children.Add(heightAnim);

        storyboard.Begin();
    }
}
