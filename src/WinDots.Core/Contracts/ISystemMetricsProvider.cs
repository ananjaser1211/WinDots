namespace WinDots.Core.Contracts;

/// <summary>
/// Immutable snapshot of system resource usage for the Dashboard resource rings.
/// Fractions are clamped to [0,1]; raw byte counts are supplied for tooltips. A snapshot with all
/// fractions at zero and no raw values is the degraded/unavailable case (metrics could not be read).
/// </summary>
/// <param name="CpuFraction">Total CPU utilisation across all logical processors, 0..1.</param>
/// <param name="MemoryFraction">Physical memory in use, 0..1.</param>
/// <param name="DiskFraction">Used space on the system drive, 0..1.</param>
/// <param name="TotalMemoryBytes">Total physical memory installed, in bytes.</param>
/// <param name="AvailableMemoryBytes">Physical memory currently available, in bytes.</param>
/// <param name="TotalDiskBytes">Total capacity of the system drive, in bytes.</param>
/// <param name="FreeDiskBytes">Free space on the system drive, in bytes.</param>
public sealed record SystemMetrics(
    double CpuFraction,
    double MemoryFraction,
    double DiskFraction,
    ulong TotalMemoryBytes,
    ulong AvailableMemoryBytes,
    ulong TotalDiskBytes,
    ulong FreeDiskBytes)
{
    /// <summary>A fully-degraded snapshot used when metrics cannot be read.</summary>
    public static SystemMetrics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// The current interactive user's identity for the Dashboard user card.
/// <paramref name="AccountPicturePath"/> is null when no picture is available.
/// </summary>
/// <param name="DisplayName">A human-friendly name; falls back to the account name if a display name is unavailable.</param>
/// <param name="AccountPicturePath">Absolute path to the account picture, or null.</param>
public sealed record UserInfo(string DisplayName, string? AccountPicturePath)
{
    /// <summary>A neutral fallback used when the user cannot be identified.</summary>
    public static UserInfo Unknown { get; } = new("User", null);
}

/// <summary>
/// Live system metrics (CPU, memory, disk), uptime and the current user's identity for the Dashboard.
/// Every member is safe to call repeatedly on a timer and never throws: on failure it degrades to
/// <see cref="SystemMetrics.Empty"/>, <see cref="UserInfo.Unknown"/>, or <see cref="TimeSpan.Zero"/>.
/// Implementations perform no disk writes and no network access.
/// </summary>
public interface ISystemMetricsProvider
{
    /// <summary>
    /// Samples CPU, memory and disk. CPU is measured as the delta since the previous call, so the first
    /// snapshot after construction reports a zero (or coarse) CPU fraction until a baseline exists.
    /// </summary>
    Task<SystemMetrics> GetSnapshotAsync(CancellationToken ct);

    /// <summary>System uptime since last boot. Cheap and synchronous.</summary>
    TimeSpan GetUptime();

    /// <summary>
    /// The current user's display name and account picture path. Cheap after first resolution
    /// (the result is cached), synchronous, and never throws.
    /// </summary>
    UserInfo GetUserInfo();
}
