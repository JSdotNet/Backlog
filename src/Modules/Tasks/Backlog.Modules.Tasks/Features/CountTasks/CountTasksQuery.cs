using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Tasks.Features.CountTasks;

/// <summary>How many live tasks the backlog holds.</summary>
public sealed record CountTasksQuery;

public sealed class CountTasksQueryHandler(ITaskRepository repository)
    : IQueryHandler<CountTasksQuery, int>
{
    public Task<int> Handle(CountTasksQuery query, CancellationToken cancellationToken = default) =>
        repository.CountAsync(cancellationToken);
}
