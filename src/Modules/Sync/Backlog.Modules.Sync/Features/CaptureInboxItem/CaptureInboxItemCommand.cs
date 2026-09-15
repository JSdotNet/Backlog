using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.CaptureInboxItem;

/// <summary>A thought pushed at the sync service from wherever the person was —
/// the phone, the editor extension — to be picked up on the desktop
/// later.</summary>
public sealed record CaptureInboxItemCommand(OwnerScope Scope, string Title, string Source);

/// <summary>
/// Writes the capture as a task-shaped document with its own kind token, so
/// the desktop pulls it through the same feed as everything else and no second
/// sync path exists for captures.
/// <para>
/// The three tokens below are wire spellings duplicated here as constants
/// rather than referenced. The Sync module has no reference to
/// Backlog.Modules.Tasks and must not grow one: the replica is a relay between
/// devices, and a service that imported the task domain to name a status would
/// have to be redeployed whenever that domain gained one. What the two sides
/// share is three literals, and a device that reads this document parses them
/// with its own vocabulary.
/// </para>
/// <para>
/// <see cref="CaptureType"/> is the one of the three that is <em>not</em> a
/// task-vocabulary word, and that is the design. It is the document kind the
/// replica's inbox predicate filters on, and the desktop's inbox intake takes
/// a document carrying it before the task merge ever sees it. A desktop build
/// that does not know the inbox has no member for it either, so it skips the
/// document as unreadable and leaves it on the replica — which is exactly what
/// keeps a capture out of that build's task table. The desktop's <em>tasks</em>
/// carry the inbox item's id in <c>SourceInboxId</c> once routed, so that field
/// stopped being able to mean "this is a capture"; the type token is what does.
/// </para>
/// </summary>
public sealed class CaptureInboxItemCommandHandler(ITaskReplica replica, TimeProvider clock)
    : ICommandHandler<CaptureInboxItemCommand, Result<InboxItem>>
{
    /// <summary>The document kind of a capture. Not a task type: the desktop's
    /// Tasks vocabulary has no member for it, on purpose (see the class remarks).
    /// The same literal is read in <c>InMemoryTaskReplica</c>, in
    /// <c>CosmosTaskReplica</c>, and in the desktop's <c>TaskReplicaMerge</c>.</summary>
    internal const string CaptureType = "capture";

    /// <summary>Unreviewed. The desktop is what promotes it once a person has
    /// looked at it.</summary>
    private const string DraftStatus = "draft";

    /// <summary>No priority was expressed, and the local store has no token for
    /// "unset" — medium is the neutral one.</summary>
    private const string MediumPriority = "medium";

    public async Task<Result<InboxItem>> Handle(
        CaptureInboxItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = clock.GetUtcNow();
        var id = Guid.CreateVersion7();

        var change = new TaskChange(
            id,
            now,
            DeletedAt: null,
            new TaskPayload(
                command.Title,
                ContentMd: string.Empty,
                CaptureType,
                DraftStatus,
                MediumPriority,
                Order: 0,
                Area: null,
                now,
                // Where it was captured — the phone, the editor — which the
                // desktop files the item under as its channel.
                command.Source,
                RecurrenceSourceId: null,
                DueOn: null,
                RemindAt: null,
                Recurrence: null,
                InMyDayOn: null,
                View: null,
                Effort: null,
                ImportPlanId: null,
                ImportItemId: null,
                AttachmentPath: null,
                Tags: [],
                RepoIds: [],
                DependsOn: [],
                SubItems: [],
                UsageEvents: [],
                ProjectionRefs: []));

        await replica.Upsert(command.Scope, [change], cancellationToken);

        return new InboxItem(id, command.Title, command.Source, now);
    }
}
