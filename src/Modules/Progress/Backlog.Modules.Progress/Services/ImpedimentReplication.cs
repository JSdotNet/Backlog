using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// The published <see cref="IImpedimentReplication"/> port: each impediment as one whole
/// document, last write wins on each one's own stamp — the rule
/// <see cref="GoalReplication"/> keeps for a goal (local ADR 0018, ADR 0035 §9). It decides
/// by stamp and nothing else; whether a copy reads as an impediment is the store's
/// question. Inside the progress gate, for the reason the goal's replication gives.
/// </summary>
internal sealed class ImpedimentReplication : IImpedimentReplication
{
    private readonly IImpedimentReplicaStore? _store;
    private readonly ImpedimentSaved _saved;
    private readonly ProgressGate _gate;
    private readonly AsyncLocal<int> _applying = new();

    /// <param name="store">Where this host keeps its impediments, or null on a head that
    /// keeps none — then there is nothing to send, and every copy reads as unreadable.</param>
    public ImpedimentReplication(IImpedimentReplicaStore? store, ImpedimentSaved saved, ProgressGate gate)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(gate);

        _store = store;
        _saved = saved;
        _gate = gate;
        _saved.Changed += OnLocalChange;
    }

    public event Action? Changed;

    public async Task<IReadOnlyList<ImpedimentReplicaCopyDto>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _store is null
            ? []
            : await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);

    public async Task<ImpedimentReplicaOutcome> ApplyAsync(ImpedimentReplicaCopyDto inbound, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inbound);

        if (_store is null) return ImpedimentReplicaOutcome.Unreadable;

        _applying.Value++;
        try
        {
            var outcome = await WriteAsync(_store, inbound, cancellationToken).ConfigureAwait(false);

            // Raised once the gate is let go, so nothing that hears it waits on this pull.
            if (outcome == ImpedimentReplicaOutcome.Taken) _saved.Raise();

            return outcome;
        }
        finally
        {
            _applying.Value--;
        }
    }

    private async Task<ImpedimentReplicaOutcome> WriteAsync(
        IImpedimentReplicaStore store,
        ImpedimentReplicaCopyDto inbound,
        CancellationToken cancellationToken)
    {
        using var held = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        var local = await store.ReadAsync(inbound.Id, cancellationToken).ConfigureAwait(false);

        if (local is not null)
        {
            if (inbound.UpdatedAt == local.UpdatedAt) return ImpedimentReplicaOutcome.Echo;
            if (inbound.UpdatedAt < local.UpdatedAt) return ImpedimentReplicaOutcome.Refused;
        }

        return await store.TryWriteAsync(inbound, cancellationToken).ConfigureAwait(false)
            ? ImpedimentReplicaOutcome.Taken
            : ImpedimentReplicaOutcome.Unreadable;
    }

    private void OnLocalChange()
    {
        if (_applying.Value > 0) return;

        Changed?.Invoke();
    }
}
