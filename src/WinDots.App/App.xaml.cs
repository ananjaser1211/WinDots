using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinDots.App.Shell;
using WinDots.Core.Contracts;
using WinDots.Windows.Display;
using WinDots.Windows.Media;

namespace WinDots.App;

public partial class App : Application
{
    private MonitorService? _monitors;
    private DrawerHost? _host;
    private ShellMessageWindow? _shell;
    private bool _quitting;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // Milestone 1: surface, do not swallow. Structured logging arrives with the settings milestone.
            System.Diagnostics.Debug.WriteLine($"Unhandled: {e.Exception}");
        };
    }

    /// <summary>Single provider instance for the process; owned by the app, injected into windows.</summary>
    public IMediaSessionProvider MediaSessions { get; } = new GsmtcSessionProvider();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // The drawer host owns the handle windows (one per monitor) and the shared drawer window. It keeps the
        // process alive with no visible main window: the handle windows stay open for the app's lifetime.
        _monitors = new MonitorService();

        // DrawerHost.Instance retains the host (and through it every window) for the process lifetime.
        _host = new DrawerHost(MediaSessions, _monitors, DispatcherQueue.GetForCurrentThread());

        // Desktop integration: the global Win+Shift+M hotkey and the tray icon live on a hidden message window,
        // pumped by this UI thread. Registration failures are logged and non-fatal (see ShellMessageWindow).
        _shell = new ShellMessageWindow(
            onToggleAtCursor: () => _host!.ToggleAtCursor(),
            onToggleOnMonitor: index => _host!.ToggleOnMonitorIndex(index),
            onDismiss: () => _host!.Dismiss(DismissReason.Escape),
            onDumpState: () => _host!.DumpState(),
            onShowInspector: DrawerHost.ShowInspector,
            onQuit: Quit,
            onPlayPause: () => _host!.DiagPlayPause(),
            onNextCandidate: () => _host!.DiagNextCandidate(),
            onSeekForward: () => _host!.DiagSeekForward());

        _ = MediaSessions.InitializeAsync(System.Threading.CancellationToken.None);
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

        // WinUI keeps the process alive while any window exists; close ours before asking the app to exit.
        _host?.Shutdown();
        _host = null;
        Exit();
    }
}
