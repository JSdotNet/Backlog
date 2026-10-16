using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Services;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Tasks.Features.GetTask;

/// <summary>One live task by its id, or null when there is none.</summary>
public sealed record GetTaskQuery(Guid Id);

public sealed class GetTaskQueryHandler(ITaskRepository repository)
    : IQueryHandler<GetTaskQuery, TaskItemDto?>
{
    public async Task<TaskItemDto?> Handle(GetTaskQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // One row, not the list: a caller that already knows which task it means
        // should not pay for mapping every other one to find it.
        var task = await repository.GetAsync(query.Id, cancellationToken).ConfigureAwait(false);

        return task?.ToDto();
    }
}
