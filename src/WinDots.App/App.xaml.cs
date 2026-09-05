using System.IO;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Storage;
using WinDots.App.Settings;
using WinDots.App.Shell;
using WinDots.Core.Contracts;
using WinDots.Core.Settings;
using WinDots.Windows.Audio;
using WinDots.Windows.Display;
using WinDots.Windows.Media;

namespace WinDots.App;

public partial class App : Application
{
    private MonitorService? _monitors;
    private DrawerHost? _host;
    private ShellMessageWindow? _shell;
    private JsonSettingsStore? _settings;
    private SettingsWindow? _settingsWindow;
    private bool _quitting;

    public App()
    {
        InitializeComponent();
        // Surface, never swallow: the shell log is the only place a packaged crash can be diagnosed from.
        UnhandledException += (_, e) =>
        {
            Diagnostics.ShellLog.Write($"UNHANDLED (xaml): {e.Message}\n{e.Exception}");
        };
        System.AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Diagnostics.ShellLog.Write($"UNHANDLED (appdomain): {e.ExceptionObject}");
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            Diagnostics.ShellLog.Write($"UNOBSERVED task: {e.Exception}");
    }

    /// <summary>Single provider instance for the process; owned by the app, injected into windows.</summary>
    public IMediaSessionProvider MediaSessions { get; } = new GsmtcSessionProvider();

    /// <summary>Core Audio (per-application volume) provider; owned by the app, disposed on Quit.</summary>
    public CoreAudioSessionProvider AudioSessions { get; } = new CoreAudioSessionProvider();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Settings are loaded before any window is created so the host and hotkey start from the persisted values.
        var settingsPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "settings.json");
        _settings = new JsonSettingsStore(settingsPath);
        try
        {
            await _settings.LoadAsync(System.Threading.CancellationToken.None);
        }
        catch (System.Exception ex)
        {
            Diagnostics.ShellLog.Write($"settings load failed, using defaults: {ex.Message}");
        }

        Diagnostics.ShellLog.Write($"settings loaded from {settingsPath}");
        if (_settings.LastLoadProblem is { } problem)
        {
            Diagnostics.ShellLog.Write($"settings load problem: {problem}");
        }

        foreach (var warning in _settings.LoadWarnings)
        {
            Diagnostics.ShellLog.Write($"settings warning: {warning}");
        }

        // The drawer host owns the handle windows (one per monitor) and the shared drawer window. It keeps the
        // process alive with no visible main window: the handle windows stay open for the app's lifetime.
        _monitors = new MonitorService();

        // DrawerHost.Instance retains the host (and through it every window) for the process lifetime.
        _host = new DrawerHost(MediaSessions, AudioSessions, _monitors, DispatcherQueue.GetForCurrentThread(), _settings);

        // Desktop integration: the global toggle hotkey (from drawer.toggleShortcut) and the tray icon live on a
        // hidden message window, pumped by this UI thread. Registration failures are logged and non-fatal.
        _shell = new ShellMessageWindow(
            _settings,
            onToggleAtCursor: () => _host!.ToggleAtCursor(),
            onToggleOnMonitor: index => _host!.ToggleOnMonitorIndex(index),
            onDismiss: () => _host!.Dismiss(DismissReason.Escape),
            onDumpState: () => _host!.DumpState(),
            onShowInspector: DrawerHost.ShowInspector,
            onShowSettings: ShowSettings,
            onQuit: Quit,
            onPlayPause: () => _host!.DiagPlayPause(),
            onNextCandidate: () => _host!.DiagNextCandidate(),
            onSeekForward: () => _host!.DiagSeekForward(),
            onAudioMatch: () => _host!.DiagAudioMatch(),
            onSetVolume25: () => _host!.DiagSetVolume25(),
            onToggleMute: () => _host!.DiagToggleMute(),
            onMediaPlayPause: () => _host!.MediaPlayPause(),
            onMediaNext: () => _host!.MediaNext(),
            onMediaPrevious: () => _host!.MediaPrevious(),
            onMediaStop: () => _host!.MediaStop());

        _ = MediaSessions.InitializeAsync(System.Threading.CancellationToken.None);
    }

    /// <summary>Opens the settings window, or brings the existing single instance to the foreground.</summary>
    private void ShowSettings()
    {
        if (_settings is null)
        {
            return;
        }

        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, _monitors, _host?.Sources, _host?.LastFm, _shell, _host?.Updates, DrawerHost.CurrentVersion, _host?.LastBackgroundUpdate);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Activate();
    }

    /// <summary>Tray "Quit": tears down desktop integration, disposes the provider, and exits.</summary>
    private async void Quit()
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;
        Diagnostics.ShellLog.Write("quit requested");

        // Quit must always quit: if orderly teardown stalls (a COM release that never returns, a window that will
        // not close), terminate the process anyway.
        _ = System.Threading.Tasks.Task.Delay(System.TimeSpan.FromSeconds(5)).ContinueWith(_ =>
        {
            Diagnostics.ShellLog.Write("quit: orderly shutdown timed out; terminating");
            System.Environment.Exit(0);
        }, System.Threading.Tasks.TaskScheduler.Default);

        _settingsWindow?.Close();
        _settingsWindow = null;

        // Remove the tray icon and unregister the hotkey before the message loop stops.
        _shell?.Dispose();
        _shell = null;

        _monitors?.Dispose();
        _monitors = null;

        try
        {
            await MediaSessions.DisposeAsync();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Quit: provider dispose threw: {ex}");
        }

        try
        {
            await AudioSessions.DisposeAsync();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Quit: audio provider dispose threw: {ex}");
        }

        // WinUI keeps the process alive while any window exists; close ours before asking the app to exit.
        _host?.Shutdown();
        _host = null;
        Exit();
    }
}
