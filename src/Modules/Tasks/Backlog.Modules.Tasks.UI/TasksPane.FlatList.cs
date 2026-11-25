using Backlog.Modules.Tasks.Abstractions;
using Backlog.UI.Components.Badges;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The Tasks list's <b>Flat</b> view (local ADR 0062 §3;
/// <c>.devbook/domain/tasks/features.md#plans-view</c>): the entries in no plan, plus a
/// plan entry in today's My Day, under a quiet bar of text tabs — the quick views, the
/// Type, Source and #tag menus, the status tabs and the sort — and, while rows are
/// picked, one line of text actions in place of the bulk bar.
/// </summary>
public partial class TasksPane
{
    /// <summary>The Flat bar's quick views, in the order drawn.</summary>
    internal enum FlatQuickView
    {
        Everything,
        MyDay,
        InTheQueue,
        Waiting,
    }

    /// <summary>The Flat bar's status tabs: its label and the state's wire.</summary>
    private static readonly (string Label, string Wire)[] FlatStatusTabs =
    [
        ("Open", TasksDesktopState.OpenStatusWire),
        ("Draft", "draft"),
        ("Ready", "ready"),
        ("In progress", "in_progress"),
        ("Done", "done"),
    ];

    /// <summary>The statuses the selection's Status menu sets.</summary>
    private static readonly (string Label, EntryStatus Status)[] FlatSelectionStatuses =
    [
        ("Draft", EntryStatus.Draft),
        ("Ready", EntryStatus.Ready),
        ("In progress", EntryStatus.InProgress),
        ("Done", EntryStatus.Done),
    ];

    /// <summary>The name typed into Make a plan, before it is a tag.</summary>
    private string _flatNewPlanName = string.Empty;

    /// <summary>Whether the Flat view is drawn: the list, as the host's Flat choice. A
    /// pane with no host choice keeps the chip bar over every entry.</summary>
    private bool FlatListed => Layout == TasksLayout.List && ListGrouping == TaskListGrouping.Flat;

    /// <summary>
    /// Puts the state's Flat scope in step with the view. Entering Flat lets go of the
    /// scopes its bar draws no control for — Ready in a plan, Next actions and its area,
    /// and the rail's plan or Unplanned — so nothing narrows the list out of sight;
    /// leaving it lets go of In the queue.
    /// </summary>
    private void SyncFlatScope()
    {
        if (FlatListed)
        {
            if (State.FlatScopeOn is null)
            {
                State.SetReadyInPlanFilter(false);
                State.SetNextActionsFilter(false);
                State.LetGoOfPlanSelection();

                // Open is the Flat bar's first status tab and the one it opens on.
                if (string.IsNullOrEmpty(State.SelectedStatusFilterWire)) State.SetStatusFilter(TasksDesktopState.OpenStatusWire);
            }

            State.SetFlatScope(Today);
        }
        else
        {
            if (State.FlatScopeOn is not null && State.SelectedStatusFilterWire == TasksDesktopState.OpenStatusWire) State.SetStatusFilter(null);
            State.SetQueuedFilter(null);
            State.SetFlatScope(null);
        }
    }

    /// <summary>The rows the Flat view lists from, before its bar narrows them: the
    /// repository scope's rows in the Flat scope.</summary>
    private IEnumerable<EntryRow> FlatPool() =>
        State.ScopedRows.Where(row => State.InFlatScope(row, Today));

    /// <summary>Whether an entry waits for a session of its own.</summary>
    private bool IsQueuedForSession(EntryRow row) =>
        row.Id is { } id && _sessionRequests.OpenFor(id) is not null;

    /// <summary>The quick view in force: none of the three scopes is Everything.</summary>
    private FlatQuickView FlatQuickViewInForce =>
        State.MyDayOn is not null ? FlatQuickView.MyDay
        : State.QueuedOnly ? FlatQuickView.InTheQueue
        : State.WaitingForOnly ? FlatQuickView.Waiting
        : FlatQuickView.Everything;

    /// <summary>A quick view's label.</summary>
    private static string FlatQuickViewLabel(FlatQuickView view) => view switch
    {
        FlatQuickView.MyDay => "My Day",
        FlatQuickView.InTheQueue => "In the queue",
        FlatQuickView.Waiting => "Waiting",
        _ => "Everything",
    };

    /// <summary>A quick view's count off the Flat pool, or null for Everything, which
    /// carries none.</summary>
    private int? FlatQuickViewCount(FlatQuickView view) => view switch
    {
        FlatQuickView.MyDay => FlatPool().Count(row => !row.IsPreviewCompleted && IsInMyDay(row)),
        FlatQuickView.InTheQueue => FlatPool().Count(IsQueuedForSession),
        FlatQuickView.Waiting => FlatPool().Count(State.IsWaitingForSomeone),
        _ => null,
    };

    /// <summary>One quick view at a time: picking one lets go of the other two.</summary>
    private void OnFlatQuickViewPicked(FlatQuickView view)
    {
        State.SetMyDayFilter(view is FlatQuickView.MyDay ? Today : null);
        State.SetQueuedFilter(view is FlatQuickView.InTheQueue ? IsQueuedForSession : null);
        State.SetWaitingForFilter(view is FlatQuickView.Waiting);
    }

    /// <summary>The general tags and people the #tag menu offers: never a plan, which
    /// lives under Plans.</summary>
    private IReadOnlyList<TagFilterOption> FlatTags =>
        [.. State.TagFilters.Where(tag => tag.Value != TasksDesktopState.NoPlanTag && TagText.Kind(tag.Value) is not TagKind.Plan)];

    /// <summary>How many unplanned open entries the bar leaves out of the list.</summary>
    private int FlatHiddenUnplannedCount
    {
        get
        {
            var shown = new HashSet<EntryRow>(State.FilteredRows, ReferenceEqualityComparer.Instance);

            return State.ScopedRows.Count(row => !row.IsPreviewCompleted && TasksDesktopState.IsUnplanned(row) && !shown.Contains(row));
        }
    }

    /// <summary>The Flat list's heading: how many open entries are in no plan, and the
    /// one way a plan entry joins them.</summary>
    private string FlatHeadingText =>
        $"Unplanned {State.ScopedRows.Count(row => !row.IsPreviewCompleted && TasksDesktopState.IsUnplanned(row))} · plan entries only from My Day";

    /// <summary>The Flat list's footer.</summary>
    private string FlatFooterText =>
        FlatHiddenUnplannedCount is var hidden and > 0
            ? $"{hidden} more unplanned · plan entries live under Plans"
            : "Plan entries live under Plans";

    /// <summary>A row's plan, bare, when the Flat list shows it only because it is in
    /// today's My Day; null for every other row.</summary>
    private string? FlatMyDayPlan(EntryRow row) =>
        FlatListed && State.IsPlanEntryOnMyDay(row, Today) && PlanEntryState.PlanTagOf(row) is { } tag
            ? PlanQueueOffer.Bare(tag)
            : null;

    /// <summary>Every plan the backlog holds, <c>+</c> and all, for Add to plan.</summary>
    private IReadOnlyList<string> FlatPlanTags() =>
        [.. State.Rows
            .Select(PlanEntryState.PlanTagOf)
            .OfType<string>()
            .Concat(State.RoadmapTags.Where(tag => TagText.Kind(tag) is TagKind.Plan))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)];

    /// <summary>The plan tag a typed name makes: lower-cased, its spaces dashes, one
    /// leading <c>+</c>. Null when nothing is left.</summary>
    internal static string? PlanTagFor(string? name)
    {
        var words = (name ?? string.Empty).Trim().TrimStart('+').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return words.Length == 0 ? null : "+" + string.Join('-', words).ToLowerInvariant();
    }

    /// <summary>Make a plan: the picked rows filed under a new plan, which takes them
    /// out of Flat and under Plans.</summary>
    private async Task OnFlatMakePlanAsync()
    {
        if (PlanTagFor(_flatNewPlanName) is not { } tag) return;

        await ApplyBulkAsync(() => State.BulkAddTagsAsync([tag]), act: BulkAct.AddTags);
        _flatNewPlanName = string.Empty;
    }

    /// <summary>Add to plan: the picked rows filed under a plan the backlog already holds.</summary>
    private Task OnFlatAddToPlanAsync(string plan) =>
        ApplyBulkAsync(() => State.BulkAddTagsAsync([plan]), act: BulkAct.AddTags);

    /// <summary>Status: one status across the picked rows.</summary>
    private Task OnFlatSelectionStatusAsync(EntryStatus status) =>
        ApplyBulkAsync(() => State.BulkChangeStatusAsync(status));

    /// <summary>The picked rows whose own queue is offered: an unplanned prompt in one
    /// repository, not already waiting. A plan entry is queued only with its plan.</summary>
    private IReadOnlyList<EntryRow> FlatQueueableSelection() =>
        [.. State.SelectedRows.Where(row =>
            row.Id is { } id
            && _sessionRequests.OpenFor(id) is null
            && SessionQueueOfferFor(row) is { IsEnabled: true, Only: not null })];

    /// <summary>Queue each: one session request per picked row the queue takes.</summary>
    private async Task OnFlatQueueEachAsync()
    {
        foreach (var row in FlatQueueableSelection())
        {
            if (row.Id is { } id && SessionQueueOfferFor(row) is { Only: { } only })
            {
                await QueueSessionInAsync(id, only);
            }
        }
    }
}
