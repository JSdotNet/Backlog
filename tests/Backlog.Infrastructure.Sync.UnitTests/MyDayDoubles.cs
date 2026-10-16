using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The My Day preferences replication port, holding the copy the test set and
/// answering whatever the test said. The sync client never sees past this port, so
/// what the store does with a copy is not this project's business — only that it was
/// handed over whole, and how the answer is counted.
/// </summary>
internal sealed class RecordingMyDayPreferencesReplication : IMyDayPreferencesReplication
{
    public event Action? Changed;

    /// <summary>What <see cref="ReadAsync"/> answers; null for a device that never set
    /// the switch.</summary>
    public MyDayPreferencesCopyDto? Held { get; set; }

    /// <summary>Every copy offered, in order.</summary>
    public List<MyDayPreferencesCopyDto> Offered { get; } = [];

    /// <summary>What <see cref="ApplyAsync"/> answers. Taken by default.</summary>
    public MyDayPreferencesReplicaOutcome Answer { get; set; } = MyDayPreferencesReplicaOutcome.Taken;

    public bool HasListeners => Changed is not null;

    public void RaiseChanged() => Changed?.Invoke();

    public Task<MyDayPreferencesCopyDto?> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Held);

    public Task<MyDayPreferencesReplicaOutcome> ApplyAsync(
        MyDayPreferencesCopyDto inbound,
        CancellationToken cancellationToken = default)
    {
        Offered.Add(inbound);
        return Task.FromResult(Answer);
    }
}
