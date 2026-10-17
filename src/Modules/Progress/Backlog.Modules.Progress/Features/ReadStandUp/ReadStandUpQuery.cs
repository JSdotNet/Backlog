using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.DomainModels;
using Backlog.Modules.Progress.Features.ReadSprintProgress;
using Backlog.Modules.Progress.Services;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Progress.Features.ReadStandUp;

/// <summary>The stand-up for <paramref name="Today"/>, read by <paramref name="Length"/>.</summary>
public sealed record ReadStandUpQuery(DateOnly Today, IterationLength Length);

/// <summary>
/// The <c>.devbook/domain/progress/domain.md#stand-up-reading</c>: the goals and the
/// impediments read against the current state of the work they name, over one iteration.
/// One read of Tasks and one of the pull requests for the whole stand-up; nothing read is
/// kept.
/// <list type="bullet">
/// <item><b>Goals</b>: every goal with its progress — the items done inside last as its
/// <c>+n</c> — and its <see cref="GoalForecasting">forecast</see> from the four windows
/// ending with last.</item>
/// <item><b>Finished</b>: each goal's counted items done inside last, then under No goal every
/// task no goal counts whose status is Done and that was completed inside last.</item>
/// <item><b>Planned</b>: for each goal, its items in progress (a blocked one is not), its open
/// items due inside next, and for each plan it holds the first entry in plan order that is
/// open, not in progress, not blocked, not archived and waits on no open work; then under No
/// goal the open tasks no goal counts that are due inside next. Each item once.</item>
/// <item><b>Impediments</b>: the open ones in the order raised, each with its age in worked
/// days and the first goal counting a task it holds back, and how many were resolved
/// inside last.</item>
/// </list>
/// A group with no item is left out. The pull requests of tasks no goal counts are not read:
/// such a task is finished by its status alone.
/// </summary>
public sealed class ReadStandUpQueryHandler(
    IGoalRepository goals,
    IImpedimentRepository impediments,
    ISprintTaskSource tasks,
    IGoalPullRequestReader pullRequests,
    IIterationSettings iteration,
    IStandUpPace pace,
    IWorkingHoursSettings? workingHours = null)
    : IQueryHandler<ReadStandUpQuery, StandUpDto>
{
    public async Task<StandUpDto> Handle(ReadStandUpQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var week = workingHours?.Current ?? WorkingHours.Default;
        var next = Iterations.Current(query.Today, query.Length, iteration.TwoWeekStart, week);
        var last = Iterations.Previous(next, week);
        var history = Iterations.History(last, GoalForecasting.HistoryLength, week);

        var all = await goals.ListAsync(cancellationToken).ConfigureAwait(false);
        var read = await tasks.ReadAsync(
            GoalCounting.TaskQuery(all) with
            {
                CompletedFrom = history[0].First,
                CompletedThrough = last.Last,
                DueFrom = next.First,
                DueThrough = next.Last
            },
            cancellationToken).ConfigureAwait(false);

        var counted = all.ToDictionary(goal => goal.Id, goal => GoalCounting.TasksOf(goal, all, read));
        var asked = all.SelectMany(goal => GoalCounting.PullRequestsOf(goal, counted[goal.Id]))
            .DistinctBy(GoalCounting.KeyOf, StringComparer.OrdinalIgnoreCase)
            .ToList();
        IReadOnlyList<LinkedPullRequestDto> answers = asked.Count == 0
            ? []
            : [.. (await pullRequests.ReadAsync(asked, cancellationToken).ConfigureAwait(false)).Select(ReadSprintProgressQueryHandler.Linked)];

        var items = all.ToDictionary(goal => goal.Id, goal => GoalCounting.Items(goal, all, read, answers));
        var raised = await impediments.ListAsync(cancellationToken).ConfigureAwait(false);
        var open = raised.Where(impediment => impediment.IsOpen).ToList();
        var heldBack = open.SelectMany(impediment => impediment.HoldsBack).ToHashSet();
        var noGoal = NoGoalTasks(read, counted);

        var pointsPerWeek = await pace.PointsPerWeekAsync(cancellationToken).ConfigureAwait(false);
        var planned = Planned(all, items, noGoal, next);

        return new StandUpDto(
            IterationWindowDto.From(last),
            IterationWindowDto.From(next),
            [.. all.Select(goal => GoalOf(goal, items[goal.Id], counted[goal.Id], heldBack, last, next, week))],
            Finished(all, items, noGoal, last),
            planned,
            planned.SelectMany(group => group.Items).Sum(item => item.Effort ?? 0),
            pointsPerWeek is { } points ? Iterations.Capacity(points, next, week) : null,
            [.. open
                .OrderBy(impediment => impediment.RaisedOn)
                .Select(impediment => ImpedimentOf(impediment, all, counted, query.Today, week))],
            raised.Count(impediment => impediment.ResolvedOn is { } on && last.Contains(on)));
    }

    private static StandUpGoalDto GoalOf(
        Goal goal,
        IReadOnlyList<GoalItem> items,
        IReadOnlyList<SprintTaskDto> counted,
        HashSet<Guid> heldBack,
        IterationWindow last,
        IterationWindow next,
        WorkingHours week)
    {
        var progress = GoalCounting.Count(items, new DayWindow(last.First, last.Last));
        var forecast = GoalForecasting.Forecast(
            progress.Total - progress.Done,
            items.Where(item => item.State == GoalItemState.Done && item.DoneOn is not null).Select(item => item.DoneOn!.Value),
            next,
            goal.DueDate,
            counted.Any(task => heldBack.Contains(task.Id)),
            week);

        return new StandUpGoalDto(goal.Id, goal.Title, goal.DueDate, progress.ToDto(), forecast.Standing, forecast.LandsOn);
    }

    /// <summary>The tasks read that no goal counts.</summary>
    private static List<SprintTaskDto> NoGoalTasks(IReadOnlyList<SprintTaskDto> read, Dictionary<Guid, IReadOnlyList<SprintTaskDto>> counted)
    {
        var held = counted.Values.SelectMany(list => list).Select(task => task.Id).ToHashSet();
        return [.. read.Where(task => !held.Contains(task.Id))];
    }

    private static List<StandUpGroupDto> Finished(
        IReadOnlyList<Goal> all,
        Dictionary<Guid, IReadOnlyList<GoalItem>> items,
        List<SprintTaskDto> noGoal,
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

        Add(groups, null, noGoal
            .Where(task => task.Status == SprintTaskStatus.Done && task.CompletedOn is { } on && last.Contains(on))
            .Select(task => new GoalItem(task, null, GoalItemState.Done, task.CompletedOn))
            .Where(listed.First)
            .Select(ItemOf));

        return groups;
    }

    private static List<StandUpGroupDto> Planned(
        IReadOnlyList<Goal> all,
        Dictionary<Guid, IReadOnlyList<GoalItem>> items,
        List<SprintTaskDto> noGoal,
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
                .Where(attachment => attachment.Kind == GoalAttachmentKind.Plan)
                .Select(plan => held
                    .Where(item => item.Task is { } task && task.PlanTags.Any(tag => PlanTags.Same(tag, plan.Plan!)))
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

    /// <summary>Open by the Done rule as far as the task itself says, and not archived.</summary>
    private static bool IsOpenTask(SprintTaskDto task) =>
        task.Status is not (SprintTaskStatus.Done or SprintTaskStatus.Archived);

    /// <summary>A plan's entry the stand-up may plan next: not in progress, not marked
    /// blocked, not archived, and waiting on no open work.</summary>
    private static bool IsReady(SprintTaskDto task) =>
        IsOpenTask(task) && task.Status != SprintTaskStatus.InProgress && !task.IsBlocked && !task.WaitsOnOpenWork;

    private static StandUpImpedimentDto ImpedimentOf(
        Impediment impediment,
        IReadOnlyList<Goal> all,
        Dictionary<Guid, IReadOnlyList<SprintTaskDto>> counted,
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
            goal?.Title);
    }

    private static void Add(List<StandUpGroupDto> groups, Goal? goal, IEnumerable<StandUpItemDto> items)
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
            int.TryParse(pull.Number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : null,
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
            _seen.Add(item.Task is { } task ? task.Id.ToString() : GoalCounting.KeyOf(item.PullRequest!));
    }
}
