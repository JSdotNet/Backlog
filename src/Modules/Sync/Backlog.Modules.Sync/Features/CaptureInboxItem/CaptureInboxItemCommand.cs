using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.CaptureInboxItem;

/// <summary>A thought pushed at the sync service from wherever the person was —
/// the phone, the editor extension — to be picked up on the desktop
/// later. Everything after <paramref name="Source"/> is optional: a
/// <paramref name="Id"/> the client minted, so a retry is recognised; the notes
/// beneath the title; tags; and a person, with or without its <c>@</c>. The
/// endpoint has bounded all of them before this is built.</summary>
public sealed record CaptureInboxItemCommand(
    OwnerScope Scope,
    string Title,
    string Source,
    Guid? Id = null,
    string? BodyMd = null,
    IReadOnlyList<string>? Tags = null,
    string? Person = null);

/// <summary>The capture the caller now has, and whether this call is what
/// wrote it. <see cref="Created"/> is false for a retry that found its own
/// capture already stored — the endpoint answers that 200 rather than 201.</summary>
public sealed record CaptureOutcome(InboxItem Item, bool Created);

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
/// <para>
/// <b>A client id makes the write idempotent.</b> A phone that timed out
/// waiting for its 201 cannot tell a capture that was never stored from one
/// whose answer was lost, so it sends the same id again. Found under this
/// owner as a capture — live, or already acknowledged — it is answered as it
/// stands and nothing is written: re-upserting would restamp it, and a
/// restamped tombstone is a withdrawn capture brought back. Found as anything
/// else it is a conflict, because that id already names one of this owner's
/// tasks and a capture written over it would replace the task on every device.
/// Another owner's document under the same id is invisible here — the lookup
/// starts from the owner — and the partitions keep the two apart.
/// </para>
/// <para>
/// <b>The person travels among the tags as <c>@name</c>.</b> The task shape
/// has no field for a person, and the Sync module adds none: the desktop's
/// intake reads the one tag carrying the sigil as the capture's person and the
/// rest as its tags. The endpoint refuses a tag that reads as a person, so the
/// sigil on the document is only ever this one.
/// </para>
/// </summary>
public sealed class CaptureInboxItemCommandHandler(ITaskReplica replica, TimeProvider clock)
    : ICommandHandler<CaptureInboxItemCommand, Result<CaptureOutcome>>
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

    public async Task<Result<CaptureOutcome>> Handle(
        CaptureInboxItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Id is { } requested
            && await replica.Find(command.Scope.OwnerId, requested, cancellationToken) is { } stored)
        {
            if (!string.Equals(stored.Change.Task.Type, CaptureType, StringComparison.Ordinal))
            {
                return Result.Failure<CaptureOutcome>(Error.Conflict(
                    SyncErrorCodes.CaptureIdTaken,
                    "That id already names a task, not a capture."));
            }

            return new CaptureOutcome(
                new InboxItem(
                    stored.Change.Id,
                    stored.Change.Task.Title,
                    stored.Change.Task.SourceInboxId ?? string.Empty,
                    stored.Change.Task.CreatedAt),
                Created: false);
        }

        var now = clock.GetUtcNow();
        var id = command.Id ?? Guid.CreateVersion7();

        var change = new TaskChange(
            id,
            now,
            DeletedAt: null,
            new TaskPayload(
                command.Title,
                ContentMd: command.BodyMd ?? string.Empty,
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
                Tags: DocumentTags(command.Tags, command.Person),
                RepoIds: [],
                DependsOn: [],
                SubItems: [],
                UsageEvents: [],
                ProjectionRefs: []));

        await replica.Upsert(command.Scope, [change], cancellationToken);

        return new CaptureOutcome(new InboxItem(id, command.Title, command.Source, now), Created: true);
    }

    /// <summary>The tags as sent, trimmed, followed by the person as one
    /// <c>@name</c> tag. Not de-duplicated or stripped of their <c>#</c>: that is
    /// the desktop's rule to apply (<c>InboxItem.SetTags</c>), and the service
    /// interprets no more of a task than it has to.</summary>
    private static IReadOnlyList<string> DocumentTags(IReadOnlyList<string>? tags, string? person)
    {
        var written = new List<string>();

        foreach (var tag in tags ?? []) written.Add(tag.Trim());

        // A bare "@" or a blank is no person at all, and is dropped rather than
        // written as a sigil with nobody behind it.
        var name = (person ?? string.Empty).Trim().TrimStart('@');
        if (name.Length > 0) written.Add("@" + name);

        return written;
    }
}
