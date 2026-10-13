using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.Tasks;

/// <summary>
/// The inbox half of a repository rename, answered out of the Inbox context's own
/// port.
/// <para>
/// A cross-context join beside <see cref="LinkedSessionStates"/>, and here for the
/// reason it is: the Repositories settings page belongs to the Tasks module and may
/// not reference the Inbox's published surface, so an adapter that may see both
/// answers Tasks' port. It passes the call on and nothing else — which items name
/// the old id, and whether they may move, is the Inbox's call.
/// </para>
/// </summary>
public sealed class InboxRepositoryRename(IInboxItems inbox) : IInboxRepositoryRename
{
    public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) =>
        inbox.RenameRepositoryAsync(oldId, newId, cancellationToken);
}
