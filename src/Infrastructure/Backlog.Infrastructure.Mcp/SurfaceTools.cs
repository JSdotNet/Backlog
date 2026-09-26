using System.ComponentModel;

using Backlog.Modules.Sessions.Abstractions;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The delivery engine's <c>delivery.surface.lifecycle@1</c> capability, as the
/// eight tools a flow reaches it by.
/// <para>
/// <see cref="IDeliverySurfaceLifecycle"/> has implemented this capability, and
/// been tested against it, since the run store landed. What was missing was the
/// door: a flow resolves a surface by matching the operation names against its
/// live tool list, so a capability nothing publishes is a capability nothing
/// finds. The engine's own contract lists <c>backlog</c> as implementing this
/// group and binds it ahead of the dashboards — which, with no tools under these
/// names, resolved to "no surface bound" and fell through to a dashboard every
/// time. This class is the difference between the port existing and the
/// capability being reachable.
/// </para>
/// <para>
/// <b>The names are a wire contract, so they are not spelled here.</b> Every
/// <c>Name</c> below is a <see cref="DeliverySurfaceOperations"/> constant, and
/// the group's <c>ToolNames</c> is that class's own <c>All</c>. A rename in this
/// file cannot then desync the catalog from the attributes, and neither can
/// desync from the contract without the constant moving — which is the one place
/// a reader would look.
/// </para>
/// <para>
/// <b>Addressed by worktree, not by repository.</b> Every other tool in this
/// assembly takes a <c>repository</c> and resolves it through
/// <see cref="RepositoryScope"/>, because what it answers about belongs to a
/// repository. A run does not: it belongs to the checkout a flow is running in,
/// two worktrees of one repository hold two independent runs, and
/// <c>start_run</c> reattaches per skill <em>within a worktree</em>. The caller
/// still says which — local ADR 0012's rule is that the caller names the scope
/// and the server never infers one, and a worktree path is that name here.
/// </para>
/// <para>
/// <b>Refusals are the adapter's, not re-implemented here.</b> A stage index
/// outside the run's list, a stage status the vocabulary does not have, a write
/// to a run that does not exist — <see cref="IDeliverySurfaceLifecycle"/>
/// refuses all three, and <c>DeliverySurfaceLifecycleTests</c> pins that it
/// does. These methods pass the arguments through and let the refusal surface.
/// A second guard here would be a second opinion about the same rule, free to
/// drift from the one the pane obeys.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class SurfaceTools(IDeliverySurfaceLifecycle surface)
{
    internal const string OpenDashboard = DeliverySurfaceOperations.OpenDashboard;
    internal const string StartRun = DeliverySurfaceOperations.StartRun;
    internal const string RecordPrompt = DeliverySurfaceOperations.RecordPrompt;
    internal const string SetRunContext = DeliverySurfaceOperations.SetRunContext;
    internal const string UpdateStage = DeliverySurfaceOperations.UpdateStage;
    internal const string FinishRun = DeliverySurfaceOperations.FinishRun;
    internal const string ListRuns = DeliverySurfaceOperations.ListRuns;
    internal const string GetRun = DeliverySurfaceOperations.GetRun;

    /// <summary>
    /// Say whether the run surface is there to watch.
    /// <para>
    /// The one operation of the eight that answers about the application rather
    /// than about a run, and the only one taking no arguments: there is one
    /// application and it either offers the pane or it does not. It answers
    /// rather than handing out a URL, because Backlog is not a page a caller
    /// opens — it is the surface, already open or not running at all. And it
    /// never navigates: what a window shows changes only on the person's click.
    /// </para>
    /// </summary>
    [McpServerTool(
        Name = OpenDashboard,
        Title = "Show the run surface",
        ReadOnly = false,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Reports whether Backlog's Sessions pane is available for the person to watch the run in — available, or "
        + "not attached to a running window, or switched off. It never changes what the window shows: the person "
        + "opens the pane themselves. It never answers with a "
        + "URL: this application is the surface rather than a page to open, so a caller that wanted a link has "
        + "already got its answer. Call it once per session.")]
    public async Task<SurfaceOpenedPayload> OpenDashboardAsync(CancellationToken cancellationToken = default)
    {
        var opened = await surface.OpenDashboardAsync(cancellationToken).ConfigureAwait(false);

        return Projections.SurfaceOpened(opened);
    }

    /// <summary>
    /// Begin a run, or reattach to the one already under way for this skill in
    /// this worktree.
    /// <para>
    /// Reattaching rather than refusing is the engine's rule and the reason this
    /// is marked idempotent: a resumed session calls it again with the same
    /// arguments and has to get the run it left, not a second one beside it.
    /// </para>
    /// </summary>
    [McpServerTool(
        Name = StartRun,
        Title = "Start or reattach to a run",
        ReadOnly = false,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Starts tracking a run and declares its stages in order, or reattaches to the run already in progress for "
        + "the same skill in the same worktree and answers resumed:true — continue from the first stage that is "
        + "not done rather than starting over. Pass every stage the flow will run, including the shared closing "
        + "phases, because stages are addressed by their index in this list.")]
    public async Task<RunStartedPayload> StartRunAsync(
        [Description("The full path of the worktree the run is running in.")]
        string worktree,
        [Description("The flow's skill id, e.g. flow-code. What a reattach matches on, with the worktree.")]
        string skillId,
        [Description("A short title for the run, as the pane lists it.")]
        string title,
        [Description("The stage names, in the order the flow runs them. Stages are addressed by index into this list.")]
        IReadOnlyList<string> stages,
        [Description("Optional. The change kind, when it is already known: new-functionality, bug-fix, dependency-update or none.")]
        string? changeKind = null,
        [Description("Optional. The host's session id, so the run and the session it ran in join by identity rather than by time.")]
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var started = await surface
            .StartRunAsync(worktree, skillId, title, stages, changeKind, sessionId, cancellationToken)
            .ConfigureAwait(false);

        return Projections.RunStarted(started);
    }

    /// <summary>Append a prompt to the run's history. The first one recorded
    /// becomes the run's original prompt.</summary>
    [McpServerTool(
        Name = RecordPrompt,
        Title = "Record a prompt against a run",
        ReadOnly = false,
        Idempotent = false,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Appends a prompt to the run's history — the request the run was started from, and each one after it. The "
        + "first recorded becomes the run's original prompt. Appends rather than replaces, so calling it twice with "
        + "the same text records it twice.")]
    public Task RecordPromptAsync(
        [Description("The full path of the worktree the run is running in.")]
        string worktree,
        [Description("The run id start_run answered with.")]
        string runId,
        [Description("The prompt text.")]
        string prompt,
        [Description("Optional. What kind of prompt it is, e.g. user or revise.")]
        string? kind = null,
        [Description("Optional. A short label for the prompt, as the pane lists it.")]
        string? label = null,
        CancellationToken cancellationToken = default) =>
        surface.RecordPromptAsync(worktree, runId, prompt, kind, label, cancellationToken);

    /// <summary>
    /// Persist the gating state a later phase reads back.
    /// <para>
    /// This is the operation that makes an approval survive a session: the
    /// engine's rule is that a pull request is created only when the persisted
    /// approval says <c>approved</c>, and a conversation's memory of an approval
    /// is not persistence.
    /// </para>
    /// </summary>
    [McpServerTool(
        Name = SetRunContext,
        Title = "Persist a run's gating state",
        ReadOnly = false,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Persists the state a later phase reads back: the change kind that selects validation depth, the Personal "
        + "Validation decision and the wording behind it, and the resolved model. Every argument is optional and "
        + "only the ones supplied are written, so a later call recording an approval does not erase the change kind "
        + "an earlier one recorded.")]
    public Task SetRunContextAsync(
        [Description("The full path of the worktree the run is running in.")]
        string worktree,
        [Description("The run id start_run answered with.")]
        string runId,
        [Description("Optional. new-functionality, bug-fix, dependency-update or none.")]
        string? changeKind = null,
        [Description("Optional. The Personal Validation decision: pending, approved or rejected.")]
        string? approval = null,
        [Description("Optional. The user's own wording for that decision.")]
        string? approvalNote = null,
        [Description("Optional. The model resolved for the run.")]
        string? model = null,
        CancellationToken cancellationToken = default) =>
        surface.SetRunContextAsync(worktree, runId, changeKind, approval, approvalNote, model, cancellationToken);

    /// <summary>
    /// Move one stage, addressed by its index in the list the run was started
    /// with.
    /// <para>
    /// Not idempotent, and that is the adapter's behaviour rather than a caution:
    /// the completion count increments on every transition to <c>done</c>, so a
    /// stage re-run after requested changes is visibly a second pass rather than
    /// the same one restated.
    /// </para>
    /// </summary>
    [McpServerTool(
        Name = UpdateStage,
        Title = "Update one stage of a run",
        ReadOnly = false,
        Idempotent = false,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Moves one stage of a run, addressed by its index in the stage list start_run was given, and answers with "
        + "the stage's new status and how many times it has completed. An index outside that list is refused rather "
        + "than ignored, as is a status outside pending, in_progress, done, blocked and skipped. Each transition to "
        + "done counts, so a stage re-run after requested changes reads as a second pass.")]
    public async Task<StageUpdatedPayload> UpdateStageAsync(
        [Description("The full path of the worktree the run is running in.")]
        string worktree,
        [Description("The run id start_run answered with.")]
        string runId,
        [Description("The stage's index in the list start_run was given, from zero.")]
        int stageIndex,
        [Description("pending, in_progress, done, blocked or skipped.")]
        string status,
        [Description("Optional. What the stage did and produced, as the pane shows it.")]
        string? output = null,
        [Description("Optional. Links to show against the stage — the started application, a review target.")]
        IReadOnlyList<StageLinkInput>? links = null,
        [Description("Optional. QA scenarios and how each one went.")]
        IReadOnlyList<ScenarioInput>? scenarios = null,
        [Description("Optional. What a runtime monitor observed while the stage ran.")]
        MonitoringInput? monitoring = null,
        CancellationToken cancellationToken = default)
    {
        var updated = await surface
            .UpdateStageAsync(
                worktree,
                runId,
                stageIndex,
                status,
                output,
                Projections.StageLinks(links),
                Projections.Scenarios(scenarios),
                Projections.Monitoring(monitoring),
                cancellationToken)
            .ConfigureAwait(false);

        return Projections.StageUpdated(updated);
    }

    /// <summary>Close a run with a final status and a summary.</summary>
    [McpServerTool(
        Name = FinishRun,
        Title = "Close a run",
        ReadOnly = false,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Closes a run with its final status — done, blocked, parked or cancelled — and a summary of what it "
        + "delivered. A run only leaves in_progress through this call, so a flow that stops without it leaves a run "
        + "that later reads as abandoned rather than finished.")]
    public Task FinishRunAsync(
        [Description("The full path of the worktree the run is running in.")]
        string worktree,
        [Description("The run id start_run answered with.")]
        string runId,
        [Description("done, blocked, parked or cancelled.")]
        string status,
        [Description("Optional. What the run delivered.")]
        string? summary = null,
        CancellationToken cancellationToken = default) =>
        surface.FinishRunAsync(worktree, runId, status, summary, cancellationToken);

    /// <summary>Every run this machine has recorded for one worktree.</summary>
    [McpServerTool(
        Name = ListRuns,
        Title = "List the runs of a worktree",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "The runs recorded for one worktree, newest first — the ones this application recorded and the ones it "
        + "imported from a dashboard, which read alike. Read-only.")]
    public async Task<RunsPayload> ListRunsAsync(
        [Description("The full path of the worktree to list runs for.")]
        string worktree,
        CancellationToken cancellationToken = default)
    {
        var runs = await surface.ListRunsAsync(worktree, cancellationToken).ConfigureAwait(false);

        return Projections.Runs(worktree, runs);
    }

    /// <summary>One run in full, or null when the worktree has no run by that
    /// id.</summary>
    [McpServerTool(
        Name = GetRun,
        Title = "Read one run",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description(
        "One run of one worktree, with its stages in order. Answers with nothing when no run of that id is "
        + "recorded there — an absent run is an answer rather than a failure, which is how a caller checks before "
        + "reattaching. Read-only.")]
    public async Task<RunPayload?> GetRunAsync(
        [Description("The full path of the worktree the run is recorded in.")]
        string worktree,
        [Description("The run id.")]
        string runId,
        CancellationToken cancellationToken = default)
    {
        var run = await surface.GetRunAsync(worktree, runId, cancellationToken).ConfigureAwait(false);

        return run is null ? null : Projections.Run(run);
    }
}
