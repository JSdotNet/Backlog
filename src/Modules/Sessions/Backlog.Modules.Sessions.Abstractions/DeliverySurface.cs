namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// The operation names of the delivery engine's <c>delivery.surface.lifecycle@1</c>
/// capability, spelled the way the contract spells them.
/// <para>
/// They are constants rather than literals at the call sites because the names are a
/// wire contract with something outside this repository: a flow resolves a surface by
/// matching these names against its live tool list, so a rename here is not a rename —
/// it is a surface that stops being found, silently, with the flow reporting "no
/// surface bound" as if that were the normal outcome it also is.
/// </para>
/// <para>
/// <see cref="All"/> is what a host enumerates when it registers the tools, and what a
/// test asserts against. The two operations of <c>delivery.surface.render@1</c> —
/// <c>render_diagram</c> and <c>render_markdown</c> — are deliberately absent: they are
/// a separate capability, bound separately, and a surface implementing half of one is
/// what the contract's split by operation group exists to allow.
/// </para>
/// </summary>
public static class DeliverySurfaceOperations
{
    /// <summary>Bring the run surface forward. Backlog answers with what the running
    /// application did, never with a URL — it is the application.</summary>
    public const string OpenDashboard = "open_dashboard";

    /// <summary>Begin a run, or reattach to the one already under way for this skill.</summary>
    public const string StartRun = "start_run";

    /// <summary>Record the prompt a run was started from, and each one after it.</summary>
    public const string RecordPrompt = "record_prompt";

    /// <summary>Persist the gating state a later stage reads back — the change kind,
    /// the approval decision, the resolved model.</summary>
    public const string SetRunContext = "set_run_context";

    /// <summary>Move one stage, addressed by its index in the list the run was started
    /// with.</summary>
    public const string UpdateStage = "update_stage";

    /// <summary>Close a run with its final status and summary.</summary>
    public const string FinishRun = "finish_run";

    /// <summary>The runs this worktree can account for.</summary>
    public const string ListRuns = "list_runs";

    /// <summary>One run, by id.</summary>
    public const string GetRun = "get_run";

    /// <summary>Every operation of the capability, in the order the contract lists
    /// them.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        OpenDashboard,
        StartRun,
        RecordPrompt,
        SetRunContext,
        UpdateStage,
        FinishRun,
        ListRuns,
        GetRun
    ];
}

/// <summary>
/// The status words a run may carry while this product is the one writing it.
/// <para>
/// A narrower set than <see cref="DeliveryRun.Status"/> reports, and that asymmetry is
/// the point: the reader accepts the seven spellings two dashboard generations left
/// behind because it is reporting what somebody else wrote, while a writer has no such
/// excuse. <c>completed</c> and <c>success</c> are not offered here — they are the older
/// generation's spelling of <see cref="Done"/>, and a third writer reviving them would
/// make the drift permanent rather than historical.
/// </para>
/// </summary>
public static class DeliveryRunStatuses
{
    /// <summary>Under way. The one status <see cref="DeliveryRun.InProgress"/> asks
    /// about, and the one <c>start_run</c> reattaches to.</summary>
    public const string InProgress = "in_progress";

    /// <summary>Finished, having delivered what it set out to.</summary>
    public const string Done = "done";

    /// <summary>Stopped on something the run could not get past.</summary>
    public const string Blocked = "blocked";

    /// <summary>Left deliberately unfinished for a person to pick up.</summary>
    public const string Parked = "parked";

    /// <summary>Abandoned.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Every status a run written here may take.</summary>
    public static readonly IReadOnlyList<string> All = [InProgress, Done, Blocked, Parked, Cancelled];

    /// <summary>Whether a status is one a run may be finished with — everything but
    /// <see cref="InProgress"/>, which is where a run starts rather than where it
    /// ends.</summary>
    public static bool IsFinal(string? status) =>
        status is not null
        && All.Contains(status, StringComparer.Ordinal)
        && !string.Equals(status, InProgress, StringComparison.Ordinal);
}

/// <summary>
/// The status words a stage may carry, which are the contract's four plus the one a
/// stage starts life in.
/// </summary>
/// <remarks>
/// <see cref="Pending"/> is not in the contract's vocabulary for <c>update_stage</c>,
/// and deliberately so: it is what <c>start_run</c> writes for every stage of the list
/// it was handed, never something a caller asks for. A run's shape is its whole stage
/// list from the first write, so a surface can show what is still to come rather than
/// growing the list one stage at a time.
/// </remarks>
public static class DeliveryStageStatuses
{
    /// <summary>Not yet reached. Written by <c>start_run</c>, never requested.</summary>
    public const string Pending = "pending";

    /// <summary>Running now.</summary>
    public const string InProgress = "in_progress";

    /// <summary>Finished. Every transition to this increments the stage's done count,
    /// so a stage repeated after requested changes stays visible as repeated.</summary>
    public const string Done = "done";

    /// <summary>Stopped on something the stage could not get past.</summary>
    public const string Blocked = "blocked";

    /// <summary>Deliberately not run, with the reason in the output.</summary>
    public const string Skipped = "skipped";

    /// <summary>The four a caller may ask for. <see cref="Pending"/> is not among
    /// them.</summary>
    public static readonly IReadOnlyList<string> Requestable = [InProgress, Done, Blocked, Skipped];
}

/// <summary>One link a stage recorded — a started application, a review target, a pull
/// request. The shape both dashboard generations wrote, which is what makes a link
/// written here render beside one that was imported.</summary>
/// <param name="Label">The short name a button carries.</param>
/// <param name="Url">Where it points.</param>
/// <param name="Description">What a reader is being sent to, where the label alone does
/// not say.</param>
public sealed record DeliveryStageLink(string? Label, string? Url, string? Description);

/// <summary>One scenario a validation stage exercised.</summary>
/// <param name="Name">What was tested.</param>
/// <param name="Status">One of <c>pass</c>, <c>fail</c>, <c>flaky</c> — carried
/// verbatim, because this is evidence rather than a decision this product makes.</param>
/// <param name="Notes">What happened.</param>
/// <param name="Evidence">Paths to screenshots, traces and captures, relative to the
/// worktree the run belongs to.</param>
public sealed record DeliveryScenario(string Name, string Status, string? Notes, IReadOnlyList<string>? Evidence);

/// <summary>What a run's log and trace monitoring found while a stage ran.</summary>
/// <param name="Summary">The monitoring verdict in a line.</param>
/// <param name="Findings">Anything the monitoring wants read, one entry each.</param>
public sealed record DeliveryMonitoring(string? Summary, IReadOnlyList<string>? Findings);

/// <summary>What <c>start_run</c> answers with.</summary>
/// <param name="RunId">The run to address every later call to.</param>
/// <param name="Resumed">True when this reattached to a run already under way for the
/// same skill in the same worktree. A caller that sees it continues from the first stage
/// that is not done rather than starting over.</param>
/// <param name="SessionTitle">What this session could usefully be renamed to, or null
/// when nothing has been written yet and the host's own title is the better name.</param>
public sealed record DeliveryRunStarted(string RunId, bool Resumed, string? SessionTitle);

/// <summary>What <c>update_stage</c> answers with.</summary>
/// <param name="RunId">The run the stage belongs to.</param>
/// <param name="StageIndex">The stage that moved.</param>
/// <param name="Status">Where it moved to.</param>
/// <param name="DoneCount">How many times this stage has now reached done.</param>
/// <param name="SessionTitle">On the same terms as
/// <see cref="DeliveryRunStarted.SessionTitle"/>.</param>
public sealed record DeliveryStageUpdated(string RunId, int StageIndex, string Status, int DoneCount, string? SessionTitle);

/// <summary>
/// What bringing the surface forward did.
/// <para>
/// <strong>The order of these members is meaningful</strong> and a reordering is a
/// behaviour change: a host with more than one window open asks each of them and keeps
/// the best answer by comparing members, because one window showing the pane is the
/// pane being shown whatever a second window was doing.
/// </para>
/// </summary>
public enum DeliverySurfaceActivation
{
    /// <summary>Nothing was listening — the application is running without a shell
    /// attached, which is the ordinary state of a host that composed the port but has
    /// not opened a window.</summary>
    Unattached,

    /// <summary>A shell answered, but the Sessions area is switched off in this
    /// person's settings, so there was no pane to bring forward.</summary>
    Disabled,

    /// <summary>The Sessions pane is what the window is showing now.</summary>
    Shown
}

/// <summary>What <c>open_dashboard</c> answers with: what the running application did,
/// and no URL. Backlog is not a page somebody opens — it is the application the caller
/// is already talking to, so the honest answer to "open the dashboard" is whether the
/// window is now showing it.</summary>
/// <param name="Activation">What happened.</param>
/// <param name="Answer">The same thing in a sentence, for a caller that reports it to a
/// person rather than branching on it.</param>
public sealed record DeliverySurfaceOpened(DeliverySurfaceActivation Activation, string Answer);

/// <summary>
/// The shell's side of <c>open_dashboard</c>: whatever is showing the application can
/// be asked to bring the Sessions pane forward.
/// <para>
/// A port in this context rather than a general "navigate to surface" service, because
/// this context is the one asking and the set of answers is this context's: attached or
/// not, gated off or not, showing or not. The surfaces themselves stay the Shell's own
/// business and stay internal to it.
/// </para>
/// <para>
/// Asynchronous because of where the call comes from. A tool call arrives on a request
/// thread with no connection to the renderer's synchronisation context, and touching
/// component state from there is the race Blazor's <c>InvokeAsync</c> exists to close.
/// The shell's implementation marshals; the caller waits for the answer, which is the
/// answer it has to give back.
/// </para>
/// </summary>
public interface ISessionsSurfaceActivator
{
    /// <summary>Bring the Sessions pane forward, and say what happened.</summary>
    Task<DeliverySurfaceActivation> ActivateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The delivery engine's <c>delivery.surface.lifecycle@1</c> capability, as this
/// product implements it.
/// <para>
/// The counterpart of <see cref="IDeliveryRunSource"/>, and the first thing in this
/// context that writes: until now every run the pane showed was one another tool left
/// in the profile, and this is what lets a run be recorded here in the first place.
/// What it writes is deliberately the same shape the reader already reads, so a run
/// driven live through these operations and one imported from a dashboard's folder are
/// the same kind of row, told apart only by which folder wrote them.
/// </para>
/// <para>
/// Every operation is scoped to a worktree. The engine's own decision is that a surface
/// never infers scope from a process's working directory — the process serving this is
/// the application's, not the session's, and one clone's worktrees share a repository
/// while having as many directories. So the caller states the folder its run is about,
/// and <see cref="DeliveryRunWorktrees.KeyOf"/> turns it into the key the runs are filed
/// under.
/// </para>
/// </summary>
public interface IDeliverySurfaceLifecycle
{
    /// <summary><c>open_dashboard</c>.</summary>
    Task<DeliverySurfaceOpened> OpenDashboardAsync(CancellationToken cancellationToken = default);

    /// <summary><c>start_run</c>. Reattaches to a run already under way for
    /// <paramref name="skillId"/> in <paramref name="worktree"/> rather than starting a
    /// second one beside it.</summary>
    /// <param name="worktree">The full path of the worktree the run is about.</param>
    /// <param name="skillId">The skill that owns the run, such as <c>flow-code</c>.</param>
    /// <param name="title">What to call the run.</param>
    /// <param name="stages">Every stage the run intends to pass, in order. The whole
    /// list is written at once and addressed by index afterwards.</param>
    /// <param name="changeKind">Where the caller already knows it.</param>
    /// <param name="sessionId">The agent session driving the run — Claude's
    /// <c>${CLAUDE_SESSION_ID}</c> — or null from a caller that does not send one. Written
    /// into the run's <c>sessionIds</c>, which a resumed run appends to, so the Sessions
    /// pane attaches the run to the sessions that drove it by identity rather than by
    /// worktree and overlapping time.</param>
    Task<DeliveryRunStarted> StartRunAsync(
        string worktree,
        string skillId,
        string title,
        IReadOnlyList<string> stages,
        string? changeKind = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default);

    /// <summary><c>record_prompt</c>. The first prompt recorded also becomes the run's
    /// <c>originalPrompt</c>, which is where the plan-item marker is read from.</summary>
    Task RecordPromptAsync(
        string worktree,
        string runId,
        string prompt,
        string? kind = null,
        string? label = null,
        CancellationToken cancellationToken = default);

    /// <summary><c>set_run_context</c>. Every argument is optional and an absent one
    /// leaves what was there — this is called repeatedly through a run, once per fact
    /// as it becomes known.</summary>
    Task SetRunContextAsync(
        string worktree,
        string runId,
        string? changeKind = null,
        string? approval = null,
        string? approvalNote = null,
        string? model = null,
        CancellationToken cancellationToken = default);

    /// <summary><c>update_stage</c>, addressing the stage by its index in the list the
    /// run was started with.</summary>
    Task<DeliveryStageUpdated> UpdateStageAsync(
        string worktree,
        string runId,
        int stageIndex,
        string status,
        string? output = null,
        IReadOnlyList<DeliveryStageLink>? links = null,
        IReadOnlyList<DeliveryScenario>? scenarios = null,
        DeliveryMonitoring? monitoring = null,
        CancellationToken cancellationToken = default);

    /// <summary><c>finish_run</c>.</summary>
    Task FinishRunAsync(
        string worktree,
        string runId,
        string status,
        string? summary = null,
        CancellationToken cancellationToken = default);

    /// <summary><c>list_runs</c> — the runs filed under one worktree, most recently
    /// updated first.</summary>
    Task<IReadOnlyList<DeliveryRun>> ListRunsAsync(string worktree, CancellationToken cancellationToken = default);

    /// <summary><c>get_run</c> — one run, or null where this worktree has no such
    /// run.</summary>
    Task<DeliveryRun?> GetRunAsync(string worktree, string runId, CancellationToken cancellationToken = default);
}
