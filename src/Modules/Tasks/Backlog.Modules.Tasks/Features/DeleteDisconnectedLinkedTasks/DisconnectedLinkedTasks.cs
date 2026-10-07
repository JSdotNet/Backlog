using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Features.DeleteDisconnectedLinkedTasks;

/// <summary>
/// The published <see cref="IDisconnectedLinkedTasks"/> port, wired to this
/// slice's query and command. Nothing but mapping, as <c>TaskItems</c> is.
/// </summary>
internal sealed class DisconnectedLinkedTasks(
    IQueryHandler<ListDisconnectedSourcesQuery, IReadOnlyList<DisconnectedSource>> list,
    ICommandHandler<DeleteDisconnectedLinkedTasksCommand, Result<int>> delete) : IDisconnectedLinkedTasks
{
    public Task<IReadOnlyList<DisconnectedSource>> ListAsync(CancellationToken cancellationToken = default) =>
        list.Handle(new ListDisconnectedSourcesQuery(), cancellationToken);

    public Task<Result<int>> DeleteAsync(string connectorId, string target, CancellationToken cancellationToken = default) =>
        delete.Handle(new DeleteDisconnectedLinkedTasksCommand(connectorId, target), cancellationToken);
}
