using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// The published <see cref="ISprintSeriesReplication"/> port: each sprint series as one
/// whole document, last write wins on each series' own stamp — the rule Roadmap's
/// replication keeps for the plan (local ADR 0018).
/// <para>
/// It decides by stamp and nothing else. A later copy is written through the store,
/// verbatim; an equal one is an echo and an older one is refused, both writing nothing.
/// Whether a copy reads as a series is the store's question, because the store is what
/// knows the shape.
/// </para>
/// <para>
/// <b>Inside the progress gate.</b> Every command loads a series, changes it and saves it
/// while holding <see cref="ProgressGate"/>. A pulled copy written in between would be
/// saved over from the copy the command loaded first, and the next push would carry the
/// loss back to the device the edit came from. So the read, the decision and the write
/// wait for the command to finish, and the stamp is compared with what it saved.
/// </para>
/// <para>
/// <b>Local changes, and the one writer that is not one.</b> <see cref="Changed"/>
/// forwards <see cref="SeriesSaved"/>, so a sync loop hears every save a person makes. A
/// copy taken from the replica raises <see cref="SeriesSaved"/> too, for anything that
/// draws the series, and that one is silenced for this port's listeners alone by an
/// asynchronous-local depth counter — the arrangement Roadmap's replication uses, for
/// the same reason.
/// </para>
/// <para>
/// <b>One repository, one series, after the copy is taken.</b> Two desktops that each
/// set up a series for one repository offline both keep theirs, so a taken copy can
/// leave a repository in two switched-on series. Still inside the gate,
/// <see cref="SeriesAssignment.ResolveClaims"/> switches every series but the one set
/// up first off, with a line saying why, and each is saved here as a change of this
/// device's own — the next push carries it. It reads the series alone, so the other
/// desktop switches off the same series when the copy reaches it
/// (<c>.devbook/domain/progress/domain.invariants.md#invariant-a-synced-repository-keeps-to-the-series-set-up-first</c>).
/// </para>
/// </summary>
internal sealed class SprintSeriesReplication : ISprintSeriesReplication
{
    private readonly ISprintSeriesReplicaStore? _store;
    private readonly ISprintSeriesRepository? _repository;
    private readonly SeriesSaved _saved;
    private readonly ProgressGate _gate;
    private readonly AsyncLocal<int> _applying = new();

    /// <param name="store">Where this host keeps its series, or null on a head that keeps
    /// none — then there is nothing to send, and every copy reads as unreadable.</param>
    /// <param name="saved">The series' save notice.</param>
    /// <param name="gate">The one-writer gate every Progress command holds.</param>
    /// <param name="repository">The series as aggregates, which a taken copy's claims are
    /// resolved over; null leaves them as they arrived.</param>
    public SprintSeriesReplication(
        ISprintSeriesReplicaStore? store,
        SeriesSaved saved,
        ProgressGate gate,
        ISprintSeriesRepository? repository = null)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(gate);

        _store = store;
        _repository = repository;
        _saved = saved;
        _gate = gate;
        _saved.Changed += OnLocalChange;
    }

    public event Action? Changed;

    public async Task<IReadOnlyList<SprintSeriesReplicaCopyDto>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _store is null
            ? []
            : await _store.ReadAllAsync(cancellationToken).ConfigureAwait(false);

    public async Task<SprintSeriesReplicaOutcome> ApplyAsync(
        SprintSeriesReplicaCopyDto inbound,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inbound);

        if (_store is null) return SprintSeriesReplicaOutcome.Unreadable;

        _applying.Value++;
        try
        {
            var outcome = await WriteAsync(_store, inbound, cancellationToken).ConfigureAwait(false);

            // Raised once the gate is let go, so nothing that hears it waits on this pull.
            if (outcome == SprintSeriesReplicaOutcome.Taken) _saved.Raise();

            return outcome;
        }
        finally
        {
            _applying.Value--;
        }
    }

    private async Task<SprintSeriesReplicaOutcome> WriteAsync(
        ISprintSeriesReplicaStore store,
        SprintSeriesReplicaCopyDto inbound,
        CancellationToken cancellationToken)
    {
        using var held = await _gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        var local = await store.ReadAsync(inbound.Id, cancellationToken).ConfigureAwait(false);

        if (local is not null)
        {
            if (inbound.UpdatedAt == local.UpdatedAt) return SprintSeriesReplicaOutcome.Echo;
            if (inbound.UpdatedAt < local.UpdatedAt) return SprintSeriesReplicaOutcome.Refused;
        }

        if (!await store.TryWriteAsync(inbound, cancellationToken).ConfigureAwait(false))
        {
            return SprintSeriesReplicaOutcome.Unreadable;
        }

        await ResolveClaimsAsync(cancellationToken).ConfigureAwait(false);
        return SprintSeriesReplicaOutcome.Taken;
    }

    private async Task ResolveClaimsAsync(CancellationToken cancellationToken)
    {
        if (_repository is null) return;

        var all = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var switchedOff in SeriesAssignment.ResolveClaims(all))
        {
            await _repository.SaveAsync(switchedOff, cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnLocalChange()
    {
        if (_applying.Value > 0) return;

        Changed?.Invoke();
    }
}
