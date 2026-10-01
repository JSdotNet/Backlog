using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Tests;

/// <summary>
/// The store a host would supply, small enough to read. Entries are held as the
/// aggregates themselves rather than as serialized text, because what the tests
/// that use it are about is the handler's own orchestration rather than the
/// storage format.
/// </summary>
/// <param name="entries">What the store already holds before the test acts —
/// seeded directly, so it does not count as a write.</param>
internal sealed class InMemoryTaskRepository(params IEnumerable<TaskItem> entries) : ITaskRepository
{
    public Dictionary<Guid, TaskItem> Entries { get; } = entries.ToDictionary(entry => entry.Id);

    /// <summary>Every save, counted, so "wrote nothing" can be asserted
    /// rather than inferred from the rows looking the same.</summary>
    public int Writes { get; set; }

    /// <summary>The one entry that is not the one saved — which is what a
    /// recurring task's successor is.</summary>
    public TaskItem Successor(Guid completedId) =>
        Assert.Single(Entries.Values, entry => entry.Id != completedId);

    public Task SaveAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        Writes++;
        Entries[task.Id] = task;
        return Task.CompletedTask;
    }

    // Both reads hide a tombstoned entry, the way the port says they must: a
    // double that answers a deleted entry when the real store would not is a
    // double that lies.
    public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Entries.TryGetValue(id, out var entry) && entry.DeletedAt is null ? entry : null);

    // And the one read that does not, for the merge that has to tell
    // "deleted here" from "never seen here".
    public Task<TaskItem?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Entries.TryGetValue(id, out var entry) ? entry : null);

    public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TaskItem>>([.. Entries.Values.Where(entry => entry.DeletedAt is null)]);

    // And the list that does not either, for the push that has to carry a
    // deletion off the machine.
    public Task<IReadOnlyList<TaskItem>> ListChangedSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TaskItem>>(
            [.. Entries.Values.Where(entry => entry.UpdatedAt > since).OrderBy(entry => entry.UpdatedAt)]);
}
