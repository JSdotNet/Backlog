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

    /// <summary>Deletes the item for good, from any state. Unlike archiving
    /// nothing is kept, except — for an item from the replica the phone may
    /// still be offering — the acknowledgement that tells it to stop.</summary>
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

    /// <summary>Turns the item into backlog entries — one per assigned
    /// repository, or one untargeted entry — and records where they went. An
    /// item routes exactly once.</summary>
    Task<Result<InboxRoutedDto>> RouteToBacklogAsync(Guid id, CancellationToken cancellationToken = default);

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

    /// <summary>Whether "Create plan" can be offered, and why not when it cannot.
    /// Read by the pane on render so the control is shown disabled with its
    /// reason rather than hidden — unavailability never hides an act.</summary>
    (bool Available, string? Reason) PlanDrafterAvailability { get; }

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
