using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Features.RestoreDeletedLinkedTasks;

/// <summary>
/// The published <see cref="IDeletedLinkedTasks"/> port, wired to this slice's
/// query and command. Nothing but mapping, as <c>DisconnectedLinkedTasks</c> is.
/// </summary>
internal sealed class DeletedLinkedTasks(
    IQueryHandler<ListDeletedLinkedSourcesQuery, IReadOnlyList<DeletedLinkedSource>> list,
    ICommandHandler<RestoreDeletedLinkedTasksCommand, Result<int>> restore) : IDeletedLinkedTasks
{
    public Task<IReadOnlyList<DeletedLinkedSource>> ListAsync(CancellationToken cancellationToken = default) =>
        list.Handle(new ListDeletedLinkedSourcesQuery(), cancellationToken);

    public Task<Result<int>> RestoreAsync(string connectorId, string target, CancellationToken cancellationToken = default) =>
        restore.Handle(new RestoreDeletedLinkedTasksCommand(connectorId, target), cancellationToken);
}
