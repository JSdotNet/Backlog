namespace Backlog.UI.Components.Feedback;

/// <summary>
/// One transient message on its way to the tray.
/// <para>
/// A record rather than a class because nothing about a message changes after it
/// is made: the tray keys on <see cref="Id"/> and Blazor keys on that same value,
/// so a message that could be edited in place would be a component that never
/// re-entered.
/// </para>
/// </summary>
public sealed record ToastMessage
{
    /// <summary>How long an ordinary result stays up. The design's own default for
    /// <c>Toast</c>, restated here so a publisher never has to name a number.
    /// </summary>
    public const int DefaultDuration = 5000;

    /// <summary>Longer, for the messages a reader is owed time to read. A failure
    /// is the one kind of toast that costs something to miss, and unlike a result
    /// there is nowhere else on screen it is also written down.</summary>
    public const int ErrorDuration = 8000;

    /// <summary>Assigned once, at construction. It is the identity the tray keys
    /// its <c>Toast</c> on and the handle <see cref="IToastChannel.Dismiss"/>
    /// takes, so two messages reading the same words are still two toasts.
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    public required string Message { get; init; }

    public string? Title { get; init; }

    public ToastSeverity Severity { get; init; } = ToastSeverity.Info;

    /// <summary>Carried through to the <c>Toast</c> the tray renders, so a
    /// selector that used to find an inline alert still finds the message after it
    /// became a toast.</summary>
    public string? TestId { get; init; }

    /// <summary>Zero or less keeps it up until it is dismissed — <c>Toast</c>'s own
    /// contract, passed through rather than reinterpreted here.</summary>
    public int DurationMilliseconds { get; init; } = DefaultDuration;

    /// <summary>A result: it reports and gets out of the way.</summary>
    public static ToastMessage Info(string message, string? testId = null) =>
        new() { Message = message, TestId = testId, Severity = ToastSeverity.Info, DurationMilliseconds = DefaultDuration };

    /// <summary>Something landed, but not all of it. Warned rather than errored
    /// because there is a result to read as well as a problem, and it takes the
    /// longer dwell for the same reason an error does.</summary>
    public static ToastMessage Warning(string message, string? testId = null) =>
        new() { Message = message, TestId = testId, Severity = ToastSeverity.Warning, DurationMilliseconds = ErrorDuration };

    /// <summary>Nothing landed. <c>Toast</c> raises this to <c>role="alert"</c> on
    /// its own, per <c>.design/accessibility.md#screen-reader--announcements</c>.
    /// </summary>
    public static ToastMessage Error(string message, string? testId = null) =>
        new() { Message = message, TestId = testId, Severity = ToastSeverity.Error, DurationMilliseconds = ErrorDuration };
}

/// <summary>
/// The app's one channel for transient messages. A screen publishes; the tray in
/// the layout renders. The two never meet, which is what lets a module's pane
/// report into a band the shell owns — the pane cannot see the shell, and the
/// shell must not learn what a pane is.
/// </summary>
public interface IToastChannel
{
    /// <summary>The messages showing right now — at most <see cref="ToastChannel.MaxVisible"/>
    /// of them, per <c>.design/interaction-guidelines.md#feedback-and-toasts</c>.
    /// A snapshot: the list handed back never changes underneath a render.</summary>
    IReadOnlyList<ToastMessage> Visible { get; }

    /// <summary>Raised on publish and on dismiss, and possibly off the renderer's
    /// thread — whoever subscribes marshals.</summary>
    event Action? Changed;

    void Publish(ToastMessage message);

    /// <summary>Takes a message out, wherever in the queue it is. Dismissing one
    /// that has already gone is a no-op.</summary>
    void Dismiss(Guid id);
}

/// <summary>
/// The queue behind that channel.
/// <para>
/// It holds a queue rather than being a bare event for two reasons. The cap the
/// design puts on how many toasts may be on screen at once needs somewhere to
/// live, and stating it here means it can be checked without rendering anything.
/// And a publish that happens while no tray is mounted — a GitHub sync finishing
/// during a route change — must wait rather than be dropped, because the message
/// it carries is the only record of what went wrong.
/// </para>
/// <para>
/// Free-threaded, because every interesting publisher is a continuation: the
/// GitHub sync, the Copilot CLI run, the AI call. All list access is under the
/// lock and <see cref="Visible"/> hands back a copy; the tray, not this, is what
/// marshals onto the renderer.
/// </para>
/// </summary>
public sealed class ToastChannel : IToastChannel
{
    /// <summary><c>.design/interaction-guidelines.md#feedback-and-toasts</c>: show
    /// no more than three at once and queue the rest. A fourth simultaneous toast
    /// is a band, and a band is the thing a toast exists not to be.</summary>
    public const int MaxVisible = 3;

    private readonly Lock _gate = new();
    private readonly List<ToastMessage> _queue = [];

    public event Action? Changed;

    public IReadOnlyList<ToastMessage> Visible
    {
        get
        {
            lock (_gate)
            {
                return _queue.Count <= MaxVisible
                    ? [.. _queue]
                    : [.. _queue.Take(MaxVisible)];
            }
        }
    }

    public void Publish(ToastMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_gate)
        {
            _queue.Add(message);
        }

        Changed?.Invoke();
    }

    public void Dismiss(Guid id)
    {
        bool removed;

        lock (_gate)
        {
            removed = _queue.RemoveAll(message => message.Id == id) > 0;
        }

        // Silent when there was nothing to remove: Toast dismisses itself on a
        // timer as well as on its button, so the same id can arrive twice, and a
        // second redraw for a message that was already gone is a re-render of the
        // whole tray for nothing.
        if (removed) Changed?.Invoke();
    }
}
