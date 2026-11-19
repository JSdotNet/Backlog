using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// The Brain notes of a host with no Brain: no Brain to read, answered as null. Registered unless the Brain's
/// adapters replace it, so every host resolves <see cref="IRoadmapBrainNotes"/> and an
/// item's <c>#brain:</c> refs read as unresolved rather than failing the gathering
/// (local ADR 0053 §3).
/// </summary>
public sealed class NoRoadmapBrainNotes : IRoadmapBrainNotes
{
    public Task<IReadOnlyList<RoadmapBrainNote>?> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RoadmapBrainNote>?>(null);
}
