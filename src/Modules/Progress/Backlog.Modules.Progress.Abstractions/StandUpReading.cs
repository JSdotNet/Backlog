using System.Globalization;

using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Modules.Progress.Abstractions;

/// <summary>
/// Everything one reading of the stand-up is computed from, gathered by whichever head
/// reads it: the desktop from its own stores, Tasks and GitHub, the phone from what the
/// task feed carried (local ADR 0035 §8).
/// </summary>
/// <param name="Today">The reading's today, in the reader's local time.</param>
/// <param name="Length">The iteration length the stand-up is read by.</param>
/// <param name="TwoWeekStart">The day two-week windows count from, or null for none set.</param>
/// <param name="Week">The person's working week.</param>
/// <param name="Goals">Every goal, in the order they were set.</param>
/// <param name="Impediments">Every impediment, open and resolved, in the order they were raised.</param>
/// <param name="Tasks">The tasks the reading has: at least those
/// <see cref="StandUpReading.TaskQuery"/> asks for. More are harmless.</param>
/// <param name="PullRequests">What the pull requests the goals count said about themselves;
/// one with no answer reads as not merged.</param>
/// <param name="PointsPerWeek">The person's pace, or null when none is known.</param>
public sealed record StandUpInputs(
    DateOnly Today,
    IterationLength Length,
    DateOnly? TwoWeekStart,
    WorkingHours Week,
    IReadOnlyList<GoalDto> Goals,
    IReadOnlyList<ImpedimentDto> Impediments,
    IReadOnlyList<ProgressTaskDto> Tasks,
    IReadOnlyList<LinkedPullRequestDto> PullRequests,
    decimal? PointsPerWeek);

/// <summary>
/// The <c>.devbook/domain/progress/domain.md#stand-up-reading</c>: the goals and the
/// impediments read against the current state of the work they name, over one iteration.
/// <list type="bullet">
/// <item><b>Goals</b>: every goal with its progress — the items done inside last as its
/// <c>+n</c> — and its <see cref="GoalForecasting">forecast</see> from the four windows
/// ending with last.</item>
/// <item><b>Finished</b>: each goal's counted items done inside last. Work no goal counts is
/// not listed: the stand-up reports what the goals track.</item>
/// <item><b>Planned</b>: for each goal, its items in progress (a blocked one is not), its open
/// items due inside next, and for each plan it holds the first entry in plan order that is
/// open, not in progress, not blocked, not archived and waits on no open work; then under No
/// goal the open tasks no goal counts that are due inside next. Each item once.</item>
/// <item><b>Impediments</b>: the open ones in the order raised, each with its age in worked
/// days and the first goal counting a task it holds back, and how many were resolved
/// inside last.</item>
/// </list>
/// A group with no item is left out. Pure, and in the module's published surface beside
/// <see cref="Iterations"/> and <see cref="GoalForecasting"/>, so the desktop and the phone
/// read the same stand-up from the same work.
/// </summary>
public static class StandUpReading
{
    /// <summary>The two windows a reading reads: last, and next.</summary>
    public static (IterationWindow Last, IterationWindow Next) Windows(DateOnly today, IterationLength length, DateOnly? twoWeekStart, WorkingHours week)
    {
        var next = Iterations.Current(today, length, twoWeekStart, week);
        return (Iterations.Previous(next, week), next);
    }

    /// <summary>What a reading has to ask Tasks for: the goals' own work, the tasks
    /// completed inside the four windows the forecast reads, and those due inside next.</summary>
    public static ProgressTaskQuery TaskQuery(IReadOnlyList<GoalDto> goals, DateOnly today, IterationLength length, DateOnly? twoWeekStart, WorkingHours week)
    {
        ArgumentNullException.ThrowIfNull(goals);

        var (last, next) = Windows(today, length, twoWeekStart, week);
        var history = Iterations.History(last, GoalForecasting.HistoryLength, week);

        return GoalItems.TaskQuery(goals) with
        {
            CompletedFrom = history[0].First,
            CompletedThrough = last.Last,
            DueFrom = next.First,
            DueThrough = next.Last
        };
    }

    /// <summary>The stand-up <paramref name="inputs"/> read.</summary>
    public static StandUpDto Read(StandUpInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var week = inputs.Week;
        var (last, next) = Windows(inputs.Today, inputs.Length, inputs.TwoWeekStart, week);
        var all = inputs.Goals;
        var read = inputs.Tasks;

        var counted = all.ToDictionary(goal => goal.Id, goal => GoalItems.TasksOf(goal, all, read));
        var items = all.ToDictionary(goal => goal.Id, goal => GoalItems.Items(goal, all, read, inputs.PullRequests));
        var open = inputs.Impediments.Where(impediment => impediment.IsOpen).ToList();
        var heldBack = open.SelectMany(impediment => impediment.HoldsBack).ToHashSet();
        var noGoal = NoGoalTasks(read, counted);
        var planned = Planned(all, items, noGoal, next);

        return new StandUpDto(
            IterationWindowDto.From(last),
            IterationWindowDto.From(next),
            [.. all.Select(goal => GoalOf(goal, items[goal.Id], counted[goal.Id], heldBack, last, next, week))],
            Finished(all, items, last),
            planned,
            planned.SelectMany(group => group.Items).Sum(item => item.Effort ?? 0),
            inputs.PointsPerWeek is { } points ? Iterations.Capacity(points, next, week) : null,
            [.. open
                .OrderBy(impediment => impediment.RaisedOn)
                .Select(impediment => ImpedimentOf(impediment, all, counted, inputs.Today, week))],
            inputs.Impediments.Count(impediment => impediment.ResolvedOn is { } on && last.Contains(on)));
    }

    private static StandUpGoalDto GoalOf(
        GoalDto goal,
        IReadOnlyList<GoalItem> items,
        IReadOnlyList<ProgressTaskDto> counted,
        HashSet<Guid> heldBack,
        IterationWindow last,
        IterationWindow next,
        WorkingHours week)
    {
        var progress = GoalItems.Count(items, last.First, last.Last);
        var forecast = GoalForecasting.Forecast(
            progress.Total - progress.Done,
            items.Where(item => item.State == GoalItemState.Done && item.DoneOn is not null).Select(item => item.DoneOn!.Value),
            next,
            goal.DueDate,
            counted.Any(task => heldBack.Contains(task.Id)),
            week);

        return new StandUpGoalDto(
            goal.Id,
            goal.Title,
            GoalForecasting.DueOn(goal.DueDate, next),
            progress,
            forecast.Standing,
            forecast.LandsOn,
            DueIsIterationEnd: goal.DueDate is null);
    }

    /// <summary>The tasks read that no goal counts.</summary>
    private static List<ProgressTaskDto> NoGoalTasks(IReadOnlyList<ProgressTaskDto> read, Dictionary<Guid, IReadOnlyList<ProgressTaskDto>> counted)
    {
        var held = counted.Values.SelectMany(list => list).Select(task => task.Id).ToHashSet();
        return [.. read.Where(task => !held.Contains(task.Id))];
    }

    private static List<StandUpGroupDto> Finished(
        IReadOnlyList<GoalDto> all,
        Dictionary<Guid, IReadOnlyList<GoalItem>> items,
        IterationWindow last)
    {
        var listed = new Listed();
        var groups = new List<StandUpGroupDto>();

        foreach (var goal in all)
        {
            Add(groups, goal, items[goal.Id]
                .Where(item => item.State == GoalItemState.Done && item.DoneOn is { } on && last.Contains(on))
                .Where(listed.First)
                .Select(ItemOf));
        }

        return groups;
    }

    private static List<StandUpGroupDto> Planned(
        IReadOnlyList<GoalDto> all,
        Dictionary<Guid, IReadOnlyList<GoalItem>> items,
        List<ProgressTaskDto> noGoal,
        IterationWindow next)
    {
        var listed = new Listed();
        var groups = new List<StandUpGroupDto>();

        foreach (var goal in all)
        {
            var held = items[goal.Id];
            var doing = held.Where(item => item.State == GoalItemState.Doing);
            var due = held.Where(item => item.State != GoalItemState.Done && item.Task is { } task && IsOpenTask(task) && task.DueOn is { } on && next.Contains(on));
            var firstReady = goal.Attachments
                .Select(attachment => attachment.Item)
                .Where(item => item.Kind == GoalAttachmentKind.Plan)
                .Select(plan => held
                    .Where(item => item.Task is { } task && task.PlanTags.Any(tag => SamePlan(tag, plan.PlanTag)))
                    .Where(item => item.State == GoalItemState.ToDo && IsReady(item.Task!))
                    .OrderBy(item => item.Task!.Order)
                    .FirstOrDefault())
                .OfType<GoalItem>();

            Add(groups, goal, doing.Concat(due).Concat(firstReady).Where(listed.First).Select(ItemOf));
        }

        Add(groups, null, noGoal
            .Where(task => IsOpenTask(task) && task.DueOn is { } on && next.Contains(on))
            .Select(task => new GoalItem(task, null, GoalItemState.ToDo, null))
            .Where(listed.First)
            .Select(ItemOf));

        return groups;
    }

    private static bool SamePlan(string? left, string? right) =>
        string.Equals(GoalItems.Bare(left), GoalItems.Bare(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>Open by the Done rule as far as the task itself says, and not archived.</summary>
    private static bool IsOpenTask(ProgressTaskDto task) =>
        task.Status is not (ProgressTaskStatus.Done or ProgressTaskStatus.Archived);

    /// <summary>A plan's entry the stand-up may plan next: not in progress, not marked
    /// blocked, not archived, and waiting on no open work.</summary>
    private static bool IsReady(ProgressTaskDto task) =>
        IsOpenTask(task) && task.Status != ProgressTaskStatus.InProgress && !task.IsBlocked && !task.WaitsOnOpenWork;

    private static StandUpImpedimentDto ImpedimentOf(
        ImpedimentDto impediment,
        IReadOnlyList<GoalDto> all,
        Dictionary<Guid, IReadOnlyList<ProgressTaskDto>> counted,
        DateOnly today,
        WorkingHours week)
    {
        var goal = all.FirstOrDefault(goal => counted[goal.Id].Any(task => impediment.HoldsBack.Contains(task.Id)));
        var age = Iterations.WorkedDays(impediment.RaisedOn, today, week);

        return new StandUpImpedimentDto(
            impediment.Id,
            impediment.Title,
            impediment.State,
            impediment.HandledBy ?? impediment.ReportedTo,
            age,
            impediment.CheckAgain,
            goal?.Id,
            goal?.Title,
            impediment.Note);
    }

    private static void Add(List<StandUpGroupDto> groups, GoalDto? goal, IEnumerable<StandUpItemDto> items)
    {
        var listed = items.ToList();
        if (listed.Count > 0) groups.Add(new StandUpGroupDto(goal?.Id, goal?.Title, listed));
    }

    private static StandUpItemDto ItemOf(GoalItem item)
    {
        if (item.Task is { } task)
        {
            return new StandUpItemDto(
                GoalAttachmentKind.Task,
                task.Id,
                task.Title,
                task.RepositoryAliases.FirstOrDefault(),
                null,
                task.Effort,
                task.DueOn,
                item.DoneOn);
        }

        var pull = item.PullRequest!;
        return new StandUpItemDto(
            GoalAttachmentKind.PullRequest,
            null,
            $"{pull.Repository}#{pull.Number}",
            pull.Repository,
            int.TryParse(pull.Number, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null,
            null,
            null,
            item.DoneOn);
    }

    /// <summary>The items one block has listed so far, so each is listed once: a task by its
    /// id, a pull request by its repository and number.</summary>
    private sealed class Listed
    {
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

        public bool First(GoalItem item) =>
            _seen.Add(item.Task is { } task ? task.Id.ToString() : GoalItems.KeyOf(item.PullRequest!));
    }
}
