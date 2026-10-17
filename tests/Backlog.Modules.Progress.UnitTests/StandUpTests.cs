using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.DomainModels;
using Backlog.Modules.Progress.Extensions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// The stand-up as a host composes it through <c>AddProgressModule()</c>
/// (<c>.devbook/domain/progress/domain.md#stand-up-reading</c>), one test per derivation rule
/// of <c>.devbook/domain/progress/requirements.md#the-stand-up</c>. Today is Monday 19 October
/// 2026 and the person reads by Week unless a test says otherwise, so last is 12 to 16
/// October and next is 19 to 23 October.
/// </summary>
public sealed class StandUpTests : IDisposable
{
    private const string Bank = "bank-integration";

    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryImpedimentRepository _impediments = new();
    private readonly FakeTaskSource _tasks = new();
    private readonly FakePullRequests _pulls = new();
    private readonly FakeGoalFeatures _features = new(goalsOn: true);
    private readonly FakePace _pace = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero));
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public StandUpTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton<IGoalRepository>(_goals);
        services.AddSingleton<IImpedimentRepository>(_impediments);
        services.AddSingleton<IProgressTaskSource>(_tasks);
        services.AddSingleton<IGoalPullRequestReader>(_pulls);
        services.AddSingleton<IAppFeatureSettings>(_features);
        services.AddSingleton<IStandUpPace>(_pace);
        services.AddProgressModule();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _provider.CreateScope();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DateOnly Oct(int day) => new(2026, 10, day);

    private static readonly DateOnly Monday19 = Oct(19);

    private IStandUp StandUp => _scope.ServiceProvider.GetRequiredService<IStandUp>();

    private async Task<StandUpDto> ReadAsync(DateOnly? today = null, IterationLength length = IterationLength.Week)
    {
        var read = await StandUp.ReadAsync(today ?? Monday19, length, Cancellation);
        Assert.True(read.IsSuccess, read.IsFailure ? read.Error.Message : null);
        return read.Value;
    }

    private async Task<Goal> GoalAsync(string title, params GoalAttachment[] holds)
    {
        var goal = MakeGoal.Named(title, holds);
        await _goals.SaveAsync(goal, Cancellation);
        return goal;
    }

    private ProgressTaskDto Add(ProgressTaskDto task)
    {
        _tasks.Tasks.Add(task);
        return task;
    }

    private ProgressTaskDto Entry(string title, ProgressTaskStatus status = ProgressTaskStatus.Ready, int order = 0, DateOnly? completedOn = null) =>
        Add(Make.Task(title, status, plan: Bank, completedOn: completedOn) with { Order = order });

    private static IReadOnlyList<string> Titles(IReadOnlyList<StandUpGroupDto> groups, string? goal) =>
        [.. groups.Single(group => group.GoalTitle == goal).Items.Select(item => item.Title)];

    [Fact]
    public async Task With_goals_off_the_stand_up_answers_off()
    {
        _features.GoalsOn = false;

        Assert.Equal("progress.goals_off", (await StandUp.ReadAsync(Monday19, IterationLength.Week, Cancellation)).Error.Code);
        Assert.Equal("progress.goals_off", (await StandUp.ReadAsync(Cancellation)).Error.Code);
    }

    /// <summary>The windows come from the day and length asked; the overload without them
    /// reads the host's today and the person's own length.</summary>
    [Fact]
    public async Task The_windows_follow_the_day_and_the_length()
    {
        var byWeek = await ReadAsync(Oct(17));
        Assert.Equal((Oct(12), Oct(16), Oct(19), Oct(23)), (byWeek.Last.First, byWeek.Last.Last, byWeek.Next.First, byWeek.Next.Last));

        _scope.ServiceProvider.GetRequiredService<IIterationSettings>().SetLength(IterationLength.Day, Monday19);
        var byDay = (await StandUp.ReadAsync(Cancellation)).Value;
        Assert.Equal((IterationLength.Day, Oct(16), Oct(19)), (byDay.Next.Length, byDay.Last.First, byDay.Next.First));
    }

    /// <summary>One read of Tasks covers the goals' work, the four windows of history and the next window.</summary>
    [Fact]
    public async Task One_read_of_tasks_asks_for_the_history_and_the_next_window()
    {
        await GoalAsync("Customers get paid faster", MakeGoal.Plan("+" + Bank));

        await ReadAsync();

        var query = _tasks.LastQuery!;
        Assert.Equal([Bank], query.PlanTags);
        Assert.Equal((new DateOnly(2026, 9, 21), Oct(16)), (query.CompletedFrom, query.CompletedThrough));
        Assert.Equal((Oct(19), Oct(23)), (query.DueFrom, query.DueThrough));
    }

    /// <summary>Scenario "A week's stand-up": Goals, Finished 7, Planned 5, Impediments 3.</summary>
    [Fact]
    public async Task The_stand_up_has_four_blocks_with_their_counts()
    {
        var doing1 = Add(Make.Task("Customer search by IBAN", ProgressTaskStatus.InProgress));
        var doing2 = Add(Make.Task("Index the chapters", ProgressTaskStatus.InProgress));
        var done = Enumerable.Range(0, 5).Select(i => Add(Make.Task($"Done {i}", ProgressTaskStatus.Done, completedOn: Oct(12 + i)))).ToList();
        await GoalAsync("Customers get paid faster", [MakeGoal.Task(doing1.Id), .. done.Take(3).Select(task => MakeGoal.Task(task.Id))]);
        await GoalAsync("Devbook search ships", [MakeGoal.Task(doing2.Id), .. done.Skip(3).Select(task => MakeGoal.Task(task.Id))]);
        await GoalAsync("Phone capture in daily use", MakeGoal.PullRequest("JSdotNet/Backlog", 77));
        _pulls.Known.Add(new GoalPullRequestDto("JSdotNet/Backlog", "77", LinkedPullRequestState.Open, null, null, null));
        Add(Make.Task("VAT rate per line item", ProgressTaskStatus.Done, completedOn: Oct(14)));
        Add(Make.Task("Credit note layout", ProgressTaskStatus.Done, completedOn: Oct(15)));
        Add(Make.Task("Due Tuesday") with { DueOn = Oct(20) });
        Add(Make.Task("Due Thursday") with { DueOn = Oct(22) });
        foreach (var title in new[] { "Bank sample files missing", "Credit note numbering unclear", "Phone build signing" })
        {
            await _impediments.SaveAsync(MakeImpediment.Raised(title), Cancellation);
        }

        var standUp = await ReadAsync();

        Assert.Equal(3, standUp.Goals.Count);
        Assert.Equal(7, standUp.Finished.Sum(group => group.Items.Count));
        Assert.Equal(5, standUp.Planned.Sum(group => group.Items.Count));
        Assert.Equal(3, standUp.Impediments.Count);
    }

    /// <summary>Scenario "A task done outside every goal".</summary>
    [Fact]
    public async Task Finished_is_grouped_by_goal_with_no_goal_last()
    {
        var invoice = Add(Make.Task("Invoice PDF export", ProgressTaskStatus.Done, completedOn: Oct(14)));
        Add(Make.Task("VAT rate per line item", ProgressTaskStatus.Done, completedOn: Oct(14)));
        Add(Make.Task("Done the week before", ProgressTaskStatus.Done, completedOn: Oct(9)));
        Add(Make.Task("Completed, not done", ProgressTaskStatus.Ready, completedOn: Oct(14)));
        await GoalAsync("Devbook search ships");
        await GoalAsync("Customers get paid faster", MakeGoal.Task(invoice.Id));

        var standUp = await ReadAsync();

        Assert.Equal(["Customers get paid faster", null], standUp.Finished.Select(group => group.GoalTitle));
        Assert.Equal(["Invoice PDF export"], Titles(standUp.Finished, "Customers get paid faster"));
        Assert.Equal(["VAT rate per line item"], Titles(standUp.Finished, null));
        Assert.Equal(Oct(14), standUp.Finished[0].Items[0].DoneOn);
    }

    [Fact]
    public async Task A_pull_request_merged_in_last_is_finished_under_its_goal()
    {
        await GoalAsync("Customers get paid faster", MakeGoal.PullRequest("JSdotNet/Backlog", 1210));
        _pulls.Known.Add(new GoalPullRequestDto("JSdotNet/Backlog", "1210", LinkedPullRequestState.Merged, Oct(15), null, null));

        var item = Assert.Single(Assert.Single((await ReadAsync()).Finished).Items);

        Assert.Equal((GoalAttachmentKind.PullRequest, "JSdotNet/Backlog", 1210, Oct(15)), (item.Kind, item.Repository, item.Number, item.DoneOn));
    }

    /// <summary>Scenario "All three sources", with the entries the first ready one is
    /// chosen past: done, in progress, blocked, waiting on open work, and held by another goal.</summary>
    [Fact]
    public async Task Planned_is_derived_from_three_sources()
    {
        Entry("Bank import mapping", ProgressTaskStatus.Done, order: 0, completedOn: Oct(9));
        Entry("Customer search by IBAN", ProgressTaskStatus.InProgress, order: 1);
        Add(Make.Task("Statement parser", plan: Bank, blocked: true) with { Order = 2 });
        Add(Make.Task("Reconcile fees", plan: Bank) with { Order = 3, WaitsOnOpenWork = true });
        var elsewhere = Entry("Held by another goal", order: 4);
        Entry("Payment matching rules", order: 5);
        Add(Make.Task("Recurring invoices", plan: Bank) with { Order = 6, DueOn = Oct(22) });
        Entry("A later step", order: 7);
        await GoalAsync("Customers get paid faster", MakeGoal.Plan("+" + Bank));
        await GoalAsync("Devbook search ships", MakeGoal.Task(elsewhere.Id));

        var standUp = await ReadAsync();

        var planned = Assert.Single(standUp.Planned);
        Assert.Equal("Customers get paid faster", planned.GoalTitle);
        Assert.Equal(["Customer search by IBAN", "Recurring invoices", "Payment matching rules"], planned.Items.Select(item => item.Title));
    }

    /// <summary>Scenario "An item from two sources".</summary>
    [Fact]
    public async Task An_item_from_two_sources_is_planned_once()
    {
        var iban = Add(Make.Task("Customer search by IBAN", ProgressTaskStatus.InProgress) with { DueOn = Oct(21) });
        await GoalAsync("Customers get paid faster", MakeGoal.Task(iban.Id));

        var item = Assert.Single(Assert.Single((await ReadAsync()).Planned).Items);

        Assert.Equal(iban.Id, item.TaskId);
    }

    [Fact]
    public async Task Open_work_no_goal_holds_is_planned_under_no_goal_when_it_is_due_in_next()
    {
        await GoalAsync("Customers get paid faster");
        Add(Make.Task("Due Thursday") with { DueOn = Oct(22) });
        Add(Make.Task("Due next week") with { DueOn = Oct(27) });
        Add(Make.Task("Archived, due Thursday", ProgressTaskStatus.Archived) with { DueOn = Oct(22) });
        Add(Make.Task("In progress, nobody's", ProgressTaskStatus.InProgress));

        var planned = Assert.Single((await ReadAsync()).Planned);

        Assert.Null(planned.GoalId);
        Assert.Equal(["Due Thursday"], planned.Items.Select(item => item.Title));
    }

    [Fact]
    public async Task A_blocked_task_is_not_planned_as_in_progress()
    {
        var blocked = Add(Make.Task("Statement parser", ProgressTaskStatus.InProgress, blocked: true));
        await GoalAsync("Customers get paid faster", MakeGoal.Task(blocked.Id));

        Assert.Empty((await ReadAsync()).Planned);
    }

    /// <summary>Scenario "A week at 18 points": <c>21 / 18 pts</c>.</summary>
    [Fact]
    public async Task The_points_tag_adds_up_planned_effort_beside_the_weekly_pace()
    {
        int?[] efforts = [5, 8, 3, 5, null];
        var doing = efforts.Select((effort, i) => Add(Make.Task($"Doing {i}", ProgressTaskStatus.InProgress) with { Effort = effort })).ToList();
        await GoalAsync("Customers get paid faster", [.. doing.Select(task => MakeGoal.Task(task.Id))]);
        _pace.PointsPerWeek = 18m;

        var standUp = await ReadAsync();

        Assert.Equal((21, 18), (standUp.PlannedEffort, standUp.Capacity));
    }

    /// <summary>Scenario "A day at 18 points a week": <c>5 / 4 pts</c>.</summary>
    [Fact]
    public async Task The_points_tag_scales_the_pace_to_a_day()
    {
        var doing = Add(Make.Task("Customer search by IBAN", ProgressTaskStatus.InProgress) with { Effort = 5 });
        await GoalAsync("Customers get paid faster", MakeGoal.Task(doing.Id));
        _pace.PointsPerWeek = 18m;

        var standUp = await ReadAsync(length: IterationLength.Day);

        Assert.Equal((5, 4), (standUp.PlannedEffort, standUp.Capacity));
    }

    [Fact]
    public async Task Without_a_pace_there_is_no_capacity()
    {
        Assert.Null((await ReadAsync()).Capacity);
    }

    /// <summary>Scenarios "One escalated, one resolved" and "A new impediment", read on
    /// Friday 16 October by Day so the 15th is last.</summary>
    [Fact]
    public async Task Read_by_Day_impediments_list_the_open_ones_with_who_age_and_goal_and_count_the_resolved()
    {
        var mapping = Entry("Bank import mapping", order: 1);
        await GoalAsync("Devbook search ships");
        await GoalAsync("Customers get paid faster", MakeGoal.Plan("+" + Bank));

        var resolved = MakeImpediment.Raised("Test data refresh", Oct(12));
        Assert.True(resolved.MoveTo(ImpedimentState.Resolved, Oct(15)).IsSuccess);
        var bank = MakeImpediment.Raised("Bank sample files missing", Oct(14));
        Assert.True(bank.MoveTo(ImpedimentState.Reported, Oct(14), "bank support").IsSuccess);
        Assert.True(bank.MoveTo(ImpedimentState.Escalated, Oct(15), "account manager").IsSuccess);
        Assert.True(bank.HoldBack(mapping.Id).IsSuccess);
        var credit = MakeImpediment.Raised("Credit note numbering unclear", Oct(15));
        await _impediments.SaveAsync(resolved, Cancellation);
        await _impediments.SaveAsync(credit, Cancellation);
        await _impediments.SaveAsync(bank, Cancellation);

        var standUp = await ReadAsync(Oct(16), IterationLength.Day);

        Assert.Equal(["Bank sample files missing", "Credit note numbering unclear"], standUp.Impediments.Select(line => line.Title));
        var line = standUp.Impediments[0];
        Assert.Equal((ImpedimentState.Escalated, "account manager", 3, "Customers get paid faster"), (line.State, line.With, line.DaysOpen, line.GoalTitle));
        var fresh = standUp.Impediments[1];
        Assert.Equal((ImpedimentState.New, (string?)null, (string?)null), (fresh.State, fresh.With, fresh.GoalTitle));
        Assert.Equal(1, standUp.ResolvedInLast);
    }

    /// <summary>An impediment's note reaches the stand-up as it is stored, so the desktop row
    /// can show and edit it; one without a note carries none.</summary>
    [Fact]
    public async Task An_impediment_line_carries_its_note()
    {
        var bank = MakeImpediment.Raised("Bank sample files missing", Oct(14));
        bank.EditNote("Ask for the CAMT.053 samples too");
        var credit = MakeImpediment.Raised("Credit note numbering unclear", Oct(15));
        await _impediments.SaveAsync(bank, Cancellation);
        await _impediments.SaveAsync(credit, Cancellation);

        var standUp = await ReadAsync(Oct(16), IterationLength.Day);

        Assert.Equal(
            [("Bank sample files missing", (string?)"Ask for the CAMT.053 samples too"), ("Credit note numbering unclear", null)],
            standUp.Impediments.Select(line => (line.Title, line.Note)));
    }

    /// <summary>Scenario "Two items a week, six open", read through the stand-up's Goals block:
    /// it lands 6 November, after its due date of 31 October.</summary>
    [Fact]
    public async Task The_goals_block_carries_progress_standing_and_landing_date()
    {
        DateOnly[] doneOn = [new(2026, 9, 21), new(2026, 9, 30), Oct(1), Oct(2), Oct(6), Oct(8), Oct(13), Oct(15)];
        foreach (var (day, i) in doneOn.Select((day, i) => (day, i))) Entry($"Done {i}", ProgressTaskStatus.Done, order: i, completedOn: day);
        foreach (var i in Enumerable.Range(0, 6)) Entry($"Open {i}", order: 10 + i);
        var goal = await GoalAsync("Customers get paid faster", MakeGoal.Plan("+" + Bank));

        var standUp = await ReadAsync(Oct(17));

        var line = Assert.Single(standUp.Goals);
        Assert.Equal((goal.Id, 8, 14, 2), (line.Id, line.Progress.Done, line.Progress.Total, line.Progress.DoneInWindow));
        Assert.Equal(new DateOnly(2026, 11, 6), line.LandsOn);
        Assert.Equal(MakeGoal.Due, line.DueDate);
        Assert.Equal(GoalStanding.AtRisk, line.Standing);
    }

    /// <summary>Scenario "An impediment on a plan's entry": a goal that lands in time reads On
    /// track until an open impediment holds back one of its open entries, then At risk.</summary>
    [Fact]
    public async Task An_open_impediment_puts_a_goal_landing_in_time_at_risk()
    {
        // Two done in the four weeks read and one open: it lands on Friday 30 October, in time.
        Entry("Done earlier", ProgressTaskStatus.Done, completedOn: Oct(6));
        Entry("Done", ProgressTaskStatus.Done, completedOn: Oct(13));
        var mapping = Entry("Bank import mapping", order: 1);
        await GoalAsync("Customers get paid faster", MakeGoal.Plan("+" + Bank));

        var before = Assert.Single((await ReadAsync(Oct(17))).Goals);
        Assert.Equal((GoalStanding.OnTrack, (DateOnly?)Oct(30)), (before.Standing, before.LandsOn));

        var impediment = MakeImpediment.Raised();
        Assert.True(impediment.MoveTo(ImpedimentState.Reported, Oct(14), "bank support").IsSuccess);
        Assert.True(impediment.MoveTo(ImpedimentState.Escalated, Oct(15), "account manager").IsSuccess);
        Assert.True(impediment.HoldBack(mapping.Id).IsSuccess);
        await _impediments.SaveAsync(impediment, Cancellation);

        var after = Assert.Single((await ReadAsync(Oct(17))).Goals);
        Assert.Equal((GoalStanding.AtRisk, (DateOnly?)Oct(30)), (after.Standing, after.LandsOn));
    }

    [Fact]
    public async Task A_goal_landing_in_time_with_only_a_resolved_impediment_is_on_track()
    {
        // Two done in the four weeks read and one open: it lands on Friday 30 October, in time.
        Entry("Done earlier", ProgressTaskStatus.Done, completedOn: Oct(6));
        Entry("Done", ProgressTaskStatus.Done, completedOn: Oct(13));
        var open = Entry("Open", order: 1);
        await GoalAsync("Customers get paid faster", MakeGoal.Plan("+" + Bank));
        var impediment = MakeImpediment.Raised();
        Assert.True(impediment.HoldBack(open.Id).IsSuccess);
        Assert.True(impediment.MoveTo(ImpedimentState.Resolved, Oct(16)).IsSuccess);
        await _impediments.SaveAsync(impediment, Cancellation);

        var line = Assert.Single((await ReadAsync(Oct(17))).Goals);

        Assert.Equal(GoalStanding.OnTrack, line.Standing);
        Assert.Equal(Oct(30), line.LandsOn);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    private sealed class FakePace : IStandUpPace
    {
        public decimal? PointsPerWeek { get; set; }

        public Task<decimal?> PointsPerWeekAsync(CancellationToken cancellationToken = default) => Task.FromResult(PointsPerWeek);
    }
}
