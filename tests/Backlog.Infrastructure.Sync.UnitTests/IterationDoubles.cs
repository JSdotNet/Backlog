using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The iteration settings replication port, holding the copy the test set and answering
/// whatever the test said. The sync client never sees past this port, so what the store
/// does with a copy is not this project's business — only that it was handed over whole,
/// and how the answer is counted.
/// </summary>
internal sealed class RecordingIterationSettingsReplication : IIterationSettingsReplication
{
    public event Action? Changed;

    /// <summary>What <see cref="ReadAsync"/> answers; null for a device that never chose
    /// an iteration.</summary>
    public IterationSettingsCopyDto? Held { get; set; }

    /// <summary>Every copy offered, in order.</summary>
    public List<IterationSettingsCopyDto> Offered { get; } = [];

    /// <summary>What <see cref="ApplyAsync"/> answers. Taken by default.</summary>
    public IterationSettingsReplicaOutcome Answer { get; set; } = IterationSettingsReplicaOutcome.Taken;

    public bool HasListeners => Changed is not null;

    public void RaiseChanged() => Changed?.Invoke();

    public Task<IterationSettingsCopyDto?> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Held);

    public Task<IterationSettingsReplicaOutcome> ApplyAsync(
        IterationSettingsCopyDto inbound,
        CancellationToken cancellationToken = default)
    {
        Offered.Add(inbound);
        return Task.FromResult(Answer);
    }
}
