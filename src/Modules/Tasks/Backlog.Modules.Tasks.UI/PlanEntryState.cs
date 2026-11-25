using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.UI.Components.Badges;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>The state a plan entry shows because its plan is queued only as a whole
/// (local ADR 0062 §4; the design board "Row states — entry ↔ queue").</summary>
public enum PlanEntryQueueState
{
    /// <summary>The entry waits on another entry, or is not yet ready itself.</summary>
    Waiting,

    /// <summary>The plan is not queued and the entry is ready and waits on nothing.</summary>
    Ready,

    /// <summary>The plan is queued and the entry waits on nothing.</summary>
    QueuedWithPlan,

    /// <summary>A plan session runs the entry.</summary>
    InPlanSession,

    /// <summary>A Read-first source the entry names cannot be read, so the run holds it:
    /// <c>execute-plan</c> marks it blocked.</summary>
    HeldForYou,

    /// <summary>A <c>test</c> or <c>task</c> entry: the run never does it.</summary>
    LeftForYou,

    /// <summary>Its pull request merged into the plan branch.</summary>
    Landed,

    /// <summary>The entry is finished.</summary>
    Done
}

/// <summary>
/// What the plan's run says about its entries: whether the plan has a session request
/// open, and the stage each entry a plan session runs has reached.
/// </summary>
/// <param name="Queued">Whether the plan has a session request open — Requested,
/// Claimed or Started.</param>
/// <param name="Stages">The stage of each entry a plan session runs, by entry id.
/// An entry absent here can still be in a session, read off its own status.</param>
public sealed record PlanRun(bool Queued, IReadOnlyDictionary<Guid, string> Stages)
{
    /// <summary>A plan with no request open and no run reported.</summary>
    public static PlanRun None { get; } = new(false, new Dictionary<Guid, string>());

    /// <summary>The run of a plan from its open request, or <see cref="None"/>'s terms
    /// when it has none.</summary>
    /// <param name="request">The plan's open session request, or null.</param>
    /// <param name="stages">The stage of each entry its session runs, when known.</param>
    public static PlanRun From(EntrySessionRequest? request, IReadOnlyDictionary<Guid, string>? stages = null) =>
        new(request is not null, stages ?? None.Stages);

    /// <summary>The stage the entry <paramref name="id"/> is at in a plan session, or null.</summary>
    public string? StageOf(Guid? id) =>
        id is { } key && Stages.TryGetValue(key, out var stage) && !string.IsNullOrWhiteSpace(stage) ? stage : null;
}

/// <summary>
/// A plan entry's state in its plan's queue, and its stage while a plan session runs it
/// (local ADR 0062 §4). Derived, never stored: from the entry, the plan's session
/// request and the plan run, so the row, the plan sections and My Day read one answer.
/// <para>
/// The first rule that holds wins, in this order: Done, Landed, Left for you, Held for
/// you, In plan session, Waiting, then Queued with plan when the plan is queued and
/// Ready when it is not. A finished entry is past every other state; a merge into the
/// plan branch is past the session; a <c>test</c> or <c>task</c> is the person's however
/// the run stands; a held entry is held even while the run goes on beside it.
/// </para>
/// </summary>
/// <param name="State">The state.</param>
/// <param name="Stage">The plan session's stage for <see cref="PlanEntryQueueState.InPlanSession"/>
/// when the run reported one, else null.</param>
public sealed record PlanEntryState(PlanEntryQueueState State, string? Stage = null)
{
    /// <summary>The prefix of the branch a plan's work lands on (<c>plan/&lt;tag&gt;</c>).</summary>
    public const string PlanBranchPrefix = "plan/";

    /// <summary>The state's words, as the row shows them.</summary>
    public string Label => State switch
    {
        PlanEntryQueueState.Waiting => "Waiting",
        PlanEntryQueueState.Ready => "Ready",
        PlanEntryQueueState.QueuedWithPlan => "Queued with plan",
        PlanEntryQueueState.InPlanSession => "In plan session",
        PlanEntryQueueState.HeldForYou => "Held for you",
        PlanEntryQueueState.LeftForYou => "Left for you",
        PlanEntryQueueState.Landed => "Landed",
        _ => "Done"
    };

    /// <summary>The state for <paramref name="row"/>, or null when the row is in no plan.</summary>
    /// <param name="row">The entry.</param>
    /// <param name="waiting">Whether the entry waits on another open entry — the Tasks
    /// state's own dependency reading.</param>
    /// <param name="run">The plan's run; <see cref="PlanRun.None"/> when null.</param>
    public static PlanEntryState? For(EntryRow row, bool waiting, PlanRun? run = null)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (PlanTagOf(row) is null) return null;

        run ??= PlanRun.None;

        if (row.IsPreviewCompleted || row.PreviewStatus is EntryStatus.Done or EntryStatus.Archived)
        {
            return new(PlanEntryQueueState.Done);
        }

        if (LandedOnPlanBranch(row)) return new(PlanEntryQueueState.Landed);

        if (row.PreviewType is EntryType.Task or EntryType.Test) return new(PlanEntryQueueState.LeftForYou);

        if (row.IsPreviewBlocked) return new(PlanEntryQueueState.HeldForYou);

        if (run.StageOf(row.Id) is { } stage) return new(PlanEntryQueueState.InPlanSession, stage);

        if (row.PreviewStatus is EntryStatus.InProgress && row.SessionLinks.Count > 0)
        {
            return new(PlanEntryQueueState.InPlanSession);
        }

        // Only a ready entry is one the run can pick up; a draft, or one in progress
        // with no session, waits as one on another entry does.
        if (waiting || row.PreviewStatus is not EntryStatus.Ready) return new(PlanEntryQueueState.Waiting);

        return new(run.Queued ? PlanEntryQueueState.QueuedWithPlan : PlanEntryQueueState.Ready);
    }

    /// <summary>The plan tag the row carries, <c>+</c> and all, or null when it carries none.</summary>
    public static string? PlanTagOf(EntryRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.PreviewTags.FirstOrDefault(tag => TagText.Kind(tag) is TagKind.Plan);
    }

    /// <summary>Whether a pull request the row links to merged into a plan branch.</summary>
    private static bool LandedOnPlanBranch(EntryRow row) =>
        row.PullRequestStatuses.Values.Any(pullRequest =>
            pullRequest.State is LinkedPullRequestState.Merged
            && pullRequest.BaseRefName is { } baseRef
            && baseRef.StartsWith(PlanBranchPrefix, StringComparison.OrdinalIgnoreCase));
}
