using System;
using System.Numerics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using WinDots.App.Diagnostics;
using WinDots.App.Media;
using WinDots.Core.Contracts;

namespace WinDots.App.Shell;

/// <summary>
/// The single shared drawer surface. It is moved to the active monitor on open and revealed by resizing the window
/// itself from the top edge: the window's height is <c>progress * height</c> and the content is translated up by the
/// remainder so its bottom edge stays glued to the window's bottom edge. Nothing outside the revealed strip ever
/// paints, which is what keeps the reveal clean without a transparent window (WinUI windows are opaque; a translated
/// full-height window would show its own background beneath the content). Settling motion is driven by
/// <see cref="DrawerHost"/> through <see cref="ApplyProgress"/>.
/// </summary>
public sealed partial class DrawerWindow : Window
{
    // Logical design size from _docs/03-ux-interaction-spec.md.
    public const double DesignWidth = 720;
    public const double DesignHeight = 300;

    private readonly DrawerHost _host;
    private readonly MediaViewModel _viewModel;
    private readonly nint _hwnd;

    private double _scale = 1.0;
    private double _heightLogical = DesignHeight;
    private double _originYLogical;
    private int _x;
    private int _y;
    private int _widthPx;
    private bool _capturing;
    private bool _shown;

    // Click-outside is armed only once the drawer has genuinely gained activation, so the transient
    // deactivation that can accompany opening from a global hotkey never dismisses it immediately.
    private bool _clickOutsideArmed;

    public DrawerWindow(DrawerHost host, MediaViewModel viewModel)
    {
        _host = host;
        _viewModel = viewModel;
        InitializeComponent();
        Page.Initialize(viewModel);

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ConfigurePresenter();

        // Only extended styles are touched. Clearing WS_CAPTION/WS_THICKFRAME by hand on a WinUI window breaks its
        // swap chain (black content); the presenter already removes the frame.
        NativeInterop.ApplyStyles(
            _hwnd,
            styleSet: 0,
            styleClear: 0,
            exSet: NativeInterop.WS_EX_TOOLWINDOW | NativeInterop.WS_EX_TOPMOST,
            exClear: NativeInterop.WS_EX_APPWINDOW);
        NativeInterop.SetRoundedCorners(_hwnd);

        Root.Height = _heightLogical;

        DragBand.PointerPressed += OnBandPressed;
        DragBand.PointerMoved += OnBandMoved;
        DragBand.PointerReleased += OnBandReleased;
        DragBand.PointerCanceled += OnBandLost;
        DragBand.PointerCaptureLost += OnBandLost;

        // Escape closes when the drawer has focus; losing activation to another app is a click-outside dismiss.
        Root.KeyDown += OnRootKeyDown;
        Activated += OnActivated;
    }

    public nint Hwnd => _hwnd;

    public double HeightLogical => _heightLogical;

    private void ConfigurePresenter()
    {
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

    /// <summary>Computes the drawer's placement at the top-centre of the monitor's work area (physical pixels).</summary>
    public void MoveToMonitor(MonitorInfo monitor)
    {
        _scale = monitor.Scale;
        _originYLogical = (monitor.WorkArea.Y - monitor.Bounds.Y) / _scale;

        // Spec geometry: clamp to 90 % of the work-area width and 60 % of its height.
        var clampedWidthLogical = Math.Min(DesignWidth, (monitor.WorkArea.Width / _scale) * 0.9);
        _heightLogical = Math.Min(DesignHeight, (monitor.WorkArea.Height / _scale) * 0.6);
        Root.Height = _heightLogical;

        _widthPx = (int)Math.Round(clampedWidthLogical * _scale);
        _x = (int)Math.Round(monitor.WorkArea.X + ((monitor.WorkArea.Width - _widthPx) / 2));
        _y = (int)Math.Round(monitor.WorkArea.Y);
        AppWindow.MoveAndResize(new RectInt32(_x, _y, _widthPx, 1));
    }

    /// <summary>Shows the window without activating it, positioned at the given progress.</summary>
    public void ShowAtProgress(double progress)
    {
        _clickOutsideArmed = false;
        ApplyProgress(progress);
        if (!_shown)
        {
            AppWindow.Show(activateWindow: false);
            _shown = true;
            _viewModel.IsDrawerOpen = true;
        }
    }

    /// <summary>
    /// Reveals <paramref name="progress"/> of the drawer: window height = progress * height (min 1 px so the window
    /// stays valid), content translated so its bottom edge sits on the window's bottom edge.
    /// </summary>
    public void ApplyProgress(double progress)
    {
        var p = Math.Clamp(progress, 0, 1.2);
        var revealedLogical = Math.Min(p, 1) * _heightLogical;
        var heightPx = Math.Max(1, (int)Math.Round(revealedLogical * _scale));

        // Beyond 1 the drawer rubber-bands: keep the window full height and nudge the content down slightly.
        var overshootLogical = p > 1 ? (p - 1) * _heightLogical * 0.5 : 0;
        Root.Translation = new Vector3(0f, (float)(revealedLogical - _heightLogical + overshootLogical), 0f);
        Root.Opacity = 1;

        var current = AppWindow.Size;
        if (current.Height != heightPx || current.Width != _widthPx)
        {
            AppWindow.MoveAndResize(new RectInt32(_x, _y, _widthPx, heightPx));
        }
    }

    public void HideWindow()
    {
        if (_shown)
        {
            AppWindow.Hide();
            _shown = false;
            _viewModel.IsDrawerOpen = false;
        }
    }

    /// <summary>
    /// Brings the drawer to the foreground so Escape and the tab order work. <c>Window.Activate</c> alone does not
    /// reliably take foreground for a window first shown non-activating, so we also go through Win32, which is
    /// permitted here because our handle window received the last input.
    /// </summary>
    public void ActivateForKeyboard()
    {
        Activate();
        var ok = NativeInterop.ForceForeground(_hwnd);
        ShellLog.Write($"drawer activate: foreground={(ok ? "drawer" : "other")}");
        if (ok)
        {
            _clickOutsideArmed = true;
        }

        // Initial keyboard focus lands on play/pause once the drawer has the foreground.
        Page.FocusDefault();
    }

    private PointerSample ToSample(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(DragBand);
        var y = _originYLogical + point.Position.Y;
        return new PointerSample(point.Position.X, y, TimeSpan.FromMicroseconds(point.Timestamp));
    }

    private void OnBandPressed(object sender, PointerRoutedEventArgs e)
    {
        _capturing = DragBand.CapturePointer(e.Pointer);
        _host.Controller.PointerDown(ToSample(e));
        _host.OnDragSampleFed();
        e.Handled = true;
    }

    private void OnBandMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_capturing)
        {
            return;
        }

        _host.Controller.PointerMove(ToSample(e));
        _host.OnDragSampleFed();
    }

    private void OnBandReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_capturing)
        {
            return;
        }

        _capturing = false;
        _host.Controller.PointerUp(ToSample(e));
        DragBand.ReleasePointerCaptures();
        _host.OnDragSampleFed();
        e.Handled = true;
    }

    private void OnBandLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_capturing)
        {
            return;
        }

        _capturing = false;
        _host.Controller.PointerUp(ToSample(e));
        _host.OnDragSampleFed();
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == global::Windows.System.VirtualKey.Escape)
        {
            _host.Controller.Dismiss(DismissReason.Escape);
            e.Handled = true;
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState != WindowActivationState.Deactivated)
        {
            // The drawer has genuinely taken focus; from now on a loss of activation is a real click-outside.
            _clickOutsideArmed = true;
            return;
        }

        // Ignore the transient deactivation that can accompany opening before the drawer ever gained focus.
        if (!_clickOutsideArmed)
        {
            return;
        }

        // Only an open drawer dismisses on click-outside; ignore the deactivation that accompanies our own close.
        if (_host.Controller.State != DrawerState.Open)
        {
            return;
        }

        // Activation moving to one of our own windows (a handle or the inspector) must not dismiss the drawer.
        if (NativeInterop.IsForegroundOwnedByThisProcess())
        {
            return;
        }

        ShellLog.Write("drawer deactivated by another app: click-outside dismiss");
        _host.Controller.Dismiss(DismissReason.ClickOutside);
    }
}
