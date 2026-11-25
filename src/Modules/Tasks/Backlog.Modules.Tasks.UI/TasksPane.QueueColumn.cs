using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.UI.Components.Badges;
using Backlog.UI.Components.Feedback;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The session queue docked as a column on the right of the Tasks list (local ADR 0062
/// §6; <c>.devbook/domain/sessions/features.md#start-a-session-from-an-entry</c>): the
/// same queue the bar's popover lists, read from the same polls and moved, cancelled and
/// paused through the same handlers, grouped as Needs you, Running, Up next, Landed today
/// and Long-running agents. At or under <see cref="ListNarrowRem"/> the column folds back
/// into the popover, the one form drawn at a time.
/// </summary>
public partial class TasksPane
{
    /// <summary>The heading the focus goes to when a Cancel or a move leaves the column
    /// with no row control to hold it — the column's stand-in for the popover's trigger.</summary>
    internal const string SessionQueueColumnHeadingId = "session-queue-column-heading";

    /// <summary>What needs the person and the Manager's slots, or null where the host
    /// composed no reader.</summary>
    private ISessionQueueAttention? _sessionAttention;

    /// <summary>The last answer <see cref="_sessionAttention"/> gave.</summary>
    private SessionQueueAttention _attention = SessionQueueAttention.None;

    /// <summary>Moved by every read of <see cref="_sessionAttention"/>, so a slow read that
    /// answers after a newer one is dropped rather than drawing the older answer.</summary>
    private int _attentionVersion;

    /// <summary>The drafts the person dismissed from Needs you in this pane, by
    /// <see cref="SessionQueuePullRequest.Key"/>. Kept for the pane's life only: dismissing
    /// says "not now", and the pull request is still a draft on GitHub.</summary>
    private readonly HashSet<string> _dismissedDrafts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The entry the last column item selected was for, or null. It reads as
    /// selected while the list is narrowed to it, or while it is the open entry.</summary>
    private Guid? _queueSelectedEntry;

    /// <summary>Whether the queue is docked beside the list: it is offered, the list is
    /// grouped as Plans or Flat by the host, and the column is wider than 48rem. The Board
    /// and a host that picks no grouping keep the popover.</summary>
    private bool QueueDocked =>
        SessionQueueOffered
        && Layout == TasksLayout.List
        && ListGrouping is not null
        && !ListNarrow;

    /// <summary>The list column's classes: the Plans grid while the rail stands beside the
    /// rows, the queue grid while the queue is docked, both at once in Plans.</summary>
    private string BacklogListCssClass =>
        string.Join(' ', new[]
        {
            "backlog-list",
            PlanRailBeside ? "backlog-list--plans" : null,
            QueueDocked ? "backlog-list--queue" : null,
        }.Where(name => name is not null));

    /// <summary>Lets go of the entry a column item narrowed the list to where no queue
    /// says so — the Board, or a host that picks no grouping. A column folded at 48rem keeps
    /// it, and the bar carries its Show all instead.</summary>
    private void LetGoOfQueueFocusWhenFolded()
    {
        if ((Layout != TasksLayout.List || ListGrouping is null) && State.QueueFocusEntry is not null)
        {
            _queueSelectedEntry = null;
            State.SetQueueFocusEntry(null);
        }
    }

    /// <summary>The title of the entry the list is narrowed to from the queue, or null.</summary>
    private string? SessionQueueFocusName() =>
        State.QueueFocusEntry is not { } focusId
            ? null
            : State.Rows.FirstOrDefault(row => row.Id == focusId) is { } row && !string.IsNullOrWhiteSpace(row.PreviewTitle)
                ? row.PreviewTitle
                : SessionQueue.Untitled;

    /// <summary>Reads what needs the person again and redraws: on the pane's start, on
    /// every poll of the requests while the column is docked, and when it docks again.
    /// The answer is written on the renderer's thread, and only when no newer read set out
    /// after it.</summary>
    private async Task RefreshSessionAttentionAsync()
    {
        if (_sessionAttention is null || !SessionQueueOffered) return;

        var version = Interlocked.Increment(ref _attentionVersion);
        SessionQueueAttention answer;

        try
        {
            answer = await _sessionAttention.ReadAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            answer = SessionQueueAttention.None;
        }

        try
        {
            await InvokeAsync(() =>
            {
                if (version != Volatile.Read(ref _attentionVersion)) return;

                _attention = answer;
                StateHasChanged();
            }).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The pane went while the read was out.
        }
    }

    /// <summary>The header dot's tone, in the library's step tones: online is active,
    /// silent blocked, not heard yet pending.</summary>
    private static FlowStepTone ManagerDotTone(DispatcherContactReading contact) => SessionQueue.ManagerTone(contact) switch
    {
        "online" => FlowStepTone.Active,
        "silent" => FlowStepTone.Blocked,
        _ => FlowStepTone.Pending
    };

    /// <summary>Whether a loaded entry carries the plan <paramref name="bareTag"/>: the docked
    /// column can select such a plan on the rail even where the bar has no chip for it —
    /// Flat lists no plan, so its bar carries none.</summary>
    private bool PlanIsLoaded(string bareTag) =>
        State.Rows.Any(row => PlanEntryState.PlanTagOf(row) is { } tag
            && string.Equals(tag.TrimStart('+'), bareTag, StringComparison.OrdinalIgnoreCase));

    /// <summary>The drafts awaiting the person's validation, minus the dismissed ones.</summary>
    private IReadOnlyList<SessionQueuePullRequest> SessionQueueDrafts() =>
        SessionQueue.Drafts(State.Rows, _dismissedDrafts);

    /// <summary>What merged today.</summary>
    private IReadOnlyList<SessionQueuePullRequest> SessionQueueLanded() =>
        SessionQueue.Landed(State.Rows, Today);

    private void OnSessionQueueDraftDismissed(SessionQueuePullRequest draft) => _dismissedDrafts.Add(draft.Key);

    /// <summary>Answer pressed: the session that asked opens, where its question is
    /// answered (local ADR 0041).</summary>
    private Task OnSessionQueueAnswerAsync(SessionQueueQuestion question) =>
        OnOpenSession.HasDelegate ? OnOpenSession.InvokeAsync(question.SessionId) : Task.CompletedTask;

    /// <summary>Whether a column item is the one the list is filtered to: an entry's while
    /// the list is narrowed to it or it is open from the column; a plan's while Plans shows
    /// that plan.</summary>
    private bool IsSessionQueueItemSelected(SessionQueueRow row) =>
        row.EntryId is { } entryId
            ? IsSessionQueueEntrySelected(entryId)
            : row.PlanTag is { } bare && IsSessionQueuePlanSelected(bare);

    private bool IsSessionQueueEntrySelected(Guid entryId) =>
        State.QueueFocusEntry == entryId
        || (_queueSelectedEntry == entryId && State.SelectedRow?.Id == entryId);

    private bool IsSessionQueuePlanSelected(string bareTag) =>
        PlansGrouped
        && State.SelectedPlan is { } plan
        && string.Equals(plan.TrimStart('+'), bareTag, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A column item selected: the list is filtered to it (local ADR 0062 §6), and
    /// selecting it again lets go. An entry request is an entry; a plan request selects
    /// that plan on the rail, switching Flat to Plans first, since Flat lists no plan.
    /// </summary>
    private async Task OnSessionQueueSelectAsync(SessionQueueRow row)
    {
        if (!row.InView) return;

        if (row.EntryId is { } entryId)
        {
            await SelectSessionQueueEntryAsync(entryId);
            return;
        }

        if (row.PlanTag is not { } bare) return;

        _queueSelectedEntry = null;
        State.SetQueueFocusEntry(null);

        if (IsSessionQueuePlanSelected(bare))
        {
            State.SelectRailItem(PlanRailItem.Plan, QueuePlanTag(bare), Today);
            return;
        }

        await ShowSessionQueuePlanAsync(bare);
    }

    /// <summary>A draft or a landed entry selected: the list filtered to its entry, as an
    /// entry request's is.</summary>
    private Task OnSessionQueuePullRequestSelectAsync(SessionQueuePullRequest item) => SelectSessionQueueEntryAsync(item.EntryId);

    /// <summary>
    /// Filters the list to one entry, or lets go when it already is. An entry in no plan
    /// narrows the list to itself — off a plan's sections first, which list a plan rather
    /// than the rows — and stays closed, so the list and the queue stay side by side: the
    /// open entry would take the width the column needs. A plan entry is listed only under
    /// its plan, so its plan is shown in Plans instead, Flat switched over first, and the
    /// entry opens there to say which of the plan's rows it is.
    /// </summary>
    private async Task SelectSessionQueueEntryAsync(Guid entryId)
    {
        if (IsSessionQueueEntrySelected(entryId))
        {
            _queueSelectedEntry = null;
            State.SetQueueFocusEntry(null);
            return;
        }

        var entry = State.Rows.FirstOrDefault(candidate => candidate.Id == entryId);
        _queueSelectedEntry = entryId;

        if (entry is not null && PlanEntryState.PlanTagOf(entry) is { } plan)
        {
            State.SetQueueFocusEntry(null);
            await ShowSessionQueuePlanAsync(plan.TrimStart('+'));
            await OnEntrySelectedAsync(EntryTaskId(entry));
            return;
        }

        if (PlanSectionsShown) State.SelectRailItem(PlanRailItem.Unplanned, null, Today);
        State.SetQueueFocusEntry(entryId);

        // The scopes that would hide it step aside, as a followed name's do — the Flat
        // bar's Open tab hides a landed entry — and the open entry is closed.
        if (entry is not null)
        {
            State.Reveal(entry);
            if (State.SelectedRow is not null) await State.SelectAsync(null);
        }
    }

    /// <summary>Shows the plan <paramref name="bareTag"/> in Plans: Flat switched over first,
    /// and the plan selected on the rail unless it already is.</summary>
    private async Task ShowSessionQueuePlanAsync(string bareTag)
    {
        if (ListGrouping != TaskListGrouping.Plans) await OnListGroupingPickedAsync(TaskListGrouping.Plans);

        var tag = QueuePlanTag(bareTag);

        if (TagText.Kind(tag) is TagKind.Plan && !State.IsRailItemSelected(PlanRailItem.Plan, tag))
        {
            State.SelectRailItem(PlanRailItem.Plan, tag, Today);
        }
    }

    /// <summary>The plan's tag as the bar writes it, or its bare tag with the sigil.</summary>
    private string QueuePlanTag(string bareTag) => PlanChipFor(bareTag)?.Value ?? $"+{bareTag}";

    /// <summary>The column's Show all: lets go of the entry it filtered the list to.</summary>
    private void OnSessionQueueFocusCleared()
    {
        _queueSelectedEntry = null;
        State.SetQueueFocusEntry(null);
    }

    /// <summary>The second line of a Running or Up next row: what it is and where it runs.</summary>
    private static string SessionQueueRowDetail(SessionQueueRow row) =>
        $"{(row.EntryId is null ? "Plan" : "Entry")} · {row.Request.Repository}";
}
