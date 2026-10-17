using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// The published <see cref="IGoalReplication"/> port: each goal as one whole document,
/// last write wins on each goal's own stamp — the rule <see cref="SprintSeriesReplication"/>
/// keeps for a series (local ADR 0018, ADR 0035 §9).
/// <para>
/// It decides by stamp and nothing else. A later copy is written through the store,
/// verbatim; an equal one is an echo and an older one is refused, both writing nothing.
/// Whether a copy reads as a goal is the store's question.
/// </para>
/// <para>
/// <b>Inside the progress gate</b>, for the reason the series' replication gives: a goal
/// command loads every goal, changes some and saves them while holding
/// <see cref="ProgressGate"/>, and a copy written in between would be saved over.
/// </para>
/// <para>
/// <b>Two goals may hold one item after a pull.</b> Two desktops that each attached the
/// same task to a different goal offline both keep theirs. The pull leaves both as they
/// arrived; the counting reads the item in both until a person detaches it from one. No
/// rule here picks a winner, because the requirement names none.
/// </para>
/// </summary>
internal sealed class GoalReplication : IGoalReplication
{
    private readonly IGoalReplicaStore? _store;
    private readonly GoalSaved _saved;
    private readonly ProgressGate _gate;
    private readonly AsyncLocal<int> _applying = new();

    /// <param name="store">Where this host keeps its goals, or null on a head that keeps
    /// none — then there is nothing to send, and every copy reads as unreadable.</param>
    public GoalReplication(IGoalReplicaStore? store, GoalSaved saved, ProgressGate gate)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(gate);

        _store = store;
        _saved = saved;
        _gate = gate;
        _saved.Changed += OnLocalChange;
    }

    public event Action? Changed;

    public async Task<IReadOnlyList<GoalReplicaCopyDto>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _store is null
            ? []
            : await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);

    public async Task<GoalReplicaOutcome> ApplyAsync(GoalReplicaCopyDto inbound, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inbound);

        if (_store is null) return GoalReplicaOutcome.Unreadable;

        _applying.Value++;
        try
        {
            var outcome = await WriteAsync(_store, inbound, cancellationToken).ConfigureAwait(false);

            // Raised once the gate is let go, so nothing that hears it waits on this pull.
            if (outcome == GoalReplicaOutcome.Taken) _saved.Raise();

            return outcome;
        }
        finally
        {
            _applying.Value--;
        }
    }

    private async Task<GoalReplicaOutcome> WriteAsync(
        IGoalReplicaStore store,
        GoalReplicaCopyDto inbound,
        CancellationToken cancellationToken)
    {
        using var held = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        var local = await store.ReadAsync(inbound.Id, cancellationToken).ConfigureAwait(false);

        if (local is not null)
        {
            if (inbound.UpdatedAt == local.UpdatedAt) return GoalReplicaOutcome.Echo;
            if (inbound.UpdatedAt < local.UpdatedAt) return GoalReplicaOutcome.Refused;
        }

        return await store.TryWriteAsync(inbound, cancellationToken).ConfigureAwait(false)
            ? GoalReplicaOutcome.Taken
            : GoalReplicaOutcome.Unreadable;
    }

    private void OnLocalChange()
    {
        if (_applying.Value > 0) return;

        Changed?.Invoke();
    }
}
