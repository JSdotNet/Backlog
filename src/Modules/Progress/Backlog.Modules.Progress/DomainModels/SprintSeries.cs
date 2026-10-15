using Backlog.Modules.Progress.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.DomainModels;

/// <summary>
/// The cadence a set of repositories works to, and the consistency boundary for every
/// sprint in it (<c>.devbook/domain/progress/domain.md#sprint-series</c>). The root
/// rather than the sprint, because closing a sprint changes two sprints at once, and
/// numbering and the one-open-sprint rule span sprints.
/// <para>
/// Its rules are <c>.devbook/domain/progress/domain.invariants.md#sprint-series</c>,
/// each with a unit test in <c>Backlog.Modules.Progress.UnitTests</c>. The rule that
/// spans series — a repository belongs to one — is <see cref="Services.SeriesAssignment"/>'s.
/// </para>
/// </summary>
public sealed class SprintSeries
{
    private readonly List<string> _repositories;
    private readonly List<GoalRule> _goalRules;
    private readonly List<Sprint> _sprints;

    private SprintSeries(
        Guid id,
        string name,
        List<string> repositories,
        SprintLength length,
        DayOfWeek startWeekday,
        NamePattern namePattern,
        BriefVisibility briefs,
        List<GoalRule> goalRules,
        GoalThreshold threshold,
        List<Sprint> sprints,
        bool isOn,
        DateTimeOffset? createdAt,
        string? offReason)
    {
        Id = id;
        Name = name;
        _repositories = repositories;
        Length = length;
        StartWeekday = startWeekday;
        NamePattern = namePattern;
        Briefs = briefs;
        _goalRules = goalRules;
        Threshold = threshold;
        _sprints = sprints;
        IsOn = isOn;
        CreatedAt = createdAt;
        OffReason = isOn || string.IsNullOrWhiteSpace(offReason) ? null : offReason.Trim();
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    /// <summary>The series' repositories by alias, compared without regard to case.</summary>
    public IReadOnlyList<string> Repositories => _repositories;

    public SprintLength Length { get; private set; }

    public DayOfWeek StartWeekday { get; private set; }

    public NamePattern NamePattern { get; private set; }

    public BriefVisibility Briefs { get; private set; }

    public IReadOnlyList<GoalRule> GoalRules => _goalRules;

    public GoalThreshold Threshold { get; private set; }

    public IReadOnlyList<Sprint> Sprints => _sprints;

    public Sprint? OpenSprint => _sprints.LastOrDefault(sprint => sprint.IsOpen);

    /// <summary>Whether the series is switched on
    /// (<c>.devbook/domain/progress/domain.md#series-switch</c>). A new series is. A
    /// series switched off keeps its repositories, its settings and its sprints, and
    /// still holds its repositories against every other series; only the reading that
    /// asks which series a repository works to passes it by.</summary>
    public bool IsOn { get; private set; }

    /// <summary>When the series was set up, on the desktop that set it up. It travels
    /// with the series, so every desktop orders two series alike when syncing left one
    /// repository in both (<c>.devbook/domain/progress/domain.invariants.md#invariant-a-synced-repository-keeps-to-the-series-set-up-first</c>).
    /// Null for a series stored before the moment was kept.</summary>
    public DateTimeOffset? CreatedAt { get; }

    /// <summary>Why the system switched the series off, as one line a person reads; null
    /// while it is on and when a person switched it off.</summary>
    public string? OffReason { get; private set; }

    /// <summary>A new series over <paramref name="repositories"/>, with no sprint yet.
    /// Refused without a repository (<c>progress.series_without_repository</c>).</summary>
    public static Result<SprintSeries> Create(
        Guid id,
        IEnumerable<string> repositories,
        string? name = null,
        SprintLength length = SprintLength.TwoWeeks,
        DayOfWeek startWeekday = DayOfWeek.Monday,
        string? namePattern = null,
        BriefVisibility? briefs = null,
        IEnumerable<GoalRule>? goalRules = null,
        int goalShare = 100,
        DateTimeOffset? createdAt = null)
    {
        var aliases = Clean(repositories);
        if (aliases.Count == 0) return ProgressErrors.SeriesWithoutRepository();
        if (!SprintLengths.IsAllowed(length)) return ProgressErrors.LengthNotAllowed((int)length);

        var pattern = namePattern is null ? Result.Success(NamePattern.Default) : NamePattern.Create(namePattern);
        if (pattern.IsFailure) return pattern.Error;

        var threshold = GoalThreshold.Create(goalShare);
        if (threshold.IsFailure) return threshold.Error;

        var rules = goalRules?.Distinct().ToList() ?? [];
        if (rules.Count == 0) rules.Add(GoalRule.Restore(GoalRuleKind.Everything, []));

        return new SprintSeries(
            id,
            string.IsNullOrWhiteSpace(name) ? string.Join(", ", aliases) : name.Trim(),
            aliases,
            length,
            startWeekday,
            pattern.Value,
            briefs ?? BriefVisibility.Neither,
            rules,
            threshold.Value,
            [],
            isOn: true,
            createdAt,
            offReason: null);
    }

    /// <summary>Rehydration from storage. Trusted, as every stored aggregate is: the
    /// rules held when it was written.</summary>
    public static SprintSeries Restore(
        Guid id,
        string name,
        IEnumerable<string> repositories,
        SprintLength length,
        DayOfWeek startWeekday,
        string namePattern,
        BriefVisibility briefs,
        IEnumerable<(GoalRuleKind Kind, IReadOnlyList<string> Choices)> goalRules,
        int goalShare,
        IEnumerable<Sprint> sprints,
        bool isOn = true,
        DateTimeOffset? createdAt = null,
        string? offReason = null) =>
        new(
            id,
            name,
            [.. repositories],
            length,
            startWeekday,
            NamePattern.Restore(namePattern),
            briefs,
            [.. goalRules.Select(rule => GoalRule.Restore(rule.Kind, rule.Choices))],
            GoalThreshold.Create(goalShare).TryGetValue(out var threshold) ? threshold : GoalThreshold.EveryGoalTask,
            [.. sprints.OrderBy(sprint => sprint.Number)],
            isOn,
            createdAt,
            offReason);

    public bool HasRepository(string alias) =>
        _repositories.Contains(alias.Trim(), StringComparer.OrdinalIgnoreCase);

    public Sprint? SprintNumber(int number) => _sprints.FirstOrDefault(sprint => sprint.Number == number);

    // --- Settings ----------------------------------------------------------------

    /// <summary>Adds a repository. Whether another series has it is
    /// <see cref="Services.SeriesAssignment"/>'s to check; this series cannot see the
    /// others.</summary>
    internal Result AddRepository(string? alias)
    {
        var trimmed = alias?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return Result.Failure(ProgressErrors.RepositoryRequired());

        if (!HasRepository(trimmed)) _repositories.Add(trimmed);
        return Result.Success();
    }

    /// <summary>Removes a repository; refused for the last one
    /// (<c>progress.series_without_repository</c>).</summary>
    internal Result RemoveRepository(string alias)
    {
        var index = _repositories.FindIndex(existing => string.Equals(existing, alias?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (index < 0) return Result.Success();
        if (_repositories.Count == 1) return Result.Failure(ProgressErrors.SeriesWithoutRepository());

        _repositories.RemoveAt(index);
        return Result.Success();
    }

    /// <summary>A changed length or weekday applies from the next sprint; the open one
    /// keeps its window. Only one or two weeks (<c>progress.length_not_allowed</c>).</summary>
    public Result ChangeCadence(SprintLength length, DayOfWeek startWeekday)
    {
        if (!SprintLengths.IsAllowed(length)) return Result.Failure(ProgressErrors.LengthNotAllowed((int)length));
        if (!Enum.IsDefined(startWeekday)) return Result.Failure(ProgressErrors.WindowOffCadence(startWeekday, (int)length));

        Length = length;
        StartWeekday = startWeekday;
        return Result.Success();
    }

    public Result Rename(string? name, string? namePattern)
    {
        var pattern = NamePattern.Create(namePattern);
        if (pattern.IsFailure) return Result.Failure(pattern.Error);

        if (!string.IsNullOrWhiteSpace(name)) Name = name.Trim();
        NamePattern = pattern.Value;
        return Result.Success();
    }

    public void ShowInBriefs(bool daily, bool weekly) => Briefs = new BriefVisibility(daily, weekly);

    /// <summary>Switches the series on or off. Nothing it holds changes either way, so
    /// switching it back on finds it as it was. A person's switch clears any
    /// <see cref="OffReason"/>. Whether another series has one of its repositories is
    /// <see cref="Services.SeriesAssignment.SwitchOn"/>'s to check.</summary>
    public void Switch(bool on)
    {
        IsOn = on;
        OffReason = null;
    }

    /// <summary>Switches the series off for the reason given, keeping everything it
    /// holds.</summary>
    internal void SwitchOff(string reason)
    {
        IsOn = false;
        OffReason = reason;
    }

    public Result SetGoalRules(IEnumerable<GoalRule> rules, int goalShare)
    {
        var threshold = GoalThreshold.Create(goalShare);
        if (threshold.IsFailure) return Result.Failure(threshold.Error);

        var distinct = rules.Distinct().ToList();
        if (distinct.Count == 0) distinct.Add(GoalRule.Restore(GoalRuleKind.Everything, []));

        _goalRules.Clear();
        _goalRules.AddRange(distinct);
        Threshold = threshold.Value;
        return Result.Success();
    }

    // --- Sprints -----------------------------------------------------------------

    /// <summary>
    /// Starts a sprint on <paramref name="startOn"/>. Refused while another is open
    /// (<c>progress.sprint_already_open</c>); its window has to match the cadence
    /// (<c>progress.window_off_cadence</c>) and follow the sprint before it without an
    /// overlap or a gap (<see cref="SprintSequence"/>). Numbered one past the highest.
    /// </summary>
    public Result<Sprint> StartSprint(DateOnly startOn, string? goal)
    {
        if (OpenSprint is { } open) return ProgressErrors.SprintAlreadyOpen(open.Number);

        var window = SprintWindow.Create(startOn, Length, StartWeekday);
        if (window.IsFailure) return window.Error;

        return Append(window.Value, goal, []);
    }

    /// <summary>
    /// Closes the open sprint and starts the next one in the same change
    /// (<c>Closing starts the next sprint</c>): from the first start weekday after the
    /// closed sprint's last day, with <paramref name="nextGoal"/>, no linked plan, and
    /// every carried task as a directly added member. Refused until every member in
    /// <paramref name="unfinished"/> has an outcome (<c>progress.unsettled_item</c>).
    /// </summary>
    /// <param name="members">The open sprint's members at closing.</param>
    /// <param name="unfinished">Those of them that are not done.</param>
    public Result<Sprint> CloseSprint(
        IReadOnlyCollection<Guid> members,
        IReadOnlyCollection<Guid> unfinished,
        IReadOnlyDictionary<Guid, CloseOutcome> outcomes,
        string? nextGoal)
    {
        var open = OpenSprint;
        if (open is null) return ProgressErrors.SprintNotFound(_sprints.Count == 0 ? 1 : _sprints[^1].Number);

        var nextStart = SprintSequence.NextStart(open.Window, StartWeekday);
        var window = SprintWindow.Create(nextStart, Length, StartWeekday);
        if (window.IsFailure) return window.Error;

        var closed = open.Close(members, unfinished, outcomes);
        if (closed.IsFailure) return closed.Error;

        var carried = unfinished
            .Where(id => outcomes[id] == CloseOutcome.Carry)
            .Select(id => SprintItem.Direct(id, nextStart));

        return Append(window.Value, nextGoal, carried);
    }

    // --- What a sprint holds and records (refused once it is closed) --------------

    /// <summary>Adds a task to sprint <paramref name="number"/> by its id.</summary>
    public Result AddTask(int number, Guid taskId, DateOnly today) =>
        Change(number, sprint => sprint.AddTask(taskId, today));

    /// <summary>Takes a directly added task out of sprint <paramref name="number"/>.</summary>
    public Result RemoveTask(int number, Guid taskId) => Change(number, sprint => sprint.RemoveTask(taskId));

    public Result LinkPlan(int number, string? planTag, DateOnly today) =>
        Change(number, sprint => sprint.LinkPlan(planTag, today));

    public Result UnlinkPlan(int number, string? planTag) => Change(number, sprint => sprint.UnlinkPlan(planTag));

    public Result SetSprintGoal(int number, string? goal) => Change(number, sprint => sprint.SetGoal(goal));

    public Result MarkGoal(int number, Guid taskId, bool isGoal) =>
        Change(number, sprint => sprint.Record(taskId, item => item.MarkGoal(isGoal)));

    /// <summary>A one-line note on an item, or null to clear it.</summary>
    public Result SetNote(int number, Guid taskId, string? note) =>
        Change(number, sprint => sprint.Record(taskId, item => item.SetNote(note)));

    public Result SetOpenQuestion(int number, Guid taskId, bool openQuestion) =>
        Change(number, sprint => sprint.Record(taskId, item => item.SetOpenQuestion(openQuestion)));

    /// <summary>Runs a change on sprint <paramref name="number"/>; refused for a closed
    /// sprint by the sprint itself.</summary>
    internal Result Change(int number, Func<Sprint, Result> change)
    {
        var sprint = SprintNumber(number);
        return sprint is null ? Result.Failure(ProgressErrors.SprintNotFound(number)) : change(sprint);
    }

    private Result<Sprint> Append(SprintWindow window, string? goal, IEnumerable<SprintItem> items)
    {
        var previous = _sprints.Count == 0 ? null : _sprints[^1];
        if (previous is not null)
        {
            var follows = SprintSequence.Follows(previous.Window, window, StartWeekday);
            if (follows.IsFailure) return follows.Error;
        }

        var sprint = Sprint.Open(SprintSequence.NextNumber(_sprints), window, goal, items);
        _sprints.Add(sprint);
        return sprint;
    }

    private static List<string> Clean(IEnumerable<string>? repositories) =>
        [.. (repositories ?? [])
            .Select(alias => alias?.Trim() ?? string.Empty)
            .Where(alias => alias.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
}

/// <summary>
/// The rules across the sprints of one series: numbered without gaps from 1, and each
/// one starting on the first start weekday after the last day of the one before it —
/// never inside it (<c>progress.sprints_overlap</c>) and never later
/// (<c>progress.sprints_not_contiguous</c>).
/// </summary>
public static class SprintSequence
{
    public static int NextNumber(IReadOnlyCollection<Sprint> sprints) =>
        sprints.Count == 0 ? 1 : sprints.Max(sprint => sprint.Number) + 1;

    /// <summary>The first <paramref name="startWeekday"/> after <paramref name="previous"/>'s
    /// last day. With an unchanged cadence that is the next day.</summary>
    public static DateOnly NextStart(SprintWindow previous, DayOfWeek startWeekday)
    {
        var day = previous.End.AddDays(1);
        while (day.DayOfWeek != startWeekday) day = day.AddDays(1);
        return day;
    }

    public static Result Follows(SprintWindow previous, SprintWindow next, DayOfWeek startWeekday)
    {
        if (next.Start <= previous.End) return Result.Failure(ProgressErrors.SprintsOverlap(previous.End));

        var expected = NextStart(previous, startWeekday);
        return next.Start == expected ? Result.Success() : Result.Failure(ProgressErrors.SprintsNotContiguous(expected));
    }
}
