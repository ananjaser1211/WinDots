using System.IO;
using Microsoft.Win32;
using Windows.System;
using Windows.Win32;
using Windows.Win32.System.SystemInformation;
using WinDots.Core.Contracts;

namespace WinDots.Windows.Metrics;

/// <summary>
/// Reads live CPU, memory and disk metrics plus system uptime and the current user's identity for the
/// Dashboard. CPU is derived from <c>GetSystemTimes</c> deltas between successive <see cref="GetSnapshotAsync"/>
/// calls, so the very first snapshot after construction reports no CPU load until a baseline exists (this
/// replaces a <c>PerformanceCounter</c> priming read). All OS calls are wrapped so a failure degrades to an
/// empty snapshot, <see cref="UserInfo.Unknown"/>, or <see cref="TimeSpan.Zero"/> rather than throwing into
/// callers. The provider performs no disk writes and no network access. WinRT user resolution runs once on a
/// background task in the constructor and is cached, keeping COM/WinRT work off the caller's thread.
/// </summary>
public sealed class SystemMetricsProvider : ISystemMetricsProvider
{
    private readonly Lock _cpuGate = new();

    // Previous GetSystemTimes sample (100-ns ticks). Kernel time includes idle time.
    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private bool _haveBaseline;

    private volatile UserInfo _user = UserInfo.Unknown;

    public SystemMetricsProvider()
    {
        // Resolve the user identity once, off the caller's thread, and cache it. Failures leave Unknown.
        _ = Task.Run(async () =>
        {
            try
            {
                _user = await ResolveUserAsync().ConfigureAwait(false);
            }
            catch
            {
                _user = UserInfo.Unknown;
            }
        });
    }

    public Task<SystemMetrics> GetSnapshotAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var cpu = SampleCpu();
            var (memFraction, totalMem, availMem) = SampleMemory();
            var (diskFraction, totalDisk, freeDisk) = SampleDisk();
            return Task.FromResult(new SystemMetrics(cpu, memFraction, diskFraction, totalMem, availMem, totalDisk, freeDisk));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Task.FromResult(SystemMetrics.Empty);
        }
    }

    public TimeSpan GetUptime()
    {
        try
        {
            return TimeSpan.FromMilliseconds(Environment.TickCount64);
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    public UserInfo GetUserInfo() => _user;

    private double SampleCpu()
    {
        if (!PInvoke.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        var idleTicks = ((ulong)(uint)idle.dwHighDateTime << 32) | (uint)idle.dwLowDateTime;
        var kernelTicks = ((ulong)(uint)kernel.dwHighDateTime << 32) | (uint)kernel.dwLowDateTime;
        var userTicks = ((ulong)(uint)user.dwHighDateTime << 32) | (uint)user.dwLowDateTime;

        lock (_cpuGate)
        {
            if (!_haveBaseline)
            {
                _prevIdle = idleTicks;
                _prevKernel = kernelTicks;
                _prevUser = userTicks;
                _haveBaseline = true;
                return 0;
            }

            var idleDelta = idleTicks - _prevIdle;
            var kernelDelta = kernelTicks - _prevKernel;
            var userDelta = userTicks - _prevUser;

            _prevIdle = idleTicks;
            _prevKernel = kernelTicks;
            _prevUser = userTicks;

            // Kernel time includes idle; total busy = (kernel + user) - idle over the same interval.
            var total = kernelDelta + userDelta;
            if (total == 0)
            {
                return 0;
            }

            var busy = (double)(total - idleDelta) / total;
            return Math.Clamp(busy, 0, 1);
        }
    }

    private static (double Fraction, ulong Total, ulong Available) SampleMemory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)global::System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!PInvoke.GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
        {
            return (0, 0, 0);
        }

        var total = status.ullTotalPhys;
        var available = status.ullAvailPhys;
        var used = total > available ? total - available : 0;
        var fraction = Math.Clamp((double)used / total, 0, 1);
        return (fraction, total, available);
    }

    private static (double Fraction, ulong Total, ulong Free) SampleDisk()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrEmpty(root))
        {
            return (0, 0, 0);
        }

        var drive = new DriveInfo(root);
        if (!drive.IsReady || drive.TotalSize <= 0)
        {
            return (0, 0, 0);
        }

        var total = (ulong)drive.TotalSize;
        var free = (ulong)Math.Max(0, drive.TotalFreeSpace);
        var used = total > free ? total - free : 0;
        var fraction = Math.Clamp((double)used / total, 0, 1);
        return (fraction, total, free);
    }

    private static async Task<UserInfo> ResolveUserAsync()
    {
        var users = await User.FindAllAsync(UserType.LocalUser, UserAuthenticationStatus.LocallyAuthenticated);
        var current = users?.FirstOrDefault();
        if (current is null)
        {
            return FallbackUser();
        }

        string? displayName = null;
        try
        {
            var value = await current.GetPropertyAsync(KnownUserProperties.DisplayName);
            displayName = value as string;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                var first = await current.GetPropertyAsync(KnownUserProperties.FirstName) as string;
                var last = await current.GetPropertyAsync(KnownUserProperties.LastName) as string;
                displayName = string.Join(' ', new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
            }
        }
        catch
        {
            // Property access can be denied; fall through to the account name.
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = SafeUserName();
        }

        return new UserInfo(displayName!, ResolveAccountPicturePath());
    }

    // The Shell records per-size account picture paths under this HKCU key; larger sizes are preferred.
    private static readonly string[] PictureValueNames =
        ["Image1080", "Image448", "Image240", "Image208", "Image192", "Image96", "Image64", "Image48", "Image40", "Image32"];

    private static string? ResolveAccountPicturePath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\AccountPicture");
            if (key is null)
            {
                return null;
            }

            foreach (var name in PictureValueNames)
            {
                if (key.GetValue(name) is string path && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    return path;
                }
            }
        }
        catch
        {
            // Registry unavailable or denied; no picture.
        }

        return null;
    }

    private static UserInfo FallbackUser() => new(SafeUserName(), null);

    private static string SafeUserName()
    {
        try
        {
            return string.IsNullOrWhiteSpace(Environment.UserName) ? "User" : Environment.UserName;
        }
        catch
        {
            return "User";
        }
    }
}
