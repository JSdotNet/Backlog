using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Abstractions.Services;

/// <summary>
/// Everything the pane may do to the inbox, in one port — the service contract
/// ADR 0005 asks a module to publish, a plain delegation to the feature slices
/// behind it, exactly as <c>ITaskItems</c> is for Tasks.
/// <para>
/// Note what is not here. Receiving a capture from the replica is
/// <see cref="IInboxIntake"/>, because the caller is the sync client and not a
/// screen; draining acknowledgements is <see cref="IInboxCaptureOutbox"/> for
/// the same reason. And there is no "mark triaged": in this product triage
/// <em>is</em> routing, deferring or archiving, so an item becomes
/// <see cref="InboxStatus.Triaged"/> as part of <see cref="RouteToBacklogAsync"/>
/// and never on its own.
/// </para>
/// </summary>
public interface IInboxItems
{
    /// <summary>Every item, list and group, in one read.</summary>
    Task<InboxSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>Captures a thought typed straight into the desktop. The one
    /// path into the inbox that involves no other device, and therefore the one
    /// a person can use offline. <paramref name="notes"/> is what was written
    /// beneath the title and becomes the item's body; null or blank is a bare
    /// one-line capture. <paramref name="channel"/> is the capture source it is
    /// filed under and defaults to the domain's word for "by hand".</summary>
    Task<Result<InboxItemDto>> CaptureAsync(
        string title,
        string? notes = null,
        string channel = InboxEnumMap.ManualChannel,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the item's tags. Bare names; a name that reads as a
    /// person (<c>@bob</c>) is refused, because a person is a source, not a tag.</summary>
    Task<Result> SetTagsAsync(Guid id, IReadOnlyList<string> tags, CancellationToken cancellationToken = default);

    /// <summary>Replaces the repositories the item will be routed to.</summary>
    Task<Result> AssignRepositoriesAsync(Guid id, IReadOnlyList<string> repoIds, CancellationToken cancellationToken = default);

    /// <summary>Re-points every item assigned to <paramref name="oldId"/> at
    /// <paramref name="newId"/> after a repository rename, and answers how many
    /// moved. Routing records are left as written. Idempotent.</summary>
    Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default);

    /// <summary>Files the item in a list, or back in the unfiled inbox with
    /// null. Filing is not triage: it is allowed in every state, archived
    /// included.</summary>
    Task<Result> MoveToListAsync(Guid id, Guid? listId, CancellationToken cancellationToken = default);

    /// <summary>Dismisses the item. Terminal, and the only terminal state an
    /// item reaches without leaving anything behind in another context.</summary>
    Task<Result> ArchiveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Dismisses the item as the same capture as
    /// <paramref name="duplicateOf"/>, and records which — "Archive as duplicate
    /// of…". Only an open item; never itself. When the other item is itself a
    /// duplicate, the root of its chain is recorded instead. Fails with
    /// <c>inbox.duplicate.not_found</c> when the other item is gone, and with
    /// <c>inbox.duplicate.circular</c> when its chain leads back to this one.</summary>
    Task<Result> ArchiveAsDuplicateAsync(Guid id, Guid duplicateOf, CancellationToken cancellationToken = default);

    /// <summary>Records that the item is already the task
    /// <paramref name="taskId"/> — "Link to task…": the item is routed to that
    /// task and nothing is created. Only an open item. Fails with
    /// <c>inbox.link.task_not_found</c> for a task the backlog no longer has.</summary>
    Task<Result> LinkToTaskAsync(Guid id, Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>Folds the item into the backlog task <paramref name="taskId"/> it
    /// repeats — "Merge into a task": the item's title, then its link when it has
    /// one, then its notes are written on the task as one comment, and the item is
    /// archived as a duplicate of the task (<c>DuplicateOf</c> names it,
    /// <c>DuplicateOfTask</c> says so). Only an open item. Fails with
    /// <c>inbox.merge.task_not_found</c> for a task the backlog no longer has,
    /// and with Tasks' <c>comment.not_prose</c> when a line of the item would
    /// become structure on the task; either way nothing is written.</summary>
    Task<Result> MergeIntoTaskAsync(Guid id, Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>What the item already has to do with the rest of the backlog:
    /// the other items that look like the same capture and the tasks that carry
    /// it, each with the reason, and the open tasks "Link to task…" can offer.
    /// Asked for any item, decided ones included.</summary>
    Task<Result<InboxRelationsDto>> RelatedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Deletes the item for good, from any state. Unlike archiving
    /// nothing is kept, except — for an item from the replica the phone may
    /// still be offering — the acknowledgement that tells it to stop, and for
    /// any other item its id, so a feed or an import cannot bring it back.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Puts the item aside until <paramref name="until"/>, or with no
    /// date until a person returns it. On an item already deferred it changes
    /// the review date.</summary>
    Task<Result> DeferAsync(Guid id, DateOnly? until, CancellationToken cancellationToken = default);

    /// <summary>Returns a deferred item to the queue now, whatever its review
    /// date.</summary>
    Task<Result> ResurfaceAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The resurface sweep: every deferred item whose review date is
    /// today or earlier becomes unprocessed. Answers how many moved. The pane
    /// runs it when it opens; nothing schedules it.</summary>
    Task<Result<int>> ResurfaceDueAsync(CancellationToken cancellationToken = default);

    // --- The same four acts across a selection ------------------------------
    //
    // Overloads, not new commands: each runs the single-item command above once
    // per item, so there is one rule per act and one place it is enforced. An
    // item the command refuses is named in the result and does not stop the
    // rest.

    /// <summary>Gives each item its own tag set — per item, because adding a tag
    /// to a selection keeps every item's other tags, so no two items need the
    /// same list.</summary>
    Task<InboxBatchResultDto> SetTagsAsync(
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> tagsByItem,
        CancellationToken cancellationToken = default);

    Task<InboxBatchResultDto> AssignRepositoriesAsync(
        IReadOnlyList<Guid> ids,
        IReadOnlyList<string> repoIds,
        CancellationToken cancellationToken = default);

    Task<InboxBatchResultDto> MoveToListAsync(
        IReadOnlyList<Guid> ids,
        Guid? listId,
        CancellationToken cancellationToken = default);

    Task<InboxBatchResultDto> ArchiveAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes each item for good, as <see cref="DeleteAsync(Guid, CancellationToken)"/>
    /// does one — what that leaves behind, each item here leaves too.</summary>
    Task<InboxBatchResultDto> DeleteAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>Turns the item into backlog entries — one per assigned
    /// repository, or one untargeted entry — and records where they went. An
    /// item routes exactly once.</summary>
    Task<Result<InboxRoutedDto>> RouteToBacklogAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Routes several items as one batch — one plan import whose
    /// entries share a new plan tag. It overloads the single route by name, but
    /// unlike the selection acts above it does not run the single-item command
    /// once per item: Tasks is asked once, for one document.
    /// <para>
    /// Items already routed or archived, or gone, are refused before Tasks is
    /// asked, and named. So is any item the backlog side cannot put into the
    /// document — notes that would split it, a repository the workspace does not
    /// know — with its own reason, while the rest still go. The ones sent go
    /// together or, when Tasks refuses the document, not at all, each named with
    /// <c>inbox.batch.refused</c>. An item whose entries were made but which
    /// could not be saved as routed is named with <c>inbox.batch.save_failed</c>
    /// and those entries.
    /// </para>
    /// <para>
    /// With <paramref name="listId"/> the tag is the list's name
    /// (<c>+reading-1a2b3c4d</c>), otherwise <c>+inbox-batch-…</c>; the list is
    /// only where the name comes from, and is left as it is. Fails whole only
    /// when <paramref name="listId"/> names no list.
    /// </para>
    /// <para>
    /// <paramref name="choices"/> are the "Before you route" panel's: the tag
    /// <see cref="ProposeBatchAsync"/> minted, a repository per item, and the
    /// dependencies left on, which become <c>after:</c> tokens and put the
    /// document in dependency order. A set of dependencies that loops fails the
    /// whole batch with <c>inbox.batch.dependency_loop</c> before Tasks is asked.
    /// Without choices the batch goes as it always did.
    /// </para></summary>
    Task<Result<InboxBatchRoutedDto>> RouteToBacklogAsync(
        IReadOnlyList<Guid> ids,
        Guid? listId = null,
        InboxBatchRouteChoicesDto? choices = null,
        CancellationToken cancellationToken = default);

    /// <summary>What routing these items as one batch would do, without doing
    /// it: the plan tag the import would write — minted now, and handed back on
    /// the route — the items that can go, the dependencies the Inbox can see
    /// between them and on the backlog's open tasks, each with the text that
    /// stated it, and the items that cannot go and why. For a list, also how
    /// many of its items are deferred and stay behind. Fails only when
    /// <paramref name="listId"/> names no list.</summary>
    Task<Result<InboxBatchProposalDto>> ProposeBatchAsync(
        IReadOnlyList<Guid> ids,
        Guid? listId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Asks the plan drafter for the order it reads into a batch, as
    /// dependencies between its items (<see cref="DependencyTier.Inferred"/>)
    /// the person keeps or turns off in the panel. Nothing is routed; asked only
    /// when the person asks, since every ask is a model call.
    /// <paramref name="planTag"/> is the tag <see cref="ProposeBatchAsync"/>
    /// minted and <paramref name="repositories"/> the panel's per-item choices —
    /// the repositories the answer may name. Fails with
    /// <c>inbox.plan.not_configured</c> when <see cref="PlanDrafterAvailability"/>
    /// says so, <c>inbox.plan.failed</c> when the drafter could not answer, and
    /// <c>inbox.order.unknown_repository</c> or <c>inbox.order.unknown_item</c>
    /// when its answer named something outside the batch — refused whole.</summary>
    Task<Result<IReadOnlyList<ProposedDependency>>> InferBatchOrderAsync(
        IReadOnlyList<Guid> ids,
        string planTag,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? repositories = null,
        CancellationToken cancellationToken = default);

    /// <summary>Asks the plan drafter for an import plan about the item, hands
    /// the plan to Tasks' import, and records the entries it produced as the
    /// item's routing. Fails with <c>inbox.plan.not_configured</c> when
    /// <see cref="PlanDrafterAvailability"/> says so.</summary>
    Task<Result<InboxRoutedDto>> CreatePlanAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Fetches one of the item's files again — the act behind Retry on a
    /// file row whose download failed. A file already on this machine with the
    /// right digest is not fetched; a failure is recorded on the file and
    /// answered, so the row shows the new reason.</summary>
    Task<Result> RetryAttachmentAsync(Guid id, Guid attachmentId, CancellationToken cancellationToken = default);

    /// <summary>The bytes of one downloaded file — what a thumbnail is drawn
    /// from. Fails with <c>inbox.attachment.not_downloaded</c> for a file that
    /// is not on this machine.</summary>
    Task<Result<byte[]>> ReadAttachmentAsync(Guid id, Guid attachmentId, CancellationToken cancellationToken = default);

    /// <summary>Opens one downloaded file with whatever this machine opens that
    /// kind of file with.</summary>
    Task<Result> OpenAttachmentAsync(Guid id, Guid attachmentId, CancellationToken cancellationToken = default);

    /// <summary>What Classification proposes for the item — tags, repositories,
    /// a destination — less the ones the reader turned down for it. Proposals
    /// only: each is applied, if at all, through the act it names. Empty for an
    /// item already routed or archived.</summary>
    Task<Result<IReadOnlyList<InboxSuggestionDto>>> SuggestAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Turns a suggestion down for the item, by its key, so it is never
    /// offered for that item again. Idempotent.</summary>
    Task<Result> DismissSuggestionAsync(Guid id, string key, CancellationToken cancellationToken = default);

    /// <summary>Whether "Create plan" can be offered, and why not when it cannot.
    /// Read by the pane on render so the control is shown disabled with its
    /// reason rather than hidden — unavailability never hides an act.</summary>
    (bool Available, string? Reason) PlanDrafterAvailability { get; }

    /// <summary>Whether a triage advisor is registered and can run. Unlike
    /// <see cref="PlanDrafterAvailability"/> there is no reason to show: while it
    /// is false every AI triage surface is hidden, not disabled (local ADR 0023 §4).</summary>
    bool TriageAdvisorAvailable { get; }

    /// <summary>The AI cards for one unprocessed item opened in triage: at most
    /// one duplicate and one plan grouping, and the repositories it would go
    /// to, held to what the inbox and the backlog hold. <paramref name="repositories"/>
    /// are the ones configured in Settings. One model call per ask. Fails with
    /// <c>inbox.triage.not_configured</c> when <see cref="TriageAdvisorAvailable"/>
    /// is false and <c>inbox.triage.failed</c> when the advisor could not answer.</summary>
    Task<Result<InboxTriageAdviceDto>> AdviseTriageAsync(
        Guid id,
        IReadOnlyList<string>? repositories = null,
        CancellationToken cancellationToken = default);

    /// <summary>The AI triage pass over the unprocessed items of
    /// <paramref name="ids"/>: plans, duplicate pairs, single routes, list
    /// filings, archives, and the items it could not place. A proposal only —
    /// nothing changes. Fails as <see cref="AdviseTriageAsync"/> does.</summary>
    Task<Result<InboxTriagePassDto>> ProposeTriagePassAsync(
        IReadOnlyList<Guid> ids,
        IReadOnlyList<string>? repositories = null,
        CancellationToken cancellationToken = default);

    Task<Result<InboxListDto>> CreateListAsync(string name, Guid? groupId = null, CancellationToken cancellationToken = default);

    Task<Result> RenameListAsync(Guid listId, string name, CancellationToken cancellationToken = default);

    /// <summary>Deletes the list outright; the items in it return to the
    /// unfiled inbox. Lists are local organisation and nothing syncs them, so
    /// there is no tombstone to leave.</summary>
    Task<Result> DeleteListAsync(Guid listId, CancellationToken cancellationToken = default);

    Task<Result> MoveListToGroupAsync(Guid listId, Guid? groupId, CancellationToken cancellationToken = default);

    Task<Result<InboxGroupDto>> CreateGroupAsync(string name, CancellationToken cancellationToken = default);

    Task<Result> RenameGroupAsync(Guid groupId, string name, CancellationToken cancellationToken = default);

    /// <summary>Dissolves the group: its lists move to the top level and the
    /// group is deleted.</summary>
    Task<Result> UngroupAsync(Guid groupId, CancellationToken cancellationToken = default);

    /// <summary>Seeds the starter groups and lists into an organiser that has
    /// none. Idempotent; the pane calls it on every start.</summary>
    Task EnsureDefaultOrganizerAsync(CancellationToken cancellationToken = default);
}
