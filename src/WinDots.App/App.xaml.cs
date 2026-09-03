using Microsoft.UI.Xaml;
using WinDots.Core.Contracts;
using WinDots.Windows.Media;

namespace WinDots.App;

public partial class App : Application
{
    private Window? _window;

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
        _window = new Diagnostics.SessionInspectorWindow(MediaSessions);
        _window.Activate();
    }
}
