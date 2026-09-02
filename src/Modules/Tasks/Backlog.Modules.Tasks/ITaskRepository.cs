using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Modules.Tasks;

/// <summary>
/// Local-first persistence for <see cref="TaskItem"/> aggregates. One local
/// store holds them whole; the task's content is markdown text inside the
/// aggregate rather than a document the store has to parse.
/// </summary>
public interface ITaskRepository
{
    /// <summary>Creates or updates a task.</summary>
    Task SaveAsync(TaskItem task, CancellationToken cancellationToken = default);

    /// <summary>Loads a full aggregate, or null if there is no task with that id.</summary>
    Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Deletes a task.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every persisted task, in rank order: hand-ranked order first, then
    /// newest-first for the tasks nobody has ranked.
    /// <para>
    /// Whole aggregates rather than summaries. A derived summary existed while a
    /// JSON index sat in front of markdown files and reading one meant parsing a
    /// document; a store that can return the rows in order makes the projection —
    /// and the second round trip per task that came with it — pure overhead.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default);
}
