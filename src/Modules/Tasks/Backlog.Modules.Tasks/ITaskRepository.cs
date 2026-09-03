using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Modules.Tasks;

/// <summary>
/// Local-first persistence for <see cref="TaskItem"/> aggregates. One local
/// store holds them whole; the task's content is markdown text inside the
/// aggregate rather than a document the store has to parse.
/// <para>
/// There is no delete member. Deleting a task is tombstoning it — see
/// <see cref="TaskItem.MarkDeleted"/> — which is an ordinary
/// <see cref="SaveAsync"/> of the aggregate, and the reads below hide what it
/// marks. A port member that removed the row outright would re-create the
/// deletion that cannot travel to the person's other machine, and it would need a
/// tombstone retention that nothing has chosen yet; the reaper arrives with the
/// sync service that decides it.
/// </para>
/// </summary>
public interface ITaskRepository
{
    /// <summary>Creates or updates a task.</summary>
    Task SaveAsync(TaskItem task, CancellationToken cancellationToken = default);

    /// <summary>Loads a full aggregate, or null if there is no task with that id —
    /// including when the row is there but tombstoned, because a deleted task is
    /// gone as far as every read is concerned.</summary>
    Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every live task, in rank order: hand-ranked order first, then
    /// newest-first for the tasks nobody has ranked. Tombstoned tasks are not
    /// listed.
    /// <para>
    /// Whole aggregates rather than summaries. A derived summary existed while a
    /// JSON index sat in front of markdown files and reading one meant parsing a
    /// document; a store that can return the rows in order makes the projection —
    /// and the second round trip per task that came with it — pure overhead.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default);
}
