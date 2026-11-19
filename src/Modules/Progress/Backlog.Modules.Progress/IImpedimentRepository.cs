using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;

namespace Backlog.Modules.Progress;

/// <summary>
/// Local-first persistence for the <see cref="Impediment"/> aggregate: whole-impediment
/// load and save, because an impediment is one consistency boundary.
/// <para>
/// An internal port, deliberately not registered by <c>AddProgressModule()</c>: which
/// adapter implements it is the host's decision — today one row per impediment in the
/// same SQLite database the tasks live in (<c>progress_impediments</c>).
/// </para>
/// </summary>
public interface IImpedimentRepository
{
    /// <summary>Every impediment, in the order they were raised. Empty, never null, on a
    /// first run.</summary>
    Task<IReadOnlyList<Impediment>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One impediment, or null when none has that id.</summary>
    Task<Impediment?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Writes the whole impediment, atomically from a reader's point of view.</summary>
    Task SaveAsync(Impediment impediment, CancellationToken cancellationToken = default);
}

/// <summary>
/// Where the replicated impediments are kept, read and written as stored text rather than
/// as the aggregate — the arrangement <see cref="IGoalReplicaStore"/> has for a goal.
/// </summary>
/// <remarks>Impediments are read in the order they were raised.</remarks>
public interface IImpedimentReplicaStore : IWholeDocumentReplicaStore<ImpedimentReplicaCopyDto>;
