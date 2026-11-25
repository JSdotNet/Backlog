using Backlog.Modules.Tasks.Services;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Tasks.Features.GetTaskVersion;

/// <summary>The version the store holds of one live task — when it was last
/// written — or null when there is none. The token a versioned write is pinned to
/// (<c>ITaskItems.GetUpdatedAtAsync</c>).</summary>
public sealed record GetTaskVersionQuery(Guid Id);

public sealed class GetTaskVersionQueryHandler(ITaskRepository repository)
    : IQueryHandler<GetTaskVersionQuery, DateTimeOffset?>
{
    public async Task<DateTimeOffset?> Handle(GetTaskVersionQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var task = await repository.GetAsync(query.Id, cancellationToken).ConfigureAwait(false);

        // The stamp the store holds, which is what a conditional save compares.
        return task?.LoadedUpdatedAt;
    }
}
