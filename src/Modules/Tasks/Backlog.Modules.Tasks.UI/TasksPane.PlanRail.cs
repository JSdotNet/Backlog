using Backlog.UI.Components.Badges;
using Backlog.UI.Components.Goals;

using Microsoft.AspNetCore.Components;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The Tasks list's <b>Plans / Flat</b> switch and the Plans view's rail (local ADR 0062
/// §1, §2; <c>.devbook/domain/tasks/features.md#plans-view</c>).
/// <para>
/// The switch is the window's view choice: the host remembers it beside the Board's
/// columns and hands it back as <see cref="ListGrouping"/>, opening on Plans the first
/// time. In Plans the rail stands on the left of the list and takes the plan chips' place
/// on the tag strip — the people and the general tags stay there — and at 48rem or
/// narrower it folds into the bar's Filter menu.
/// </para>
/// </summary>
public partial class TasksPane
{
    /// <summary>How the list groups, as the host remembers it; the shell's own default —
    /// the first time — is Plans (<see cref="ITasksViewPreferences.ListGrouping"/>).
    /// Null for a pane drawn with no host choice: one list of every entry under the
    /// chip bar, neither the Plans rail nor Flat's unplanned-only list. The switch
    /// reads it as Flat, which it was before Flat listed only the unplanned.</summary>
    [Parameter]
    public TaskListGrouping? ListGrouping { get; set; }

    /// <summary>Raised with the grouping the reader picked, for the host to remember.</summary>
    [Parameter]
    public EventCallback<TaskListGrouping> ListGroupingChanged { get; set; }

    /// <summary>Whether the Plans view is drawn: the list, grouped by plan. The Board
    /// is unchanged by the switch.</summary>
    private bool PlansGrouped => Layout == TasksLayout.List && ListGrouping == TaskListGrouping.Plans;

    /// <summary>Whether the rail stands beside the list: Plans on a wide column. On a
    /// narrow one it is drawn inside the Filter menu instead — one form, never both.</summary>
    private bool PlanRailBeside => PlansGrouped && !ListNarrow;

    private async Task OnListGroupingPickedAsync(TaskListGrouping grouping)
    {
        if (grouping == ListGrouping) return;

        // Taken at once, so the list answers the press before the host has written
        // the choice down and handed it back.
        ListGrouping = grouping;
        if (!PlansGrouped) State.SetNowAcrossPlansFilter(false);
        SyncFlatScope();
        await ListGroupingChanged.InvokeAsync(grouping);
    }

    /// <summary>The tags the strip draws: every one in Flat; in Plans, all but the
    /// plans and No plan, which the rail lists.</summary>
    private IReadOnlyList<TagFilterOption> StripTags =>
        PlansGrouped
            ? State.TagFilters.Where(tag => tag.Value != TasksDesktopState.NoPlanTag && TagText.Kind(tag.Value) is not TagKind.Plan).ToList()
            : State.TagFilters;

    /// <summary>
    /// What names the open impediment holding a Blocked plan back, as the host cascades it
    /// (local ADR 0035 §5): an impediment is Progress's, and this pane may not reach it
    /// (§6). With none cascaded a Blocked plan reads plain Blocked.
    /// </summary>
    [CascadingParameter]
    public PlanImpedimentSource? PlanImpediments { get; set; }

    private PlanImpedimentSource? _watchedPlanImpediments;
    private IDisposable? _planImpedimentsWatch;

    /// <summary>Watches the cascaded source, so the rail and the folded lines draw again
    /// when an impediment is raised, linked or resolved; a different source replaces the
    /// subscription, the same one keeps it.</summary>
    private void FollowPlanImpediments()
    {
        if (ReferenceEquals(PlanImpediments, _watchedPlanImpediments)) return;

        _planImpedimentsWatch?.Dispose();
        _watchedPlanImpediments = PlanImpediments;
        _planImpedimentsWatch = PlanImpediments?.Watch(HandleSessionRequestsChanged);
    }

    private void StopFollowingPlanImpediments()
    {
        _planImpedimentsWatch?.Dispose();
        _planImpedimentsWatch = null;
        _watchedPlanImpediments = null;
    }

    /// <summary>The rail's plans, with their counts and run states — a Blocked one with
    /// the impediment that holds it or one of its open entries back.</summary>
    private IReadOnlyList<PlanRailPlan> RailPlans() =>
        PlanRail.Plans(State.TagFilters, State.ScopedRows, _planSessions.Open, SessionQueueRows(), PlanImpedimentOf);

    private string? PlanImpedimentOf(string plan, IReadOnlyList<EntryRow> open) =>
        PlanImpediments?.TitleOf(plan, [.. open.Where(row => row.Id is not null).Select(row => row.Id!.Value)]);

    /// <summary>How many open entries are plan work in a session, off the rows the
    /// repository scope leaves in view — the pool the bar's scope chips count.</summary>
    private int NowAcrossPlansCount => State.ScopedRows.Count(State.IsInPlanSession);

    /// <summary>How many open entries are in no plan, off the same pool.</summary>
    private int UnplannedCount => State.ScopedRows.Count(row => !row.IsPreviewCompleted && TasksDesktopState.IsUnplanned(row));

    private void OnRailItemPressed(PlanRailItem item, string? planTag = null) =>
        State.SelectRailItem(item, planTag, Today);

    /// <summary>The coloured text's slug, by run state: the badge wears it as
    /// <c>plan-rail__state--&lt;slug&gt;</c>.</summary>
    private static string RailStateSlug(PlanRunState state) => state switch
    {
        PlanRunState.Running => "running",
        PlanRunState.Queued => "queued",
        PlanRunState.Blocked => "blocked",
        _ => "quiet",
    };

    /// <summary>A plan row's accessible name: its name, progress and state.</summary>
    private static string RailPlanLabel(PlanRailPlan plan) =>
        string.IsNullOrEmpty(plan.StateText)
            ? $"{plan.Name}, {plan.Done} of {plan.Total} done"
            : $"{plan.Name}, {plan.Done} of {plan.Total} done, {plan.StateTitle}";
}
