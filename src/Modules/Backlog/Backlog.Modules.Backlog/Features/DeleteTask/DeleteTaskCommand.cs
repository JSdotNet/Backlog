using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Backlog.Features.DeleteTask;

/// <summary>Removes an entry for good. Deliberate and explicit — the app asks
/// before it gets here.</summary>
public sealed record DeleteTaskCommand(Guid Id);

public sealed class DeleteTaskCommandHandler(ITaskRepository entries)
    : ICommandHandler<DeleteTaskCommand>
{
    public Task Handle(DeleteTaskCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return entries.DeleteAsync(command.Id, cancellationToken);
    }
}
