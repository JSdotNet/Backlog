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
/// <param name="Goals">Every goal, in the person's order (<see cref="GoalOrder"/>).</param>
/// <param name="Impediments">Every impediment, open and resolved, in the order they were raised.</param>
/// <param name="Tasks">The tasks the reading has: at least those
/// <see cref="StandUpReading.TaskQuery"/> asks for. More are harmless.</param>
/// <param name="PullRequests">What the pull requests the goals count, and those the open
/// impediments hold back (<see cref="StandUpReading.PullRequestsHeldBack"/>), said about
/// themselves; one with no answer reads as not merged and is never settled.</param>
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
/// days, the goal of the first item linked that a goal counts, each item it holds back with
/// whether it is settled, and whether it reads Ready to resolve; and how many were resolved
/// inside last.</item>
/// </list>
/// A goal is At risk while an open impediment holds back a task it counts, a pull request it
/// counts, or a plan one of whose entries it counts — Ready to resolve or not.
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

    /// <summary>What a reading has to ask Tasks for: the goals' own work, the tasks and the
    /// plans' entries the open impediments hold back in any repository, the tasks completed
    /// inside the four windows the forecast reads, and those due inside next.</summary>
    public static ProgressTaskQuery TaskQuery(
        IReadOnlyList<GoalDto> goals,
        IReadOnlyList<ImpedimentDto> impediments,
        DateOnly today,
        IterationLength length,
        DateOnly? twoWeekStart,
        WorkingHours week)
    {
        ArgumentNullException.ThrowIfNull(goals);
        ArgumentNullException.ThrowIfNull(impediments);

        var (last, next) = Windows(today, length, twoWeekStart, week);
        var history = Iterations.History(last, GoalForecasting.HistoryLength, week);
        var goalsWork = GoalItems.TaskQuery(goals);
        var held = impediments.Where(impediment => impediment.IsOpen).SelectMany(impediment => impediment.HoldsBack).ToList();

        return goalsWork with
        {
            TaskIds = [.. goalsWork.TaskIds.Concat(held.Where(item => item.Kind == HeldBackKind.Task && item.TaskId is not null).Select(item => item.TaskId!.Value)).Distinct()],
            PlanTags =
            [
                .. goalsWork.PlanTags
                    .Concat(held.Where(item => item.Kind == HeldBackKind.Plan).Select(item => GoalItems.Bare(item.PlanTag)))
                    .Where(tag => tag.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ],
            CompletedFrom = history[0].First,
            CompletedThrough = last.Last,
            DueFrom = next.First,
            DueThrough = next.Last
        };
    }

    /// <summary>The pull requests the open <paramref name="impediments"/> hold back, each once
    /// whatever its spelling — what a reading asks GitHub about beside the goals' own.</summary>
    public static IReadOnlyList<PullRequestRefDto> PullRequestsHeldBack(IEnumerable<ImpedimentDto> impediments)
    {
        ArgumentNullException.ThrowIfNull(impediments);

        return
        [
            .. impediments
                .Where(impediment => impediment.IsOpen)
                .SelectMany(impediment => impediment.HoldsBack)
                .Where(item => item.Kind == HeldBackKind.PullRequest && item.Repository is not null && item.Number is not null)
                .Select(HeldBackItems.PullRequestRef)
                .DistinctBy(GoalItems.KeyOf, StringComparer.OrdinalIgnoreCase)
        ];
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
        var pulls = all.ToDictionary(goal => goal.Id, goal => GoalItems.PullRequestsOf(goal, counted[goal.Id]));
        var items = all.ToDictionary(goal => goal.Id, goal => GoalItems.Items(goal, all, read, inputs.PullRequests));
        var open = inputs.Impediments.Where(impediment => impediment.IsOpen).ToList();
        var heldBack = open.SelectMany(impediment => impediment.HoldsBack).ToList();
        var noGoal = NoGoalTasks(read, counted);
        var planned = Planned(all, items, noGoal, next);

        GoalDto? GoalOfItem(HeldBackItemDto item) =>
            all.FirstOrDefault(goal => HeldBackItems.HoldsBackGoal(item, counted[goal.Id], pulls[goal.Id]));

        return new StandUpDto(
            IterationWindowDto.From(last),
            IterationWindowDto.From(next),
            [.. all.Select(goal => GoalOf(goal, items[goal.Id], heldBack.Any(item => HeldBackItems.HoldsBackGoal(item, counted[goal.Id], pulls[goal.Id])), last, next, week))],
            Finished(all, items, last),
            planned,
            planned.SelectMany(group => group.Items).Sum(item => item.Effort ?? 0),
            inputs.PointsPerWeek is { } points ? Iterations.Capacity(points, next, week) : null,
            [.. open
                .OrderBy(impediment => impediment.RaisedOn)
                .Select(impediment => ImpedimentOf(impediment, GoalOfItem, inputs, week))],
            inputs.Impediments.Count(impediment => impediment.ResolvedOn is { } on && last.Contains(on)));
    }

    private static StandUpGoalDto GoalOf(
        GoalDto goal,
        IReadOnlyList<GoalItem> items,
        bool heldBack,
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
            heldBack,
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
        Func<HeldBackItemDto, GoalDto?> goalOf,
        StandUpInputs inputs,
        WorkingHours week)
    {
        var goal = impediment.HoldsBack.Select(goalOf).FirstOrDefault(goal => goal is not null);
        var age = Iterations.WorkedDays(impediment.RaisedOn, inputs.Today, week);

        return new StandUpImpedimentDto(
            impediment.Id,
            impediment.Title,
            impediment.State,
            impediment.HandledBy ?? impediment.ReportedTo,
            age,
            impediment.CheckAgain,
            goal?.Id,
            goal?.Title,
            impediment.Note,
            HeldBackItems.ReadyToResolve(impediment, inputs.Tasks, inputs.PullRequests),
            [.. impediment.HoldsBack.Select(item => new StandUpHeldBackItemDto(
                item,
                TitleOf(item, inputs.Tasks),
                HeldBackItems.Settled(item, inputs.Tasks, inputs.PullRequests)))]);
    }

    /// <summary>A held-back item's words: the task's title, or null for a task the reading
    /// does not have; <c>owner/name#number</c>; or <c>+tag</c>.</summary>
    private static string? TitleOf(HeldBackItemDto item, IReadOnlyList<ProgressTaskDto> tasks) => item.Kind switch
    {
        HeldBackKind.Task => tasks.FirstOrDefault(task => task.Id == item.TaskId)?.Title,
        HeldBackKind.PullRequest => $"{item.Repository}#{item.Number?.ToString(CultureInfo.InvariantCulture)}",
        _ => $"+{GoalItems.Bare(item.PlanTag)}",
    };

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
