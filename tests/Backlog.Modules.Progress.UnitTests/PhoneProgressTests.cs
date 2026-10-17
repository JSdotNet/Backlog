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
/// What the module gives the phone (local ADR 0035 §8,
/// <c>.devbook/domain/progress/features.md#goals-on-the-phone</c>), as a host composes it
/// through <c>AddProgressModule()</c>: the stand-up context a desktop sends — the pull
/// request statuses the goals count, the pace, the working week and the iteration choice —
/// and the impediment edits the phone asks for, applied through the impediment features.
/// </summary>
public sealed class PhoneProgressTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryImpedimentRepository _impediments = new();
    private readonly FakeTaskSource _tasks = new();
    private readonly FakePullRequests _pulls = new();
    private readonly FakeGoalFeatures _features = new(goalsOn: true);
    private readonly FakeTimeProvider _time = new(Morning);
    private readonly ServiceProvider _provider;

    public PhoneProgressTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton<ISprintSeriesRepository>(new InMemorySprintSeriesRepository());
        services.AddSingleton<IGoalRepository>(_goals);
        services.AddSingleton<IImpedimentRepository>(_impediments);
        services.AddSingleton<ISprintTaskSource>(_tasks);
        services.AddSingleton<IGoalPullRequestReader>(_pulls);
        services.AddSingleton<IAppFeatureSettings>(_features);
        services.AddProgressModule();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private IStandUpContextSource Context => _provider.GetRequiredService<IStandUpContextSource>();

    private IImpedimentEditIntake Edits => _provider.GetRequiredService<IImpedimentEditIntake>();

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task The_context_carries_every_pull_request_the_goals_count_with_its_checks_and_approvals()
    {
        var linked = new PullRequestRefDto("JSdotNet/Backlog", "1360");
        var task = Make.Task("Read the checks", SprintTaskStatus.InProgress, pullRequests: linked);
        _tasks.Tasks.Add(task);
        await _goals.SaveAsync(MakeGoal.Named("Merge without opening GitHub", MakeGoal.Task(task.Id), MakeGoal.PullRequest("JSdotNet/Backlog", 1364)), Cancellation);
        _pulls.Known.Add(new GoalPullRequestDto("JSdotNet/Backlog", "1360", LinkedPullRequestState.Open, null,
            new GoalPullRequestChecksDto(9, 0, 5, []), new GoalPullRequestApprovalsDto(1, 2)));
        _pulls.Known.Add(new GoalPullRequestDto("JSdotNet/Backlog", "1364", LinkedPullRequestState.Merged, new DateOnly(2026, 10, 16),
            new GoalPullRequestChecksDto(14, 0, 0, []), new GoalPullRequestApprovalsDto(1, null)));

        var copy = await Context.ReadAsync(Cancellation);

        Assert.NotNull(copy);
        var context = ProgressDocuments.ReadContext(copy.Content);
        Assert.NotNull(context);
        Assert.Equal(Morning, copy.UpdatedAt);
        Assert.Equal(Morning, context.ReadAt);
        Assert.Equal(["1360", "1364"], context.PullRequests.Select(pull => pull.Number));
        var running = context.PullRequests[0];
        Assert.Equal(new GoalPullRequestChecksDto(9, 0, 5, []).Total, running.Checks!.Total);
        Assert.Equal(9, running.Checks.Passed);
        Assert.Equal(new GoalPullRequestApprovalsDto(1, 2), running.Approvals);
        Assert.Equal(new DateOnly(2026, 10, 16), context.PullRequests[1].MergedOn);
        Assert.Null(context.PullRequests[1].Approvals!.Required);
        Assert.Equal(IterationLength.Week, context.Length);
    }

    [Fact]
    public async Task An_unchanged_reading_keeps_its_stamp_and_a_changed_one_is_stamped_afresh()
    {
        await _goals.SaveAsync(MakeGoal.Named("Merge without opening GitHub", MakeGoal.PullRequest("JSdotNet/Backlog", 1364)), Cancellation);
        _pulls.Known.Add(new GoalPullRequestDto("JSdotNet/Backlog", "1364", LinkedPullRequestState.Open, null, new GoalPullRequestChecksDto(2, 0, 12, []), new GoalPullRequestApprovalsDto(0, 1)));

        var first = await Context.ReadAsync(Cancellation);
        _time.Advance(TimeSpan.FromMinutes(5));
        var again = await Context.ReadAsync(Cancellation);

        Assert.Equal(first, again);

        _pulls.Known[0] = _pulls.Known[0] with { Checks = new GoalPullRequestChecksDto(14, 0, 0, []) };
        var changed = await Context.ReadAsync(Cancellation);

        Assert.Equal(Morning.AddMinutes(5), changed!.UpdatedAt);
        Assert.Equal(14, ProgressDocuments.ReadContext(changed.Content)!.PullRequests[0].Checks!.Passed);
    }

    [Fact]
    public async Task There_is_no_context_to_send_with_no_goal_or_with_the_goals_switch_off()
    {
        Assert.Null(await Context.ReadAsync(Cancellation));

        await _goals.SaveAsync(MakeGoal.Named(), Cancellation);
        _features.GoalsOn = false;

        Assert.Null(await Context.ReadAsync(Cancellation));
    }

    [Fact]
    public async Task A_state_change_from_the_phone_moves_the_impediment_through_its_features()
    {
        var impediment = await RaisedAsync();
        _time.Advance(TimeSpan.FromHours(1));

        var outcome = await Edits.ApplyAsync(Edit(impediment.Id, ImpedimentEditKind.State, state: ImpedimentState.Reported, with: "bank support"), Cancellation);

        Assert.Equal(ImpedimentEditOutcome.Applied, outcome);
        var stored = await _impediments.GetAsync(impediment.Id, Cancellation);
        Assert.Equal(ImpedimentState.Reported, stored!.State);
        Assert.Equal("bank support", stored.ReportedTo);
        Assert.Equal(new DateOnly(2026, 10, 19), stored.History[^1].On);
    }

    [Fact]
    public async Task A_check_again_date_and_a_note_from_the_phone_are_set()
    {
        var impediment = await RaisedAsync();
        _time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(ImpedimentEditOutcome.Applied, await Edits.ApplyAsync(Edit(impediment.Id, ImpedimentEditKind.CheckAgain, checkAgain: new DateOnly(2026, 10, 21)), Cancellation));
        Assert.Equal(ImpedimentEditOutcome.Applied, await Edits.ApplyAsync(Edit(impediment.Id, ImpedimentEditKind.Note, note: "Asked twice"), Cancellation));

        var stored = await _impediments.GetAsync(impediment.Id, Cancellation);
        Assert.Equal(new DateOnly(2026, 10, 21), stored!.CheckAgain);
        Assert.Equal("Asked twice", stored.Note);
    }

    [Fact]
    public async Task An_edit_of_a_field_changed_since_the_phone_saw_it_is_stale_and_changes_nothing()
    {
        var impediment = await RaisedAsync();
        impediment.EditNote("Written on the desktop since");
        await _impediments.SaveAsync(impediment, Cancellation);
        var saves = _impediments.Saves;

        var note = await Edits.ApplyAsync(Edit(impediment.Id, ImpedimentEditKind.Note, note: "Older than the desktop's") with { WasNote = null }, Cancellation);
        var state = await Edits.ApplyAsync(Edit(impediment.Id, ImpedimentEditKind.State, state: ImpedimentState.Escalated) with { WasState = ImpedimentState.Reported }, Cancellation);

        Assert.Equal(ImpedimentEditOutcome.Stale, note);
        Assert.Equal(ImpedimentEditOutcome.Stale, state);
        Assert.Equal(saves, _impediments.Saves);
        Assert.Equal("Written on the desktop since", (await _impediments.GetAsync(impediment.Id, Cancellation))!.Note);
    }

    [Fact]
    public async Task Edits_of_one_field_in_a_row_apply_in_order_and_a_second_meeting_changes_nothing()
    {
        var impediment = await RaisedAsync();
        var reported = Edit(impediment.Id, ImpedimentEditKind.State, state: ImpedimentState.Reported, with: "bank support") with { WasState = ImpedimentState.New };
        var escalated = Edit(impediment.Id, ImpedimentEditKind.State, state: ImpedimentState.Escalated, with: "account manager") with { WasState = ImpedimentState.Reported };

        Assert.Equal(ImpedimentEditOutcome.Applied, await Edits.ApplyAsync(reported, Cancellation));
        Assert.Equal(ImpedimentEditOutcome.Applied, await Edits.ApplyAsync(escalated, Cancellation));
        Assert.Equal(ImpedimentEditOutcome.Stale, await Edits.ApplyAsync(reported, Cancellation));
        Assert.Equal(ImpedimentEditOutcome.Unchanged, await Edits.ApplyAsync(escalated, Cancellation));

        var stored = await _impediments.GetAsync(impediment.Id, Cancellation);
        Assert.Equal([ImpedimentState.New, ImpedimentState.Reported, ImpedimentState.Escalated], stored!.History.Select(step => step.State));
    }

    [Fact]
    public async Task An_edit_that_asks_for_what_the_impediment_says_writes_nothing()
    {
        var impediment = await RaisedAsync();
        _time.Advance(TimeSpan.FromHours(1));
        var saves = _impediments.Saves;

        Assert.Equal(ImpedimentEditOutcome.Unchanged, await Edits.ApplyAsync(Edit(impediment.Id, ImpedimentEditKind.State, state: ImpedimentState.New), Cancellation));
        Assert.Equal(ImpedimentEditOutcome.Unchanged, await Edits.ApplyAsync(Edit(impediment.Id, ImpedimentEditKind.CheckAgain), Cancellation));
        Assert.Equal(saves, _impediments.Saves);
    }

    [Fact]
    public async Task A_move_the_states_do_not_allow_is_refused_and_an_unknown_impediment_is_reported_unknown()
    {
        var impediment = await RaisedAsync();
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(ImpedimentEditOutcome.Applied, await Edits.ApplyAsync(Edit(impediment.Id, ImpedimentEditKind.State, state: ImpedimentState.Resolved), Cancellation));
        _time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(ImpedimentEditOutcome.Refused, await Edits.ApplyAsync(Edit(impediment.Id, ImpedimentEditKind.State, state: ImpedimentState.Reported), Cancellation));
        Assert.Equal(ImpedimentEditOutcome.Unknown, await Edits.ApplyAsync(Edit(Guid.NewGuid(), ImpedimentEditKind.Note, note: "x"), Cancellation));
    }

    [Fact]
    public async Task Edits_are_applied_with_the_goals_switch_off_so_nothing_asked_for_is_lost()
    {
        var impediment = await RaisedAsync();
        _features.GoalsOn = false;
        _time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(ImpedimentEditOutcome.Applied, await Edits.ApplyAsync(Edit(impediment.Id, ImpedimentEditKind.Note, note: "Kept"), Cancellation));
        Assert.Equal("Kept", (await _impediments.GetAsync(impediment.Id, Cancellation))!.Note);
    }

    private async Task<Impediment> RaisedAsync()
    {
        var impediment = MakeImpediment.Raised(on: new DateOnly(2026, 10, 14));
        await _impediments.SaveAsync(impediment, Cancellation);
        return impediment;
    }

    private ImpedimentEditDto Edit(
        Guid id,
        ImpedimentEditKind kind,
        ImpedimentState? state = null,
        string? with = null,
        DateOnly? checkAgain = null,
        string? note = null) =>
        new(id, kind, state, with, checkAgain, note, _time.GetUtcNow());
}

/// <summary>
/// The Progress documents a head reads without the module behind it
/// (<see cref="ProgressDocuments"/>): the goal and impediment documents in the shape the
/// desktop's store writes, and the two documents the phone and the desktop exchange.
/// </summary>
public sealed class ProgressDocumentsTests
{
    private static readonly Guid GoalId = Guid.Parse("0f6e2c3a-1b4d-4e5f-8a9b-0c1d2e3f4a5b");
    private static readonly Guid TaskId = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");

    [Fact]
    public void A_goal_document_reads_with_its_attachments_and_set_stamp()
    {
        var read = ProgressDocuments.ReadGoal($$"""
            {"version":1,"id":"{{GoalId}}","title":"Customers get paid faster","outcome":"Invoices settle in a day","dueDate":"2026-10-31",
             "attachments":[{"kind":"Plan","plan":"bank-integration","attachedOn":"2026-10-17"},
                            {"kind":"Task","taskId":"{{TaskId}}","attachedOn":"2026-10-17"},
                            {"kind":"PullRequest","repository":"JSdotNet/Backlog","number":1352,"attachedOn":"2026-10-17"}],
             "createdAt":"2026-10-17T09:00:00+00:00"}
            """, GoalId);

        Assert.NotNull(read);
        Assert.False(read.Deleted);
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero), read.CreatedAt);
        Assert.Equal("Invoices settle in a day", read.Goal.Outcome);
        Assert.Equal(new DateOnly(2026, 10, 31), read.Goal.DueDate);
        Assert.Equal(
            [GoalItemDto.ForPlan("bank-integration"), GoalItemDto.ForTask(TaskId), GoalItemDto.ForPullRequest("JSdotNet/Backlog", 1352)],
            read.Goal.Attachments.Select(attachment => attachment.Item));
    }

    [Fact]
    public void An_attachment_of_a_kind_this_build_does_not_know_is_left_out_and_the_goal_still_reads()
    {
        var read = ProgressDocuments.ReadGoal($$"""
            {"id":"{{GoalId}}","title":"Goal","dueDate":"2026-10-31",
             "attachments":[{"kind":"Milestone","attachedOn":"2026-10-17"},{"kind":"Task","taskId":"{{TaskId}}","attachedOn":"2026-10-17"}]}
            """);

        Assert.Equal([GoalItemDto.ForTask(TaskId)], read!.Goal.Attachments.Select(attachment => attachment.Item));
    }

    [Fact]
    public void A_tombstone_reads_as_deleted_and_text_that_is_not_a_goal_reads_as_nothing()
    {
        var tombstone = ProgressDocuments.ReadGoal($$"""{"id":"{{GoalId}}","title":"Goal","deletedAt":"2026-10-18T10:00:00+00:00"}""");

        Assert.True(tombstone!.Deleted);
        Assert.Null(ProgressDocuments.ReadGoal("not json"));
        Assert.Null(ProgressDocuments.ReadGoal($$"""{"id":"{{GoalId}}","title":" "}"""));
        Assert.Null(ProgressDocuments.ReadGoal($$"""{"id":"{{GoalId}}","title":"Goal"}""", Guid.NewGuid()));
    }

    [Fact]
    public void An_impediment_document_reads_with_its_state_from_its_last_step()
    {
        var read = ProgressDocuments.ReadImpediment($$"""
            {"id":"{{GoalId}}","title":"Bank sample files missing","reportedTo":"bank support","checkAgain":"2026-10-21","note":"Asked twice",
             "history":[{"state":"New","on":"2026-10-14"},{"state":"Reported","on":"2026-10-15","with":"bank support"}],
             "holdsBack":["{{TaskId}}"]}
            """);

        Assert.NotNull(read);
        var impediment = read.Impediment;
        Assert.Equal(ImpedimentState.Reported, impediment.State);
        Assert.Equal(new DateOnly(2026, 10, 14), impediment.RaisedOn);
        Assert.Equal(new DateOnly(2026, 10, 21), impediment.CheckAgain);
        Assert.Equal([TaskId], impediment.HoldsBack);
        Assert.Null(ProgressDocuments.ReadImpediment($$"""{"id":"{{GoalId}}","title":"No history"}"""));
    }

    [Fact]
    public void An_edit_and_the_context_come_back_as_they_were_written()
    {
        var edit = new ImpedimentEditDto(GoalId, ImpedimentEditKind.State, ImpedimentState.BeingHandled, "Priya", null, null,
            new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero));
        var context = new StandUpContextDto(
            new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero),
            [new GoalPullRequestDto("JSdotNet/Backlog", "1364", LinkedPullRequestState.Open, null, new GoalPullRequestChecksDto(2, 1, 11, ["build"]), new GoalPullRequestApprovalsDto(0, 2))],
            18m,
            WorkingHours.Default,
            IterationLength.TwoWeeks,
            new DateOnly(2026, 10, 12));

        Assert.Equal(edit, ProgressDocuments.ReadEdit(ProgressDocuments.WriteEdit(edit)));

        var back = ProgressDocuments.ReadContext(ProgressDocuments.WriteContext(context))!;
        Assert.Equal(context.ReadAt, back.ReadAt);
        Assert.Equal(18m, back.PointsPerWeek);
        Assert.Equal(IterationLength.TwoWeeks, back.Length);
        Assert.Equal(new DateOnly(2026, 10, 12), back.TwoWeekStart);
        Assert.Equal(["build"], back.PullRequests[0].Checks!.FailingNames);
        Assert.Equal(new GoalPullRequestApprovalsDto(0, 2), back.PullRequests[0].Approvals);
        Assert.True(back.Week!.IsWorked(DayOfWeek.Monday));
        Assert.False(back.Week.IsWorked(DayOfWeek.Saturday));
    }

    [Fact]
    public void An_edit_that_names_no_impediment_or_no_state_to_move_to_reads_as_nothing()
    {
        Assert.Null(ProgressDocuments.ReadEdit("""{"impedimentId":"00000000-0000-0000-0000-000000000000","kind":"Note","askedAt":"2026-10-19T09:00:00+00:00"}"""));
        Assert.Null(ProgressDocuments.ReadEdit($$"""{"impedimentId":"{{GoalId}}","kind":"State","askedAt":"2026-10-19T09:00:00+00:00"}"""));
        Assert.Null(ProgressDocuments.ReadEdit($$"""{"impedimentId":"{{GoalId}}","kind":"Delete","askedAt":"2026-10-19T09:00:00+00:00"}"""));
    }
}
