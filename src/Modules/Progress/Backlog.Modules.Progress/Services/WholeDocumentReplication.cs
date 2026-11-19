using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.SharedKernel.Replication;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// The published <see cref="IGoalReplication"/> port: each goal as one whole document, last
/// write wins on each goal's own stamp (local ADR 0018, ADR 0035 §9).
/// <para>
/// <b>Two goals may hold one item after a pull.</b> Two desktops that each attached the
/// same task to a different goal offline both keep theirs. The pull leaves both as they
/// arrived; the counting reads the item in both until a person detaches it from one. No
/// rule here picks a winner, because the requirement names none.
/// </para>
/// </summary>
internal sealed class GoalReplication(IGoalReplicaStore? store, GoalSaved saved, ProgressGate gate)
    : WholeDocumentReplication<GoalReplicaCopyDto>(store, saved, gate), IGoalReplication;

/// <summary>The published <see cref="IImpedimentReplication"/> port: each impediment as one
/// whole document, on the terms <see cref="GoalReplication"/> keeps for a goal.</summary>
internal sealed class ImpedimentReplication(IImpedimentReplicaStore? store, ImpedimentSaved saved, ProgressGate gate)
    : WholeDocumentReplication<ImpedimentReplicaCopyDto>(store, saved, gate), IImpedimentReplication;

/// <summary>
/// One Progress document kind replicated whole, last write wins on each document's own
/// stamp (local ADR 0018, ADR 0035 §9) — the goals' and the impediments' alike.
/// <para>
/// It decides by stamp and nothing else. A later copy is written through the store,
/// verbatim; an equal one is an echo and an older one is refused, both writing nothing.
/// Whether a copy reads as the document is the store's question.
/// </para>
/// <para>
/// <b>Inside the progress gate</b>: a command loads every document of its kind, changes
/// some and saves them while holding <see cref="ProgressGate"/>, and a copy written in
/// between would be saved over.
/// </para>
/// </summary>
/// <typeparam name="TCopy">One copy of the document as it travels.</typeparam>
internal abstract class WholeDocumentReplication<TCopy> : IWholeDocumentReplication<TCopy>
    where TCopy : class, IWholeDocumentCopy
{
    private readonly IWholeDocumentReplicaStore<TCopy>? _store;
    private readonly DocumentSaved _saved;
    private readonly ProgressGate _gate;
    private readonly AsyncLocal<int> _applying = new();

    /// <param name="store">Where this host keeps the documents, or null on a head that keeps
    /// none — then there is nothing to send, and every copy reads as unreadable.</param>
    /// <param name="saved">The notice every save of this kind raises.</param>
    /// <param name="gate">The gate every Progress command holds while it writes.</param>
    protected WholeDocumentReplication(IWholeDocumentReplicaStore<TCopy>? store, DocumentSaved saved, ProgressGate gate)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(gate);

        _store = store;
        _saved = saved;
        _gate = gate;
        _saved.Changed += OnLocalChange;
    }

    public event Action? Changed;

    public async Task<IReadOnlyList<TCopy>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _store is null
            ? []
            : await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);

    public async Task<ReplicaOutcome> ApplyAsync(TCopy inbound, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inbound);

        if (_store is null) return ReplicaOutcome.Unreadable;

        _applying.Value++;
        try
        {
            var outcome = await WriteAsync(_store, inbound, cancellationToken).ConfigureAwait(false);

            // Raised once the gate is let go, so nothing that hears it waits on this pull.
            if (outcome == ReplicaOutcome.Taken) _saved.Raise();

            return outcome;
        }
        finally
        {
            _applying.Value--;
        }
    }

    private async Task<ReplicaOutcome> WriteAsync(
        IWholeDocumentReplicaStore<TCopy> store,
        TCopy inbound,
        CancellationToken cancellationToken)
    {
        using var held = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        var local = await store.ReadAsync(inbound.Id, cancellationToken).ConfigureAwait(false);

        if (LastWriteWins.Weigh(inbound.UpdatedAt, local?.UpdatedAt) is { } settled) return settled;

        return await store.TryWriteAsync(inbound, cancellationToken).ConfigureAwait(false)
            ? ReplicaOutcome.Taken
            : ReplicaOutcome.Unreadable;
    }

    private void OnLocalChange()
    {
        if (_applying.Value > 0) return;

        Changed?.Invoke();
    }
}
