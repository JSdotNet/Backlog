using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.SharedKernel.Replication;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>Progress's goal replication port, holding the goals the test set and answering
/// whatever the test said.</summary>
internal sealed class RecordingGoalReplication : IGoalReplication
{
    public event Action? Changed;

    public List<GoalReplicaCopyDto> Held { get; } = [];

    public List<GoalReplicaCopyDto> Offered { get; } = [];

    public ReplicaOutcome Answer { get; set; } = ReplicaOutcome.Taken;

    public void RaiseChanged() => Changed?.Invoke();

    public Task<IReadOnlyList<GoalReplicaCopyDto>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GoalReplicaCopyDto>>([.. Held]);

    public Task<ReplicaOutcome> ApplyAsync(GoalReplicaCopyDto inbound, CancellationToken cancellationToken = default)
    {
        Offered.Add(inbound);
        return Task.FromResult(Answer);
    }

    public void Hold(GoalReplicaCopyDto copy)
    {
        Held.RemoveAll(held => held.Id == copy.Id);
        Held.Add(copy);
    }
}
