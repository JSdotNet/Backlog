using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The delivery surface's eight tools over the port that implements them.
/// <para>
/// These are about the layer and nothing under it. Whether a stage index out of
/// range is refused, whether a run file is written atomically, whether
/// <c>start_run</c> reattaches — all of that belongs to
/// <c>LocalDeliverySurfaceLifecycle</c> and is held by
/// <c>DeliverySurfaceLifecycleTests</c> against the real adapter and real files.
/// What is only true here is that each tool reaches the operation it names,
/// hands over the arguments it was given without interpreting them, and projects
/// the answer instead of passing a module's DTO out onto the wire.
/// </para>
/// </summary>
public class SurfaceToolsTests
{
    private const string Worktree = @"D:\Repos\Backlog\.claude\worktrees\example";

    [Fact]
    public async Task Open_dashboard_answers_with_what_the_application_did()
    {
        var surface = new FakeDeliverySurfaceLifecycle(DeliverySurfaceActivation.Unattached);

        var opened = await new SurfaceTools(surface).OpenDashboardAsync(TestContext.Current.CancellationToken);

        // The member name, not the ordinal. The enum's own doc calls its order
        // load-bearing, so a number here would re-point the day a member is
        // inserted — and a caller reading "1" has no way to notice.
        Assert.Equal("Unattached", opened.Activation);
        Assert.Equal("The Sessions pane is showing.", opened.Answer);
        Assert.Equal([DeliverySurfaceOperations.OpenDashboard], surface.Calls);
    }

    /// <summary>
    /// The one field of <c>start_run</c>'s answer a caller must not drop: a
    /// reattached run continues from the first stage that is not done, and a
    /// caller that ignored this would restart a flow and redo work the run has
    /// already recorded.
    /// </summary>
    [Fact]
    public async Task Start_run_carries_the_stage_list_over_and_reports_a_reattach()
    {
        var surface = new FakeDeliverySurfaceLifecycle();

        var started = await new SurfaceTools(surface).StartRunAsync(
            Worktree,
            "flow-code",
            "Expose the delivery surface",
            ["Update Base", "Scope Discovery", "Implementation"],
            changeKind: "new-functionality",
            sessionId: "session-1",
            TestContext.Current.CancellationToken);

        Assert.Equal("run-1", started.RunId);
        Assert.True(started.Resumed);

        Assert.Equal(Worktree, surface.Worktree);
        Assert.Equal(["Update Base", "Scope Discovery", "Implementation"], surface.Stages);
        Assert.Equal("new-functionality", surface.ChangeKind);
        Assert.Equal("session-1", surface.SessionId);
    }

    [Fact]
    public async Task Update_stage_maps_its_links_scenarios_and_monitoring()
    {
        var surface = new FakeDeliverySurfaceLifecycle();

        var updated = await new SurfaceTools(surface).UpdateStageAsync(
            Worktree,
            "run-1",
            stageIndex: 2,
            status: "done",
            output: "Eight tools published.",
            links: [new StageLinkInput("Harness", "http://localhost:5001", "The app under test")],
            scenarios: [new ScenarioInput("tools/list", "passed", "Eight names present", ["evidence/list.json"])],
            monitoring: new MonitoringInput("No errors", ["One warning, pre-existing"]),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, updated.StageIndex);
        Assert.Equal("done", updated.Status);

        // The count the adapter answered with, not one this layer derived. A
        // stage re-run after requested changes reads as a second pass, and that
        // is the adapter's to say.
        Assert.Equal(2, updated.DoneCount);

        var link = Assert.Single(surface.Links!);
        Assert.Equal("Harness", link.Label);
        Assert.Equal("http://localhost:5001", link.Url);

        var scenario = Assert.Single(surface.Scenarios!);
        Assert.Equal("tools/list", scenario.Name);
        Assert.Equal("passed", scenario.Status);
        Assert.Equal(["evidence/list.json"], scenario.Evidence);

        Assert.Equal("No errors", surface.Monitoring!.Summary);
    }

    /// <summary>
    /// Absent stays absent. The port reads a null list as "this call says nothing
    /// about links" and an empty one as "there are none", so a stage updated for
    /// its status alone must not arrive carrying an empty list that erases what
    /// an earlier call set.
    /// </summary>
    [Fact]
    public async Task A_stage_updated_for_its_status_alone_says_nothing_about_the_rest()
    {
        var surface = new FakeDeliverySurfaceLifecycle();

        await new SurfaceTools(surface).UpdateStageAsync(
            Worktree,
            "run-1",
            stageIndex: 0,
            status: "in_progress",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(surface.Links);
        Assert.Null(surface.Scenarios);
        Assert.Null(surface.Monitoring);
    }

    [Fact]
    public async Task The_writes_that_answer_nothing_still_reach_their_operation()
    {
        var surface = new FakeDeliverySurfaceLifecycle();
        var tools = new SurfaceTools(surface);

        await tools.RecordPromptAsync(Worktree, "run-1", "Expose the surface", cancellationToken: TestContext.Current.CancellationToken);
        await tools.SetRunContextAsync(Worktree, "run-1", approval: "approved", cancellationToken: TestContext.Current.CancellationToken);
        await tools.FinishRunAsync(Worktree, "run-1", "done", "Eight tools published.", TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                DeliverySurfaceOperations.RecordPrompt,
                DeliverySurfaceOperations.SetRunContext,
                DeliverySurfaceOperations.FinishRun
            ],
            surface.Calls);

        Assert.Equal("done", surface.Status);

        // Only what it was given. set_run_context writes the fields supplied and
        // leaves the rest, so a call recording an approval must not arrive with a
        // change kind of this layer's invention.
        Assert.Null(surface.ChangeKind);
    }

    [Fact]
    public async Task List_runs_projects_every_run_and_counts_them()
    {
        var surface = new FakeDeliverySurfaceLifecycle(runs: [Runs.Run("run-1"), Runs.Run("run-2", status: "done")]);

        var listed = await new SurfaceTools(surface).ListRunsAsync(Worktree, TestContext.Current.CancellationToken);

        Assert.Equal(Worktree, listed.Worktree);
        Assert.Equal(2, listed.Count);
        Assert.Equal(["run-1", "run-2"], listed.Runs.Select(run => run.Id));

        Assert.True(listed.Runs[0].InProgress);
        Assert.False(listed.Runs[1].InProgress);
    }

    [Fact]
    public async Task Get_run_projects_the_run_and_its_stages()
    {
        var surface = new FakeDeliverySurfaceLifecycle(
            run: Runs.Run(
                stages: [new DeliveryRunStage("Scope Discovery", "done", 1200, 1), new DeliveryRunStage("Implementation", "in_progress", null, 0)],
                sessionIds: ["session-1"]));

        var run = await new SurfaceTools(surface).GetRunAsync(Worktree, "run-1", TestContext.Current.CancellationToken);

        Assert.NotNull(run);
        Assert.Equal("flow-code", run.SkillId);
        Assert.Equal("new-functionality", run.ChangeKind);
        Assert.Equal(["session-1"], run.SessionIds);

        Assert.Equal(["Scope Discovery", "Implementation"], run.Stages.Select(stage => stage.Name));
        Assert.Equal(1200, run.Stages[0].DurationMs);
        Assert.Null(run.Stages[1].DurationMs);
    }

    /// <summary>
    /// An absent run is an answer rather than a failure — it is how a caller
    /// checks before reattaching, and a throw would make "no run yet" look like a
    /// broken surface.
    /// </summary>
    [Fact]
    public async Task Get_run_answers_with_nothing_for_a_run_the_worktree_does_not_have()
    {
        var surface = new FakeDeliverySurfaceLifecycle();

        var run = await new SurfaceTools(surface).GetRunAsync(Worktree, "run-nobody-started", TestContext.Current.CancellationToken);

        Assert.Null(run);
        Assert.Equal("run-nobody-started", surface.RunId);
    }
}
