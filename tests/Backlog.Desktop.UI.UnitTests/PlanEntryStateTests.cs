using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A plan entry's state in its plan's queue (local ADR 0062 §4; the design board "Row
/// states — entry ↔ queue"): each state derived from the entry, the plan's session
/// request and the plan run, the order the rules win in, and the Tasks state's reading
/// for the row and the plan section.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class PlanEntryStateTests
{
    private const string Plan = "plan-first-tasks";

    private static EntryRow Row(string tokens, Guid? id = null) =>
        new() { Id = id ?? Guid.NewGuid(), RawText = $"# Derive the states\n`{tokens.Replace(" ", "` `", StringComparison.Ordinal)}` `+{Plan}`\n\nBuild it.\n" };

    private static readonly EntrySessionRequest Request =
        new(Guid.NewGuid(), EntrySessionRequestState.Requested, "JSdotNet/Backlog", DateTimeOffset.UtcNow);

    private static void Merged(EntryRow row, string baseRef)
    {
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 1852);
        row.PullRequestLinks = [pr];
        row.PullRequestStatuses = new Dictionary<EntryPullRequestLink, LinkedPullRequest>
        {
            [pr] = new(LinkedPullRequestState.Merged) { BaseRefName = baseRef }
        };
    }

    private static PlanEntryQueueState? StateOf(EntryRow row, bool waiting = false, PlanRun? run = null) =>
        PlanEntryState.For(row, waiting, run)?.State;

    [Fact]
    public void An_entry_in_no_plan_has_no_plan_state()
    {
        var row = new EntryRow { Id = Guid.NewGuid(), RawText = "# Alone\n`prompt` `!ready`\n" };

        Assert.Null(PlanEntryState.For(row, waiting: false));
    }

    [Fact]
    public void A_prompt_waiting_on_another_entry_of_an_unqueued_plan_is_waiting()
    {
        Assert.Equal(PlanEntryQueueState.Waiting, StateOf(Row("prompt !ready"), waiting: true));
    }

    [Fact]
    public void A_prompt_waiting_on_nothing_in_an_unqueued_plan_is_ready()
    {
        Assert.Equal(PlanEntryQueueState.Ready, StateOf(Row("prompt !ready")));
    }

    [Fact]
    public void A_ready_prompt_of_a_queued_plan_is_queued_with_the_plan()
    {
        Assert.Equal(PlanEntryQueueState.QueuedWithPlan, StateOf(Row("prompt !ready"), run: PlanRun.From(Request)));
    }

    [Fact]
    public void A_waiting_prompt_of_a_queued_plan_still_waits()
    {
        Assert.Equal(PlanEntryQueueState.Waiting, StateOf(Row("prompt !ready"), waiting: true, run: PlanRun.From(Request)));
    }

    [Fact]
    public void A_prompt_the_plan_session_runs_is_in_the_plan_session_with_its_stage()
    {
        var id = Guid.NewGuid();
        var run = PlanRun.From(Request, new Dictionary<Guid, string> { [id] = "Implement · 3 of 9" });

        var state = PlanEntryState.For(Row("prompt !ready", id), waiting: false, run);

        Assert.Equal(new PlanEntryState(PlanEntryQueueState.InPlanSession, "Implement · 3 of 9"), state);
        Assert.Equal("In plan session", state!.Label);
    }

    [Fact]
    public void A_prompt_in_progress_with_a_linked_session_is_in_the_plan_session_without_a_stage()
    {
        var row = Row("prompt !in-progress");
        row.SessionLinks = [new EntrySessionLink("JSdotNet/Backlog", "f6deac3b")];

        Assert.Equal(new PlanEntryState(PlanEntryQueueState.InPlanSession), PlanEntryState.For(row, waiting: false, PlanRun.From(Request)));
    }

    [Theory]
    [InlineData("prompt !in-progress")]
    [InlineData("prompt !draft")]
    public void A_prompt_that_is_not_ready_and_in_no_session_waits_even_in_a_queued_plan(string tokens)
    {
        Assert.Equal(PlanEntryQueueState.Waiting, StateOf(Row(tokens)));
        Assert.Equal(PlanEntryQueueState.Waiting, StateOf(Row(tokens), run: PlanRun.From(Request)));
    }

    [Fact]
    public void A_prompt_the_run_held_for_an_unreadable_source_is_held_for_you()
    {
        Assert.Equal(PlanEntryQueueState.HeldForYou, StateOf(Row("prompt !ready blocked:2026-11-25"), run: PlanRun.From(Request)));
    }

    [Fact]
    public void A_held_prompt_stays_held_while_the_run_reports_a_stage_for_it()
    {
        var id = Guid.NewGuid();
        var run = PlanRun.From(Request, new Dictionary<Guid, string> { [id] = "Scope" });

        Assert.Equal(PlanEntryQueueState.HeldForYou, StateOf(Row("prompt !ready blocked:2026-11-25", id), run: run));
    }

    [Theory]
    [InlineData("task")]
    [InlineData("test")]
    public void A_task_or_test_entry_is_left_for_you_however_the_plan_stands(string type)
    {
        Assert.Equal(PlanEntryQueueState.LeftForYou, StateOf(Row($"{type} !ready")));
        Assert.Equal(PlanEntryQueueState.LeftForYou, StateOf(Row($"{type} !ready"), waiting: true, run: PlanRun.From(Request)));
    }

    [Fact]
    public void An_entry_whose_pull_request_merged_into_the_plan_branch_has_landed()
    {
        var row = Row("prompt !in-progress");
        Merged(row, $"plan/{Plan}");

        Assert.Equal(PlanEntryQueueState.Landed, StateOf(row, run: PlanRun.From(Request)));
    }

    [Fact]
    public void A_pull_request_merged_into_the_default_branch_is_not_a_landing_on_the_plan_branch()
    {
        var row = Row("prompt !ready");
        Merged(row, "main");

        Assert.Equal(PlanEntryQueueState.Ready, StateOf(row));
    }

    [Theory]
    [InlineData("prompt !done")]
    [InlineData("prompt !archived")]
    [InlineData("task !done")]
    public void A_finished_entry_is_done_even_after_it_landed(string tokens)
    {
        var row = Row(tokens);
        Merged(row, $"plan/{Plan}");

        var state = PlanEntryState.For(row, waiting: false, PlanRun.From(Request));

        Assert.Equal(PlanEntryQueueState.Done, state!.State);
        Assert.Equal("Done", state.Label);
    }

    [Theory]
    [InlineData(PlanEntryQueueState.Waiting, "Waiting")]
    [InlineData(PlanEntryQueueState.Ready, "Ready")]
    [InlineData(PlanEntryQueueState.QueuedWithPlan, "Queued with plan")]
    [InlineData(PlanEntryQueueState.InPlanSession, "In plan session")]
    [InlineData(PlanEntryQueueState.HeldForYou, "Held for you")]
    [InlineData(PlanEntryQueueState.LeftForYou, "Left for you")]
    [InlineData(PlanEntryQueueState.Landed, "Landed")]
    [InlineData(PlanEntryQueueState.Done, "Done")]
    public void Each_state_is_worded_as_the_row_shows_it(PlanEntryQueueState state, string label)
    {
        Assert.Equal(label, new PlanEntryState(state).Label);
    }

    [Fact]
    public void A_plan_with_no_request_is_not_queued()
    {
        Assert.False(PlanRun.From(null).Queued);
        Assert.Null(PlanRun.None.StageOf(Guid.NewGuid()));
    }

    // --- Read from the Tasks state ---------------------------------------------

    [Fact]
    public async Task The_tasks_state_reads_waiting_from_its_own_dependency_chain()
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var first = await host.WriteEntryAsync($"# First\n`prompt` `!ready` `+{Plan}`\n\nDo it.\n");
        var second = await host.WriteEntryAsync($"# Second\n`prompt` `!ready` `+{Plan}` `after:{first.Id}`\n\nThen this.\n");

        Assert.Equal(PlanEntryQueueState.Ready, host.State.PlanEntryStateOf(first)!.State);
        Assert.Equal(PlanEntryQueueState.Waiting, host.State.PlanEntryStateOf(second)!.State);
        Assert.Equal(PlanEntryQueueState.QueuedWithPlan, host.State.PlanEntryStateOf(first, PlanRun.From(Request))!.State);
    }

    [Theory]
    [InlineData("!done")]
    [InlineData("!archived")]
    public async Task A_ready_entry_after_a_done_status_entry_with_no_completion_date_is_next_not_waiting(string status)
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var first = await host.WriteEntryAsync($"# First\n`prompt` `{status}` `+{Plan}`\n\nDone already.\n");
        var second = await host.WriteEntryAsync($"# Second\n`prompt` `!ready` `+{Plan}` `after:{first.Id}`\n\nThen this.\n");

        Assert.False(first.IsPreviewCompleted);
        Assert.True(first.IsPreviewFinished);
        Assert.True(host.State.IsNotWaiting(second));
        Assert.True(host.State.IsNextAction(second));
        Assert.False(host.State.IsNextAction(first));
        Assert.Equal(PlanEntryQueueState.Ready, host.State.PlanEntryStateOf(second)!.State);
        Assert.Equal(PlanEntryQueueState.QueuedWithPlan, host.State.PlanEntryStateOf(second, PlanRun.From(Request))!.State);
    }

    [Fact]
    public async Task The_plan_rail_counts_a_done_status_entry_with_no_completion_date_as_done()
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var first = await host.WriteEntryAsync($"# First\n`prompt` `!done` `+{Plan}`\n\nDone already.\n");
        await host.WriteEntryAsync($"# Second\n`prompt` `!ready` `+{Plan}` `after:{first.Id}`\n\nThen this.\n");

        var plan = Assert.Single(
            PlanRail.Plans(
                host.State.TagFilters,
                host.State.ScopedRows,
                new Dictionary<string, EntrySessionRequest>(),
                [],
                row => host.State.PlanEntryStateOf(row) is { } state && PlanSections.SectionOf(row, state) is PlanSection.Next),
            plan => plan.Name == Plan);

        Assert.Equal((1, 2), (plan.Done, plan.Total));
        Assert.Equal(1, plan.ReadyCount);
    }

    [Fact]
    public async Task A_plan_section_reads_every_entry_of_its_plan_and_only_those()
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var prompt = await host.WriteEntryAsync($"# Build\n`prompt` `!ready` `+{Plan}`\n\nDo it.\n");
        var check = await host.WriteEntryAsync($"# Check it\n`test` `!ready` `+{Plan}`\n\nLook.\n");
        await host.WriteEntryAsync("# Elsewhere\n`prompt` `!ready` `+another-plan`\n\nNot this.\n");
        await host.WriteEntryAsync("# Unplanned\n`task` `!ready`\n\nNor this.\n");

        var states = host.State.PlanEntryStatesOf("+" + Plan.ToUpperInvariant());

        Assert.Equal(
            [(prompt.Id, PlanEntryQueueState.Ready), (check.Id, PlanEntryQueueState.LeftForYou)],
            states.Select(pair => (pair.Row.Id, pair.State.State)));
        Assert.Empty(host.State.PlanEntryStatesOf(" "));
    }
}
