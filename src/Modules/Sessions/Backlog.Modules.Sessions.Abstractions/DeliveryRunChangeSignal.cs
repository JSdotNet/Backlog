namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// Says that a delivery run was written on this machine — started, resumed by another
/// session, given its prompt or context, moved a stage, finished, or had its Personal
/// Validation answered — so an open Sessions pane can re-read the runs rather than go on
/// showing the list it read when it opened or when the reader last pressed Refresh.
/// <see cref="IDeliverySurfaceLifecycle"/> is its only raiser: every write that reaches a
/// run file through the surface tools or a validation answer goes through it.
/// <para>
/// The Sessions twin of the Inbox's <c>IInboxChangeSignal</c>, and kept as small: a
/// signal and not an event stream, carrying no run, because a listener reads what changed
/// through <see cref="IDeliveryRunSource"/> as it always does. It has no suppression scope
/// and no self-write mark, because the pane never writes a run — a validation answer is
/// written by the notification handler, outside the pane, and the pane should hear it.
/// The hook telemetry that writes the same files does not raise it: it writes once per
/// hook event, often once per tool call, and its figures show on the next lifecycle write
/// — <c>finish_run</c> always being one.
/// </para>
/// <para>
/// One per process, as every data change signal is (local ADR 0043, §5): the MCP listener
/// raises it on a request thread, and every window's pane listens on the same instance.
/// A listener marshals onto its own thread before it touches anything, and returns at
/// once: the raiser may be holding the run's write gate.
/// </para>
/// </summary>
public interface IDeliveryRunChangeSignal
{
    /// <summary>Raised after a run file was written, on whatever thread wrote it.</summary>
    event Action? Changed;

    /// <summary>Tells the listeners a run was written.</summary>
    void Raise();
}

/// <summary>The one <see cref="IDeliveryRunChangeSignal"/>: an event, raised as asked.</summary>
public sealed class DeliveryRunChangeSignal : IDeliveryRunChangeSignal
{
    public event Action? Changed;

    public void Raise() => Changed?.Invoke();
}
