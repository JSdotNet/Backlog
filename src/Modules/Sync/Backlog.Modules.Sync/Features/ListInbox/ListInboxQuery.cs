using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.ListInbox;

/// <summary>The captures waiting for the calling owner. There is no filter and
/// no paging parameter: the inbox is what has not been triaged yet, and a
/// person who has thousands of those has a different problem.</summary>
public sealed record ListInboxQuery(OwnerScope Scope);

/// <summary>
/// Projects the owner's live capture documents onto the inbox contract the
/// phone, the desktop pane, and the editor extension already read.
/// <para>
/// A capture is a task-shaped document with its own kind token
/// (<c>CaptureInboxItemCommandHandler.CaptureType</c>). That is what lets the
/// desktop pull it through the ordinary task feed and hand it to its inbox
/// rather than its task table, and what lets acknowledging one be a tombstone
/// (see <c>AcknowledgeInboxItemCommand</c>): the id names the capture and
/// nothing the desktop made from it.
/// </para>
/// <para>
/// What this reads out of the payload — the title, the body, when it was
/// made, where from, and its tags — is copied rather than understood. The one
/// reading it does is the <c>@name</c> tag the capture handler wrote the person
/// as, handed back as the person so no reader has to know the convention.
/// Nothing else is interpreted here, which is what keeps .devbook/arc42/adr/0005's "no
/// domain logic runs against the replica" true of the one view it serves.
/// </para>
/// </summary>
public sealed class ListInboxQueryHandler(ITaskReplica replica)
    : IQueryHandler<ListInboxQuery, Result<IReadOnlyList<InboxItem>>>
{
    public async Task<Result<IReadOnlyList<InboxItem>>> Handle(
        ListInboxQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var captures = await replica.ListCaptures(query.Scope.OwnerId, cancellationToken);

        IReadOnlyList<InboxItem> items =
        [
            .. captures.Select(record => Project(record.Change)),
        ];

        return Result.Success(items);
    }

    /// <summary>A capture document as the inbox contract reads it. Shared with
    /// the capture handler, which answers a retried id with the stored capture
    /// and must answer it in the same shape the list does.</summary>
    internal static InboxItem Project(TaskChange change)
    {
        var task = change.Task;
        var tags = new List<string>();
        string? person = null;

        foreach (var tag in task.Tags)
        {
            // The first @name is the person; the endpoint refuses a tag that
            // reads as one, so there is only ever the one the handler wrote.
            if (person is null && tag.StartsWith('@') && tag.Length > 1) person = tag[1..];
            else tags.Add(tag);
        }

        return new InboxItem(
            change.Id,
            task.Title,
            task.SourceInboxId ?? string.Empty,
            task.CreatedAt,
            string.IsNullOrEmpty(task.ContentMd) ? null : task.ContentMd,
            tags,
            person,
            task.Attachments);
    }
}
