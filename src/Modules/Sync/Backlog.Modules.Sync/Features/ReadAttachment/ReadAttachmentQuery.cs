using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.ReadAttachment;

/// <summary>A device fetches one attachment — the desktop taking a capture's
/// files into its Inbox.</summary>
public sealed record ReadAttachmentQuery(OwnerScope Scope, Guid Id);

/// <summary>
/// Opens the caller's attachment for streaming. Another owner's attachment is
/// not found rather than forbidden: the store is asked under the caller's own
/// owner, so there is no read here that could reach it, and "forbidden" would
/// confirm the id exists.
/// </summary>
public sealed class ReadAttachmentQueryHandler(IAttachmentStore store)
    : IQueryHandler<ReadAttachmentQuery, Result<AttachmentContent>>
{
    public async Task<Result<AttachmentContent>> Handle(
        ReadAttachmentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await store.Open(query.Scope.OwnerId, query.Id, cancellationToken) is { } content
            ? content
            : Result.Failure<AttachmentContent>(Error.NotFound(
                SyncErrorCodes.AttachmentNotFound,
                "No attachment with that id is stored for this owner."));
    }
}
