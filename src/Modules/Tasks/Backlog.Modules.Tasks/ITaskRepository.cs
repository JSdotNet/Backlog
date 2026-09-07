using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Modules.Tasks;

/// <summary>
/// Local-first persistence for <see cref="TaskItem"/> aggregates. One local
/// store holds them whole; the task's content is markdown text inside the
/// aggregate rather than a document the store has to parse.
/// <para>
/// There is no delete member. Deleting a task is tombstoning it — see
/// <see cref="TaskItem.MarkDeleted"/> — which is an ordinary
/// <see cref="SaveAsync"/> of the aggregate. The ordinary reads below hide what
/// it marks; the two sync reads — <see cref="GetIncludingDeletedAsync"/> and
/// <see cref="ListChangedSinceAsync"/> — are the deliberate exceptions, because
/// a deletion has to travel to the person's other machine and a row that is
/// simply gone cannot. A port member that removed the row outright would
/// re-create exactly that, and it would need a tombstone retention that nothing
/// has chosen yet; the reaper arrives with the sync service that decides it.
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
    /// The one read that sees a tombstone: the same aggregate as
    /// <see cref="GetAsync"/>, and the row it hides as well.
    /// <para>
    /// Reconciling this machine's copy of a task with another machine's is the
    /// one caller that has to tell "deleted here" from "never seen here".
    /// <see cref="GetAsync"/> answers null to both by design, so a merge asking
    /// through it would read a local deletion as an absence, take the stale live
    /// document the other device still had, and resurrect a task the person
    /// deleted — the exact failure
    /// <c>.arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md</c>
    /// exists to remove.
    /// </para>
    /// <para>
    /// Deliberately not the read anything else uses. A screen that called this
    /// would have to decide what to do with a tombstone, and there is exactly one
    /// right answer to that — hide it — which <see cref="GetAsync"/> already
    /// gives without asking.
    /// </para>
    /// </summary>
    Task<TaskItem?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Every task whose <see cref="TaskItem.UpdatedAt"/> is strictly later than
    /// <paramref name="since"/>, oldest first — <b>tombstones included</b>. The
    /// sync read, and the only list that sees one.
    /// <para>
    /// <see cref="ListAsync"/> hides a tombstone because every screen wants it
    /// hidden, and replication is the one caller that must not have it hidden: a
    /// deletion that does not travel is a deletion the person's other machine
    /// never learns about, so that machine keeps the task and hands it back the
    /// next time the two reconcile. The tombstone is the only form a deletion can
    /// take on the wire — a row that is simply gone says nothing at all.
    /// </para>
    /// <para>
    /// Ascending by <see cref="TaskItem.UpdatedAt"/> because the caller advances a
    /// watermark as it goes: sending in stamp order means the highest stamp
    /// accepted so far is the last one sent, so a run that stops half way leaves a
    /// watermark that is true rather than one that has stepped over unsent work.
    /// </para>
    /// <para>
    /// Strictly later, not "at or later". A task whose stamp equals the watermark
    /// is one the last run had accepted, and including it would make every push
    /// re-send its own final item for the life of the device.
    /// </para>
    /// </summary>
    /// <param name="since">The exclusive lower bound — the caller's watermark.</param>
    Task<IReadOnlyList<TaskItem>> ListChangedSinceAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default);
}
