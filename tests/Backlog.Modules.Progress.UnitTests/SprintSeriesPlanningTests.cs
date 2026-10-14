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
        services.AddSingleton<ISprintPullRequestReader>(_pulls);
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
        var item = Assert.Single(removed.Value.OpenSprint!.Items);
        Assert.False(item.AddedDirectly);
        Assert.True(item.IsGoal);
        Assert.Equal("Waiting on review", item.Note);
        Assert.True(item.OpenQuestion);
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

    [Fact]
    public async Task A_closed_sprint_refuses_a_change_through_the_port()
    {
        var ct = TestContext.Current.CancellationToken;
        var series = await CreateWithOpenSprintAsync();
        await Planning.CloseSprintAsync(series.Id, new Dictionary<Guid, CloseOutcome>(), "Next", ct);

        var added = await Planning.AddTaskAsync(series.Id, 1, Guid.NewGuid(), ct);

        Assert.Equal("progress.sprint_closed", added.Error.Code);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
