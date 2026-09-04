using System;
using System.Numerics;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System.Power;
using Windows.UI.ViewManagement;
using WinRT;
using WinDots.App.Diagnostics;
using WinDots.App.Media;
using WinDots.Core.Contracts;
using WinDots.Core.Settings;

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
    // Logical design size from _docs/03-ux-interaction-spec.md. Sized to comfortably fit the media page's artwork,
    // metadata, transport, volume row and lyrics column without crowding.
    public const double DesignWidth = 820;

    // The tab strip sits on top of the active page; the drawer's total design height is TabStripHeight plus the active
    // page's design height. The Media page keeps its historical 344 px so it looks and behaves exactly as before.
    public const double TabStripHeight = 72;
    private const double MediaPageHeight = 344;

    // Default = Media (the landing tab): 72 (tabs) + 344 (media). Used as the gesture-math fallback height.
    public const double DesignHeight = TabStripHeight + MediaPageHeight;

    // Per-tab page (content, excluding the tab strip) design heights.
    private static double PageDesignHeight(DashboardTab tab) => tab switch
    {
        DashboardTab.Media => MediaPageHeight,
        DashboardTab.Dashboard => 486,
        DashboardTab.Performance => 220,
        DashboardTab.Weather => 220,
        _ => MediaPageHeight,
    };

    private readonly DrawerHost _host;
    private readonly MediaViewModel _viewModel;
    private readonly nint _hwnd;
    private OverlappedPresenter? _presenter;

    private double _scale = 1.0;
    private double _heightLogical = DesignHeight;
    private double _workAreaHeightLogical = double.PositiveInfinity;
    private DashboardTab _currentTab = DashboardTab.Media;
    private double _originYLogical;
    private int _x;
    private int _y;
    private int _widthPx;
    private bool _capturing;
    private bool _shown;

    // Upward drag-to-close on the tab strip: only after the pointer moves up past this slop do we capture and begin
    // feeding the controller, so a plain tap still activates a tab.
    private const double DragSlop = 8;
    private bool _stripPressed;
    private bool _stripDragging;
    private double _stripStartY;

    // Click-outside is armed only once the drawer has genuinely gained activation, so the transient
    // deactivation that can accompany opening from a global hotkey never dismisses it immediately.
    private bool _clickOutsideArmed;

    // Acrylic backdrop (_docs/04-visual-design.md). The controller is created lazily when acrylic is chosen and
    // the environment allows it; otherwise the drawer paints an opaque Surface. Disposed on Close.
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfig;
    private DispatcherQueueController? _dispatcherQueueController;
    private Backdrop _backdropSetting = Backdrop.Acrylic;
    private bool _highContrast;
    private bool _acrylicActive;

    public DrawerWindow(DrawerHost host, MediaViewModel viewModel, ISystemMetricsProvider metrics, DashboardTab initialTab, int sampleIntervalMs)
    {
        _host = host;
        _viewModel = viewModel;
        InitializeComponent();
        Page.Initialize(viewModel);

        // Dashboard wiring: the page shares the metrics provider and the media view-model, so the mini media card and
        // the Media tab read one source. The Enable affordance on the weather placeholder routes to the host (settings);
        // the consent line is primed here and refreshed on settings changes via RefreshWeatherConsent.
        DashPage.Initialize(metrics, viewModel, sampleIntervalMs);
        DashPage.WeatherEnableRequested += (_, _) => _host.RequestEnableWeather();
        DashPage.SetWeatherConsent(_host.WeatherConsentGranted);

        // The tab strip uses the same per-artwork palette accent the media UI exposes (same brush instance, live-tinted).
        TabStrip.AccentBrush = viewModel.AccentBrush;
        TabStrip.SelectionChanged += OnTabStripSelectionChanged;
        _currentTab = initialTab;
        TabStrip.Selected = initialTab;
        ApplyActivePage(initialTab);

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

        _heightLogical = TabStripHeight + PageDesignHeight(initialTab);
        Root.Height = _heightLogical;

        // Drag-to-close lives on the tab strip. handledEventsToo:true so we still see the pointer even after a tab
        // button marks the press handled; the movement threshold in the handlers keeps taps working.
        TabStrip.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnBandPressed), handledEventsToo: true);
        TabStrip.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnBandMoved), handledEventsToo: true);
        TabStrip.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnBandReleased), handledEventsToo: true);
        TabStrip.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnBandLost), handledEventsToo: true);
        TabStrip.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnBandLost), handledEventsToo: true);

        // Escape closes when the drawer has focus; losing activation to another app is a click-outside dismiss.
        Root.KeyDown += OnRootKeyDown;
        Root.PointerMoved += OnRootActivity;
        Activated += OnActivated;

        // Re-tint and re-theme the acrylic when the drawer's resolved theme changes (system light/dark switch).
        Page.ActualThemeChanged += OnPageThemeChanged;
        Closed += OnClosed;
    }

    public nint Hwnd => _hwnd;

    public double HeightLogical => _heightLogical;

    /// <summary>The drawer's resolved theme, used to key the artwork palette (dark vs light contrast floors).</summary>
    public bool IsDarkTheme => Page.ActualTheme == ElementTheme.Dark;

    // --- Acrylic backdrop ---

    /// <summary>
    /// Applies <c>appearance.backdrop</c>. Enables a <see cref="DesktopAcrylicController"/> tinted with the Surface
    /// token at 70 % / luminosity 0.9 only when acrylic is chosen, high contrast is off, and the environment allows
    /// it (advanced effects on, not on battery saver, not over Remote Desktop). Any failure falls back to an opaque
    /// Surface. The reason is logged as <c>backdrop: acrylic</c> or <c>backdrop: opaque (&lt;reason&gt;)</c>.
    /// </summary>
    public void ConfigureBackdrop(Backdrop backdrop, bool highContrast)
    {
        _backdropSetting = backdrop;
        _highContrast = highContrast;
        ApplyBackdrop();
    }

    private void ApplyBackdrop()
    {
        string? reason = BackdropFallbackReason();
        if (reason is null)
        {
            try
            {
                EnableAcrylic();
                SetSurfacesTransparent(true);
                _acrylicActive = true;
                ShellLog.Write("backdrop: acrylic");
                return;
            }
            catch (Exception ex)
            {
                reason = $"controller failed ({ex.GetType().Name})";
            }
        }

        DisableAcrylic();
        SetSurfacesTransparent(false);
        _acrylicActive = false;
        ShellLog.Write($"backdrop: opaque ({reason})");
    }

    /// <summary>Returns null when acrylic should be used, or a short reason string for the opaque fallback.</summary>
    private string? BackdropFallbackReason()
    {
        if (_backdropSetting != Backdrop.Acrylic)
        {
            return "setting=opaque";
        }

        if (_highContrast)
        {
            return "high-contrast";
        }

        if (!DesktopAcrylicController.IsSupported())
        {
            return "unsupported";
        }

        if (!new UISettings().AdvancedEffectsEnabled)
        {
            return "advanced-effects-off";
        }

        if (PowerManager.EnergySaverStatus == EnergySaverStatus.On)
        {
            return "energy-saver";
        }

        if (NativeInterop.IsRemoteSession())
        {
            return "remote-session";
        }

        return null;
    }

    private void EnableAcrylic()
    {
        EnsureDispatcherQueueController();

        _backdropConfig ??= new SystemBackdropConfiguration { IsInputActive = true };
        _backdropConfig.Theme = Page.ActualTheme == ElementTheme.Light ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark;

        if (_acrylicController is null)
        {
            _acrylicController = new DesktopAcrylicController();
            _acrylicController.SetSystemBackdropConfiguration(_backdropConfig);
            _acrylicController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
        }

        global::Windows.UI.Color surface = SurfaceColor();
        _acrylicController.TintColor = surface;
        _acrylicController.FallbackColor = surface;
        _acrylicController.TintOpacity = 0.70f;
        _acrylicController.LuminosityOpacity = 0.90f;
    }

    private void DisableAcrylic()
    {
        if (_acrylicController is not null)
        {
            _acrylicController.Dispose();
            _acrylicController = null;
        }
    }

    /// <summary>Toggles the drawer surfaces between transparent (acrylic shows through) and the opaque Surface token.</summary>
    private void SetSurfacesTransparent(bool transparent)
    {
        if (transparent)
        {
            RootHost.Background = new SolidColorBrush(global::Microsoft.UI.Colors.Transparent);
            Root.Background = new SolidColorBrush(global::Microsoft.UI.Colors.Transparent);
            Page.Background = new SolidColorBrush(global::Microsoft.UI.Colors.Transparent);
        }
        else
        {
            var surface = new SolidColorBrush(SurfaceColor());
            RootHost.Background = surface;
            Root.Background = new SolidColorBrush(SurfaceColor());
            Page.Background = new SolidColorBrush(SurfaceColor());
        }
    }

    /// <summary>The Surface token colour resolved for the drawer's current theme (from Tokens.xaml, theme-aware).</summary>
    private global::Windows.UI.Color SurfaceColor()
    {
        string key = Page.ActualTheme == ElementTheme.Light ? "Light" : "Default";
        foreach (ResourceDictionary md in Application.Current.Resources.MergedDictionaries)
        {
            if (md.ThemeDictionaries.TryGetValue(key, out object? themed) &&
                themed is ResourceDictionary dict &&
                dict.TryGetValue("WdSurfaceBrush", out object? brush) &&
                brush is SolidColorBrush scb)
            {
                return scb.Color;
            }
        }

        // Token fallback (dark Surface) if the dictionary cannot be resolved.
        return global::Windows.UI.Color.FromArgb(0xFF, 0x10, 0x14, 0x16);
    }

    private void EnsureDispatcherQueueController()
    {
        // The WinUI UI thread already owns a DispatcherQueue, which is all the acrylic controller needs. Only create
        // one on the rare thread that lacks it (defensive; not expected in this single-UI-thread app).
        if (DispatcherQueue.GetForCurrentThread() is not null || _dispatcherQueueController is not null)
        {
            return;
        }

        _dispatcherQueueController = DispatcherQueueController.CreateOnCurrentThread();
    }

    private void OnPageThemeChanged(FrameworkElement sender, object args)
    {
        if (_acrylicActive)
        {
            ApplyBackdrop();
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Page.ActualThemeChanged -= OnPageThemeChanged;
        DisableAcrylic();
        if (_dispatcherQueueController is not null)
        {
            _ = _dispatcherQueueController.ShutdownQueueAsync();
            _dispatcherQueueController = null;
        }
    }

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
        _presenter = presenter;
    }

    /// <summary>Forwards the idle-motion / high-contrast appearance settings to the media page.</summary>
    public void ConfigureVisuals(bool backgroundBlobs, bool reducedMotion, bool highContrast) =>
        Page.SetVisualEffects(backgroundBlobs, reducedMotion, highContrast);

    /// <summary>Applies <c>drawer.alwaysOnTop</c>: keeps the open drawer topmost when true.</summary>
    public void SetAlwaysOnTop(bool value)
    {
        if (_presenter is not null)
        {
            _presenter.IsAlwaysOnTop = value;
        }
    }

    /// <summary>Computes the drawer's placement at the top-centre of the monitor's work area (physical pixels).</summary>
    public void MoveToMonitor(MonitorInfo monitor)
    {
        _scale = monitor.Scale;
        _originYLogical = (monitor.WorkArea.Y - monitor.Bounds.Y) / _scale;
        _workAreaHeightLogical = monitor.WorkArea.Height / _scale;

        // Spec geometry: clamp to 90 % of the work-area width and 60 % of its height. Height follows the active tab.
        var clampedWidthLogical = Math.Min(DesignWidth, (monitor.WorkArea.Width / _scale) * 0.9);
        _heightLogical = DesignHeightForTab(_currentTab);
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
            Page.SetDrawerVisible(true);
            UpdateDashboardActive();
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
            Page.SetDrawerVisible(false);
            UpdateDashboardActive();
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

        // Initial keyboard focus lands on play/pause on the Media tab; elsewhere on the tab strip so Ctrl+Tab and
        // arrow navigation have a starting point.
        if (_currentTab == DashboardTab.Media)
        {
            Page.FocusDefault();
        }
        else if (!TabStrip.FocusSelected())
        {
            // The selected tab button could not take focus (rare); fall back to the media page's default so Escape /
            // Ctrl+Tab still have a focused element to bubble from.
            Page.FocusDefault();
        }
    }

    private PointerSample ToSample(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(TabStrip);
        var y = _originYLogical + point.Position.Y;
        return new PointerSample(point.Position.X, y, TimeSpan.FromMicroseconds(point.Timestamp));
    }

    // Pressing the strip does not immediately capture or start a drag; a tab click must still fire. Only when the
    // pointer moves up past DragSlop do we capture and begin feeding the controller the close gesture.
    private void OnBandPressed(object sender, PointerRoutedEventArgs e)
    {
        _stripPressed = true;
        _stripDragging = false;
        _capturing = false;
        _stripStartY = e.GetCurrentPoint(TabStrip).Position.Y;
    }

    private void OnBandMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_stripPressed)
        {
            return;
        }

        if (!_stripDragging)
        {
            var y = e.GetCurrentPoint(TabStrip).Position.Y;
            if (_stripStartY - y <= DragSlop)
            {
                return;
            }

            _stripDragging = true;
            _capturing = TabStrip.CapturePointer(e.Pointer);
            _host.Controller.PointerDown(ToSample(e));
            _host.OnDragSampleFed();
            return;
        }

        _host.Controller.PointerMove(ToSample(e));
        _host.OnDragSampleFed();
    }

    private void OnBandReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_stripDragging)
        {
            _host.Controller.PointerUp(ToSample(e));
            TabStrip.ReleasePointerCaptures();
            _host.OnDragSampleFed();
            e.Handled = true;
        }

        _stripPressed = false;
        _stripDragging = false;
        _capturing = false;
    }

    private void OnBandLost(object sender, PointerRoutedEventArgs e)
    {
        if (_stripDragging)
        {
            _host.Controller.PointerUp(ToSample(e));
            _host.OnDragSampleFed();
        }

        _stripPressed = false;
        _stripDragging = false;
        _capturing = false;
    }

    // --- Tab strip / per-tab sizing ---

    /// <summary>The drawer's currently selected tab.</summary>
    public DashboardTab CurrentTab => _currentTab;

    /// <summary>
    /// The full drawer design height for a tab: the fixed tab strip plus the page, where only the page portion is
    /// clamped to 60 % of the work area. Clamping the page (not the strip + page) keeps the media area at its historical
    /// 344 px on constrained/high-scale displays instead of shrinking it by the strip's 72 px.
    /// </summary>
    public double DesignHeightForTab(DashboardTab tab)
    {
        double pageCap = (_workAreaHeightLogical * 0.6) - TabStripHeight;
        double page = double.IsFinite(pageCap) ? Math.Min(PageDesignHeight(tab), Math.Max(0, pageCap)) : PageDesignHeight(tab);
        return TabStripHeight + page;
    }

    /// <summary>Sets the revealed design height and re-applies the current reveal so the window resizes to match.</summary>
    public void SetHeightLogical(double heightLogical)
    {
        _heightLogical = heightLogical;
        Root.Height = _heightLogical;
        if (_shown)
        {
            ApplyProgress(Math.Clamp(_host.Controller.Progress, 0, 1));
        }
    }

    // Shows only the selected tab's page, updates the weather consent line, and (while closed) snaps the height. While
    // open the height is animated by the host, so it is left untouched here.
    private void ApplyActivePage(DashboardTab tab)
    {
        Page.Visibility = tab == DashboardTab.Media ? Visibility.Visible : Visibility.Collapsed;
        DashPage.Visibility = tab == DashboardTab.Dashboard ? Visibility.Visible : Visibility.Collapsed;
        PerfPage.Visibility = tab == DashboardTab.Performance ? Visibility.Visible : Visibility.Collapsed;
        WeatherPageView.Visibility = tab == DashboardTab.Weather ? Visibility.Visible : Visibility.Collapsed;

        if (tab == DashboardTab.Weather)
        {
            WeatherPageView.SetConsent(_host.WeatherConsentGranted);
        }

        UpdateDashboardActive();

        if (!_shown)
        {
            _heightLogical = DesignHeightForTab(tab);
            Root.Height = _heightLogical;
        }
    }

    /// <summary>Updates the Weather placeholder's consent line when the setting changes. No network access.</summary>
    public void RefreshWeatherConsent(bool consentGranted)
    {
        DashPage.SetWeatherConsent(consentGranted);
        if (_currentTab == DashboardTab.Weather)
        {
            WeatherPageView.SetConsent(consentGranted);
        }
    }

    /// <summary>Forwards a live change to <c>performance.sampleIntervalMs</c> to the Dashboard's metrics sampler.</summary>
    public void UpdateDashboardSampleInterval(int sampleIntervalMs) => DashPage.UpdateSampleInterval(sampleIntervalMs);

    // The Dashboard's timers run only while its tab is active and the drawer is shown; this mirrors the media
    // timeline timer's gating so nothing samples the system in the background.
    private void UpdateDashboardActive() => DashPage.SetActive(_shown && _currentTab == DashboardTab.Dashboard);

    private void OnTabStripSelectionChanged(object? sender, DashboardTab tab)
    {
        _currentTab = tab;
        ApplyActivePage(tab);
        _host.OnTabSelected(tab);
    }

    /// <summary>Applies a tab change originating outside the drawer (e.g. a settings sync). Does not re-persist.</summary>
    public void SelectTabExternally(DashboardTab tab)
    {
        if (tab == _currentTab)
        {
            return;
        }

        _currentTab = tab;
        TabStrip.Selected = tab;
        ApplyActivePage(tab);
        _host.OnTabSelected(tab, persist: false);
    }

    private void OnRootActivity(object sender, PointerRoutedEventArgs e) => _host.OnDrawerActivity();

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        _host.OnDrawerActivity();
        if (e.Key == global::Windows.System.VirtualKey.Escape)
        {
            _host.Controller.Dismiss(DismissReason.Escape);
            e.Handled = true;
            return;
        }

        // Ctrl+Tab / Ctrl+Shift+Tab cycle tabs while the drawer is open (no global input; handled here).
        if (e.Key == global::Windows.System.VirtualKey.Tab && IsCtrlDown())
        {
            TabStrip.CycleNext(backward: IsShiftDown());
            e.Handled = true;
        }
    }

    private static bool IsCtrlDown() => IsKeyDown(global::Windows.System.VirtualKey.Control);

    private static bool IsShiftDown() => IsKeyDown(global::Windows.System.VirtualKey.Shift);

    private static bool IsKeyDown(global::Windows.System.VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (_backdropConfig is not null)
        {
            _backdropConfig.IsInputActive = e.WindowActivationState != WindowActivationState.Deactivated;
        }

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
