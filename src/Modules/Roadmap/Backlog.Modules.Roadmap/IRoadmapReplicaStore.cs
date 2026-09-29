using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Modules.Roadmap;

/// <summary>
/// Where one replicated roadmap document is kept, read and written as stored text
/// rather than as the aggregate (local ADR 0018).
/// <para>
/// Text, because a copy from another device is stored verbatim and never
/// re-serialised: a document a newer build wrote keeps the fields this build does
/// not know. The store is the one place that knows the document's shape, so it is
/// also the one that says whether a copy reads as that document before anything
/// is written.
/// </para>
/// <para>
/// An internal port like <see cref="IRoadmapPlanRepository"/>, answered by the
/// adapter that already holds the document — the plan's row, the pace's file — and
/// registered by the host.
/// </para>
/// </summary>
public interface IRoadmapReplicaStore
{
    /// <summary>Which document this store holds.</summary>
    RoadmapReplicaDocument Document { get; }

    /// <summary>The stored document and its stamp, or <c>null</c> when nothing was
    /// ever saved.</summary>
    Task<RoadmapReplicaCopyDto?> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the stored document with <paramref name="copy"/>'s text,
    /// verbatim, at <paramref name="copy"/>'s stamp. Answers <c>false</c> and writes
    /// nothing when the text does not read as this document.</summary>
    Task<bool> TryWriteAsync(RoadmapReplicaCopyDto copy, CancellationToken cancellationToken = default);
}
