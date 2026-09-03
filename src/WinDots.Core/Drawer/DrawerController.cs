using WinDots.Core.Contracts;

namespace WinDots.Core.Drawer;

/// <summary>
/// The drawer reveal state machine from _docs/03-ux-interaction-spec.md. Pure and deterministic: the view feeds pointer
/// samples (with their own timestamps), reads <see cref="Progress"/> after each one, animates towards <see cref="Target"/>
/// while settling, and calls <see cref="AnimationCompleted"/> when its spring has come to rest.
/// </summary>
public sealed class DrawerController : IDrawerController
{
    private readonly DrawerOptions options;
    private readonly VelocityTracker velocity;

    private bool pressed;
    private bool dragging;
    private double pressY;
    private double pressProgress;

    public DrawerController(DrawerOptions options)
        : this(options, new VelocityTracker())
    {
    }

    public DrawerController(DrawerOptions options, VelocityTracker velocity)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(velocity);
        this.options = options.Validate();
        this.velocity = velocity;
    }

    public event EventHandler<DrawerTransition>? Transition;

    public DrawerOptions Options => options;

    public DrawerState State { get; private set; } = DrawerState.Closed;

    public double Progress { get; private set; }

    public double Target { get; private set; }

    public void PointerDown(PointerSample sample)
    {
        if (pressed)
        {
            // A second press while one is active (multi-touch, or a lost PointerUp) is ignored; the first gesture continues.
            return;
        }

        if (State is not (DrawerState.Closed or DrawerState.Open))
        {
            return;
        }

        pressed = true;
        dragging = false;
        pressY = sample.Y;
        pressProgress = Progress;
        velocity.Clear();
        velocity.Add(sample);
    }

    public void PointerMove(PointerSample sample)
    {
        if (!pressed)
        {
            return;
        }

        velocity.Add(sample);
        var dy = sample.Y - pressY;

        if (!dragging)
        {
            if (Math.Abs(dy) < options.DragThresholdPx)
            {
                return;
            }

            dragging = true;
            SetState(DrawerState.Dragging, Progress, velocity.VelocityPxPerSecond);
        }

        Progress = ProgressFor(dy);
    }

    public void PointerUp(PointerSample sample)
    {
        if (!pressed)
        {
            return;
        }

        velocity.Add(sample);
        var dy = sample.Y - pressY;
        var v = velocity.VelocityPxPerSecond;
        var wasDragging = dragging;
        var fromOpen = pressProgress >= 1;
        pressed = false;
        dragging = false;

        if (!wasDragging)
        {
            // A click on the handle toggles; a click in the open drawer's top band does nothing.
            if (State == DrawerState.Closed)
            {
                Settle(open: true, v);
            }

            return;
        }

        Progress = ProgressFor(dy);
        Settle(ShouldOpenOnRelease(dy, v, fromOpen), v);
    }

    public void Toggle()
    {
        switch (State)
        {
            case DrawerState.Closed:
            case DrawerState.SettlingClosed:
                CancelPress();
                Settle(open: true, 0);
                break;
            case DrawerState.Open:
            case DrawerState.SettlingOpen:
            case DrawerState.Dragging:
                CancelPress();
                Settle(open: false, 0);
                break;
            default:
                break;
        }
    }

    public void Dismiss(DismissReason reason)
    {
        if (State is DrawerState.Closed or DrawerState.SettlingClosed)
        {
            return;
        }

        CancelPress();
        Settle(open: false, 0);
    }

    public void AnimationCompleted()
    {
        switch (State)
        {
            case DrawerState.SettlingOpen:
                Progress = 1;
                SetState(DrawerState.Open, 1, 0);
                break;
            case DrawerState.SettlingClosed:
                Progress = 0;
                SetState(DrawerState.Closed, 0, 0);
                break;
            default:
                break;
        }
    }

    /// <summary>Progress for a signed vertical travel from the press origin; clamps at 0 and rubber-bands past 1.</summary>
    internal double ProgressFor(double dy)
    {
        var h = options.DrawerHeight;
        var travel = (pressProgress * h) + dy;
        if (travel <= 0)
        {
            return 0;
        }

        if (travel <= h)
        {
            return travel / h;
        }

        return 1 + (options.RubberBandFactor * Math.Tanh((travel - h) / h));
    }

    private bool ShouldOpenOnRelease(double dy, double v, bool fromOpen)
    {
        var threshold = options.VelocityThresholdPxPerS;
        if (v >= threshold)
        {
            return true;
        }

        if (v <= -threshold)
        {
            return false;
        }

        if (fromOpen)
        {
            // From the open drawer's top band, upward travel past the drag threshold closes; anything else stays open.
            return dy > -options.DragThresholdPx;
        }

        return Progress >= options.OpenThreshold || Progress >= 0.5;
    }

    private void Settle(bool open, double v)
    {
        Target = open ? 1 : 0;
        if (options.ReducedMotion)
        {
            Progress = Target;
            SetState(open ? DrawerState.Open : DrawerState.Closed, Progress, v);
            return;
        }

        SetState(open ? DrawerState.SettlingOpen : DrawerState.SettlingClosed, Progress, v);
    }

    private void CancelPress()
    {
        pressed = false;
        dragging = false;
        velocity.Clear();
    }

    private void SetState(DrawerState to, double progress, double v)
    {
        var from = State;
        if (from == to)
        {
            return;
        }

        State = to;
        Transition?.Invoke(this, new DrawerTransition(from, to, progress, v));
    }
}
