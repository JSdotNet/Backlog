using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// Progress's replication port, holding the series the test set and answering whatever
/// the test said. The sync client never sees past this port, so what Progress does with
/// a copy is not this project's business — only that it was handed over whole, and how
/// the answer is counted.
/// </summary>
internal sealed class RecordingSprintSeriesReplication : ISprintSeriesReplication
{
    public event Action? Changed;

    /// <summary>What <see cref="ReadAllAsync"/> answers, in order.</summary>
    public List<SprintSeriesReplicaCopyDto> Held { get; } = [];

    /// <summary>Every copy offered, in order.</summary>
    public List<SprintSeriesReplicaCopyDto> Offered { get; } = [];

    /// <summary>What <see cref="ApplyAsync"/> answers. Taken by default.</summary>
    public SprintSeriesReplicaOutcome Answer { get; set; } = SprintSeriesReplicaOutcome.Taken;

    public void RaiseChanged() => Changed?.Invoke();

    public Task<IReadOnlyList<SprintSeriesReplicaCopyDto>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SprintSeriesReplicaCopyDto>>([.. Held]);

    public Task<SprintSeriesReplicaOutcome> ApplyAsync(
        SprintSeriesReplicaCopyDto inbound,
        CancellationToken cancellationToken = default)
    {
        Offered.Add(inbound);
        return Task.FromResult(Answer);
    }

    /// <summary>Replaces a held series, as a person's edit on this device would.</summary>
    public void Hold(SprintSeriesReplicaCopyDto copy)
    {
        Held.RemoveAll(held => held.Id == copy.Id);
        Held.Add(copy);
    }
}
