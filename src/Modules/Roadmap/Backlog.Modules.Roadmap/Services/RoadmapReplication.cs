using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Modules.Roadmap.Services;

/// <summary>
/// The published <see cref="IRoadmapReplication"/> port: the plan and the pace as
/// two whole documents, last write wins on each one's own stamp (local ADR 0018).
/// <para>
/// It decides by stamp and nothing else. A later copy is written through the store
/// that holds the document, verbatim; an equal one is an echo and an older one is
/// refused, both writing nothing — the same rule the task merge keeps, and the one
/// the replica applies to a push. Whether a copy reads as the document it claims to
/// be is the store's question, because the store is what knows the shape.
/// </para>
/// <para>
/// <b>Local changes, and the one writer that is not one.</b> <see cref="Changed"/>
/// forwards the plan's own change notice and the pace file's, so a sync loop hears
/// every save a person makes. A copy taken from the replica raises both too — the
/// plan's so the roadmap redraws, the pace file's because the file raises it on
/// every write — and those are silenced for this port's listeners alone, by an
/// asynchronous-local depth counter: the arrangement <c>TaskChangeSignal</c> uses,
/// for the same reason. A person saving on the UI thread while a pull applies is
/// still heard.
/// </para>
/// <para>
/// A singleton over singletons: the stores, the plan's change notice and the pace
/// settings all live as long as the host, so the subscriptions below are never
/// what keeps something alive past its time.
/// </para>
/// </summary>
internal sealed class RoadmapReplication : IRoadmapReplication
{
    private readonly Dictionary<RoadmapReplicaDocument, IRoadmapReplicaStore> _stores;
    private readonly RoadmapPlanChanges _planChanges;
    private readonly AsyncLocal<int> _applying = new();

    /// <param name="stores">Whichever documents this host keeps. One missing is a
    /// head that keeps no such document, and a copy of it reads as
    /// unreadable.</param>
    /// <param name="planChanges">The plan's change notice — what the roadmap redraws
    /// on, and what a local save raises.</param>
    /// <param name="pace">The pace settings, for their change notice only. Optional:
    /// a host without a pace file has no pace change to hear.</param>
    public RoadmapReplication(
        IEnumerable<IRoadmapReplicaStore> stores,
        RoadmapPlanChanges planChanges,
        IPlanningVelocitySettings? pace = null)
    {
        ArgumentNullException.ThrowIfNull(stores);
        ArgumentNullException.ThrowIfNull(planChanges);

        _stores = [];
        foreach (var store in stores)
        {
            _stores.TryAdd(store.Document, store);
        }

        _planChanges = planChanges;
        _planChanges.Changed += OnLocalChange;
        if (pace is not null) pace.Changed += OnLocalChange;
    }

    public event Action? Changed;

    public async Task<RoadmapReplicaCopyDto?> ReadAsync(
        RoadmapReplicaDocument document,
        CancellationToken cancellationToken = default) =>
        _stores.TryGetValue(document, out var store)
            ? await store.ReadAsync(cancellationToken).ConfigureAwait(false)
            : null;

    public async Task<RoadmapReplicaOutcome> ApplyAsync(
        RoadmapReplicaDocument document,
        RoadmapReplicaCopyDto inbound,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inbound);

        if (!_stores.TryGetValue(document, out var store)) return RoadmapReplicaOutcome.Unreadable;

        var local = await store.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (local is not null)
        {
            if (inbound.UpdatedAt == local.UpdatedAt) return RoadmapReplicaOutcome.Echo;
            if (inbound.UpdatedAt < local.UpdatedAt) return RoadmapReplicaOutcome.Refused;
        }

        _applying.Value++;
        try
        {
            if (!await store.TryWriteAsync(inbound, cancellationToken).ConfigureAwait(false))
            {
                return RoadmapReplicaOutcome.Unreadable;
            }

            // The roadmap redraws on the plan's own notice. Raised for the pace too:
            // bar lengths are divided by it, and the band reads it on every load.
            _planChanges.Raise();
        }
        finally
        {
            _applying.Value--;
        }

        return RoadmapReplicaOutcome.Taken;
    }

    private void OnLocalChange()
    {
        if (_applying.Value > 0) return;

        Changed?.Invoke();
    }
}
