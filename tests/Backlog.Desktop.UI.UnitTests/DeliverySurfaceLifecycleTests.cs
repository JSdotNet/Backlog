using System.Text.Json.Nodes;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI.Adapters;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The eight operations of <c>delivery.surface.lifecycle@1</c> against a fixture
/// profile.
/// <para>
/// Nearly every assertion here reads the run back through
/// <see cref="DeliveryRunReader"/> rather than off the file. That is the property
/// worth testing: a run recorded live has to render as the same kind of row as one
/// imported from a dashboard's folder, and the only way to know is to ask the reader
/// the pane asks. A test that checked the JSON it had just written would prove the
/// writer consistent with itself and nothing more.
/// </para>
/// </summary>
public sealed class DeliverySurfaceLifecycleTests : IDisposable
{
    private const string Machine = "DEV-TOWER";
    private const string MachineId = "6b8e6f0c-1a4f-4a2e-9f4b-6a2c0f5d3a71";

    /// <summary>A worktree path that does not have to exist: the key is derived from
    /// the text of the path, never from the folder being there.</summary>
    private const string Worktree = @"D:\Repos\Backlog\.claude\worktrees\lifecycle-tools-8da0a4";

    private static readonly string[] Stages = ["Update Base", "Scope Discovery", "Implementation", "Summary"];

    private readonly string _home = Path.Combine(
        Path.GetTempPath(),
        "backlog-delivery-surface-tests",
        Guid.NewGuid().ToString("n"));

    [Fact]
    public void The_capability_is_the_eight_the_contract_names()
    {
        // Spelled out rather than compared against a helper, because the point of the
        // test is the spelling. A flow finds a surface by matching these against its
        // live tool list, so a rename is not a rename — it is a surface that silently
        // stops being found.
        Assert.Equal(
            [
                "open_dashboard",
                "start_run",
                "record_prompt",
                "set_run_context",
                "update_stage",
                "finish_run",
                "list_runs",
                "get_run"
            ],
            DeliverySurfaceOperations.All);
    }

    [Fact]
    public void The_render_capability_is_not_implemented_here()
    {
        // delivery.surface.render@1 is a separate capability, bound separately. The
        // contract's whole reason for splitting by operation group is that an
        // implementation may answer one group and not another, so naming these here
        // would claim a capability this surface does not have.
        Assert.DoesNotContain("render_diagram", DeliverySurfaceOperations.All);
        Assert.DoesNotContain("render_markdown", DeliverySurfaceOperations.All);
    }

    /// <summary>
    /// A run started with the driving session's id records it, and a session that picks
    /// the run back up is added beside it rather than over it — the file names every
    /// session that drove the run, and the reader hands them to the pane.
    /// </summary>
    [Fact]
    public async Task A_run_records_every_session_that_drove_it()
    {
        var surface = Surface();

        var first = await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, sessionId: "session-a", cancellationToken: TestContext.Current.CancellationToken);
        var again = await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, sessionId: "session-b", cancellationToken: TestContext.Current.CancellationToken);
        await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, sessionId: "session-a", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(again.Resumed);
        Assert.Equal(first.RunId, again.RunId);

        var run = Assert.Single((await new DeliveryRunReader(_home, MachineId, Machine).ReadAsync(TestContext.Current.CancellationToken)).Runs);
        Assert.Equal(["session-a", "session-b"], run.SessionIds);
    }

    [Fact]
    public async Task A_started_run_reads_back_as_the_pane_reads_an_imported_one()
    {
        var surface = Surface();

        var started = await surface.StartRunAsync(Worktree, "flow-code", "Surface lifecycle tools", Stages, "new-functionality", cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(started.Resumed);

        var run = Assert.Single((await new DeliveryRunReader(_home, MachineId, Machine).ReadAsync(TestContext.Current.CancellationToken)).Runs);

        Assert.Equal(started.RunId, run.Id);
        Assert.Equal("backlog", run.Dashboard);
        Assert.Equal("flow-code", run.SkillId);
        Assert.Equal("Surface lifecycle tools", run.Title);
        Assert.Equal("in_progress", run.Status);
        Assert.Equal("new-functionality", run.ChangeKind);

        // Stamped with this device exactly as an imported run is, because it is the
        // same reader doing the stamping — the run file names no machine either way.
        Assert.Equal(MachineId, run.EnvironmentId);
        Assert.Equal(Machine, run.Environment);

        // The worktree key is the one a session's folder derives, which is what lets
        // the pane put the run and the session that drove it on one row.
        Assert.Equal(DeliveryRunWorktrees.KeyOf(Worktree), run.Worktree);
        Assert.Equal("lifecycle-tools-8da0a4", run.WorktreeName);

        // Every stage from the first write, named and pending. A list that grew one
        // stage at a time could never show what is still to come, and an index would
        // address a different stage on every call.
        Assert.Equal<IReadOnlyList<string>>(Stages, [.. run.Stages.Select(stage => stage.Name)]);
        Assert.All(run.Stages, stage => Assert.Equal("pending", stage.Status));
    }

    [Fact]
    public async Task Starting_again_for_the_same_skill_reattaches_rather_than_forking_the_run()
    {
        var surface = Surface();

        var first = await surface.StartRunAsync(Worktree, "flow-code", "First", Stages, cancellationToken: TestContext.Current.CancellationToken);
        var second = await surface.StartRunAsync(Worktree, "flow-code", "Second", Stages, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(second.Resumed);
        Assert.Equal(first.RunId, second.RunId);

        // One file, not two. A second run for the same skill in the same worktree
        // would not be a second run — it would be this one, listed twice, with the
        // stages split between them.
        Assert.Single(await surface.ListRunsAsync(Worktree, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_finished_run_is_not_reattached_to()
    {
        var surface = Surface();

        var first = await surface.StartRunAsync(Worktree, "flow-code", "First", Stages, cancellationToken: TestContext.Current.CancellationToken);
        await surface.FinishRunAsync(Worktree, first.RunId, "done", "Landed.", TestContext.Current.CancellationToken);

        var second = await surface.StartRunAsync(Worktree, "flow-code", "Second", Stages, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(second.Resumed);
        Assert.NotEqual(first.RunId, second.RunId);
        Assert.Equal(2, (await surface.ListRunsAsync(Worktree, TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task A_run_for_another_skill_is_not_reattached_to()
    {
        var surface = Surface();

        var code = await surface.StartRunAsync(Worktree, "flow-code", "Code", Stages, cancellationToken: TestContext.Current.CancellationToken);
        var spec = await surface.StartRunAsync(Worktree, "flow-spec", "Spec", Stages, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(spec.Resumed);
        Assert.NotEqual(code.RunId, spec.RunId);
    }

    [Fact]
    public async Task A_run_in_another_worktree_is_not_reattached_to()
    {
        var surface = Surface();
        const string Elsewhere = @"D:\Repos\Backlog\.claude\worktrees\something-else-1a2b3c";

        var here = await surface.StartRunAsync(Worktree, "flow-code", "Here", Stages, cancellationToken: TestContext.Current.CancellationToken);
        var there = await surface.StartRunAsync(Elsewhere, "flow-code", "There", Stages, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(there.Resumed);

        // And each worktree lists only its own. Scope is the folder the caller states,
        // never the process's own directory — one clone's worktrees share a repository
        // while having as many directories.
        Assert.Equal(here.RunId, Assert.Single(await surface.ListRunsAsync(Worktree, TestContext.Current.CancellationToken)).Id);
        Assert.Equal(there.RunId, Assert.Single(await surface.ListRunsAsync(Elsewhere, TestContext.Current.CancellationToken)).Id);
    }

    [Fact]
    public async Task A_stage_is_addressed_by_index_and_counts_every_pass()
    {
        var surface = Surface();
        var started = await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, cancellationToken: TestContext.Current.CancellationToken);

        await surface.UpdateStageAsync(Worktree, started.RunId, 2, "in_progress", cancellationToken: TestContext.Current.CancellationToken);

        var first = await surface.UpdateStageAsync(Worktree, started.RunId, 2, "done", "Implemented.", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, first.DoneCount);

        // A second pass after requested changes, which is what the count exists to
        // make visible: the stage is done twice, not done once and then done again
        // indistinguishably.
        await surface.UpdateStageAsync(Worktree, started.RunId, 2, "in_progress", cancellationToken: TestContext.Current.CancellationToken);

        var second = await surface.UpdateStageAsync(Worktree, started.RunId, 2, "done", "Revised.", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, second.DoneCount);

        var run = Assert.Single(await surface.ListRunsAsync(Worktree, TestContext.Current.CancellationToken));

        Assert.Equal("Implementation", run.Stages[2].Name);
        Assert.Equal("done", run.Stages[2].Status);
        Assert.Equal(2, run.Stages[2].DoneCount);

        // The stages either side are untouched: an index addresses one stage.
        Assert.Equal("pending", run.Stages[1].Status);
        Assert.Equal("pending", run.Stages[3].Status);
    }

    [Fact]
    public async Task A_stage_index_outside_the_list_is_refused_rather_than_ignored()
    {
        var surface = Surface();
        var started = await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, cancellationToken: TestContext.Current.CancellationToken);

        // A caller one stage off is reporting the wrong stage for the rest of the run.
        // Swallowing that leaves it doing so against a surface that merely looks
        // stalled, which is the failure this refusal exists to prevent.
        var refused = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => surface.UpdateStageAsync(Worktree, started.RunId, 4, "done", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("4 stage(s)", refused.Message, StringComparison.Ordinal);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => surface.UpdateStageAsync(Worktree, started.RunId, -1, "done", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_status_the_vocabulary_does_not_have_is_refused()
    {
        var surface = Surface();
        var started = await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, cancellationToken: TestContext.Current.CancellationToken);

        // "completed" is the older dashboard generation's spelling of done. The reader
        // still accepts it because it reports what somebody else wrote; a writer has
        // no such excuse, and reviving the spelling would make the drift permanent.
        await Assert.ThrowsAsync<ArgumentException>(
            () => surface.UpdateStageAsync(Worktree, started.RunId, 0, "completed", cancellationToken: TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(
            () => surface.FinishRunAsync(Worktree, started.RunId, "completed", cancellationToken: TestContext.Current.CancellationToken));

        // Nor may a run be finished into the status it is already in.
        await Assert.ThrowsAsync<ArgumentException>(
            () => surface.FinishRunAsync(Worktree, started.RunId, "in_progress", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_gate_stage_carries_its_links_scenarios_and_monitoring()
    {
        var surface = Surface();
        var started = await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, cancellationToken: TestContext.Current.CancellationToken);

        await surface.UpdateStageAsync(
            Worktree,
            started.RunId,
            2,
            "done",
            "Validated.",
            [new DeliveryStageLink("Pull request", "https://github.com/JSdotNet/Backlog/pull/572", "The change under review")],
            [new DeliveryScenario("Sessions pane shows a live run", "pass", "Row appeared without a refresh", [".qa-workspace/sessions-pane.png"])],
            new DeliveryMonitoring("No errors during the run", ["One warning from the harness at startup"]),
            TestContext.Current.CancellationToken);

        // The pull request reaches the reader as a reference, which is the whole
        // reason a stage carries links: it is where a run records what it delivered,
        // and the pane reads the address from there rather than from prose.
        var run = Assert.Single(await surface.ListRunsAsync(Worktree, TestContext.Current.CancellationToken));
        var reference = Assert.Single(run.References);

        Assert.Equal(DeliveryRunReferenceKind.PullRequest, reference.Kind);
        Assert.Equal("JSdotNet/Backlog", reference.Repository);

        // Scenarios and monitoring are evidence a surface renders inline; the reader
        // does not project them, so they are checked where they were written.
        var stage = StageOf(started.RunId, 2);

        Assert.Equal("pass", stage["scenarios"]![0]!["status"]!.GetValue<string>());
        Assert.Equal(".qa-workspace/sessions-pane.png", stage["scenarios"]![0]!["evidence"]![0]!.GetValue<string>());
        Assert.Equal("No errors during the run", stage["monitoring"]!["summary"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_first_prompt_recorded_is_what_links_a_run_back_to_its_plan_item()
    {
        var surface = Surface();
        var started = await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, cancellationToken: TestContext.Current.CancellationToken);

        await surface.RecordPromptAsync(
            Worktree,
            started.RunId,
            "Backlog plan item `lifecycle-tools` of plan `backlog-mcp-server` for `Backlog` — run it with the `backlog-run-plan-item` skill.",
            cancellationToken: TestContext.Current.CancellationToken);

        await surface.RecordPromptAsync(Worktree, started.RunId, "Also cover the resume case.", cancellationToken: TestContext.Current.CancellationToken);

        var run = Assert.Single(await surface.ListRunsAsync(Worktree, TestContext.Current.CancellationToken));
        var entry = Assert.Single(run.References);

        Assert.Equal(DeliveryRunReferenceKind.Task, entry.Kind);
        Assert.Equal("lifecycle-tools", entry.Label);

        // A Backlog entry is in this product, not on a page, so there is no address
        // to open — the plan and the id are what finds it.
        Assert.Null(entry.Url);

        var document = Document(started.RunId);

        Assert.StartsWith("Backlog plan item", document["originalPrompt"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(2, document["promptHistory"]!.AsArray().Count);
        Assert.Equal("initial", document["promptHistory"]![0]!["kind"]!.GetValue<string>());

        // The second prompt does not overwrite the first as the run's origin: the
        // marker is read from originalPrompt, and a run whose origin drifted to its
        // latest instruction would stop pointing at the entry it came from.
        Assert.Equal("Also cover the resume case.", document["promptHistory"]![1]!["prompt"]!.GetValue<string>());
    }

    [Fact]
    public async Task Run_context_accumulates_rather_than_replacing_what_it_does_not_mention()
    {
        var surface = Surface();
        var started = await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, cancellationToken: TestContext.Current.CancellationToken);

        await surface.SetRunContextAsync(Worktree, started.RunId, changeKind: "new-functionality", cancellationToken: TestContext.Current.CancellationToken);
        await surface.SetRunContextAsync(Worktree, started.RunId, approval: "approved", approvalNote: "Checked in the harness.", cancellationToken: TestContext.Current.CancellationToken);
        await surface.SetRunContextAsync(Worktree, started.RunId, model: "opus", cancellationToken: TestContext.Current.CancellationToken);

        var document = Document(started.RunId);

        // Each call carried one fact and left the others standing. A call that nulled
        // what it was not about would erase the gate decision every time a model was
        // recorded.
        Assert.Equal("new-functionality", document["changeKind"]!.GetValue<string>());
        Assert.Equal("approved", document["approval"]!["personalValidation"]!.GetValue<string>());
        Assert.Equal("Checked in the harness.", document["approval"]!["note"]!.GetValue<string>());
        Assert.NotNull(document["approval"]!["decidedAt"]);

        var run = Assert.Single(await surface.ListRunsAsync(Worktree, TestContext.Current.CancellationToken));

        Assert.Equal("new-functionality", run.ChangeKind);
        Assert.Equal(["opus"], run.TokenUsage!.Models);

        // The model is recorded; the consumption is not, and must not be. Those
        // figures are measured by a collector watching a session's own tool calls,
        // which this product cannot see — an absent bucket reads as zero, which is
        // true, where an invented one would not be.
        Assert.Equal(0, run.TokenUsage.Total.InputTokens);
        Assert.Equal(0, run.TokenUsage.Total.ModelCalls);
    }

    [Fact]
    public async Task Finishing_a_run_records_the_status_and_the_summary()
    {
        var surface = Surface();
        var started = await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, cancellationToken: TestContext.Current.CancellationToken);

        await surface.FinishRunAsync(Worktree, started.RunId, "parked", "Parked for Personal Validation; PR https://github.com/JSdotNet/Backlog/pull/572 is open.", TestContext.Current.CancellationToken);

        var run = await surface.GetRunAsync(Worktree, started.RunId, TestContext.Current.CancellationToken);

        Assert.NotNull(run);
        Assert.Equal("parked", run.Status);
        Assert.False(run.InProgress);

        // The address in the closing summary becomes a reference too — a run that
        // opened a pull request often states it there and nowhere else.
        Assert.Contains(run.References, reference => reference.Kind == DeliveryRunReferenceKind.PullRequest);
    }

    [Fact]
    public async Task An_unknown_run_is_null_from_get_and_a_refusal_from_everything_that_writes()
    {
        var surface = Surface();

        Assert.Null(await surface.GetRunAsync(Worktree, "run-nothing-here", TestContext.Current.CancellationToken));
        Assert.Empty(await surface.ListRunsAsync(Worktree, TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => surface.FinishRunAsync(Worktree, "run-nothing-here", "done", cancellationToken: TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => surface.RecordPromptAsync(Worktree, "run-nothing-here", "Anything.", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_run_is_written_where_the_reader_already_looks()
    {
        var surface = Surface();
        var started = await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, cancellationToken: TestContext.Current.CancellationToken);

        // The folder is the third dashboard, not a share of the surface dashboard's:
        // that one belongs to a server that resumes runs out of it on its own
        // schedule, and two writers in one folder is the collision a folder of our
        // own avoids. The layout is identical, which is what the reader needs.
        var expected = Path.Combine(_home, "backlog", DeliveryRunWorktrees.KeyOf(Worktree)!, "runs", $"{started.RunId}.json");

        Assert.True(File.Exists(expected), $"Expected a run file at {expected}.");
    }

    [Fact]
    public async Task Opening_the_dashboard_answers_with_the_application_and_never_a_url()
    {
        // No shell attached: the honest answer, and not an error. A host recording
        // runs without a window open is an ordinary state, not a broken one.
        var headless = await Surface().OpenDashboardAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DeliverySurfaceActivation.Unattached, headless.Activation);
        Assert.DoesNotContain("http", headless.Answer, StringComparison.OrdinalIgnoreCase);

        var shown = await Surface(new StubActivator(DeliverySurfaceActivation.Shown)).OpenDashboardAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DeliverySurfaceActivation.Shown, shown.Activation);
        Assert.Contains("Sessions", shown.Answer, StringComparison.Ordinal);

        // No URL in any branch. Backlog is not a page somebody opens — it is the
        // application the caller is already talking to, so an address here would
        // open a second window onto the thing it is holding.
        Assert.DoesNotContain("http", shown.Answer, StringComparison.OrdinalIgnoreCase);

        var disabled = await Surface(new StubActivator(DeliverySurfaceActivation.Disabled)).OpenDashboardAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DeliverySurfaceActivation.Disabled, disabled.Activation);
        Assert.DoesNotContain("http", disabled.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_run_file_is_never_seen_half_written()
    {
        var surface = Surface();
        var started = await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, cancellationToken: TestContext.Current.CancellationToken);
        var path = Path.Combine(_home, "backlog", DeliveryRunWorktrees.KeyOf(Worktree)!, "runs", $"{started.RunId}.json");

        // Every intermediate state of the file is a whole document, because the write
        // is a move onto the name rather than a truncate-then-fill. The pane re-reads
        // these files while a live run updates them, and the reader drops a run it
        // cannot parse — so the failure would not be a crash but a row that flickers
        // out of the list and back, which is worse for being plausible.
        for (var pass = 0; pass < 25; pass++)
        {
            await surface.UpdateStageAsync(Worktree, started.RunId, pass % 4, "in_progress", $"Pass {pass}.", cancellationToken: TestContext.Current.CancellationToken);

            var json = File.ReadAllText(path);

            Assert.NotNull(JsonNode.Parse(json));
        }

        // And no temp files are left behind once the writes are done.
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    private LocalDeliverySurfaceLifecycle Surface(ISessionsSurfaceActivator? shell = null) =>
        new(_home, MachineId, Machine, shell);

    private JsonObject Document(string runId)
    {
        var path = Path.Combine(_home, "backlog", DeliveryRunWorktrees.KeyOf(Worktree)!, "runs", $"{runId}.json");

        return (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
    }

    private JsonObject StageOf(string runId, int index) =>
        (JsonObject)Document(runId)["stages"]![index]!;

    private sealed class StubActivator(DeliverySurfaceActivation answer) : ISessionsSurfaceActivator
    {
        public Task<DeliverySurfaceActivation> ActivateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(answer);
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }
}
