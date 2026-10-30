using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Backlog.UI.Components.Feedback;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The Backlog entries the session list and the pull requests list name, answered for
/// one window (local ADR 0043 §3, §5): the entry that linked a session, the entry a pull
/// request belongs to, and opening an entry — a jump to the Tasks surface through the
/// window's navigator, so it lands in the window it was pressed in.
/// <para>
/// Here rather than in either list because the records are on the entries, which
/// belong to Tasks, and the sessions and pull requests are other contexts'. Which entry
/// a session or a pull request belongs to is asked of Tasks (<see cref="ITaskLookup"/>,
/// local ADR 0042 V8), over the same window's rows; opening one reads that window's
/// <see cref="TasksDesktopState"/>. It moved out of <c>Home</c> when those lists became
/// surfaces a window of their own can show. One per window.
/// </para>
/// </summary>
public sealed class ShellTaskReferences
{
    /// <summary>The test id of the toast for an entry this workspace does not hold.</summary>
    public const string PlanEntryMissingTestId = "sessions-plan-entry-missing";

    private readonly TasksDesktopState _state;
    private readonly ITaskLookup? _lookup;
    private readonly ISurfaceNavigator _navigator;
    private readonly IToastChannel? _toasts;

    /// <param name="lookup">Tasks' lookup over this window's rows, or null in a host that
    /// composes none — then no session or pull request has a task.</param>
    public ShellTaskReferences(TasksDesktopState state, ITaskLookup? lookup, ISurfaceNavigator navigator, IToastChannel? toasts)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(navigator);

        _state = state;
        _lookup = lookup;
        _navigator = navigator;
        _toasts = toasts;
    }

    /// <summary>
    /// Opens a Backlog entry a run, a session or a pull request names: the Tasks view
    /// with that entry in its editor, in this window.
    /// <para>
    /// Matched first on the entry's stored id, when the reference has one — the line the
    /// app puts on every entry it copies names it, and so does an entry that linked the
    /// session — because nothing identifies an entry more surely. Then on the pair the
    /// import writes down — the plan and the item's id within it — because that is what
    /// identifies an entry across plan versions; the plan is stored with its sigil and
    /// the marker states the slug, so the comparison drops it. An entry this workspace
    /// does not hold is said out loud rather than silently doing nothing.
    /// </para>
    /// </summary>
    public Task OpenTaskAsync(DeliveryRunReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        // Named by id alone, the label is the entry's title rather than an item id,
        // so there is no plan item to fall back on.
        var byIdOnly = reference.EntryId is not null && reference.Plan is null;

        var row = reference.EntryId is { } entryId
            ? _state.Rows.FirstOrDefault(candidate => candidate.Id == entryId)
            : null;

        row ??= byIdOnly
            ? null
            : _state.Rows.FirstOrDefault(candidate =>
                string.Equals(candidate.PreviewImportItemId, reference.Label, StringComparison.OrdinalIgnoreCase)
                && InPlan(candidate, reference.Plan));

        if (row is not { Id: { } id })
        {
            _toasts?.Publish(ToastMessage.Warning(
                byIdOnly
                    ? $"The entry {reference.Label} is not in this workspace."
                    : $"No entry of plan {reference.Plan} with the id {reference.Label} is in this workspace.",
                PlanEntryMissingTestId));

            return Task.CompletedTask;
        }

        _navigator.Show(TasksSurfaceQuery.SurfaceId, TasksSurfaceQuery.ForEntry(id.ToString()));
        return Task.CompletedTask;
    }

    /// <summary>
    /// The entry that linked a session: the entry's own record of the session it was
    /// worked in, written by the Backlog server's <c>link_session</c>. Asked of Tasks
    /// and answered by stored id, so opening it is the surest match
    /// <see cref="OpenTaskAsync"/> makes.
    /// </summary>
    public DeliveryRunReference? SessionTaskFor(string sessionId) =>
        TaskRunReference(_lookup?.TaskForSession(sessionId));

    /// <summary>
    /// The entry a pull request belongs to, asked of Tasks, which finds it in order of
    /// how sure each relation is: the entry's own <c>pull-request</c> projection, a
    /// GitHub issue a linked task follows that the pull request closes, a linked task's
    /// external key in its title or branch, and the entry that linked its session.
    /// Answered by stored id, so opening it is the surest match
    /// <see cref="OpenTaskAsync"/> makes. With <paramref name="mineOnly"/> only my
    /// tasks are asked — open ones — which is what the list's "Mine" adds to the pull
    /// requests I opened myself.
    /// </summary>
    public DeliveryRunReference? PullRequestTaskFor(PullRequestTaskQuestion pull, bool mineOnly) =>
        TaskRunReference(_lookup?.TaskForPullRequest(pull, mineOnly));

    /// <summary>
    /// Whether an entry belongs to the plan a run names: the plan id Import stamps, or
    /// the plan tag an entry typed by hand carries. The sigil is trimmed on both sides,
    /// and a reference that names no plan matches on the item id alone.
    /// </summary>
    private static bool InPlan(EntryRow row, string? plan)
    {
        if (plan is null) return true;

        var wanted = plan.TrimStart('+');

        return string.Equals((row.ImportPlanId ?? string.Empty).TrimStart('+'), wanted, StringComparison.OrdinalIgnoreCase)
            || row.PreviewTags.Any(tag => string.Equals(tag.TrimStart('+'), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A task as a reference another pane can hand back: the title Tasks gave
    /// it, and its id to open it by.</summary>
    private static DeliveryRunReference? TaskRunReference(TaskReference? task) =>
        task is null
            ? null
            : new DeliveryRunReference(
                DeliveryRunReferenceKind.Task,
                task.Title,
                Title: null,
                Url: null,
                Repository: null,
                EntryId: task.EntryId);
}
