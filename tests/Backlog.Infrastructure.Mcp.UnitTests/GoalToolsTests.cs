using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The goal tools: <c>list_goals</c>, <c>create_goal</c>, <c>update_goal</c>,
/// <c>attach_to_goal</c>, <c>detach_from_goal</c> and <c>delete_goal</c>. Each hands its
/// arguments to Progress's <see cref="IGoals"/> port, answers the goal as a session reads it,
/// and turns a refusal into the failure a client sees with its code unaltered.
/// </summary>
public sealed class GoalToolsTests
{
    private static readonly DateOnly Due = new(2026, 11, 2);

    private static readonly Guid LaunchGoal = Guid.NewGuid();
    private static readonly Guid SyncGoal = Guid.NewGuid();
    private static readonly Guid FinishedTask = Guid.NewGuid();
    private static readonly Guid PlannedTask = Guid.NewGuid();
    private static readonly Guid BankImpediment = Guid.NewGuid();

    /// <summary>One full reading: two goals, one at risk and one due at the iteration's end,
    /// a task and a pull request finished, a planned task under No goal, and an impediment
    /// being handled whose three held-back items are all settled.</summary>
    private static readonly StandUpDto Reading = new(
        new IterationWindowDto(IterationLength.Week, new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 23)),
        new IterationWindowDto(IterationLength.Week, new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 30)),
        [
            new StandUpGoalDto(LaunchGoal, "Launch", new DateOnly(2026, 11, 20),
                new GoalProgressDto(Done: 2, Total: 5, Doing: 1, Blocked: 1, ToDo: 1, DoneInWindow: 2),
                GoalStanding.AtRisk, new DateOnly(2026, 11, 27)),
            new StandUpGoalDto(SyncGoal, "Sync", new DateOnly(2026, 11, 8),
                new GoalProgressDto(Done: 0, Total: 1, Doing: 0, Blocked: 0, ToDo: 1, DoneInWindow: 0),
                GoalStanding.OnTrack, LandsOn: null, DueIsIterationEnd: true)
        ],
        [
            new StandUpGroupDto(LaunchGoal, "Launch",
            [
                new StandUpItemDto(GoalAttachmentKind.Task, FinishedTask, "Ship the installer", "backlog", null, 3, null, new DateOnly(2026, 10, 21)),
                new StandUpItemDto(GoalAttachmentKind.PullRequest, null, "JSdotNet/Backlog#1477", "JSdotNet/Backlog", 1477, null, null, new DateOnly(2026, 10, 23))
            ])
        ],
        [
            new StandUpGroupDto(LaunchGoal, "Launch", []),
            new StandUpGroupDto(null, null,
            [
                new StandUpItemDto(GoalAttachmentKind.Task, PlannedTask, "Write the release notes", "backlog", null, 5, new DateOnly(2026, 11, 6), null)
            ])
        ],
        PlannedEffort: 8,
        Capacity: 13,
        [
            new StandUpImpedimentDto(BankImpediment, "Bank sample files missing", ImpedimentState.BeingHandled, "bank support",
                DaysOpen: 4, CheckAgain: new DateOnly(2026, 10, 28), LaunchGoal, "Launch", "Asked twice", ReadyToResolve: true,
                [
                    new StandUpHeldBackItemDto(HeldBackItemDto.ForTask(FinishedTask), "Ship the installer", Settled: true),
                    new StandUpHeldBackItemDto(HeldBackItemDto.ForPullRequest("JSdotNet/Backlog", 1477), "JSdotNet/Backlog#1477", Settled: true),
                    new StandUpHeldBackItemDto(HeldBackItemDto.ForPlan("bank-integration"), "+bank-integration", Settled: true)
                ])
        ],
        ResolvedInLast: 2);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static GoalTools Tools(FakeGoals goals, params string[] repositories) =>
        new(goals, new FakeRepositoryDirectory([.. repositories.Select(Repository)]), new FakeStandUp());

    private static GoalTools Tools(FakeStandUp standUp, TimeProvider? clock = null) =>
        new(new FakeGoals(), new FakeRepositoryDirectory(), standUp, clock);

    private static TasksRepositoryRef Repository(string id)
    {
        var parts = id.Split('/');
        return new TasksRepositoryRef(parts[1].ToLowerInvariant(), parts[0], parts[1]);
    }

    // --- list_goals --------------------------------------------------------------

    [Fact]
    public async Task List_answers_every_goal_with_its_fields_its_items_and_its_progress()
    {
        var goals = new FakeGoals();
        var task = Guid.NewGuid();
        var goal = goals.Seed("Local development", "Runs on one machine", Due,
            GoalItemDto.ForPlan("+goals"), GoalItemDto.ForTask(task), GoalItemDto.ForPullRequest("JSdotNet/Backlog", 1477));
        goals.ProgressOf[goal] = new GoalProgressDto(Done: 1, Total: 3, Doing: 1, Blocked: 0, ToDo: 1, DoneInWindow: 0);

        var payload = await Tools(goals).ListAsync(Cancellation);

        var listed = Assert.Single(payload.Goals);
        Assert.Equal(goal, listed.Id);
        Assert.Equal("Local development", listed.Title);
        Assert.Equal("Runs on one machine", listed.Outcome);
        Assert.Equal(Due, listed.DueDate);
        Assert.Equal(
            [
                new GoalItemPayload("plan", "+goals", null, null, null, FakeGoals.Today),
                new GoalItemPayload("task", null, task, null, null, FakeGoals.Today),
                new GoalItemPayload("pull-request", null, null, "JSdotNet/Backlog", 1477, FakeGoals.Today)
            ],
            listed.Items);
        Assert.Equal(new GoalProgressPayload(Done: 1, Total: 3, Doing: 1, Blocked: 0, ToDo: 1), listed.Progress);
    }

    // --- read_iterations ---------------------------------------------------------

    [Fact]
    public async Task Read_iterations_without_a_length_reads_the_persons_own_iteration()
    {
        var standUp = new FakeStandUp { Setting = IterationLength.TwoWeeks };

        var payload = await Tools(standUp).ReadIterationsAsync(cancellationToken: Cancellation);

        Assert.Equal(new IterationWindowPayload("two-weeks", new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 25)), payload.Last);
        Assert.Equal(new IterationWindowPayload("two-weeks", new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 8)), payload.Next);
    }

    [Theory]
    [InlineData("day", "day")]
    [InlineData("Week", "week")]
    [InlineData("two-weeks", "two-weeks")]
    [InlineData(" MONTH ", "month")]
    public async Task Read_iterations_with_a_length_reads_that_length_as_of_today(string length, string token)
    {
        var standUp = new FakeStandUp { Setting = IterationLength.Week };
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero));

        var payload = await Tools(standUp, clock).ReadIterationsAsync(length, Cancellation);

        Assert.Equal(new IterationWindowPayload(token, new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 24)), payload.Next);
    }

    [Theory]
    [InlineData("sprint")]
    [InlineData("two_weeks")]
    [InlineData("TwoWeeks")]
    public async Task An_unknown_length_is_refused_and_nothing_is_read(string length)
    {
        var standUp = new FakeStandUp();

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools(standUp).ReadIterationsAsync(length, Cancellation));

        Assert.StartsWith("progress.iteration_length_unknown", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, standUp.Reads);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("month")]
    public async Task A_refused_reading_reaches_the_client_with_its_code(string? length)
    {
        var standUp = new FakeStandUp { Refusal = Error.Unexpected("progress.read_failed", "The goals could not be read.") };

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools(standUp).ReadIterationsAsync(length, Cancellation));

        Assert.StartsWith("progress.read_failed", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_iterations_answers_each_goal_with_its_standing_its_landing_and_what_it_did_in_the_window()
    {
        var payload = await Tools(new FakeStandUp { Answer = Reading }).ReadIterationsAsync(cancellationToken: Cancellation);

        Assert.Equal(
            [
                new IterationGoalPayload(LaunchGoal, "Launch", new DateOnly(2026, 11, 20), false, "at-risk", new DateOnly(2026, 11, 27),
                    new IterationGoalProgressPayload(Done: 2, Total: 5, Doing: 1, Blocked: 1, ToDo: 1, DoneInWindow: 2)),
                new IterationGoalPayload(SyncGoal, "Sync", new DateOnly(2026, 11, 8), true, "on-track", null,
                    new IterationGoalProgressPayload(Done: 0, Total: 1, Doing: 0, Blocked: 0, ToDo: 1, DoneInWindow: 0))
            ],
            payload.Goals);
        Assert.Equal((8, 13, 2), (payload.PlannedEffort, payload.Capacity, payload.ResolvedInLast));
    }

    [Fact]
    public async Task Read_iterations_answers_what_was_finished_and_what_is_planned_grouped_by_goal()
    {
        var payload = await Tools(new FakeStandUp { Answer = Reading }).ReadIterationsAsync(cancellationToken: Cancellation);

        var finished = Assert.Single(payload.Finished);
        Assert.Equal((LaunchGoal, "Launch"), (finished.GoalId, finished.GoalTitle));
        Assert.Equal(
            [
                new IterationItemPayload("task", FinishedTask, "Ship the installer", "backlog", null, 3, null, new DateOnly(2026, 10, 21)),
                new IterationItemPayload("pull-request", null, "JSdotNet/Backlog#1477", "JSdotNet/Backlog", 1477, null, null, new DateOnly(2026, 10, 23))
            ],
            finished.Items);

        Assert.Equal([(LaunchGoal, "Launch"), (null, null)], payload.Planned.Select(group => (group.GoalId, group.GoalTitle)));
        Assert.Equal(
            new IterationItemPayload("task", PlannedTask, "Write the release notes", "backlog", null, 5, new DateOnly(2026, 11, 6), null),
            Assert.Single(payload.Planned[1].Items));
    }

    [Fact]
    public async Task Read_iterations_answers_each_open_impediment_with_what_it_holds_back_and_whether_it_reads_ready_to_resolve()
    {
        var payload = await Tools(new FakeStandUp { Answer = Reading }).ReadIterationsAsync(cancellationToken: Cancellation);

        var impediment = Assert.Single(payload.Impediments);
        Assert.Equal(
            (BankImpediment, "Bank sample files missing", "being-handled", "bank support", 4, new DateOnly(2026, 10, 28), (Guid?)LaunchGoal, "Launch", "Asked twice", true),
            (impediment.Id, impediment.Title, impediment.State, impediment.With, impediment.DaysOpen, impediment.CheckAgain, impediment.GoalId, impediment.GoalTitle, impediment.Note, impediment.ReadyToResolve));
        Assert.Equal(
            [
                new IterationHeldBackPayload("task", FinishedTask, null, null, null, "Ship the installer", true),
                new IterationHeldBackPayload("pull-request", null, "JSdotNet/Backlog", 1477, null, "JSdotNet/Backlog#1477", true),
                new IterationHeldBackPayload("plan", null, null, null, "bank-integration", "+bank-integration", true)
            ],
            impediment.HoldsBack);
    }

    // --- create_goal -------------------------------------------------------------

    [Fact]
    public async Task Create_sets_a_goal_holding_nothing_yet()
    {
        var goals = new FakeGoals();

        var payload = await Tools(goals).CreateAsync("Fundering Financieel", Due, "Funding is signed", Cancellation);

        Assert.Equal("Fundering Financieel", payload.Title);
        Assert.Equal("Funding is signed", payload.Outcome);
        Assert.Equal(Due, payload.DueDate);
        Assert.Empty(payload.Items);
        Assert.Equal(payload.Id, Assert.Single(goals.Stored).Id);
    }

    [Fact]
    public async Task A_refused_create_reaches_the_client_with_its_code()
    {
        var goals = new FakeGoals();

        var failure = await Assert.ThrowsAsync<McpException>(() => Tools(goals).CreateAsync(" ", Due, null, Cancellation));

        Assert.StartsWith("progress.goal_without_title", failure.Message, StringComparison.Ordinal);
        Assert.Empty(goals.Stored);
    }

    [Fact]
    public async Task Create_without_a_due_date_sets_a_goal_without_one()
    {
        var goals = new FakeGoals();

        var payload = await Tools(goals).CreateAsync("Fundering Financieel", cancellationToken: Cancellation);

        Assert.Null(payload.DueDate);
        Assert.Null(Assert.Single(goals.Stored).DueDate);
    }

    // --- update_goal -------------------------------------------------------------

    [Fact]
    public async Task Update_changes_only_the_fields_it_is_given()
    {
        var goals = new FakeGoals();
        var goal = goals.Seed("Local development", "Runs on one machine", Due);

        var payload = await Tools(goals).UpdateAsync(goal, title: "Local development v2", cancellationToken: Cancellation);

        Assert.Equal("Local development v2", payload.Title);
        Assert.Equal("Runs on one machine", payload.Outcome);
        Assert.Equal(Due, payload.DueDate);
        Assert.Equal(new SetGoalRequest("Local development v2", "Runs on one machine", Due), goals.Edited);
    }

    [Fact]
    public async Task Update_moves_the_due_date_alone()
    {
        var goals = new FakeGoals();
        var goal = goals.Seed("Local development", "Runs on one machine", Due);
        var later = new DateOnly(2026, 12, 1);

        var payload = await Tools(goals).UpdateAsync(goal, dueDate: later, cancellationToken: Cancellation);

        Assert.Equal(("Local development", "Runs on one machine", later), (payload.Title, payload.Outcome, payload.DueDate));
    }

    [Fact]
    public async Task An_empty_outcome_clears_it()
    {
        var goals = new FakeGoals();
        var goal = goals.Seed("Local development", "Runs on one machine", Due);

        var payload = await Tools(goals).UpdateAsync(goal, outcome: "", cancellationToken: Cancellation);

        Assert.Null(payload.Outcome);
        Assert.Equal("Local development", payload.Title);
    }

    [Theory]
    [InlineData(" ", null, "progress.goal_without_title")]
    [InlineData(null, "two\nlines", "progress.outcome_not_one_line")]
    public async Task A_refused_update_reaches_the_client_with_its_code(string? title, string? outcome, string code)
    {
        var goals = new FakeGoals();
        var goal = goals.Seed("Local development", null, Due);

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            Tools(goals).UpdateAsync(goal, title, outcome, cancellationToken: Cancellation));

        Assert.StartsWith(code, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Updating_an_unknown_goal_is_not_found()
    {
        var failure = await Assert.ThrowsAsync<McpException>(() =>
            Tools(new FakeGoals()).UpdateAsync(Guid.NewGuid(), title: "Anything", cancellationToken: Cancellation));

        Assert.StartsWith("progress.goal_not_found", failure.Message, StringComparison.Ordinal);
    }

    // --- attach_to_goal ----------------------------------------------------------

    [Fact]
    public async Task Attach_by_plan_tag_attaches_the_plan()
    {
        var goals = new FakeGoals();
        var goal = goals.Seed("Local development", null, Due);

        var payload = await Tools(goals).AttachAsync(goal, planTag: "+goals", cancellationToken: Cancellation);

        Assert.Equal(GoalItemDto.ForPlan("+goals"), goals.Attached!.Value.Item);
        Assert.Equal("plan", Assert.Single(payload.Items).Kind);
    }

    [Fact]
    public async Task Attach_by_task_id_attaches_the_task()
    {
        var goals = new FakeGoals();
        var goal = goals.Seed("Local development", null, Due);
        var task = Guid.NewGuid();

        await Tools(goals).AttachAsync(goal, taskId: task, cancellationToken: Cancellation);

        Assert.Equal(GoalItemDto.ForTask(task), goals.Attached!.Value.Item);
    }

    [Fact]
    public async Task Attach_by_repository_and_number_attaches_the_pull_request_under_the_registered_identity()
    {
        var goals = new FakeGoals();
        var goal = goals.Seed("Local development", null, Due);

        await Tools(goals, "JSdotNet/Backlog").AttachAsync(goal, repository: "jsdotnet/backlog", number: 1477, cancellationToken: Cancellation);

        Assert.Equal(GoalItemDto.ForPullRequest("JSdotNet/Backlog", 1477), goals.Attached!.Value.Item);
    }

    [Fact]
    public async Task Attaching_a_pull_request_in_an_unknown_repository_is_refused_before_the_port_is_asked()
    {
        var goals = new FakeGoals();
        var goal = goals.Seed("Local development", null, Due);

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            Tools(goals, "JSdotNet/Backlog").AttachAsync(goal, repository: "someone/else", number: 3, cancellationToken: Cancellation));

        Assert.StartsWith("repository.not_found", failure.Message, StringComparison.Ordinal);
        Assert.Null(goals.Attached);
    }

    public static TheoryData<string?, Guid?, string?, int?, string> Selectors => new()
    {
        { null, null, null, null, "selector.required" },
        { "+goals", Guid.NewGuid(), null, null, "selector.required" },
        { "+goals", null, "JSdotNet/Backlog", 1477, "selector.required" },
        { null, Guid.NewGuid(), "JSdotNet/Backlog", 1477, "selector.required" },
        { " ", null, null, null, "selector.required" },
        { null, null, "JSdotNet/Backlog", null, "selector.incomplete" },
        { null, null, null, 1477, "selector.incomplete" },
    };

    [Theory]
    [MemberData(nameof(Selectors))]
    public async Task Attach_takes_exactly_one_selector(string? planTag, Guid? taskId, string? repository, int? number, string code)
    {
        var goals = new FakeGoals();
        var goal = goals.Seed("Local development", null, Due);

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            Tools(goals, "JSdotNet/Backlog").AttachAsync(goal, planTag, taskId, repository, number, cancellationToken: Cancellation));

        Assert.StartsWith(code, failure.Message, StringComparison.Ordinal);
        Assert.Null(goals.Attached);
    }

    [Fact]
    public async Task An_item_another_goal_holds_is_refused_without_the_move_flag()
    {
        var goals = new FakeGoals();
        var task = Guid.NewGuid();
        var other = goals.Seed("Schuldregeling aansluiten", null, Due, GoalItemDto.ForTask(task));
        var goal = goals.Seed("Local development", null, Due);

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            Tools(goals).AttachAsync(goal, taskId: task, cancellationToken: Cancellation));

        Assert.StartsWith("progress.item_in_other_goal", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Schuldregeling aansluiten", failure.Message, StringComparison.Ordinal);
        Assert.Single(goals.Find(other).Attachments);
    }

    [Fact]
    public async Task With_the_move_flag_the_item_leaves_the_other_goal_and_joins_this_one()
    {
        var goals = new FakeGoals();
        var task = Guid.NewGuid();
        var other = goals.Seed("Schuldregeling aansluiten", null, Due, GoalItemDto.ForTask(task));
        var goal = goals.Seed("Local development", null, Due);

        var payload = await Tools(goals).AttachAsync(goal, taskId: task, moveFromOtherGoal: true, cancellationToken: Cancellation);

        Assert.Equal(task, Assert.Single(payload.Items).TaskId);
        Assert.Empty(goals.Find(other).Attachments);
    }

    // --- detach_from_goal --------------------------------------------------------

    [Fact]
    public async Task Detach_by_pull_request_detaches_it()
    {
        var goals = new FakeGoals();
        var goal = goals.Seed("Local development", null, Due,
            GoalItemDto.ForPullRequest("JSdotNet/Backlog", 1477), GoalItemDto.ForPlan("+goals"));

        var payload = await Tools(goals, "JSdotNet/Backlog").DetachAsync(goal, repository: "jsdotnet/backlog", number: 1477, cancellationToken: Cancellation);

        Assert.Equal("plan", Assert.Single(payload.Items).Kind);
    }

    [Fact]
    public async Task Detach_reaches_a_pull_request_whose_repository_is_no_longer_registered()
    {
        var goals = new FakeGoals();
        var goal = goals.Seed("Local development", null, Due, GoalItemDto.ForPullRequest("Old/Repo", 3));

        var payload = await Tools(goals).DetachAsync(goal, repository: "Old/Repo", number: 3, cancellationToken: Cancellation);

        Assert.Empty(payload.Items);
    }

    [Fact]
    public async Task Detaching_what_the_goal_does_not_hold_changes_nothing()
    {
        var goals = new FakeGoals();
        var goal = goals.Seed("Local development", null, Due, GoalItemDto.ForPlan("+goals"));

        var payload = await Tools(goals).DetachAsync(goal, taskId: Guid.NewGuid(), cancellationToken: Cancellation);

        Assert.Equal("plan", Assert.Single(payload.Items).Kind);
    }

    [Theory]
    [MemberData(nameof(Selectors))]
    public async Task Detach_takes_exactly_one_selector(string? planTag, Guid? taskId, string? repository, int? number, string code)
    {
        var goals = new FakeGoals();
        var goal = goals.Seed("Local development", null, Due, GoalItemDto.ForPlan("+goals"));

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            Tools(goals, "JSdotNet/Backlog").DetachAsync(goal, planTag, taskId, repository, number, Cancellation));

        Assert.StartsWith(code, failure.Message, StringComparison.Ordinal);
        Assert.Single(goals.Find(goal).Attachments);
    }

    // --- delete_goal -------------------------------------------------------------

    [Fact]
    public async Task Delete_removes_the_goal()
    {
        var goals = new FakeGoals();
        var goal = goals.Seed("Local development", null, Due, GoalItemDto.ForPlan("+goals"));

        var payload = await Tools(goals).DeleteAsync(goal, Cancellation);

        Assert.Equal(goal, payload.Id);
        Assert.Empty(goals.Stored);
    }

    [Fact]
    public async Task Deleting_an_unknown_goal_is_not_found()
    {
        var failure = await Assert.ThrowsAsync<McpException>(() => Tools(new FakeGoals()).DeleteAsync(Guid.NewGuid(), Cancellation));

        Assert.StartsWith("progress.goal_not_found", failure.Message, StringComparison.Ordinal);
    }
}

/// <summary>
/// Progress's goal port over a list, holding the rules the tools rely on it for: a title,
/// a one-line outcome, an item in at most one goal unless moved, and detaching what
/// is not held changing nothing. It records the last edit and attach it was asked for.
/// </summary>
internal sealed class FakeGoals : IGoals
{
    public static readonly DateOnly Today = new(2026, 10, 26);

    private readonly List<GoalDto> _goals = [];

    public event Action? Changed { add { } remove { } }

    public IReadOnlyList<GoalDto> Stored => _goals;

    public Dictionary<Guid, GoalProgressDto> ProgressOf { get; } = [];

    public SetGoalRequest? Edited { get; private set; }

    public (Guid GoalId, GoalItemDto Item, bool Move)? Attached { get; private set; }

    public Guid Seed(string title, string? outcome, DateOnly due, params GoalItemDto[] items)
    {
        var id = Guid.NewGuid();
        _goals.Add(new GoalDto(id, title, outcome, due, [.. items.Select(item => new GoalAttachmentDto(item, Today))]));
        return id;
    }

    public GoalDto Find(Guid id) => _goals.Single(goal => goal.Id == id);

    public Task<Result<IReadOnlyList<GoalSummaryDto>>> ListAsync(DateOnly? windowStart = null, DateOnly? windowEnd = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success<IReadOnlyList<GoalSummaryDto>>(
            [.. _goals.Select(goal => new GoalSummaryDto(goal, ProgressOf.GetValueOrDefault(goal.Id, new GoalProgressDto(0, goal.Attachments.Count, 0, 0, goal.Attachments.Count, 0))))]));

    public Task<Result<GoalDto>> GetAsync(Guid goalId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Lookup(goalId));

    public Task<Result<GoalDto?>> FindGoalOfAsync(GoalItemDto item, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(_goals.FirstOrDefault(goal => goal.Attachments.Any(a => a.Item == item))));

    public Task<Result<GoalDto>> CreateAsync(SetGoalRequest request, CancellationToken cancellationToken = default)
    {
        if (Refusal(request) is { } error) return Task.FromResult(Result.Failure<GoalDto>(error));

        var goal = new GoalDto(Guid.NewGuid(), request.Title.Trim(), Blank(request.Outcome), request.DueDate, []);
        _goals.Add(goal);
        return Task.FromResult(Result.Success(goal));
    }

    public Task<Result<GoalDto>> EditAsync(Guid goalId, SetGoalRequest request, CancellationToken cancellationToken = default)
    {
        var found = Lookup(goalId);
        if (found.IsFailure) return Task.FromResult(found);
        if (Refusal(request) is { } error) return Task.FromResult(Result.Failure<GoalDto>(error));

        Edited = request;
        return Task.FromResult(Result.Success(Replace(found.Value with
        {
            Title = request.Title.Trim(),
            Outcome = Blank(request.Outcome),
            DueDate = request.DueDate
        })));
    }

    public Task<Result> DeleteAsync(Guid goalId, CancellationToken cancellationToken = default)
    {
        var found = Lookup(goalId);
        if (found.IsFailure) return Task.FromResult(Result.Failure(found.Error));

        _goals.Remove(found.Value);
        return Task.FromResult(Result.Success());
    }

    /// <summary>No tool moves a goal; the port's move is the Goals view's.</summary>
    public Task<Result> MoveAsync(Guid goalId, int toIndex, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<GoalDto>> AttachAsync(Guid goalId, GoalItemDto item, bool moveFromOtherGoal = false, CancellationToken cancellationToken = default)
    {
        Attached = (goalId, item, moveFromOtherGoal);

        var found = Lookup(goalId);
        if (found.IsFailure) return Task.FromResult(found);

        if (_goals.FirstOrDefault(goal => goal.Id != goalId && goal.Attachments.Any(a => a.Item == item)) is { } other)
        {
            if (!moveFromOtherGoal) return Task.FromResult(Result.Failure<GoalDto>(ProgressErrors.ItemInOtherGoal(other.Title)));
            Replace(other with { Attachments = [.. other.Attachments.Where(a => a.Item != item)] });
        }

        var goal = found.Value;
        return Task.FromResult(Result.Success(goal.Attachments.Any(a => a.Item == item)
            ? goal
            : Replace(goal with { Attachments = [.. goal.Attachments, new GoalAttachmentDto(item, Today)] })));
    }

    public Task<Result<GoalDto>> DetachAsync(Guid goalId, GoalItemDto item, CancellationToken cancellationToken = default)
    {
        var found = Lookup(goalId);
        if (found.IsFailure) return Task.FromResult(found);

        return Task.FromResult(Result.Success(Replace(found.Value with
        {
            Attachments = [.. found.Value.Attachments.Where(a => a.Item != item)]
        })));
    }

    private Result<GoalDto> Lookup(Guid goalId) =>
        _goals.FirstOrDefault(goal => goal.Id == goalId) is { } goal ? goal : ProgressErrors.GoalNotFound(goalId);

    private GoalDto Replace(GoalDto goal)
    {
        _goals[_goals.FindIndex(stored => stored.Id == goal.Id)] = goal;
        return goal;
    }

    private static Error? Refusal(SetGoalRequest request) =>
        string.IsNullOrWhiteSpace(request.Title) ? ProgressErrors.GoalWithoutTitle()
        : request.Outcome?.Contains('\n', StringComparison.Ordinal) == true ? ProgressErrors.OutcomeNotOneLine()
        : null;

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>
/// Progress's iteration reading, answering <see cref="Answer"/> over two fourteen-day windows
/// either side of the day it reads for, in the length it reads with: the person's
/// <see cref="Setting"/> as of <see cref="HostToday"/> when no length is named, else the
/// length and the day the caller gives. <see cref="Refusal"/> makes every read fail with it.
/// </summary>
internal sealed class FakeStandUp : IStandUp
{
    public IterationLength Setting { get; init; } = IterationLength.Week;

    public DateOnly HostToday { get; init; } = FakeGoals.Today;

    public StandUpDto Answer { get; init; } = new(
        new IterationWindowDto(IterationLength.Week, FakeGoals.Today, FakeGoals.Today),
        new IterationWindowDto(IterationLength.Week, FakeGoals.Today, FakeGoals.Today),
        [], [], [], PlannedEffort: 0, Capacity: null, [], ResolvedInLast: 0);

    public Error? Refusal { get; init; }

    public int Reads { get; private set; }

    public Task<Result<StandUpDto>> ReadAsync(DateOnly today, IterationLength length, CancellationToken cancellationToken = default) =>
        Task.FromResult(Read(today, length));

    public Task<Result<StandUpDto>> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Read(HostToday, Setting));

    private Result<StandUpDto> Read(DateOnly today, IterationLength length)
    {
        Reads++;

        if (Refusal is { } error) return Result.Failure<StandUpDto>(error);

        return Answer with
        {
            Last = new IterationWindowDto(length, today.AddDays(-14), today.AddDays(-1)),
            Next = new IterationWindowDto(length, today, today.AddDays(13)),
        };
    }
}
