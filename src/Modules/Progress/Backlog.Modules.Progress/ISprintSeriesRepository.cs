using Backlog.Modules.Progress.DomainModels;

namespace Backlog.Modules.Progress;

/// <summary>
/// Local-first persistence for the <see cref="SprintSeries"/> aggregate: whole-series
/// load and save, because a series is one consistency boundary.
/// <para>
/// An internal port, deliberately not registered by <c>AddProgressModule()</c>: which
/// adapter implements it is the host's decision — today one row per series in the same
/// SQLite database the tasks live in.
/// </para>
/// </summary>
public interface ISprintSeriesRepository
{
    /// <summary>Every series, in the order they were created. Empty, never null, on a
    /// first run.</summary>
    Task<IReadOnlyList<SprintSeries>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One series, or null when none has that id.</summary>
    Task<SprintSeries?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Writes the whole series, atomically from a reader's point of view.</summary>
    Task SaveAsync(SprintSeries series, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the series and every sprint it holds. From then on <see cref="ListAsync"/>
    /// and <see cref="GetAsync"/> answer as though it never existed, so its repositories
    /// are in no series. An adapter whose series travel between devices keeps a
    /// tombstone under the id, stamped like a save, so the deletion travels as the
    /// series' last change (<c>.devbook/domain/progress/domain.md#sprint-series</c>).
    /// Deleting an id it does not hold does nothing.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
