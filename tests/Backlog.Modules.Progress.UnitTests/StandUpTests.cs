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
        var done = Enumerable.Range(0, 7).Select(i => Add(Make.Task($"Done {i}", ProgressTaskStatus.Done, completedOn: Oct(12 + (i % 5))))).ToList();
        await GoalAsync("Customers get paid faster", [MakeGoal.Task(doing1.Id), .. done.Take(4).Select(task => MakeGoal.Task(task.Id))]);
        await GoalAsync("Devbook search ships", [MakeGoal.Task(doing2.Id), .. done.Skip(4).Select(task => MakeGoal.Task(task.Id))]);
        await GoalAsync("Phone capture in daily use", MakeGoal.PullRequest("JSdotNet/Backlog", 77));
        _pulls.Known.Add(new GoalPullRequestDto("JSdotNet/Backlog", "77", LinkedPullRequestState.Open, null, null, null));
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
    public async Task Finished_lists_only_what_a_goal_counts()
    {
        var invoice = Add(Make.Task("Invoice PDF export", ProgressTaskStatus.Done, completedOn: Oct(14)));
        Add(Make.Task("VAT rate per line item", ProgressTaskStatus.Done, completedOn: Oct(14)));
        Add(Make.Task("Done the week before", ProgressTaskStatus.Done, completedOn: Oct(9)));
        Add(Make.Task("Completed, not done", ProgressTaskStatus.Ready, completedOn: Oct(14)));
        await GoalAsync("Devbook search ships");
        await GoalAsync("Customers get paid faster", MakeGoal.Task(invoice.Id));

        var standUp = await ReadAsync();

        Assert.Equal(["Customers get paid faster"], standUp.Finished.Select(group => group.GoalTitle));
        Assert.Equal(["Invoice PDF export"], Titles(standUp.Finished, "Customers get paid faster"));
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
        bank.HoldBack(HeldBackItem.ForTask(mapping.Id).Value);
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

    /// <summary>A goal without a due date is due on the last day of next, Friday 23 October,
    /// and says so; a dated goal carries its own date.</summary>
    [Fact]
    public async Task A_goal_without_a_due_date_reads_as_due_at_the_end_of_next()
    {
        var dateless = MakeGoal.Named("Invoices settle in a day");
        Assert.True(dateless.ChangeDueDate(null).IsSuccess);
        await _goals.SaveAsync(dateless, Cancellation);
        var dated = await GoalAsync("Customers get paid faster");

        var standUp = await ReadAsync();

        var derived = standUp.Goals.Single(goal => goal.Id == dateless.Id);
        Assert.Equal((Oct(23), true), (derived.DueDate, derived.DueIsIterationEnd));
        var own = standUp.Goals.Single(goal => goal.Id == dated.Id);
        Assert.Equal((MakeGoal.Due, false), (own.DueDate, own.DueIsIterationEnd));
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
        impediment.HoldBack(HeldBackItem.ForTask(mapping.Id).Value);
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
        impediment.HoldBack(HeldBackItem.ForTask(open.Id).Value);
        Assert.True(impediment.MoveTo(ImpedimentState.Resolved, Oct(16)).IsSuccess);
        await _impediments.SaveAsync(impediment, Cancellation);

        var line = Assert.Single((await ReadAsync(Oct(17))).Goals);

        Assert.Equal(GoalStanding.OnTrack, line.Standing);
        Assert.Equal(Oct(30), line.LandsOn);
    }

    private async Task<Impediment> ImpedimentAsync(string title, params HeldBackItem[] holds)
    {
        var impediment = MakeImpediment.Raised(title, Oct(14));
        foreach (var item in holds) impediment.HoldBack(item);
        await _impediments.SaveAsync(impediment, Cancellation);
        return impediment;
    }

    private static HeldBackItem HeldTask(ProgressTaskDto task) => HeldBackItem.ForTask(task.Id).Value;

    private static HeldBackItem HeldPullRequest(string repository, int number) => HeldBackItem.ForPullRequest(repository, number).Value;

    private static HeldBackItem HeldPlan(string tag) => HeldBackItem.ForPlan(tag).Value;

    /// <summary>A goal that lands in time — two done in the four weeks read, one open — with
    /// <paramref name="open"/> as its open entry: it lands on Friday 30 October.</summary>
    private async Task<Goal> LandingInTimeAsync(ProgressTaskDto open)
    {
        Entry("Done earlier", ProgressTaskStatus.Done, completedOn: Oct(6));
        Entry("Done", ProgressTaskStatus.Done, completedOn: Oct(13));
        Add(open);
        return await GoalAsync("Customers get paid faster", MakeGoal.Plan("+" + Bank));
    }

    /// <summary>Scenario "An impediment on a pull request": it holds back
    /// <c>acme/invoicing#1358</c> alone, which a task the goal counts links — the line names
    /// that goal, and the goal reads At risk.</summary>
    [Fact]
    public async Task An_impediment_on_a_pull_request_a_counted_task_links_names_that_goal_and_puts_it_at_risk()
    {
        var mapping = Make.Task("Bank import mapping", plan: Bank, pullRequests: new PullRequestRefDto("acme/invoicing", "1358")) with { Order = 1 };
        var goal = await LandingInTimeAsync(mapping);
        await GoalAsync("Devbook search ships");
        await ImpedimentAsync("Bank sandbox down", HeldPullRequest("ACME/Invoicing", 1358));

        var standUp = await ReadAsync(Oct(17));

        Assert.Equal((goal.Id, "Customers get paid faster"), (standUp.Impediments[0].GoalId, standUp.Impediments[0].GoalTitle));
        Assert.Equal(GoalStanding.AtRisk, standUp.Goals.Single(line => line.Id == goal.Id).Standing);
    }

    /// <summary>Scenario "An impediment on the goal's plan".</summary>
    [Fact]
    public async Task An_open_impediment_on_the_goals_plan_puts_it_at_risk()
    {
        var goal = await LandingInTimeAsync(Make.Task("Bank import mapping", plan: Bank) with { Order = 1 });
        await ImpedimentAsync("Bank API contract unsigned", HeldPlan("+" + Bank));

        var standUp = await ReadAsync(Oct(17));

        var line = Assert.Single(standUp.Goals);
        Assert.Equal((GoalStanding.AtRisk, (DateOnly?)Oct(30)), (line.Standing, line.LandsOn));
        Assert.Equal(goal.Title, Assert.Single(standUp.Impediments).GoalTitle);
    }

    /// <summary>Scenario "An impediment ready to resolve": the goal stays At risk until the
    /// person resolves it.</summary>
    [Fact]
    public async Task A_goal_stays_at_risk_while_its_impediment_reads_ready_to_resolve()
    {
        var done = Make.Task("Bank sample import", ProgressTaskStatus.Done, plan: Bank, completedOn: Oct(14));
        await LandingInTimeAsync(Make.Task("Bank import mapping", plan: Bank) with { Order = 1 });
        Add(done);
        await ImpedimentAsync("Bank sample files missing", HeldTask(done));

        var standUp = await ReadAsync(Oct(17));

        Assert.True(Assert.Single(standUp.Impediments).ReadyToResolve);
        Assert.Equal(GoalStanding.AtRisk, Assert.Single(standUp.Goals).Standing);
    }

    /// <summary>A goal whose every item is done stays At risk while an open impediment —
    /// here one that reads Ready to resolve — holds back one of its tasks, and reads On track
    /// once the person resolves it.</summary>
    [Fact]
    public async Task A_finished_goal_stays_at_risk_until_the_impediment_holding_it_back_is_resolved()
    {
        var done = Add(Make.Task("Bank sample import", ProgressTaskStatus.Done, completedOn: Oct(14)));
        var goal = await GoalAsync("Customers get paid faster", MakeGoal.Task(done.Id));
        var impediment = await ImpedimentAsync("Bank sample files missing", HeldTask(done));

        var held = (await ReadAsync(Oct(17))).Goals.Single(line => line.Id == goal.Id);
        Assert.Equal((1, 1), (held.Progress.Done, held.Progress.Total));
        Assert.Equal(GoalStanding.AtRisk, held.Standing);

        Assert.True(impediment.MoveTo(ImpedimentState.Resolved, Oct(16)).IsSuccess);
        await _impediments.SaveAsync(impediment, Cancellation);

        var resolved = (await ReadAsync(Oct(17))).Goals.Single(line => line.Id == goal.Id);
        Assert.Equal((GoalStanding.OnTrack, (DateOnly?)null), (resolved.Standing, resolved.LandsOn));
    }

    /// <summary>When its items belong to several goals, the line names the goal of the first
    /// item linked.</summary>
    [Fact]
    public async Task The_line_names_the_goal_of_the_first_item_linked()
    {
        var search = Add(Make.Task("Index the chapters", ProgressTaskStatus.InProgress));
        var mapping = Add(Make.Task("Bank import mapping", ProgressTaskStatus.InProgress));
        var unattached = Add(Make.Task("Tidy the readme", ProgressTaskStatus.InProgress));
        await GoalAsync("Customers get paid faster", MakeGoal.Task(mapping.Id));
        await GoalAsync("Devbook search ships", MakeGoal.Task(search.Id));
        await ImpedimentAsync("Laptop in repair", HeldTask(unattached), HeldTask(search), HeldTask(mapping));

        var line = Assert.Single((await ReadAsync()).Impediments);

        Assert.Equal("Devbook search ships", line.GoalTitle);
    }

    /// <summary>Scenarios "A done task still marked blocked" and "Work that comes back":
    /// Ready to resolve follows the work on every reading, and the reading stores nothing.</summary>
    [Fact]
    public async Task Ready_to_resolve_follows_the_work_and_reading_it_stores_nothing()
    {
        var task = Add(Make.Task("Beschikbaarheid van reserveringen", ProgressTaskStatus.Done, blocked: true, completedOn: Oct(16)));
        var impediment = await ImpedimentAsync("Beschikbaarheid van reserveringen", HeldTask(task));
        var saves = _impediments.Saves;

        var ready = Assert.Single((await ReadAsync()).Impediments);
        Assert.Equal((ImpedimentState.New, true), (ready.State, ready.ReadyToResolve));

        _tasks.Tasks[_tasks.Tasks.IndexOf(task)] = task with { Status = ProgressTaskStatus.InProgress, CompletedOn = null };
        var back = Assert.Single((await ReadAsync()).Impediments);

        Assert.Equal((ImpedimentState.New, false), (back.State, back.ReadyToResolve));
        Assert.Equal(saves, _impediments.Saves);
        Assert.Equal([new ImpedimentStateChange(ImpedimentState.New, Oct(14), null)], impediment.History);
    }

    /// <summary>Each held-back item is read with its kind, its words and whether it is
    /// settled: a pull request from an archived repository never is, whatever it says.</summary>
    [Fact]
    public async Task The_line_lists_each_held_back_item_with_whether_it_is_settled()
    {
        var archived = Add(Make.Task("Old CSV import", ProgressTaskStatus.Archived));
        var gone = Guid.NewGuid();
        Add(Make.Task("Payment matching rules", ProgressTaskStatus.InProgress, plan: Bank));
        _pulls.Known.Add(new GoalPullRequestDto("acme/invoicing", "1344", LinkedPullRequestState.Closed, null, null, null));
        _pulls.Known.Add(new GoalPullRequestDto("acme/legacy-billing", "77", LinkedPullRequestState.Merged, null, null, null, InArchivedRepository: true));
        await ImpedimentAsync(
            "Bank sandbox down",
            HeldTask(archived),
            HeldBackItem.ForTask(gone).Value,
            HeldPullRequest("acme/invoicing", 1344),
            HeldPullRequest("acme/legacy-billing", 77),
            HeldPlan(Bank));

        var line = Assert.Single((await ReadAsync()).Impediments);

        Assert.Equal(
            [
                (HeldBackKind.Task, (string?)"Old CSV import", true),
                (HeldBackKind.Task, null, false),
                (HeldBackKind.PullRequest, "acme/invoicing#1344", true),
                (HeldBackKind.PullRequest, "acme/legacy-billing#77", false),
                (HeldBackKind.Plan, "+" + Bank, false),
            ],
            line.HoldsBack.Select(item => (item.Item.Kind, item.Title, item.Settled)));
        Assert.False(line.ReadyToResolve);
    }

    /// <summary>One read of Tasks and one of GitHub cover what the open impediments hold
    /// back; a resolved one asks for nothing.</summary>
    [Fact]
    public async Task The_reading_asks_for_what_the_open_impediments_hold_back()
    {
        var task = Guid.NewGuid();
        await ImpedimentAsync("Bank sandbox down", HeldBackItem.ForTask(task).Value, HeldPlan("+bank-statements"), HeldPullRequest("acme/invoicing", 1358));
        var resolved = await ImpedimentAsync("Old blocker", HeldPullRequest("acme/invoicing", 9), HeldPlan("old-plan"));
        Assert.True(resolved.MoveTo(ImpedimentState.Resolved, Oct(15)).IsSuccess);
        await _impediments.SaveAsync(resolved, Cancellation);

        await ReadAsync();

        var query = _tasks.LastQuery!;
        Assert.Contains(task, query.TaskIds);
        Assert.Equal(["bank-statements"], query.PlanTags);
        Assert.True(query.AnyRepository);
        Assert.Equal([new PullRequestRefDto("acme/invoicing", "1358")], _pulls.Asked);
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
