namespace WinDots.Core.Contracts;

public readonly record struct Rect(double X, double Y, double Width, double Height);

public sealed record MonitorInfo(string DeviceId, Rect Bounds, Rect WorkArea, double Scale, bool IsPrimary);

/// <summary>Tracks display bounds, work areas, and scale. Implemented in Milestone 2.</summary>
public interface IMonitorService
{
    IReadOnlyList<MonitorInfo> Monitors { get; }

    event EventHandler? TopologyChanged;
}
