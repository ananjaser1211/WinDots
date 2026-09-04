using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Microsoft.UI.Dispatching;
using Windows.Storage;
using Windows.UI.ViewManagement;
using WinDots.App.Diagnostics;
using WinDots.App.Media;
using WinDots.Core.Contracts;
using WinDots.Core.Drawer;
using WinDots.Core.Media;
using WinDots.Core.Settings;

namespace WinDots.App.Shell;

/// <summary>
/// Owns the shared <see cref="DrawerController"/>, one <see cref="HandleWindow"/> per enabled monitor, the single
/// <see cref="DrawerWindow"/>, and the record of which monitor the drawer is on. Everything here runs on the UI
/// thread; only provider and monitor events cross threads and they are marshalled here. Settling motion is a
/// <see cref="SpringMotion"/> stepped by a UI-thread timer because the reveal resizes the window itself.
/// </summary>
public sealed class DrawerHost
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(8);
    private static readonly TimeSpan ReducedMotionDuration = TimeSpan.FromMilliseconds(150);

    private readonly IMediaSessionProvider _provider;
    private readonly IMonitorService _monitors;
    private readonly DispatcherQueue _dispatcher;
    private readonly ISettingsStore _settings;
    private readonly DrawerController _controller;
    private readonly SessionCoordinator _coordinator;
    private readonly ArtworkCache _artworkCache;
    private readonly MediaViewModel _viewModel;
    private readonly DrawerWindow _drawer;
    private readonly List<HandleWindow> _handles = new();
    private readonly DispatcherQueueTimer _frameTimer;
    private readonly DispatcherQueueTimer _autoHideTimer;
    private readonly SpringMotion _spring = new() { PositionTolerance = 0.5, VelocityTolerance = 2 };

    // Built-in player aliases; user aliases from settings are merged on top of these.
    private static readonly IReadOnlyDictionary<string, string> DefaultAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Chrome"] = "Chrome",
            ["msedge"] = "Microsoft Edge",
            ["Spotify"] = "Spotify",
            ["ZuneMusic"] = "Media Player",
            ["WinDots.TestPlayer"] = "Test Player",
        };

    private DrawerSettings _drawerSettings;
    private AppearanceSettings _appearanceSettings;
    private MonitorsSettings _monitorSettings;
    private int _autoHideMs;
    private bool _hideAfterCommand;

    private MonitorInfo _activeMonitor;
    private MonitorInfo? _pendingMonitor;
    private bool _drawerShown;
    private nint _previousForeground;
    private long _lastFrameTicks;
    private double _reducedFrom;
    private double _reducedTo;
    private TimeSpan _reducedElapsed;
    private string _topologyKey = string.Empty;

    public DrawerHost(IMediaSessionProvider provider, IMonitorService monitors, DispatcherQueue dispatcher, ISettingsStore settings)
    {
        _provider = provider;
        _monitors = monitors;
        _dispatcher = dispatcher;
        _settings = settings;

        global::WinDots.Core.Settings.Settings current = settings.Current;
        _drawerSettings = current.Drawer;
        _appearanceSettings = current.Appearance;
        _monitorSettings = current.Monitors;
        _autoHideMs = current.Drawer.AutoHideMs;
        _hideAfterCommand = current.Drawer.HideAfterCommand;

        var reducedMotion = ResolveReducedMotion(_appearanceSettings.ReduceMotion);
        var options = BuildDrawerOptions(current.Drawer, reducedMotion);
        _controller = new DrawerController(options);
        _controller.Transition += OnTransition;

        _frameTimer = _dispatcher.CreateTimer();
        _frameTimer.Interval = FrameInterval;
        _frameTimer.IsRepeating = true;
        _frameTimer.Tick += OnFrame;

        _autoHideTimer = _dispatcher.CreateTimer();
        _autoHideTimer.IsRepeating = false;
        _autoHideTimer.Tick += OnAutoHideElapsed;

        // Media pipeline: options (built-in aliases merged with the user's, per _docs/06-settings-schema.md), the
        // session coordinator, a persistent artwork cache, and the presentation model that feeds the media page.
        var mediaOptions = BuildMediaOptions(current);

        _coordinator = new SessionCoordinator(provider, mediaOptions);
        var artworkDir = Path.Combine(ApplicationData.Current.LocalFolder.Path, "cache", "artwork");
        _artworkCache = new ArtworkCache(artworkDir, 32L * 1024 * 1024);
        _viewModel = new MediaViewModel(_coordinator, provider, _artworkCache, mediaOptions, _dispatcher);
        _viewModel.CommandInvoked += OnCommandInvoked;

        _activeMonitor = PickDefaultMonitor();
        _drawer = new DrawerWindow(this, _viewModel);
        _drawer.SetAlwaysOnTop(current.Drawer.AlwaysOnTop);

        BuildHandles();
        _monitors.TopologyChanged += OnTopologyChanged;
        _settings.Changed += OnSettingsChanged;

        Instance = this;
        ShellLog.Write(
            $"host ready: monitors={_monitors.Monitors.Count} reducedMotion={reducedMotion} " +
            $"drawer.enabled={current.Drawer.Enabled} dragThresholdPx={current.Drawer.DragThresholdPx} " +
            $"openThreshold={current.Drawer.OpenThreshold} velocityThresholdPxPerS={current.Drawer.VelocityThresholdPxPerS} " +
            $"width={current.Drawer.Width} height={current.Drawer.Height} autoHideMs={current.Drawer.AutoHideMs} " +
            $"hideAfterCommand={current.Drawer.HideAfterCommand} alwaysOnTop={current.Drawer.AlwaysOnTop} " +
            $"monitors.mode={current.Monitors.Mode} handleOffsetPercent={current.Monitors.HandleOffsetPercent} " +
            $"timelineTickMs={mediaOptions.TimelineTickMs} aliases={mediaOptions.PlayerAliases.Count}");
    }

    private static bool ResolveReducedMotion(ReduceMotion mode) => mode switch
    {
        ReduceMotion.On => true,
        ReduceMotion.Off => false,
        _ => !new UISettings().AnimationsEnabled,
    };

    private static DrawerOptions BuildDrawerOptions(DrawerSettings d, bool reducedMotion) => new(
        DrawerHeight: d.Height > 0 ? d.Height : DrawerWindow.DesignHeight,
        DragThresholdPx: d.DragThresholdPx,
        OpenThreshold: d.OpenThreshold,
        VelocityThresholdPxPerS: d.VelocityThresholdPxPerS,
        ReducedMotion: reducedMotion);

    private static MediaOptions BuildMediaOptions(global::WinDots.Core.Settings.Settings s)
    {
        MediaOptions fromSettings = s.ToMediaOptions();
        var merged = new Dictionary<string, string>(DefaultAliases, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in fromSettings.PlayerAliases)
        {
            merged[pair.Key] = pair.Value;
        }

        return fromSettings with { PlayerAliases = merged };
    }

    /// <summary>Set on construction so the tray menu can reach <see cref="ShowInspector"/>.</summary>
    public static DrawerHost? Instance { get; private set; }

    internal DrawerController Controller => _controller;

    /// <summary>Opens the inspector spike window.</summary>
    public static void ShowInspector()
    {
        if (Instance is null)
        {
            return;
        }

        var window = new SessionInspectorWindow(Instance._provider);
        window.Activate();
    }

    /// <summary>
    /// Toggles the drawer, targeting the given monitor. If the drawer is open on another monitor it closes there
    /// first and reopens on the target once the close has finished (see <see cref="FinalizeClose"/>).
    /// </summary>
    public void Toggle(MonitorInfo monitor)
    {
        if (TryRequestMonitorSwitch(monitor))
        {
            return;
        }

        _pendingMonitor = null;
        _activeMonitor = monitor;
        _controller.Toggle();
    }

    /// <summary>Toggles the drawer on the monitor that currently contains the pointer (global-hotkey / tray path).</summary>
    public void ToggleAtCursor() => Toggle(MonitorAtCursor());

    /// <summary>Diagnostics hook: toggles on the monitor at <paramref name="index"/> in enumeration order.</summary>
    public void ToggleOnMonitorIndex(int index)
    {
        var list = _monitors.Monitors;
        if (index < 0 || index >= list.Count)
        {
            ShellLog.Write($"toggle: monitor index {index} out of range ({list.Count})");
            return;
        }

        Toggle(list[index]);
    }

    /// <summary>Diagnostics hook and Escape path.</summary>
    public void Dismiss(DismissReason reason) => _controller.Dismiss(reason);

    /// <summary>
    /// Closes every window this host owns. WinUI keeps the process alive while any window exists, so Quit must call
    /// this before <c>Application.Exit</c>.
    /// </summary>
    public void Shutdown()
    {
        _frameTimer.Stop();
        _autoHideTimer.Stop();
        _monitors.TopologyChanged -= OnTopologyChanged;
        _settings.Changed -= OnSettingsChanged;
        _controller.Transition -= OnTransition;
        _viewModel.CommandInvoked -= OnCommandInvoked;
        _viewModel.Dispose();
        _coordinator.Dispose();
        _artworkCache.Dispose();
        _drawer.HideWindow();
        _drawer.Close();
        foreach (var handle in _handles.ToArray())
        {
            handle.Close();
        }

        _handles.Clear();
        ShellLog.Write("host shut down");
    }

    /// <summary>Diagnostics hook: play/pause the active session.</summary>
    public void DiagPlayPause() => _ = _viewModel.PlayPauseAsync();

    /// <summary>Diagnostics hook: pin the next candidate after the current active; wraps to Automatic past the end.</summary>
    public void DiagNextCandidate()
    {
        IReadOnlyList<IMediaSession> candidates = _coordinator.Candidates;
        if (candidates.Count == 0)
        {
            ShellLog.Write("diag next-candidate: no candidates");
            return;
        }

        IMediaSession? active = _coordinator.Active;
        int index = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (active is not null && candidates[i].Id == active.Id)
            {
                index = i;
                break;
            }
        }

        int next = index + 1;
        if (next >= candidates.Count)
        {
            ShellLog.Write("diag next-candidate: wrap -> Automatic");
            _viewModel.SelectPlayer(null);
        }
        else
        {
            ShellLog.Write($"diag next-candidate: pin index {next}");
            _viewModel.SelectPlayer(candidates[next].Id);
        }
    }

    /// <summary>Diagnostics hook: seek the active session forward by 10 seconds.</summary>
    public void DiagSeekForward() => _ = _viewModel.SeekAsync(_viewModel.Position + TimeSpan.FromSeconds(10));

    /// <summary>Diagnostics hook: writes the host's state to the shell log.</summary>
    public void DumpState()
    {
        ShellLog.Write(
            $"state: controller={_controller.State} progress={_controller.Progress:0.###} shown={_drawerShown} " +
            $"active={_activeMonitor.DeviceId} pending={_pendingMonitor?.DeviceId ?? "-"} handles={_handles.Count} " +
            $"drawerHwnd=0x{_drawer.Hwnd:X} foregroundIsDrawer={NativeInterop.GetForegroundWindow() == _drawer.Hwnd}");

        IMediaSession? active = _coordinator.Active;
        ShellLog.Write(
            $"media: activeId={active?.Id ?? "-"} reason={_coordinator.Reason} " +
            $"state={active?.Current.State.ToString() ?? "-"} hasMetadata={active?.Current.HasMetadata.ToString() ?? "-"} " +
            $"candidates={_coordinator.Candidates.Count}");
    }

    private MonitorInfo MonitorAtCursor()
    {
        if (NativeInterop.GetCursorPos(out var pt))
        {
            // MonitorInfo.Bounds is in physical pixels, as is the cursor position.
            foreach (var monitor in _monitors.Monitors)
            {
                var b = monitor.Bounds;
                if (pt.X >= b.X && pt.X < b.X + b.Width && pt.Y >= b.Y && pt.Y < b.Y + b.Height)
                {
                    return monitor;
                }
            }
        }

        return PickDefaultMonitor();
    }

    /// <summary>
    /// If the drawer is currently shown on a different monitor, starts closing it there and schedules a reopen on
    /// <paramref name="monitor"/>. Returns false when no switch is needed (caller proceeds normally).
    /// </summary>
    internal bool TryRequestMonitorSwitch(MonitorInfo monitor)
    {
        if (!_drawerShown || monitor.DeviceId == _activeMonitor.DeviceId)
        {
            return false;
        }

        ShellLog.Write($"monitor switch requested: {_activeMonitor.DeviceId} -> {monitor.DeviceId}");
        _pendingMonitor = monitor;
        if (_controller.State is DrawerState.Open or DrawerState.Dragging or DrawerState.SettlingOpen)
        {
            _controller.Dismiss(DismissReason.MonitorChange);
        }

        return true;
    }

    internal void SetActiveMonitor(MonitorInfo monitor)
    {
        if (!_drawerShown)
        {
            _activeMonitor = monitor;
        }
    }

    /// <summary>Called after each pointer sample so the drawer can follow the pointer during a drag.</summary>
    internal void OnDragSampleFed()
    {
        ResetAutoHide();
        if (_controller.State == DrawerState.Dragging && _drawerShown)
        {
            _drawer.ApplyProgress(_controller.Progress);
        }
    }

    /// <summary>Pointer or keyboard activity inside the open drawer resets the inactivity timer.</summary>
    internal void OnDrawerActivity() => ResetAutoHide();

    private void OnTransition(object? sender, DrawerTransition e)
    {
        ShellLog.Write($"transition {e.From} -> {e.To} progress={e.Progress:0.###} v={e.VelocityPxPerSecond:0}");
        switch (e.To)
        {
            case DrawerState.Dragging:
                StopMotion();
                EnsureShown();
                _drawer.ApplyProgress(_controller.Progress);
                break;

            case DrawerState.SettlingOpen:
                EnsureShown();
                StartSpring(target: 1, e.VelocityPxPerSecond);
                break;

            case DrawerState.SettlingClosed:
                StartSpring(target: 0, e.VelocityPxPerSecond);
                break;

            case DrawerState.Open:
                StopMotion();
                EnsureShown();
                if (_controller.Options.ReducedMotion && e.From != DrawerState.SettlingOpen)
                {
                    StartReducedMotion(from: _controller.Progress, to: 1, thenActivate: true);
                }
                else
                {
                    _drawer.ApplyProgress(1);
                    _drawer.ActivateForKeyboard();
                }

                ResetAutoHide();
                break;

            case DrawerState.Closed:
                StopMotion();
                if (_controller.Options.ReducedMotion && _drawerShown && e.From != DrawerState.SettlingClosed)
                {
                    StartReducedMotion(from: _controller.Progress, to: 0, thenActivate: false);
                }
                else
                {
                    FinalizeClose();
                }

                break;

            default:
                break;
        }
    }

    private void StartSpring(double target, double velocityPxPerSecond)
    {
        // Spring state is in logical pixels of reveal; velocity from the gesture carries into the settle.
        var height = _drawer.HeightLogical;
        _spring.Start(_controller.Progress * height, velocityPxPerSecond, target * height);
        _reducedElapsed = TimeSpan.MinValue;
        _lastFrameTicks = Environment.TickCount64;
        _frameTimer.Start();
    }

    private void StartReducedMotion(double from, double to, bool thenActivate)
    {
        _reducedFrom = from;
        _reducedTo = to;
        _reducedElapsed = TimeSpan.Zero;
        _lastFrameTicks = Environment.TickCount64;
        _frameTimer.Start();
        if (thenActivate)
        {
            _drawer.ActivateForKeyboard();
        }
    }

    private void StopMotion() => _frameTimer.Stop();

    private void OnFrame(DispatcherQueueTimer sender, object args)
    {
        var now = Environment.TickCount64;
        var elapsed = TimeSpan.FromMilliseconds(Math.Max(0, now - _lastFrameTicks));
        _lastFrameTicks = now;

        if (_reducedElapsed != TimeSpan.MinValue)
        {
            _reducedElapsed += elapsed;
            var t = Math.Clamp(_reducedElapsed / ReducedMotionDuration, 0, 1);
            var p = _reducedFrom + ((_reducedTo - _reducedFrom) * t);
            _drawer.ApplyProgress(p);
            if (t >= 1)
            {
                _frameTimer.Stop();
                if (_reducedTo == 0)
                {
                    FinalizeClose();
                }
            }

            return;
        }

        var height = _drawer.HeightLogical;
        var settled = _spring.Step(elapsed);
        _drawer.ApplyProgress(_spring.Position / height);
        if (settled)
        {
            _frameTimer.Stop();
            _controller.AnimationCompleted();
        }
    }

    private void EnsureShown()
    {
        if (_drawerShown)
        {
            return;
        }

        // Remember where to return focus, but never to one of our own windows (the tray menu briefly makes the
        // hidden message window foreground); restoring focus to a hidden window would strand the keyboard.
        _previousForeground = NativeInterop.IsForegroundOwnedByThisProcess() ? 0 : NativeInterop.GetForegroundWindow();
        _drawer.MoveToMonitor(_activeMonitor);
        _drawer.ShowAtProgress(0);
        _drawerShown = true;
        ShellLog.Write($"drawer shown on {_activeMonitor.DeviceId} (scale {_activeMonitor.Scale})");
    }

    private void FinalizeClose()
    {
        _frameTimer.Stop();
        _autoHideTimer.Stop();
        _drawer.HideWindow();
        _drawerShown = false;
        ShellLog.Write("drawer hidden");
        if (_previousForeground != 0)
        {
            _ = NativeInterop.SetForegroundWindow(_previousForeground);
            _previousForeground = 0;
        }

        // A cross-monitor toggle closed the drawer here; reopen it on the requested monitor once this transition
        // has fully unwound (Toggle re-enters the controller, which must not happen inside its own event).
        if (_pendingMonitor is { } next)
        {
            _pendingMonitor = null;
            _dispatcher.TryEnqueue(() => Toggle(next));
        }
    }

    private void OnTopologyChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(RebuildForTopology);

    private void RebuildForTopology()
    {
        var key = TopologyKey(_monitors.Monitors);
        if (key == _topologyKey)
        {
            ShellLog.Write("topology event with identical layout: ignored");
            return;
        }

        ShellLog.Write($"topology changed: {key}");
        _pendingMonitor = null;
        if (_drawerShown)
        {
            _controller.Dismiss(DismissReason.MonitorChange);
            FinalizeClose();
        }

        BuildHandles();

        // Keep the active monitor valid after the topology change.
        if (_monitors.Monitors.All(m => m.DeviceId != _activeMonitor.DeviceId))
        {
            _activeMonitor = PickDefaultMonitor();
        }
    }

    private static string TopologyKey(IReadOnlyList<MonitorInfo> monitors) =>
        string.Join(";", monitors.Select(m => $"{m.DeviceId}:{m.Bounds}:{m.WorkArea}:{m.Scale}:{m.IsPrimary}"));

    private void BuildHandles()
    {
        var old = _handles.ToArray();
        _handles.Clear();
        _topologyKey = TopologyKey(_monitors.Monitors);

        // drawer.enabled is the master switch: when off, no handles exist (the hotkey and tray still work).
        if (_drawerSettings.Enabled)
        {
            int offset = Math.Clamp(_monitorSettings.HandleOffsetPercent, 0, 100);
            foreach (var monitor in EnabledMonitors())
            {
                _handles.Add(new HandleWindow(monitor, this, offset));
            }
        }

        ShellLog.Write($"handles built: {_handles.Count} (closing {old.Length} old; enabled={_drawerSettings.Enabled} mode={_monitorSettings.Mode})");

        // Close the previous handles only after the replacements exist, so the app never drops to zero windows.
        foreach (var handle in old)
        {
            handle.Close();
        }
    }

    /// <summary>The monitors that should carry a handle, per <c>monitors.mode</c> and <c>enabledDeviceIds</c>.</summary>
    private IEnumerable<MonitorInfo> EnabledMonitors()
    {
        IReadOnlyList<MonitorInfo> all = _monitors.Monitors;
        return _monitorSettings.Mode switch
        {
            MonitorMode.Primary => all.Where(m => m.IsPrimary),
            MonitorMode.List => all.Where(m => _monitorSettings.EnabledDeviceIds.Contains(m.DeviceId, StringComparer.Ordinal)),
            _ => all,
        };
    }

    private void OnCommandInvoked(object? sender, EventArgs e)
    {
        if (_hideAfterCommand && _drawerShown && _controller.State == DrawerState.Open)
        {
            ShellLog.Write("hideAfterCommand: dismissing drawer");
            _controller.Dismiss(DismissReason.AfterCommand);
        }
    }

    private void OnAutoHideElapsed(DispatcherQueueTimer sender, object args)
    {
        _autoHideTimer.Stop();
        if (_drawerShown && _controller.State == DrawerState.Open)
        {
            ShellLog.Write($"autoHide: {_autoHideMs}ms inactivity elapsed; dismissing drawer");
            _controller.Dismiss(DismissReason.Inactivity);
        }
    }

    /// <summary>Restarts the inactivity timer on any drawer activity; a value of 0 disables auto-hide.</summary>
    internal void ResetAutoHide()
    {
        _autoHideTimer.Stop();
        if (_autoHideMs > 0 && _drawerShown && _controller.State == DrawerState.Open)
        {
            _autoHideTimer.Interval = TimeSpan.FromMilliseconds(_autoHideMs);
            _autoHideTimer.Start();
        }
    }

    private void OnSettingsChanged(object? sender, global::WinDots.Core.Settings.Settings s) => _dispatcher.TryEnqueue(() => ApplySettings(s));

    private void ApplySettings(global::WinDots.Core.Settings.Settings s)
    {
        bool handlesNeedRebuild =
            s.Drawer.Enabled != _drawerSettings.Enabled ||
            s.Monitors.Mode != _monitorSettings.Mode ||
            s.Monitors.HandleOffsetPercent != _monitorSettings.HandleOffsetPercent ||
            !s.Monitors.EnabledDeviceIds.SequenceEqual(_monitorSettings.EnabledDeviceIds, StringComparer.Ordinal);

        _drawerSettings = s.Drawer;
        _appearanceSettings = s.Appearance;
        _monitorSettings = s.Monitors;
        _autoHideMs = s.Drawer.AutoHideMs;
        _hideAfterCommand = s.Drawer.HideAfterCommand;

        var reducedMotion = ResolveReducedMotion(_appearanceSettings.ReduceMotion);
        var options = BuildDrawerOptions(s.Drawer, reducedMotion);
        if (!_controller.TryUpdateOptions(options))
        {
            ShellLog.Write("settings: controller busy; drawer options deferred");
        }

        _viewModel.UpdateOptions(BuildMediaOptions(s));
        _coordinator.UpdateOptions(BuildMediaOptions(s));
        _drawer.SetAlwaysOnTop(s.Drawer.AlwaysOnTop);
        ResetAutoHide();

        if (handlesNeedRebuild)
        {
            BuildHandles();
        }

        ShellLog.Write(
            $"settings applied: drawer.enabled={s.Drawer.Enabled} dragThresholdPx={s.Drawer.DragThresholdPx} " +
            $"reducedMotion={reducedMotion} autoHideMs={s.Drawer.AutoHideMs} hideAfterCommand={s.Drawer.HideAfterCommand} " +
            $"alwaysOnTop={s.Drawer.AlwaysOnTop} monitors.mode={s.Monitors.Mode} handleOffsetPercent={s.Monitors.HandleOffsetPercent}");
    }

    private MonitorInfo PickDefaultMonitor()
    {
        var list = _monitors.Monitors;
        return list.FirstOrDefault(m => m.IsPrimary) ?? list[0];
    }
}
