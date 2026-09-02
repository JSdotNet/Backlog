using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Services;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Tasks.Features.ListTasks;

/// <summary>Every task in the backlog, in rank order.</summary>
public sealed record ListTasksQuery;

public sealed class ListTasksQueryHandler(ITaskRepository repository)
    : IQueryHandler<ListTasksQuery, IReadOnlyList<TaskItemDto>>
{
    public async Task<IReadOnlyList<TaskItemDto>> Handle(
        ListTasksQuery query,
        CancellationToken cancellationToken = default)
    {
        // The store returns whole aggregates already ordered. This used to read a
        // derived index and then fetch each task behind it one at a time, because
        // the index and the truth were two different files that could disagree.
        var tasks = await repository.ListAsync(cancellationToken);

        return [.. tasks.Select(task => task.ToDto())];
    }
}
