using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.AcknowledgeInboxItem;

/// <summary>The desktop says it has taken a capture and the inbox need not
/// offer it again.</summary>
public sealed record AcknowledgeInboxItemCommand(OwnerScope Scope, Guid Id);

/// <summary>
/// Clears the document's source inbox id. It does not delete anything, and that
/// is the decision this slice exists to hold.
/// <para>
/// A capture is an ordinary task document that happens to carry a source inbox
/// id, and the replica is whole-document last-write-wins across every device an
/// owner has. Writing a tombstone here would therefore delete that task
/// <em>everywhere</em> — so acknowledging a capture the desktop had already
/// pulled and turned into real work would silently destroy the work. Clearing
/// the source id drops the document out of the inbox predicate instead, and the
/// task survives on every device as what it has become.
/// </para>
/// <para>
/// If a future reader is about to "fix" this into a delete: the test
/// <c>Acknowledging_a_capture_keeps_the_task</c> is what will stop them, and
/// this paragraph is why it should.
/// </para>
/// <para>
/// A document belonging to another owner is not found rather than forbidden.
/// The lookup starts from the owner in the token, so there is no query here that
/// could see it, and saying "forbidden" would confirm the id exists.
/// </para>
/// </summary>
public sealed class AcknowledgeInboxItemCommandHandler(ITaskReplica replica, TimeProvider clock)
    : ICommandHandler<AcknowledgeInboxItemCommand, Result>
{
    public async Task<Result> Handle(
        AcknowledgeInboxItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await replica.Find(command.Scope.OwnerId, command.Id, cancellationToken);

        if (found is null || found.Change.DeletedAt is not null || found.Change.Task.SourceInboxId is null)
        {
            return Result.Failure(Error.NotFound(
                SyncErrorCodes.InboxItemNotFound,
                "No capture with that id is waiting for this owner."));
        }

        var acknowledged = found.Change with
        {
            UpdatedAt = clock.GetUtcNow(),
            Task = found.Change.Task with { SourceInboxId = null },
        };

        await replica.Upsert(command.Scope, [acknowledged], cancellationToken);

        return Result.Success();
    }
}
