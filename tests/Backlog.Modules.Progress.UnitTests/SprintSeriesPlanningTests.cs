using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Extensions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// The published ports as a host composes them through <c>AddProgressModule()</c>: the
/// progress switch, the use cases end to end over an in-memory repository, and closing
/// worked out by the same reading the progress service gives.
/// </summary>
public sealed class SprintSeriesPlanningTests : IDisposable
{
    private readonly InMemorySprintSeriesRepository _repository = new();
    private readonly FakeTaskSource _tasks = new();
    private readonly FakePullRequests _pulls = new();
    private readonly FakeFeatures _features = new(progressOn: true);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public SprintSeriesPlanningTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton<ISprintSeriesRepository>(_repository);
        services.AddSingleton<ISprintTaskSource>(_tasks);
        services.AddSingleton<IGoalPullRequestReader>(_pulls);
        services.AddSingleton<IAppFeatureSettings>(_features);
        services.AddProgressModule();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _provider.CreateScope();
    }

    private ISprintSeriesPlanning Planning => _scope.ServiceProvider.GetRequiredService<ISprintSeriesPlanning>();

    private ISprintProgress Progress => _scope.ServiceProvider.GetRequiredService<ISprintProgress>();

    private async Task<SprintSeriesDto> CreateWithOpenSprintAsync()
    {
        var series = (await Planning.CreateSeriesAsync(new CreateSprintSeries(["backlog", "docs"]), TestContext.Current.CancellationToken)).Value;
        return (await Planning.StartSprintAsync(series.Id, Make.Monday, "Ship the sprint model", TestContext.Current.CancellationToken)).Value;
    }

    [Fact]
    public async Task With_progress_off_every_port_answers_off_and_nothing_is_written()
    {
        _features.ProgressOn = false;
        var ct = TestContext.Current.CancellationToken;

        var created = await Planning.CreateSeriesAsync(new CreateSprintSeries(["backlog"]), ct);
        var listed = await Planning.ListSeriesAsync(ct);
        var read = await Progress.ReadAsync(Guid.NewGuid(), cancellationToken: ct);

        Assert.Equal("progress.off", created.Error.Code);
        Assert.Equal("progress.off", listed.Error.Code);
        Assert.Equal("progress.off", read.Error.Code);
        Assert.Equal(0, _repository.Saves);
    }

    [Fact]
    public async Task Series_recorded_while_on_come_back_when_progress_is_turned_on_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = await CreateWithOpenSprintAsync();

        _features.ProgressOn = false;
        Assert.True((await Planning.GetSeriesAsync(series.Id, ct)).IsFailure);

        _features.ProgressOn = true;
        Assert.Equal(series.Id, (await Planning.GetSeriesAsync(series.Id, ct)).Value.Id);
    }

    [Fact]
    public async Task A_new_series_has_the_devbooks_defaults()
    {
        var series = (await Planning.CreateSeriesAsync(new CreateSprintSeries(["backlog", "docs"]), TestContext.Current.CancellationToken)).Value;

        Assert.Equal(SprintLength.TwoWeeks, series.Length);
        Assert.Equal(DayOfWeek.Monday, series.StartWeekday);
        Assert.Equal("Sprint {n}", series.NamePattern);
        Assert.False(series.ShowInDailyBrief);
        Assert.False(series.ShowInWeeklyBrief);
        Assert.Equal(100, series.GoalShare);
        Assert.Equal(GoalRuleKind.Everything, Assert.Single(series.GoalRules).Kind);
        Assert.Null(series.OpenSprint);
    }

    [Fact]
    public async Task A_repository_already_in_a_series_is_refused_for_another_and_stays_where_it_was()
    {
        var ct = TestContext.Current.CancellationToken;
        var core = (await Planning.CreateSeriesAsync(new CreateSprintSeries(["backlog"], "Core"), ct)).Value;
        var docs = (await Planning.CreateSeriesAsync(new CreateSprintSeries(["docs"], "Docs"), ct)).Value;

        var added = await Planning.AddRepositoryAsync(docs.Id, "backlog", ct);
        var second = await Planning.CreateSeriesAsync(new CreateSprintSeries(["backlog"]), ct);

        Assert.Equal("progress.repository_in_other_series", added.Error.Code);
        Assert.Equal("progress.repository_in_other_series", second.Error.Code);
        Assert.Equal(core.Id, (await Planning.FindSeriesOfAsync("backlog", ct)).Value!.Id);
        Assert.Null((await Planning.FindSeriesOfAsync("tools", ct)).Value);
    }

    [Fact]
    public async Task The_progress_service_reads_the_open_sprint_through_the_task_source()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = await CreateWithOpenSprintAsync();
        var planned = Make.Task("Planned", SprintTaskStatus.InProgress, "+progress-sprints");
        var direct = Make.Task("Direct", SprintTaskStatus.Done, repository: "tools", completedOn: Make.Monday);
        _tasks.Tasks.AddRange([planned, direct, Make.Task("Unrelated")]);

        await Planning.LinkPlanAsync(series.Id, 1, "+progress-sprints", ct);
        await Planning.AddTaskAsync(series.Id, 1, direct.Id, ct);
        var progress = (await Progress.ReadAsync(series.Id, cancellationToken: ct)).Value;

        Assert.Equal([direct.Id], _tasks.LastQuery!.TaskIds);
        Assert.Equal(["progress-sprints"], _tasks.LastQuery.PlanTags);
        Assert.Equal(["backlog", "docs"], _tasks.LastQuery.RepositoryAliases);
        Assert.Equal(2, progress.Members.Count);
        Assert.Equal(1, progress.AllWork.Done);
        Assert.Equal(1, progress.AllWork.InProgress);
        Assert.Equal(4, progress.WorkingDaysElapsed);
    }

    [Fact]
    public async Task Membership_marks_and_notes_change_nothing_on_the_task()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = await CreateWithOpenSprintAsync();
        var task = Make.Task("Untouched", SprintTaskStatus.InProgress, "+plan");
        _tasks.Tasks.Add(task);

        await Planning.AddTaskAsync(series.Id, 1, task.Id, ct);
        await Planning.MarkGoalAsync(series.Id, 1, task.Id, true, ct);
        await Planning.SetNoteAsync(series.Id, 1, task.Id, "Waiting on review", ct);
        await Planning.SetOpenQuestionAsync(series.Id, 1, task.Id, true, ct);
        var removed = await Planning.RemoveTaskAsync(series.Id, 1, task.Id, ct);

        Assert.Same(task, Assert.Single(_tasks.Tasks));
        Assert.Empty(removed.Value.OpenSprint!.Items);
    }

    /// <summary>No linked plan brings the task in, so removing it ends its membership,
    /// and what the sprint recorded on it goes with it: added again, it starts clean.</summary>
    [Fact]
    public async Task A_removed_task_no_plan_brings_in_comes_back_without_its_old_marks()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = await CreateWithOpenSprintAsync();
        var task = Make.Task("Direct", SprintTaskStatus.InProgress, "+unlinked");
        _tasks.Tasks.Add(task);

        await Planning.AddTaskAsync(series.Id, 1, task.Id, ct);
        await Planning.MarkGoalAsync(series.Id, 1, task.Id, true, ct);
        await Planning.SetNoteAsync(series.Id, 1, task.Id, "Waiting on review", ct);
        await Planning.SetOpenQuestionAsync(series.Id, 1, task.Id, true, ct);
        await Planning.RemoveTaskAsync(series.Id, 1, task.Id, ct);
        var added = (await Planning.AddTaskAsync(series.Id, 1, task.Id, ct)).Value;

        var item = Assert.Single(added.OpenSprint!.Items);
        Assert.True(item.AddedDirectly);
        Assert.False(item.IsGoal);
        Assert.Null(item.Note);
        Assert.False(item.OpenQuestion);
    }

    /// <summary>A linked plan still brings the task in, so it stays a member when it is
    /// no longer added directly, and keeps what the sprint recorded on it.</summary>
    [Fact]
    public async Task A_removed_task_a_linked_plan_still_brings_in_keeps_its_marks()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = await CreateWithOpenSprintAsync();
        var task = Make.Task("Planned", SprintTaskStatus.InProgress, "+plan");
        _tasks.Tasks.Add(task);
        await Planning.LinkPlanAsync(series.Id, 1, "+plan", ct);

        await Planning.AddTaskAsync(series.Id, 1, task.Id, ct);
        await Planning.MarkGoalAsync(series.Id, 1, task.Id, true, ct);
        await Planning.SetNoteAsync(series.Id, 1, task.Id, "Waiting on review", ct);
        var removed = (await Planning.RemoveTaskAsync(series.Id, 1, task.Id, ct)).Value;

        var item = Assert.Single(removed.OpenSprint!.Items);
        Assert.False(item.AddedDirectly);
        Assert.True(item.IsGoal);
        Assert.Equal("Waiting on review", item.Note);
    }

    [Fact]
    public async Task Closing_is_refused_until_every_unfinished_member_has_an_outcome()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = await CreateWithOpenSprintAsync();
        var carried = Make.Task("Carried", SprintTaskStatus.InProgress, "+plan");
        var unsettled = Make.Task("Unsettled", SprintTaskStatus.Ready, "+plan");
        var done = Make.Task("Done", SprintTaskStatus.Done, "+plan", completedOn: Make.Monday);
        _tasks.Tasks.AddRange([carried, unsettled, done]);
        await Planning.LinkPlanAsync(series.Id, 1, "+plan", ct);

        var refused = await Planning.CloseSprintAsync(
            series.Id,
            new Dictionary<Guid, CloseOutcome> { [carried.Id] = CloseOutcome.Carry },
            "Next",
            ct);

        Assert.Equal("progress.unsettled_item", refused.Error.Code);
        Assert.True((await Planning.GetSeriesAsync(series.Id, ct)).Value.Sprints.Single().IsOpen);
    }

    [Fact]
    public async Task Closing_starts_the_next_sprint_with_only_the_carried_task_and_leaves_every_task_alone()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = await CreateWithOpenSprintAsync();
        var carry = Make.Task("Carry", SprintTaskStatus.InProgress, "+plan");
        var back = Make.Task("Back", SprintTaskStatus.Ready, "+plan");
        var drop = Make.Task("Drop", SprintTaskStatus.InProgress, "+plan");
        _tasks.Tasks.AddRange([carry, back, drop]);
        await Planning.LinkPlanAsync(series.Id, 1, "+plan", ct);

        var closed = (await Planning.CloseSprintAsync(
            series.Id,
            new Dictionary<Guid, CloseOutcome>
            {
                [carry.Id] = CloseOutcome.Carry,
                [back.Id] = CloseOutcome.BackToBacklog,
                [drop.Id] = CloseOutcome.Drop
            },
            "Ship the sprint view",
            ct)).Value;

        var next = closed.OpenSprint!;
        Assert.Equal(2, next.Number);
        Assert.Equal("Sprint 2", next.Name);
        Assert.Equal(new DateOnly(2026, 10, 26), next.Start);
        Assert.Equal(new DateOnly(2026, 11, 8), next.End);
        Assert.Equal("Ship the sprint view", next.Goal);

        _time.SetUtcNow(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
        var reading = (await Progress.ReadAsync(series.Id, cancellationToken: ct)).Value;
        Assert.Equal([carry.Id], reading.Members.Select(member => member.TaskId));

        var first = (await Progress.ReadAsync(series.Id, 1, ct)).Value;
        Assert.Equal(CloseOutcome.Drop, first.Members.Single(member => member.TaskId == drop.Id).Outcome);
        Assert.Equal(SprintTaskStatus.InProgress, _tasks.Tasks.Single(task => task.Id == drop.Id).Status);
    }

    // Thursday 15 October is the day that tells the two apart: a next sprint started after
    // today would begin Monday 19 October. Monday 19 October is the scenario's own day.
    [Theory]
    [InlineData(15)]
    [InlineData(19)]
    public async Task Closing_early_starts_the_next_sprint_after_the_closed_ones_window_not_after_today(int octoberDay)
    {
        var ct = TestContext.Current.CancellationToken;
        var series = await CreateWithOpenSprintAsync();
        _time.SetUtcNow(new DateTimeOffset(2026, 10, octoberDay, 9, 0, 0, TimeSpan.Zero));

        var closed = (await Planning.CloseSprintAsync(series.Id, new Dictionary<Guid, CloseOutcome>(), "Next", ct)).Value;

        var first = closed.Sprints.Single(sprint => sprint.Number == 1);
        Assert.False(first.IsOpen);
        Assert.Equal(new DateOnly(2026, 10, 12), first.Start);
        Assert.Equal(new DateOnly(2026, 10, 25), first.End);
        var next = closed.OpenSprint!;
        Assert.Equal(new DateOnly(2026, 10, 26), next.Start);
        Assert.Equal(new DateOnly(2026, 11, 8), next.End);

        var reading = (await Progress.ReadAsync(series.Id, cancellationToken: ct)).Value;
        Assert.True(reading.IsOpen);
        Assert.Equal(new DateOnly(2026, 10, 26), reading.Start);
        Assert.Equal(10, reading.WorkingDays);
        Assert.Equal(0, reading.WorkingDaysElapsed);

        _time.SetUtcNow(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero));
        var onTheFirstDay = (await Progress.ReadAsync(series.Id, cancellationToken: ct)).Value;
        Assert.Equal(1, onTheFirstDay.WorkingDaysElapsed);
    }

    [Fact]
    public async Task A_new_series_is_on()
    {
        var ct = TestContext.Current.CancellationToken;

        var created = (await Planning.CreateSeriesAsync(new CreateSprintSeries(["backlog"]), ct)).Value;

        Assert.True(created.IsOn);
    }

    [Fact]
    public async Task A_switched_off_series_keeps_its_repositories_and_sprints_and_its_repositories_read_as_in_no_series()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = await CreateWithOpenSprintAsync();

        var off = (await Planning.SwitchAsync(series.Id, on: false, ct)).Value;

        Assert.False(off.IsOn);
        Assert.Equal(["backlog", "docs"], off.Repositories);
        Assert.NotNull(off.OpenSprint);
        Assert.Null((await Planning.FindSeriesOfAsync("backlog", ct)).Value);
        Assert.False((await Planning.GetSeriesAsync(series.Id, ct)).Value.IsOn);
    }

    [Fact]
    public async Task A_switched_off_series_still_holds_its_repositories_against_another_series()
    {
        var ct = TestContext.Current.CancellationToken;
        var core = (await Planning.CreateSeriesAsync(new CreateSprintSeries(["backlog"], "Core"), ct)).Value;
        var docs = (await Planning.CreateSeriesAsync(new CreateSprintSeries(["docs"], "Docs"), ct)).Value;
        await Planning.SwitchAsync(core.Id, on: false, ct);

        var added = await Planning.AddRepositoryAsync(docs.Id, "backlog", ct);

        Assert.Equal("progress.repository_in_other_series", added.Error.Code);
    }

    [Fact]
    public async Task Switching_a_series_back_on_brings_its_repositories_back()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = await CreateWithOpenSprintAsync();
        await Planning.SwitchAsync(series.Id, on: false, ct);

        var on = (await Planning.SwitchAsync(series.Id, on: true, ct)).Value;

        Assert.True(on.IsOn);
        Assert.Equal(series.Id, (await Planning.FindSeriesOfAsync("docs", ct)).Value!.Id);
    }

    [Fact]
    public async Task Switching_answers_off_while_progress_is_off()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = await CreateWithOpenSprintAsync();
        _features.ProgressOn = false;

        var switched = await Planning.SwitchAsync(series.Id, on: false, ct);

        Assert.Equal("progress.off", switched.Error.Code);
    }

    [Fact]
    public async Task A_closed_sprint_refuses_a_change_through_the_port()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = await CreateWithOpenSprintAsync();
        await Planning.CloseSprintAsync(series.Id, new Dictionary<Guid, CloseOutcome>(), "Next", ct);

        var added = await Planning.AddTaskAsync(series.Id, 1, Guid.NewGuid(), ct);

        Assert.Equal("progress.sprint_closed", added.Error.Code);
    }

    // --- Deleting a series (.devbook/domain/progress/requirements.md#requirement-a-deleted-series-frees-its-repositories)

    [Fact]
    public async Task Deleting_a_series_removes_it_and_every_sprint_it_recorded()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = await CreateWithOpenSprintAsync();
        var task = Guid.NewGuid();
        await Planning.AddTaskAsync(series.Id, 1, task, ct);

        var deleted = await Planning.DeleteSeriesAsync(series.Id, ct);

        Assert.True(deleted.IsSuccess);
        Assert.Empty((await Planning.ListSeriesAsync(ct)).Value);
        Assert.Equal("progress.series_not_found", (await Planning.GetSeriesAsync(series.Id, ct)).Error.Code);
        Assert.Null((await Planning.FindSeriesOfAsync("backlog", ct)).Value);
        Assert.Equal(1, _repository.Deletes);
    }

    [Fact]
    public async Task A_deleted_series_frees_its_repositories_for_another_series()
    {
        var ct = TestContext.Current.CancellationToken;
        var core = (await Planning.CreateSeriesAsync(new CreateSprintSeries(["backlog"], "Core"), ct)).Value;
        var docs = (await Planning.CreateSeriesAsync(new CreateSprintSeries(["docs"], "Docs"), ct)).Value;
        await Planning.SwitchAsync(core.Id, on: false, ct);

        await Planning.DeleteSeriesAsync(core.Id, ct);
        var added = await Planning.AddRepositoryAsync(docs.Id, "backlog", ct);

        Assert.True(added.IsSuccess);
        Assert.Equal(docs.Id, (await Planning.FindSeriesOfAsync("backlog", ct)).Value!.Id);
    }

    [Fact]
    public async Task Deleting_leaves_every_other_series_as_it_was()
    {
        var ct = TestContext.Current.CancellationToken;
        var core = (await Planning.CreateSeriesAsync(new CreateSprintSeries(["backlog"], "Core"), ct)).Value;
        var docs = (await Planning.CreateSeriesAsync(new CreateSprintSeries(["docs"], "Docs"), ct)).Value;

        await Planning.DeleteSeriesAsync(core.Id, ct);

        var left = Assert.Single((await Planning.ListSeriesAsync(ct)).Value);
        Assert.Equal(docs.Id, left.Id);
        Assert.Equal(["docs"], left.Repositories);
        Assert.True(left.IsOn);
    }

    [Fact]
    public async Task Deleting_a_series_that_is_not_there_is_refused_and_deletes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await CreateWithOpenSprintAsync();

        var deleted = await Planning.DeleteSeriesAsync(Guid.NewGuid(), ct);

        Assert.Equal("progress.series_not_found", deleted.Error.Code);
        Assert.Single((await Planning.ListSeriesAsync(ct)).Value);
        Assert.Equal(0, _repository.Deletes);
    }

    [Fact]
    public async Task Deleting_answers_off_while_progress_is_off_and_keeps_the_series()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = await CreateWithOpenSprintAsync();
        _features.ProgressOn = false;

        var deleted = await Planning.DeleteSeriesAsync(series.Id, ct);

        Assert.Equal("progress.off", deleted.Error.Code);
        Assert.Equal(0, _repository.Deletes);
        _features.ProgressOn = true;
        Assert.True((await Planning.GetSeriesAsync(series.Id, ct)).IsSuccess);
    }

    [Fact]
    public async Task A_delete_is_announced_to_the_sync_like_any_change()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = await CreateWithOpenSprintAsync();
        var replication = _provider.GetRequiredService<ISprintSeriesReplication>();
        var heard = 0;
        replication.Changed += () => heard++;

        await Planning.DeleteSeriesAsync(series.Id, ct);

        Assert.Equal(1, heard);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
