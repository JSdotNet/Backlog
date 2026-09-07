using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// A task store that keeps whole aggregates in a dictionary, with the port's
/// tombstone rules honoured: both ordinary reads hide a deleted task and only
/// <see cref="GetIncludingDeletedAsync"/> sees one. A double that answered a
/// deleted task through <see cref="GetAsync"/> would make the merge's tombstone
/// test pass for the wrong reason.
/// </summary>
internal sealed class InMemoryTaskStore : ITaskRepository
{
    public Dictionary<Guid, TaskItem> Tasks { get; } = [];

    /// <summary>Every save, in order, so a test can assert that a page which
    /// changes nothing writes nothing rather than writing the same thing
    /// again.</summary>
    public List<Guid> Writes { get; } = [];

    public void Seed(TaskItem task) => Tasks[task.Id] = task;

    public Task SaveAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        Writes.Add(task.Id);
        Tasks[task.Id] = task;
        return Task.CompletedTask;
    }

    public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Tasks.TryGetValue(id, out var task) && task.DeletedAt is null ? task : null);

    public Task<TaskItem?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Tasks.TryGetValue(id, out var task) ? task : null);

    public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TaskItem>>([.. Tasks.Values.Where(task => task.DeletedAt is null)]);

    /// <summary>The sync read, and the only one here that shows a tombstone. A
    /// double that filtered one out would make a push look correct while the
    /// deletion it was supposed to carry stayed on this machine.</summary>
    public Task<IReadOnlyList<TaskItem>> ListChangedSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TaskItem>>(
            [.. Tasks.Values.Where(task => task.UpdatedAt > since).OrderBy(task => task.UpdatedAt)]);
}
