using Backlog.Modules.Progress.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Progress;

/// <summary>
/// Where the replicated sprint series are kept, read and written as stored text rather
/// than as the aggregate — the arrangement Roadmap's <c>IRoadmapReplicaStore</c> has
/// for the plan (local ADR 0018).
/// <para>
/// Text, because a copy from another device is stored verbatim and never re-serialised:
/// a series a newer build wrote keeps the fields this build does not know. The store is
/// the one place that knows the document's shape, so it is also the one that says
/// whether a copy reads as a series before anything is written.
/// </para>
/// <para>
/// An internal port like <see cref="ISprintSeriesRepository"/>, answered by the adapter
/// that already holds the series — one row per series — and registered by the host.
/// </para>
/// </summary>
public interface ISprintSeriesReplicaStore
{
    /// <summary>Every stored series and its stamp, in the order they were created.
    /// Empty when none was ever saved.</summary>
    Task<IReadOnlyList<SprintSeriesReplicaCopyDto>> ReadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>One stored series and its stamp, or <c>null</c> when this device holds
    /// none with that id.</summary>
    Task<SprintSeriesReplicaCopyDto?> ReadAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Replaces the stored series with <paramref name="copy"/>'s text, verbatim,
    /// at <paramref name="copy"/>'s stamp — or adds it when this device has none. Answers
    /// <c>false</c> and writes nothing when the text does not read as a series, or names
    /// another series than <see cref="SprintSeriesReplicaCopyDto.Id"/>.</summary>
    Task<bool> TryWriteAsync(SprintSeriesReplicaCopyDto copy, CancellationToken cancellationToken = default);
}
