using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;
using WinDots.Core.Contracts;
using WinDots.Core.Display;

namespace WinDots.Windows.Display;

/// <summary>
/// Enumerates monitors (physical pixels plus per-monitor scale) and raises <see cref="TopologyChanged"/> when the
/// display set, resolution, DPI, or work area changes. A hidden <c>WS_POPUP</c> top-level window on a dedicated
/// thread receives WM_DISPLAYCHANGE, WM_DPICHANGED, and WM_SETTINGCHANGE(SPI_SETWORKAREA); it must be top-level
/// because the system does not deliver these broadcasts to message-only (<c>HWND_MESSAGE</c>) windows. Bursts are
/// debounced by <see cref="DebounceInterval"/>. <see cref="TopologyChanged"/> is raised on a thread-pool thread after
/// <see cref="Monitors"/> has been refreshed; subscribers hop to their own dispatcher.
/// <para>
/// Enumeration always runs under <c>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2</c> (set per thread around
/// <c>EnumDisplayMonitors</c>), so <see cref="MonitorInfo.Bounds"/> and <see cref="MonitorInfo.Scale"/> are the
/// real physical values even when the host process is DPI-unaware, such as a test runner.
/// </para>
/// </summary>
public sealed class MonitorService : IMonitorService, IDisposable
{
    public static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan ThreadJoinTimeout = TimeSpan.FromSeconds(5);

    private readonly string _className = "WinDots.MonitorService." + Guid.NewGuid().ToString("N");
    private readonly WNDPROC _wndProc; // Kept alive for the lifetime of the window.
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _windowReady = new();
    private readonly Timer _debounce;
    private readonly Lock _gate = new();

    private HWND _hwnd;
    private Exception? _startupError;
    private IReadOnlyList<MonitorInfo> _monitors;
    private volatile bool _disposed;

    public MonitorService()
    {
        _monitors = Enumerate();
        _debounce = new Timer(_ => OnDebounceElapsed(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _wndProc = WndProc;
        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "WinDots.Monitor",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _windowReady.Wait();
        if (_startupError is not null)
        {
            Dispose();
            throw new InvalidOperationException("Failed to create the monitor message window.", _startupError);
        }
    }

    public IReadOnlyList<MonitorInfo> Monitors => Volatile.Read(ref _monitors);

    public event EventHandler? TopologyChanged;

    /// <summary>Re-enumerates immediately, bypassing the debounce. Does not raise <see cref="TopologyChanged"/>.</summary>
    public void Refresh() => Volatile.Write(ref _monitors, Enumerate());

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _debounce.Dispose();
        _windowReady.Wait();
        if (!_hwnd.IsNull)
        {
            PInvoke.PostMessage(_hwnd, PInvoke.WM_CLOSE, default, default);
        }

        if (Thread.CurrentThread != _thread)
        {
            _thread.Join(ThreadJoinTimeout);
        }

        _windowReady.Dispose();
    }

    internal static unsafe IReadOnlyList<MonitorInfo> Enumerate()
    {
        var result = new List<MonitorInfo>();
        MONITORENUMPROC callback = (monitor, _, _, _) =>
        {
            if (TryDescribe(monitor, out var info))
            {
                result.Add(info);
            }

            return true;
        };

        // GetMonitorInfo and GetDpiForMonitor virtualise their results to the calling thread's DPI awareness;
        // force PMv2 so a DPI-unaware host still sees physical pixels and the true per-monitor scale.
        var previous = PInvoke.SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        try
        {
            PInvoke.EnumDisplayMonitors(HDC.Null, null, callback, default);
        }
        finally
        {
            if (previous != default)
            {
                PInvoke.SetThreadDpiAwarenessContext(previous);
            }
        }

        GC.KeepAlive(callback);

        // Callers rely on exactly one primary; enforce it defensively if the OS reported something odd.
        if (result.Count > 0 && !result.Any(m => m.IsPrimary))
        {
            var candidate = result.FirstOrDefault(m => m.Bounds.X == 0 && m.Bounds.Y == 0) ?? result[0];
            result[result.IndexOf(candidate)] = candidate with { IsPrimary = true };
        }

        return result.AsReadOnly();
    }

    private static unsafe bool TryDescribe(HMONITOR monitor, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MonitorInfo? info)
    {
        var native = new MONITORINFOEXW();
        native.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (!PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&native))
        {
            info = null;
            return false;
        }

        double scale = 1.0;
        if (PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Succeeded)
        {
            scale = DisplayGeometry.ScaleFromDpi(dpiX);
        }

        var deviceId = native.szDevice.ToString();
        if (string.IsNullOrEmpty(deviceId))
        {
            deviceId = "MONITOR#" + ((nint)monitor.Value).ToString("X", CultureInfo.InvariantCulture);
        }

        info = new MonitorInfo(
            deviceId,
            ToRect(native.monitorInfo.rcMonitor),
            ToRect(native.monitorInfo.rcWork),
            scale,
            (native.monitorInfo.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0);
        return true;
    }

    private static Rect ToRect(RECT r) => new(r.left, r.top, r.right - r.left, r.bottom - r.top);

    private unsafe void MessageLoop()
    {
        try
        {
            // WM_DPICHANGED is only delivered to windows whose thread is per-monitor aware.
            PInvoke.SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            var module = new HINSTANCE(PInvoke.GetModuleHandle((PCWSTR)null).Value);
            fixed (char* className = _className)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = _wndProc,
                    hInstance = module,
                    lpszClassName = className,
                };
                if (PInvoke.RegisterClassEx(in wc) == 0)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "RegisterClassEx failed.");
                }

                // Top-level (parent HWND.Null), never shown: broadcasts such as WM_DISPLAYCHANGE and
                // WM_SETTINGCHANGE skip message-only windows. WS_EX_TOOLWINDOW keeps it out of Alt+Tab.
                fixed (char* title = "WinDots monitor watcher")
                {
                    _hwnd = PInvoke.CreateWindowEx(
                        WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
                        className,
                        title,
                        WINDOW_STYLE.WS_POPUP,
                        0,
                        0,
                        0,
                        0,
                        HWND.Null,
                        HMENU.Null,
                        module,
                        null);
                }

                if (_hwnd.IsNull)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateWindowEx failed.");
                }
            }
        }
        catch (Exception ex)
        {
            _startupError = ex;
            _windowReady.Set();
            return;
        }

        _windowReady.Set();

        while (PInvoke.GetMessage(out var msg, HWND.Null, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(in msg);
            PInvoke.DispatchMessage(in msg);
        }

        fixed (char* className = _className)
        {
            PInvoke.UnregisterClass(className, new HINSTANCE(PInvoke.GetModuleHandle((PCWSTR)null).Value));
        }
    }

    private LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        switch (msg)
        {
            case PInvoke.WM_DISPLAYCHANGE:
            case PInvoke.WM_DPICHANGED:
                Kick();
                break;
            case PInvoke.WM_SETTINGCHANGE when (uint)wParam.Value == (uint)SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETWORKAREA:
                Kick();
                break;
            case PInvoke.WM_CLOSE:
                PInvoke.DestroyWindow(hwnd);
                return default;
            case PInvoke.WM_DESTROY:
                _hwnd = HWND.Null;
                PInvoke.PostQuitMessage(0);
                return default;
        }

        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void Kick()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _debounce.Change(DebounceInterval, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Raced with Dispose; nothing to do.
        }
    }

    private void OnDebounceElapsed()
    {
        if (_disposed)
        {
            return;
        }

        Refresh();
        TopologyChanged?.Invoke(this, EventArgs.Empty);
    }
}
