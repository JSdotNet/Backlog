using System.Globalization;

using Backlog.Modules.Tasks.Abstractions;
using Backlog.UI.Components.Badges;
using Backlog.UI.Components.Selects;
using Backlog.UI.Components.Tasks;

using Microsoft.AspNetCore.Components;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The Board layout of the Tasks pane: the filtered rows grouped into columns,
/// one <see cref="TaskCard"/> each.
/// <para>
/// The grouping is here and not in <see cref="TaskBoard"/>, because every
/// grouping is a reading of an entry — its status, its plan tags, its
/// repository, its priority — and the board is a shared component that knows
/// none of them. What a drop means is here for the same reason: the lifecycle
/// graph is <see cref="EntryStatusFlow"/>, and the board only asks.
/// </para>
/// </summary>
public partial class TasksPane
{
    /// <summary>The Board's columns in the order they are drawn, while they are by
    /// status. Archived has none: it is the state past Done that the board's
    /// reader has put away.</summary>
    private static readonly EntryStatus[] BoardStatuses =
        [EntryStatus.Draft, EntryStatus.Ready, EntryStatus.InProgress, EntryStatus.Done];

    /// <summary>Highest first, the order a reader scans a priority board in.</summary>
    private static readonly Priority[] BoardPriorities =
        [Priority.Critical, Priority.High, Priority.Medium, Priority.Low];

    private const string NoPlanColumn = "plan:";

    /// <summary>Keys no tag or alias can produce — both start with a letter — so
    /// a repository aliased "none" is never the bucket of entries with none.</summary>
    private const string NoRepositoryColumn = "repo:";

    private static readonly IReadOnlyList<SelectorOption> BoardGroupingOptions =
    [
        new(nameof(TaskBoardGrouping.Status), "Status"),
        new(nameof(TaskBoardGrouping.Plan), "Plan"),
        new(nameof(TaskBoardGrouping.Repository), "Repository"),
        new(nameof(TaskBoardGrouping.Priority), "Priority")
    ];

    /// <summary>The board while the pane is in its Board layout; the focus goes
    /// back to the open entry's card where it would go back to a row.</summary>
    private TaskBoard? _board;

    /// <summary>What the Board's columns are. The shell holds the choice, beside
    /// the view it belongs to, and hands it back here.</summary>
    [Parameter]
    public TaskBoardGrouping BoardGrouping { get; set; } = TaskBoardGrouping.Status;

    /// <summary>Raised when the reader picks another grouping from "Columns".</summary>
    [Parameter]
    public EventCallback<TaskBoardGrouping> BoardGroupingChanged { get; set; }

    /// <summary>The Status radiogroup is off the bar while the Board's columns are
    /// the statuses.</summary>
    private bool StatusFilterHidden => Layout == TasksLayout.Board && BoardGrouping == TaskBoardGrouping.Status;

    private string BoardGroupingWord => BoardGrouping.ToString().ToLowerInvariant();

    protected override void OnParametersSet()
    {
        // Every status is a column while the columns are by status, so the filter
        // that would narrow to one is set aside for as long as that holds.
        State.SetStatusFilterSuspended(StatusFilterHidden);
    }

    private async Task OnBoardGroupingPickedAsync(string? value)
    {
        if (!Enum.TryParse<TaskBoardGrouping>(value, out var grouping) || grouping == BoardGrouping) return;

        // Taken at once, so the board answers the select before the shell has
        // written the choice down and handed it back.
        BoardGrouping = grouping;
        State.SetStatusFilterSuspended(StatusFilterHidden);
        await BoardGroupingChanged.InvokeAsync(grouping);
    }

    /// <summary>
    /// The filtered rows, as the board's columns under the current grouping.
    /// <para>
    /// Status and priority draw every column whatever is in it — the vocabulary is
    /// fixed, and an empty Done column is a fact. Plans and repositories draw a
    /// column per value the rows in view carry, in their own order, with the rows
    /// that carry none last; an entry under two plans is a card in both, the way
    /// the tag chips count it twice.
    /// </para>
    /// </summary>
    private IReadOnlyList<TaskBoardColumn> BoardColumns()
    {
        var rows = State.FilteredRows;
        var tasks = ToTaskRows(rows);
        var chain = TaskChain.Resolve(tasks, DependencyUniverse())
            .ToDictionary(status => status.Id, StringComparer.Ordinal);

        var cards = rows
            .Select((row, index) => (Row: row, Card: BoardCard(row, tasks[index], chain)))
            .ToList();

        return BoardGrouping switch
        {
            TaskBoardGrouping.Plan => PlanColumns(cards),
            TaskBoardGrouping.Repository => RepositoryColumns(cards),
            TaskBoardGrouping.Priority =>
            [
                .. BoardPriorities.Select(priority => new TaskBoardColumn(
                    priority.ToString(),
                    priority.ToString(),
                    [.. cards.Where(pair => pair.Row.PreviewPriority == priority).Select(pair => pair.Card)],
                    $"task-board__dot--{priority.ToString().ToLowerInvariant()}"))
            ],
            // An archived entry has no column: the reader put it away past Done.
            // The status filter is set aside here, so "All" would otherwise hand
            // it in; it still counts under the other groupings, which have no
            // column per status to leave it out of.
            _ =>
            [
                .. BoardStatuses.Select(status => new TaskBoardColumn(
                    status.ToString(),
                    StatusTitle(status),
                    [.. cards.Where(pair => BoardStatusOf(pair.Row) == status).Select(pair => pair.Card)],
                    $"task-board__dot--{StatusSlug(status)}",
                    CanAdd: true))
            ]
        };
    }

    private static IReadOnlyList<TaskBoardColumn> PlanColumns(List<(EntryRow Row, TaskBoardCard Card)> cards)
    {
        var plans = cards
            .SelectMany(pair => PlanTags(pair.Row))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var columns = plans
            .Select(plan => new TaskBoardColumn(
                "plan:" + plan,
                TagText.Display(plan),
                [.. cards.Where(pair => PlanTags(pair.Row).Contains(plan, StringComparer.OrdinalIgnoreCase)).Select(pair => pair.Card)]))
            .ToList();

        var unplanned = cards.Where(pair => !PlanTags(pair.Row).Any()).Select(pair => pair.Card).ToList();
        columns.Add(new TaskBoardColumn(NoPlanColumn, "No plan", unplanned));

        return columns;
    }

    private IReadOnlyList<TaskBoardColumn> RepositoryColumns(List<(EntryRow Row, TaskBoardCard Card)> cards)
    {
        var byAlias = cards
            .GroupBy(pair => RepositoryFor(pair.Row)?.Alias ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Card).ToList(), StringComparer.OrdinalIgnoreCase);

        // In the order the repositories are configured, the order every repository
        // control in the product lists them in; one the settings no longer name
        // still gets its column, after them.
        var configured = State.Repositories.Select(repository => repository.Alias).ToList();
        var aliases = configured
            .Where(byAlias.ContainsKey)
            .Concat(byAlias.Keys
                .Where(alias => alias.Length > 0 && !configured.Contains(alias, StringComparer.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase))
            .ToList();

        var columns = aliases
            .Select(alias => new TaskBoardColumn("repo:" + alias, alias, byAlias[alias]))
            .ToList();

        if (byAlias.TryGetValue(string.Empty, out var none))
        {
            columns.Add(new TaskBoardColumn(NoRepositoryColumn, "No repository", none));
        }

        return columns;
    }

    private static IEnumerable<string> PlanTags(EntryRow row) => row.PreviewTags.Where(TagText.IsPlan);

    private TaskBoardCard BoardCard(EntryRow row, TaskRow task, Dictionary<string, TaskChainStatus> chain) =>
        new(
            task,
            chain.GetValueOrDefault(task.Id),
            row.PreviewPriority.ToString(),
            RepositoryFor(row)?.Alias,
            row.PreviewEffort,
            Overdue: !row.IsPreviewCompleted && row.PreviewDueOn is { } due && due < Today,
            Finished: row.PreviewCompletedOn is { } finished ? finished.ToString("d", CultureInfo.CurrentCulture) : null,
            CssClass: State.RepositoryMarkClass(row));

    /// <summary>The column an entry sits in while the columns are by status. The
    /// tick wins over the lifecycle, as it does on the list and the Calendar: an
    /// entry ticked off is done whatever status its text still names.</summary>
    private static EntryStatus BoardStatusOf(EntryRow row) =>
        row.IsPreviewCompleted ? EntryStatus.Done : row.PreviewStatus;

    private static string StatusTitle(EntryStatus status) => status switch
    {
        EntryStatus.InProgress => "In progress",
        _ => status.ToString()
    };

    private static string StatusSlug(EntryStatus status) => status switch
    {
        EntryStatus.InProgress => "in-progress",
        _ => status.ToString().ToLowerInvariant()
    };

    /// <summary>
    /// What dropping an entry on a status column would do, by
    /// <see cref="EntryStatusFlow"/>: the move the graph allows says what it does,
    /// and the one it refuses says why, naming the step in between when there is
    /// one. Null outside the status grouping, where nothing is dragged.
    /// </summary>
    private TaskBoardDropRule? BoardDropRule(string taskId, string columnKey)
    {
        if (BoardGrouping != TaskBoardGrouping.Status
            || EntryFor(taskId) is not { } row
            || !Enum.TryParse<EntryStatus>(columnKey, out var to))
        {
            return null;
        }

        // Ticked off but still recorded as something before Done — an edit can
        // leave it so. The card sits in Done, but the move the graph is asked
        // about is the one the status change will actually make.
        return DropRule(row.PreviewStatus, to, started: row.PreviewStartedOn is not null);
    }

    /// <summary>The rule on its own, for the status a card is in and the column
    /// it is held over. Internal so the wording can be held to the graph edge by
    /// edge.
    /// <para>
    /// <paramref name="started"/> is whether the entry already carries a
    /// <c>started:</c> stamp. Any move into progress writes one when there is
    /// none — a reopen too, for a Done entry that never had one — and the slot
    /// says so on its second line; an entry that was started before keeps the day
    /// it has, so for it the slot does not promise a stamp the drop will not
    /// write.
    /// </para></summary>
    internal static TaskBoardDropRule DropRule(EntryStatus from, EntryStatus to, bool started = false)
    {
        if (EntryStatusFlow.IsAllowed(from, to))
        {
            var stamps = to == EntryStatus.InProgress && !started;

            return new TaskBoardDropRule(true, to switch
            {
                EntryStatus.InProgress when from == EntryStatus.Done => "Drop to reopen",
                EntryStatus.InProgress => "Drop to start",
                EntryStatus.Done => "Drop to finish",
                EntryStatus.Ready when from == EntryStatus.Draft => "Drop to mark ready",
                EntryStatus.Ready => "Drop to move back to Ready",
                EntryStatus.Draft => "Drop to move back to Draft",
                _ => $"Drop to move to {StatusTitle(to)}"
            }, stamps ? "Moves to In progress and stamps Started" : null);
        }

        // The step that would make the move possible: the first status this one
        // may go to from which the target is one move away.
        var via = EntryStatusFlow.NextFrom(from)
            .Cast<EntryStatus?>()
            .FirstOrDefault(next => EntryStatusFlow.IsAllowed(next!.Value, to));

        var reason = via is { } step
            ? StepWords(from, step) + " first"
            : "it can move to " + string.Join(" or ", EntryStatusFlow.NextFrom(from).Select(StatusTitle)) + " from here";

        return new TaskBoardDropRule(false, $"{StatusTitle(from)} can't move straight to {StatusTitle(to)} — {reason}");
    }

    /// <summary>The step from one status to the next, said as the thing to do.</summary>
    private static string StepWords(EntryStatus from, EntryStatus to) => to switch
    {
        EntryStatus.InProgress when from == EntryStatus.Done => "reopen it",
        EntryStatus.InProgress => "start it",
        EntryStatus.Done => "finish it",
        EntryStatus.Ready when from == EntryStatus.Draft => "mark it ready",
        EntryStatus.Ready => "move it back to Ready",
        EntryStatus.Archived => "archive it",
        EntryStatus.Draft => "move it back to Draft",
        _ => $"move it to {StatusTitle(to)}"
    };

    /// <summary>A card dropped on a column the rule allowed: the same status change
    /// the row's status selector makes.</summary>
    private async Task OnBoardDropAsync(TaskBoardDrop drop)
    {
        if (EntryFor(drop.TaskId) is { } row && Enum.TryParse<EntryStatus>(drop.ColumnKey, out var status))
        {
            await State.ChangeStatusAsync(row, status);

            // The tick puts a card in Done whatever its status says, so a card
            // moved out of Done has to lose it too, or it lands straight back.
            if (status != EntryStatus.Done && row.IsPreviewCompleted) await State.ChangeCompletedAsync(row, null);
        }
    }

    /// <summary>"+ New entry" at the foot of a status column: a new entry, in that
    /// status, opened on its title the way the list's "New entry" opens one.</summary>
    private void OnBoardAdd(string columnKey)
    {
        if (Enum.TryParse<EntryStatus>(columnKey, out var status)) State.NewRow(status);
    }
}
