namespace WinDots.Core.Contracts;

public enum DrawerState
{
    Closed,
    Dragging,
    SettlingOpen,
    SettlingClosed,
    Open,
}

public enum DismissReason
{
    Gesture,
    Escape,
    ClickOutside,
    Inactivity,
    AfterCommand,
    FullScreenApp,
    MonitorChange,
    Toggle,
}

/// <summary>A pointer sample in device-independent pixels with a monotonic timestamp.</summary>
public readonly record struct PointerSample(double X, double Y, TimeSpan Timestamp);

public sealed record DrawerTransition(DrawerState From, DrawerState To, double Progress, double VelocityPxPerSecond);

/// <summary>The drawer reveal state machine. Pure logic; views feed pointer samples and render <see cref="Progress"/>. Implemented in Milestone 2.</summary>
public interface IDrawerController
{
    DrawerState State { get; }

    /// <summary>0 = fully hidden, 1 = fully open. May slightly exceed 1 while rubber-banding.</summary>
    double Progress { get; }

    /// <summary>The resting progress (0 or 1) the view should animate towards while settling.</summary>
    double Target { get; }

    void PointerDown(PointerSample sample);

    void PointerMove(PointerSample sample);

    void PointerUp(PointerSample sample);

    void Toggle();

    void Dismiss(DismissReason reason);

    void AnimationCompleted();

    event EventHandler<DrawerTransition>? Transition;
}
