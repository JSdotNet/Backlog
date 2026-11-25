using System.Globalization;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.UI.Components.Badges;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>The kinds of item on the Plans view's rail (local ADR 0062 §2), top to bottom.</summary>
public enum PlanRailItem
{
    /// <summary>Today's My Day.</summary>
    MyDay,

    /// <summary>The entries in a session in any plan.</summary>
    NowAcrossPlans,

    /// <summary>One plan.</summary>
    Plan,

    /// <summary>The entries in no plan.</summary>
    Unplanned
}

/// <summary>A plan's run state as its rail row words it (local ADR 0062 §2).</summary>
public enum PlanRunState
{
    /// <summary>Nothing to say: no request, nothing ready, blocked or all draft.</summary>
    None,

    /// <summary>A dispatcher holds the plan's request, or its session started.</summary>
    Running,

    /// <summary>The plan's request waits in the session queue, at a place.</summary>
    Queued,

    /// <summary>An open entry of the plan is marked blocked and none is ready.</summary>
    Blocked,

    /// <summary>Entries of the plan are ready to pick up.</summary>
    Ready,

    /// <summary>Every open entry of the plan is still a draft.</summary>
    Draft
}

/// <summary>One plan on the rail.</summary>
/// <param name="Tag">The plan tag, <c>+</c> and all, as the tag filter's value.</param>
/// <param name="Name">The plan's name: the tag without its sigil.</param>
/// <param name="Done">The plan's finished entries.</param>
/// <param name="Total">All its entries, open and finished.</param>
/// <param name="State">Its run state.</param>
/// <param name="QueuePlace">Its 1-based place in the queue, for <see cref="PlanRunState.Queued"/>.</param>
/// <param name="ReadyCount">How many of its open entries are ready and not marked blocked.</param>
public sealed record PlanRailPlan(
    string Tag,
    string Name,
    int Done,
    int Total,
    PlanRunState State,
    int QueuePlace,
    int ReadyCount)
{
    /// <summary>The run state as the row's coloured text — <c>Running</c>,
    /// <c>Queued 2nd</c>, <c>Blocked</c>, <c>3 ready</c>, <c>Draft</c> — or empty.</summary>
    public string StateText => State switch
    {
        PlanRunState.Running => "Running",
        PlanRunState.Queued => QueuePlace > 0 ? $"Queued {PlanRail.Ordinal(QueuePlace)}" : "Queued",
        PlanRunState.Blocked => "Blocked",
        PlanRunState.Ready => $"{ReadyCount} ready",
        PlanRunState.Draft => "Draft",
        _ => string.Empty,
    };

    /// <summary>How far along the plan is, 0 to 100, for its progress line.</summary>
    public int ProgressPercent => Total <= 0 ? 0 : (int)Math.Round(Done * 100.0 / Total, MidpointRounding.AwayFromZero);
}

/// <summary>
/// The Plans view's rail (local ADR 0062 §2): which plans it lists and the run state
/// each one's row words. Pure, so the counts and states are answered from what the pane
/// already holds — the tag filter's plan chips, the rows in scope, the plans' open
/// session requests and the queue's order — and never read again.
/// </summary>
public static class PlanRail
{
    /// <summary>
    /// The rail's plans, in the tag strip's order: every plan chip the bar would offer,
    /// counted as the chip counts it, with its run state.
    /// </summary>
    /// <param name="tagFilters">The bar's tag filter options; the plan ones are kept.</param>
    /// <param name="scopedRows">The rows the repository scope leaves in view.</param>
    /// <param name="planRequests">The plans' open session requests, by bare tag.</param>
    /// <param name="queue">The session queue's rows in queue order: a Requested plan's
    /// place is counted among the Requested rows.</param>
    public static IReadOnlyList<PlanRailPlan> Plans(
        IReadOnlyList<TagFilterOption> tagFilters,
        IReadOnlyList<EntryRow> scopedRows,
        IReadOnlyDictionary<string, EntrySessionRequest> planRequests,
        IReadOnlyList<SessionQueueRow> queue)
    {
        ArgumentNullException.ThrowIfNull(tagFilters);
        ArgumentNullException.ThrowIfNull(scopedRows);
        ArgumentNullException.ThrowIfNull(planRequests);
        ArgumentNullException.ThrowIfNull(queue);

        var waiting = queue.Where(row => row.CanMove).ToList();
        var plans = new List<PlanRailPlan>();

        foreach (var option in tagFilters)
        {
            if (TagText.Kind(option.Value) is not TagKind.Plan) continue;

            var bare = option.Value.TrimStart('+');
            var open = scopedRows
                .Where(row => !row.IsPreviewCompleted && row.PreviewTags.Contains(option.Value, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var ready = open.Count(row => row.PreviewStatus is EntryStatus.Ready && !row.IsPreviewBlocked);
            var place = waiting.FindIndex(row => string.Equals(row.PlanTag, bare, StringComparison.OrdinalIgnoreCase)) + 1;
            planRequests.TryGetValue(bare, out var request);

            var state = RunState(request?.State, open, ready);

            plans.Add(new PlanRailPlan(
                option.Value,
                bare,
                option.CompletedCount,
                option.OpenCount + option.CompletedCount,
                state,
                state is PlanRunState.Queued ? place : 0,
                ready));
        }

        return plans;
    }

    /// <summary>A plan's run state: its request first — Running once a dispatcher holds
    /// it, Queued while it waits — then its open entries: Blocked when one is marked
    /// blocked and none is ready, the ready count when any is, Draft when all are drafts.</summary>
    internal static PlanRunState RunState(EntrySessionRequestState? request, IReadOnlyList<EntryRow> open, int ready)
    {
        if (request is EntrySessionRequestState.Claimed or EntrySessionRequestState.Started) return PlanRunState.Running;
        if (request is EntrySessionRequestState.Requested) return PlanRunState.Queued;
        if (ready == 0 && open.Any(row => row.IsPreviewBlocked)) return PlanRunState.Blocked;
        if (ready > 0) return PlanRunState.Ready;
        if (open.Count > 0 && open.All(row => row.PreviewStatus is EntryStatus.Draft)) return PlanRunState.Draft;

        return PlanRunState.None;
    }

    /// <summary>An English ordinal: 1st, 2nd, 3rd, 4th, 11th, 21st.</summary>
    public static string Ordinal(int number)
    {
        var suffix = (number % 100) is 11 or 12 or 13
            ? "th"
            : (number % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };

        return number.ToString(CultureInfo.InvariantCulture) + suffix;
    }
}
