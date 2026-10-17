using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Extensions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// The published <see cref="IGoals"/> port as a host composes it through
/// <c>AddProgressModule()</c>: the goals switch, and each feature end to end over an
/// in-memory repository — setting, editing and deleting a goal
/// (<c>.devbook/domain/progress/features.md#setting-a-goal</c>), attaching and detaching
/// work (<c>#attaching-work-to-a-goal</c>), and the list with its counts.
/// </summary>
public sealed class GoalsTests : IDisposable
{
    private readonly InMemoryGoalRepository _repository = new();
    private readonly FakeTaskSource _tasks = new();
    private readonly FakePullRequests _pulls = new();
    private readonly FakeGoalFeatures _features = new(goalsOn: true);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public GoalsTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton<ISprintSeriesRepository>(new InMemorySprintSeriesRepository());
        services.AddSingleton<IGoalRepository>(_repository);
        services.AddSingleton<ISprintTaskSource>(_tasks);
        services.AddSingleton<IGoalPullRequestReader>(_pulls);
        services.AddSingleton<IAppFeatureSettings>(_features);
        services.AddProgressModule();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _provider.CreateScope();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private IGoals Goals => _scope.ServiceProvider.GetRequiredService<IGoals>();

    private static SetGoalRequest Paid => new("Customers get paid faster", "Invoices settle in a day", MakeGoal.Due);

    private async Task<GoalDto> CreateAsync(string title = "Customers get paid faster") =>
        (await Goals.CreateAsync(Paid with { Title = title }, Cancellation)).Value;

    [Fact]
    public async Task With_goals_off_every_call_answers_off_and_nothing_is_written()
    {
        _features.GoalsOn = false;
        var item = GoalItemDto.ForTask(Guid.NewGuid());

        Assert.Equal("progress.goals_off", (await Goals.CreateAsync(Paid, Cancellation)).Error.Code);
        Assert.Equal("progress.goals_off", (await Goals.ListAsync(cancellationToken: Cancellation)).Error.Code);
        Assert.Equal("progress.goals_off", (await Goals.GetAsync(Guid.NewGuid(), Cancellation)).Error.Code);
        Assert.Equal("progress.goals_off", (await Goals.EditAsync(Guid.NewGuid(), Paid, Cancellation)).Error.Code);
        Assert.Equal("progress.goals_off", (await Goals.DeleteAsync(Guid.NewGuid(), Cancellation)).Error.Code);
        Assert.Equal("progress.goals_off", (await Goals.AttachAsync(Guid.NewGuid(), item, cancellationToken: Cancellation)).Error.Code);
        Assert.Equal("progress.goals_off", (await Goals.DetachAsync(Guid.NewGuid(), item, Cancellation)).Error.Code);
        Assert.Equal("progress.goals_off", (await Goals.FindGoalOfAsync(item, Cancellation)).Error.Code);
        Assert.Equal(0, _repository.Saves);
    }

    [Fact]
    public async Task Goals_recorded_while_on_come_back_when_the_switch_is_turned_on_again()
    {
        var goal = await CreateAsync();

        _features.GoalsOn = false;
        Assert.True((await Goals.GetAsync(goal.Id, Cancellation)).IsFailure);

        _features.GoalsOn = true;
        Assert.Equal(goal.Title, (await Goals.GetAsync(goal.Id, Cancellation)).Value.Title);
    }

    // --- Setting a goal ----------------------------------------------------------

    [Fact]
    public async Task A_new_goal_holds_its_title_outcome_and_due_date_and_nothing_else()
    {
        var goal = await CreateAsync();

        Assert.Equal("Customers get paid faster", goal.Title);
        Assert.Equal("Invoices settle in a day", goal.Outcome);
        Assert.Equal(MakeGoal.Due, goal.DueDate);
        Assert.Empty(goal.Attachments);
        Assert.Equal(1, _repository.Saves);
    }

    /// <summary>Scenario "No due date": not saved.</summary>
    [Fact]
    public async Task A_goal_without_a_title_or_a_due_date_is_not_saved()
    {
        Assert.Equal("progress.goal_without_due_date", (await Goals.CreateAsync(Paid with { DueDate = null }, Cancellation)).Error.Code);
        Assert.Equal("progress.goal_without_title", (await Goals.CreateAsync(Paid with { Title = " " }, Cancellation)).Error.Code);
        Assert.Equal(0, _repository.Saves);
    }

    [Fact]
    public async Task Editing_replaces_the_three_fields_and_keeps_what_the_goal_holds()
    {
        var goal = await CreateAsync();
        var task = Guid.NewGuid();
        await Goals.AttachAsync(goal.Id, GoalItemDto.ForTask(task), cancellationToken: Cancellation);

        var edited = await Goals.EditAsync(goal.Id, new SetGoalRequest("Paid in a day", null, MakeGoal.Due.AddDays(7)), Cancellation);

        Assert.Equal("Paid in a day", edited.Value.Title);
        Assert.Null(edited.Value.Outcome);
        Assert.Equal(MakeGoal.Due.AddDays(7), edited.Value.DueDate);
        Assert.Equal(task, Assert.Single(edited.Value.Attachments).Item.TaskId);
    }

    [Fact]
    public async Task A_refused_edit_changes_nothing()
    {
        var goal = await CreateAsync();
        var saves = _repository.Saves;

        var edited = await Goals.EditAsync(goal.Id, new SetGoalRequest("Renamed", "two\nlines", MakeGoal.Due), Cancellation);

        Assert.Equal("progress.outcome_not_one_line", edited.Error.Code);
        Assert.Equal(saves, _repository.Saves);
        Assert.Equal("Customers get paid faster", (await Goals.GetAsync(goal.Id, Cancellation)).Value.Title);
    }

    [Fact]
    public async Task Editing_or_attaching_to_a_goal_that_is_not_there_is_not_found()
    {
        Assert.Equal("progress.goal_not_found", (await Goals.EditAsync(Guid.NewGuid(), Paid, Cancellation)).Error.Code);
        Assert.Equal("progress.goal_not_found", (await Goals.AttachAsync(Guid.NewGuid(), GoalItemDto.ForPlan("x"), cancellationToken: Cancellation)).Error.Code);
        Assert.Equal("progress.goal_not_found", (await Goals.DeleteAsync(Guid.NewGuid(), Cancellation)).Error.Code);
    }

    /// <summary>Scenario "Deleting a goal with a plan attached": the goal is gone and what
    /// it held belongs to no goal, the work itself untouched — the port writes no task.</summary>
    [Fact]
    public async Task Deleting_a_goal_frees_what_it_held()
    {
        var goal = await CreateAsync();
        var credit = Guid.NewGuid();
        await Goals.AttachAsync(goal.Id, GoalItemDto.ForPlan("+bank-integration"), cancellationToken: Cancellation);
        await Goals.AttachAsync(goal.Id, GoalItemDto.ForTask(credit), cancellationToken: Cancellation);

        Assert.True((await Goals.DeleteAsync(goal.Id, Cancellation)).IsSuccess);

        Assert.Equal("progress.goal_not_found", (await Goals.GetAsync(goal.Id, Cancellation)).Error.Code);
        Assert.Null((await Goals.FindGoalOfAsync(GoalItemDto.ForTask(credit), Cancellation)).Value);
        Assert.Empty((await Goals.ListAsync(cancellationToken: Cancellation)).Value);
    }

    // --- Attaching work to a goal ------------------------------------------------

    [Fact]
    public async Task A_plan_a_task_and_a_pull_request_attach_on_the_day_they_are_attached()
    {
        var goal = await CreateAsync();
        var task = Guid.NewGuid();

        await Goals.AttachAsync(goal.Id, GoalItemDto.ForPlan("+bank-integration"), cancellationToken: Cancellation);
        await Goals.AttachAsync(goal.Id, GoalItemDto.ForTask(task), cancellationToken: Cancellation);
        var attached = await Goals.AttachAsync(goal.Id, GoalItemDto.ForPullRequest("JSdotNet/Backlog", 1352), cancellationToken: Cancellation);

        Assert.Equal(
            [GoalItemDto.ForPlan("bank-integration"), GoalItemDto.ForTask(task), GoalItemDto.ForPullRequest("JSdotNet/Backlog", 1352)],
            attached.Value.Attachments.Select(a => a.Item));
        Assert.All(attached.Value.Attachments, a => Assert.Equal(MakeGoal.Today, a.AttachedOn));
    }

    [Fact]
    public async Task An_item_that_does_not_name_itself_is_refused()
    {
        var goal = await CreateAsync();

        Assert.Equal("progress.pull_request_without_repository",
            (await Goals.AttachAsync(goal.Id, new GoalItemDto(GoalAttachmentKind.PullRequest, Number: 4), cancellationToken: Cancellation)).Error.Code);
        Assert.Equal("progress.plan_tag_required",
            (await Goals.AttachAsync(goal.Id, new GoalItemDto(GoalAttachmentKind.Plan), cancellationToken: Cancellation)).Error.Code);
    }

    /// <summary>Scenario "A task another goal holds": refused with "Already in …", and the
    /// task stays in that goal.</summary>
    [Fact]
    public async Task Attaching_what_another_goal_holds_is_refused_naming_it()
    {
        var search = await CreateAsync("Devbook search ships");
        var paid = await CreateAsync();
        var highlight = GoalItemDto.ForTask(Guid.NewGuid());
        await Goals.AttachAsync(search.Id, highlight, cancellationToken: Cancellation);

        var refused = await Goals.AttachAsync(paid.Id, highlight, cancellationToken: Cancellation);

        Assert.Equal("Already in Devbook search ships", refused.Error.Message);
        Assert.Equal(search.Id, (await Goals.FindGoalOfAsync(highlight, Cancellation)).Value!.Id);
    }

    [Fact]
    public async Task Moving_an_item_saves_both_goals_and_leaves_it_in_one()
    {
        var search = await CreateAsync("Devbook search ships");
        var paid = await CreateAsync();
        var highlight = GoalItemDto.ForTask(Guid.NewGuid());
        await Goals.AttachAsync(search.Id, highlight, cancellationToken: Cancellation);

        var moved = await Goals.AttachAsync(paid.Id, highlight, moveFromOtherGoal: true, Cancellation);

        Assert.True(moved.IsSuccess);
        Assert.Empty((await Goals.GetAsync(search.Id, Cancellation)).Value.Attachments);
        Assert.Equal(paid.Id, (await Goals.FindGoalOfAsync(highlight, Cancellation)).Value!.Id);
    }

    [Fact]
    public async Task Detaching_changes_nothing_on_the_item_and_frees_it()
    {
        var goal = await CreateAsync();
        var item = GoalItemDto.ForPullRequest("JSdotNet/Backlog", 1352);
        await Goals.AttachAsync(goal.Id, item, cancellationToken: Cancellation);

        var detached = await Goals.DetachAsync(goal.Id, item, Cancellation);
        var again = await Goals.DetachAsync(goal.Id, item, Cancellation);

        Assert.Empty(detached.Value.Attachments);
        Assert.True(again.IsSuccess);
        Assert.Null((await Goals.FindGoalOfAsync(item, Cancellation)).Value);
    }

    // --- The list with its counts ------------------------------------------------

    [Fact]
    public async Task The_list_counts_each_goal_from_its_work_in_the_order_they_were_set()
    {
        var paid = await CreateAsync();
        var search = await CreateAsync("Devbook search ships");
        _tasks.Tasks.AddRange(Enumerable.Range(0, 9).Select(i => Make.Task(
            $"Entry {i}",
            i < 5 ? SprintTaskStatus.Done : SprintTaskStatus.Ready,
            plan: "bank-integration",
            repository: i % 2 == 0 ? "backlog" : "docs",
            completedOn: i < 5 ? new DateOnly(2026, 10, 13) : null)));
        var mapping = _tasks.Tasks[8];
        _pulls.Known.Add(new GoalPullRequestDto("JSdotNet/Backlog", "1352", LinkedPullRequestState.Merged, new DateOnly(2026, 10, 14), Checks: null, Approvals: null, Author: "jobs"));

        await Goals.AttachAsync(paid.Id, GoalItemDto.ForPlan("+bank-integration"), cancellationToken: Cancellation);
        await Goals.AttachAsync(search.Id, GoalItemDto.ForTask(mapping.Id), cancellationToken: Cancellation);
        await Goals.AttachAsync(search.Id, GoalItemDto.ForPullRequest("JSdotNet/Backlog", 1352), cancellationToken: Cancellation);

        var listed = (await Goals.ListAsync(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 16), Cancellation)).Value;

        Assert.Equal([paid.Id, search.Id], listed.Select(summary => summary.Goal.Id));
        Assert.Equal(new GoalProgressDto(Done: 5, Total: 8, Doing: 0, Blocked: 0, ToDo: 3, DoneInWindow: 5), listed[0].Progress);
        Assert.Equal(new GoalProgressDto(Done: 1, Total: 2, Doing: 0, Blocked: 0, ToDo: 1, DoneInWindow: 1), listed[1].Progress);
        Assert.True(_tasks.LastQuery!.AnyRepository);
    }

    [Fact]
    public async Task Without_a_window_nothing_is_counted_as_done_in_it()
    {
        var goal = await CreateAsync();
        var task = Make.Task("Done", SprintTaskStatus.Done, completedOn: MakeGoal.Today);
        _tasks.Tasks.Add(task);
        await Goals.AttachAsync(goal.Id, GoalItemDto.ForTask(task.Id), cancellationToken: Cancellation);

        var progress = Assert.Single((await Goals.ListAsync(cancellationToken: Cancellation)).Value).Progress;

        Assert.Equal((1, 1, 0), (progress.Done, progress.Total, progress.DoneInWindow));
    }

    // --- The notice --------------------------------------------------------------

    [Fact]
    public async Task Every_stored_change_is_announced_and_a_refused_one_is_not()
    {
        var heard = 0;
        Goals.Changed += () => heard++;

        var goal = await CreateAsync();
        await Goals.AttachAsync(goal.Id, GoalItemDto.ForPlan("x"), cancellationToken: Cancellation);
        await Goals.CreateAsync(Paid with { DueDate = null }, Cancellation);
        await Goals.DeleteAsync(goal.Id, Cancellation);

        Assert.Equal(3, heard);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
