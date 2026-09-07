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
/// Writes the capture as an ordinary task document, so the desktop pulls it
/// through the same feed as everything else and no second sync path exists for
/// captures.
/// <para>
/// The three tokens below are the wire spellings the local SQLite store already
/// writes, duplicated here as constants rather than referenced. The Sync module
/// has no reference to Backlog.Modules.Tasks and must not grow one: the replica
/// is a relay between devices, and a service that imported the task domain to
/// name a status would have to be redeployed whenever that domain gained one.
/// What the two share is three literals, and a device that reads this document
/// parses them with its own vocabulary.
/// </para>
/// </summary>
public sealed class CaptureInboxItemCommandHandler(ITaskReplica replica, TimeProvider clock)
    : ICommandHandler<CaptureInboxItemCommand, Result<InboxItem>>
{
    /// <summary>A capture is a task, not a prompt and not an idea.</summary>
    private const string TaskType = "task";

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
                TaskType,
                DraftStatus,
                MediumPriority,
                Order: 0,
                Area: null,
                now,
                // The source is what makes this document a capture. Clearing it
                // is how it stops being one.
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
