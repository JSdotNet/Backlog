using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Backlog.Features.DeleteEntry;

/// <summary>Removes an entry for good. Deliberate and explicit — the app asks
/// before it gets here.</summary>
public sealed record DeleteEntryCommand(Guid Id);

public sealed class DeleteEntryCommandHandler(IBacklogRepository entries)
    : ICommandHandler<DeleteEntryCommand>
{
    public Task Handle(DeleteEntryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return entries.DeleteAsync(command.Id, cancellationToken);
    }
}
