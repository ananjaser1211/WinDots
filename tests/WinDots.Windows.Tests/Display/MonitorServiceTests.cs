using System.Runtime.InteropServices;
using WinDots.Core.Display;
using WinDots.Windows.Display;

namespace WinDots.Windows.Tests.Display;

/// <summary>Real monitor enumeration. Requires an interactive desktop session.</summary>
[Trait("Category", "Platform")]
public class MonitorServiceTests
{
    [Fact]
    public void EnumeratesAtLeastOneMonitorWithExactlyOnePrimary()
    {
        using var service = new MonitorService();
        var monitors = service.Monitors;

        Assert.NotEmpty(monitors);
        Assert.Single(monitors, m => m.IsPrimary);
        Assert.Equal(monitors.Count, monitors.Select(m => m.DeviceId).Distinct().Count());

        foreach (var m in monitors)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.DeviceId));
            Assert.True(m.Scale >= 1.0, $"{m.DeviceId} scale {m.Scale}");
            Assert.True(m.Bounds.Width > 0 && m.Bounds.Height > 0, $"{m.DeviceId} bounds {m.Bounds}");
            Assert.True(m.WorkArea.Width > 0 && m.WorkArea.Height > 0, $"{m.DeviceId} work area {m.WorkArea}");
            Assert.True(DisplayGeometry.Contains(m.Bounds, m.WorkArea), $"{m.DeviceId} work area {m.WorkArea} outside bounds {m.Bounds}");

            var logical = DisplayGeometry.LogicalBounds(m);
            Assert.Equal(m.Bounds.Width / m.Scale, logical.Width, 6);
        }
    }

    /// <summary>
    /// The test host is DPI-unaware, so a service that enumerated in the host's context would report virtualised
    /// (logical) bounds and Scale == 1 for every monitor. Compare against values read on a PMv2 thread.
    /// </summary>
    [Fact]
    public void ReportsPhysicalBoundsAndTrueScaleEvenInDpiUnawareHost()
    {
        using var service = new MonitorService();
        var reported = service.Monitors.ToDictionary(m => m.DeviceId);

        var expected = Native.EnumeratePerMonitorAwareV2();

        Assert.Equal(expected.Count, reported.Count);
        foreach (var (deviceId, physical) in expected)
        {
            Assert.True(reported.TryGetValue(deviceId, out var actual), $"{deviceId} not reported by the service");
            Assert.Equal(DisplayGeometry.ScaleFromDpi(physical.Dpi), actual!.Scale, 6);
            Assert.Equal(physical.Width, actual.Bounds.Width);
            Assert.Equal(physical.Height, actual.Bounds.Height);
        }
    }

    /// <summary>
    /// Broadcasts reach top-level windows only; a message-only window would never see this and the event would
    /// never fire.
    /// </summary>
    [Fact]
    public void TopologyChangedFiresOnWorkAreaBroadcast()
    {
        using var service = new MonitorService();
        using var fired = new ManualResetEventSlim();
        var raisedOnMessageThread = false;
        service.TopologyChanged += (_, _) =>
        {
            raisedOnMessageThread = Thread.CurrentThread.Name == "WinDots.Monitor";
            fired.Set();
        };

        Native.BroadcastWorkAreaChanged();

        Assert.True(fired.Wait(TimeSpan.FromSeconds(3)), "TopologyChanged did not fire after WM_SETTINGCHANGE(SPI_SETWORKAREA).");
        Assert.False(raisedOnMessageThread, "TopologyChanged must not be raised on the message-loop thread.");
    }

    [Fact]
    public async Task DisposeReturnsPromptly()
    {
        var service = new MonitorService();
        _ = service.Monitors;

        var disposal = Task.Run(service.Dispose);
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));

        // Idempotent.
        service.Dispose();
    }

    [Fact]
    public void RefreshMatchesInitialEnumeration()
    {
        using var service = new MonitorService();
        var before = service.Monitors;
        service.Refresh();
        Assert.Equal(before.Count, service.Monitors.Count);
    }

    /// <summary>Minimal P/Invoke used only to produce independent expectations for the tests above.</summary>
    private static class Native
    {
        private const uint WmSettingChange = 0x001A;
        private const nuint SpiSetWorkArea = 0x002F;
        private const uint SmtoAbortIfHung = 0x0002;
        private const nint HwndBroadcast = 0xFFFF;
        private const nint PerMonitorAwareV2 = -4;

        public sealed record PhysicalMonitor(double Width, double Height, uint Dpi);

        public static void BroadcastWorkAreaChanged()
        {
            _ = SendMessageTimeoutW(HwndBroadcast, WmSettingChange, SpiSetWorkArea, 0, SmtoAbortIfHung, 1000, out _);
        }

        /// <summary>Reads each monitor's rectangle and DPI from a thread switched to PMv2.</summary>
        public static Dictionary<string, PhysicalMonitor> EnumeratePerMonitorAwareV2()
        {
            var result = new Dictionary<string, PhysicalMonitor>();
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    Assert.NotEqual(0, SetThreadDpiAwarenessContext(PerMonitorAwareV2));
                    MonitorEnumProc callback = (monitor, _, _, _) =>
                    {
                        var info = new MonitorInfoEx { CbSize = Marshal.SizeOf<MonitorInfoEx>() };
                        Assert.True(GetMonitorInfoW(monitor, ref info));
                        Assert.Equal(0, GetDpiForMonitor(monitor, 0, out var dpiX, out _));
                        result[info.Device] = new PhysicalMonitor(
                            info.Monitor.Right - info.Monitor.Left,
                            info.Monitor.Bottom - info.Monitor.Top,
                            dpiX);
                        return true;
                    };
                    Assert.True(EnumDisplayMonitors(0, 0, callback, 0));
                    GC.KeepAlive(callback);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.Start();
            thread.Join();
            if (error is not null)
            {
                throw new InvalidOperationException("PMv2 enumeration failed.", error);
            }

            return result;
        }

        private delegate bool MonitorEnumProc(nint monitor, nint hdc, nint rect, nint data);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfoEx
        {
            public int CbSize;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string Device;
        }

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern nint SendMessageTimeoutW(nint hwnd, uint msg, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern nint SetThreadDpiAwarenessContext(nint context);

        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfoEx info);

        [DllImport("shcore.dll", ExactSpelling = true)]
        private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
    }
}
