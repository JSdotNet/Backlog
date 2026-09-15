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

    /// <summary>Files the item in a list, or back in the unfiled inbox with
    /// null. Filing is not triage: it is allowed in every state, archived
    /// included.</summary>
    Task<Result> MoveToListAsync(Guid id, Guid? listId, CancellationToken cancellationToken = default);

    /// <summary>Dismisses the item. Terminal, and the only terminal state an
    /// item reaches without leaving anything behind in another context.</summary>
    Task<Result> ArchiveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Turns the item into backlog entries — one per assigned
    /// repository, or one untargeted entry — and records where they went. An
    /// item routes exactly once.</summary>
    Task<Result<InboxRoutedDto>> RouteToBacklogAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Asks the plan drafter for an import plan about the item, hands
    /// the plan to Tasks' import, and records the entries it produced as the
    /// item's routing. Fails with <c>inbox.plan.not_configured</c> when
    /// <see cref="PlanDrafterAvailability"/> says so.</summary>
    Task<Result<InboxRoutedDto>> CreatePlanAsync(Guid id, CancellationToken cancellationToken = default);

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
