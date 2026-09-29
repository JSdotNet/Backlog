using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// Roadmap's replication port, holding one copy of each document and answering
/// whatever the test set. The sync client never sees past this port (local ADR
/// 0018), so what the roadmap does with a copy is not this project's business —
/// only that it was handed over whole, and how the answer is counted.
/// </summary>
internal sealed class RecordingRoadmapReplication : IRoadmapReplication
{
    public event Action? Changed;

    /// <summary>What <see cref="ReadAsync"/> answers, per document. Absent is a
    /// document this device never saved.</summary>
    public Dictionary<RoadmapReplicaDocument, RoadmapReplicaCopyDto> Held { get; } = [];

    /// <summary>Every copy offered, in order.</summary>
    public List<(RoadmapReplicaDocument Document, RoadmapReplicaCopyDto Copy)> Offered { get; } = [];

    /// <summary>What <see cref="ApplyAsync"/> answers. Taken by default.</summary>
    public RoadmapReplicaOutcome Answer { get; set; } = RoadmapReplicaOutcome.Taken;

    public void RaiseChanged() => Changed?.Invoke();

    public Task<RoadmapReplicaCopyDto?> ReadAsync(
        RoadmapReplicaDocument document,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Held.TryGetValue(document, out var copy) ? copy : null);

    public Task<RoadmapReplicaOutcome> ApplyAsync(
        RoadmapReplicaDocument document,
        RoadmapReplicaCopyDto inbound,
        CancellationToken cancellationToken = default)
    {
        Offered.Add((document, inbound));
        return Task.FromResult(Answer);
    }
}

/// <summary>Roadmap documents the way the desktop writes them onto the feed.</summary>
internal static class RoadmapReplicaChanges
{
    public static TaskChange Plan(string content, DateTimeOffset updatedAt) =>
        RoadmapReplicaDocuments.ToChange(RoadmapReplicaDocument.Plan, content, updatedAt);

    public static TaskChange Pace(string content, DateTimeOffset updatedAt) =>
        RoadmapReplicaDocuments.ToChange(RoadmapReplicaDocument.Pace, content, updatedAt);
}
