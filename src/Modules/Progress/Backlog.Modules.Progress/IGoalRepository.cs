using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;

namespace Backlog.Modules.Progress;

/// <summary>
/// Local-first persistence for the <see cref="Goal"/> aggregate: whole-goal load and save,
/// because a goal is one consistency boundary.
/// <para>
/// An internal port, deliberately not registered by <c>AddProgressModule()</c>: which
/// adapter implements it is the host's decision — today one row per goal in the same
/// SQLite database the tasks live in (<c>progress_goals</c>).
/// </para>
/// </summary>
public interface IGoalRepository
{
    /// <summary>Every goal, in the person's order (<see cref="Abstractions.GoalOrder"/>): the order
    /// they were set in until one is moved. Empty, never null, on a first run.</summary>
    Task<IReadOnlyList<Goal>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One goal, or null when none has that id.</summary>
    Task<Goal?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Writes the whole goal, atomically from a reader's point of view.</summary>
    Task SaveAsync(Goal goal, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the goal and its attachments. From then on <see cref="ListAsync"/> and
    /// <see cref="GetAsync"/> answer as though it never existed. An adapter whose goals
    /// travel between devices keeps a tombstone under the id, stamped like a save, so the
    /// deletion travels as the goal's last change. Deleting an id it does not hold does
    /// nothing.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Where the replicated goals are kept, read and written as stored text rather than as
/// the aggregate.
/// A copy from another device is stored verbatim, and the store is the one place that
/// knows the document's shape, so it also says whether a copy reads as a goal before
/// anything is written. Answered by the adapter that holds the goals, registered by the host.
/// </summary>
/// <remarks>Goals are read in the order they were set, tombstones included.</remarks>
public interface IGoalReplicaStore : IWholeDocumentReplicaStore<GoalReplicaCopyDto>;

/// <summary>
/// Where one kind of replicated Progress document is kept, read and written as stored
/// text rather than as the aggregate. <see cref="IGoalReplicaStore"/> and
/// <see cref="IImpedimentReplicaStore"/> are this store for their document.
/// </summary>
/// <typeparam name="TCopy">One copy of the document as it travels.</typeparam>
public interface IWholeDocumentReplicaStore<TCopy>
    where TCopy : class, IWholeDocumentCopy
{
    /// <summary>Every stored document and its stamp. Empty when none was ever saved.</summary>
    Task<IReadOnlyList<TCopy>> ReadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>One stored document and its stamp, or null when this device holds none
    /// with that id.</summary>
    Task<TCopy?> ReadAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Replaces the stored document with <paramref name="copy"/>'s text,
    /// verbatim, at <paramref name="copy"/>'s stamp — or adds it. Answers false and writes
    /// nothing when the text does not read as the document, or names another one than
    /// <see cref="IWholeDocumentCopy.Id"/>.</summary>
    Task<bool> TryWriteAsync(TCopy copy, CancellationToken cancellationToken = default);
}
