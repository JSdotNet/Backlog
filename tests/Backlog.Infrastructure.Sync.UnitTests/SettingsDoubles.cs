using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Replication;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>The iteration settings replication port, on <see cref="RecordingSettingsReplication{TCopy}"/>'s terms.</summary>
internal sealed class RecordingIterationSettingsReplication
    : RecordingSettingsReplication<IterationSettingsCopyDto>, IIterationSettingsReplication;

/// <summary>The My Day preferences replication port, on <see cref="RecordingSettingsReplication{TCopy}"/>'s terms.</summary>
internal sealed class RecordingMyDayPreferencesReplication
    : RecordingSettingsReplication<MyDayPreferencesCopyDto>, IMyDayPreferencesReplication;

/// <summary>
/// A single settings document's replication port, holding the copy the test set and
/// answering whatever the test said. The sync client never sees past this port, so what
/// the store does with a copy is not this project's business — only that it was handed
/// over whole, and how the answer is counted.
/// </summary>
/// <typeparam name="TCopy">The document's copy as it travels.</typeparam>
internal abstract class RecordingSettingsReplication<TCopy>
    where TCopy : class
{
    public event Action? Changed;

    /// <summary>What <see cref="ReadAsync"/> answers; null for a device that never set
    /// the document.</summary>
    public TCopy? Held { get; set; }

    /// <summary>Every copy offered, in order.</summary>
    public List<TCopy> Offered { get; } = [];

    /// <summary>What <see cref="ApplyAsync"/> answers. Taken by default.</summary>
    public ReplicaOutcome Answer { get; set; } = ReplicaOutcome.Taken;

    public bool HasListeners => Changed is not null;

    public void RaiseChanged() => Changed?.Invoke();

    public Task<TCopy?> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Held);

    public Task<ReplicaOutcome> ApplyAsync(TCopy inbound, CancellationToken cancellationToken = default)
    {
        Offered.Add(inbound);
        return Task.FromResult(Answer);
    }
}
