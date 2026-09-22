using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.Sync.Sessions;

/// <summary>
/// What other environments' agents were doing, answered as a second
/// implementation of <see cref="IAgentActivitySource"/>.
/// <para>
/// <see cref="ReplicatedAgentSessionSource"/>'s twin, for the second of the two
/// ports the Sessions context publishes. That one answers the cheap question —
/// which sessions there were — from the records the pull loop wrote; this
/// answers the expensive one — when an agent was producing — from the same
/// records, because the intervals travel on them. It is the widening
/// <c>LocalAgentActivitySource</c> was shaped for: that source answers a port,
/// and a second implementation of the port answers for a fleet without the local
/// reader changing to make room.
/// </para>
/// <para>
/// <strong>Cheap here, unlike its local counterpart.</strong> The local source
/// earns its separate port by parsing hundreds of megabytes of transcript; this
/// one walks a list already in memory. The port is still the right one to answer,
/// because the Dashboard asks through it and asks for a horizon, and the
/// intervals on the wire were folded by the machine that could afford to — which
/// is the whole reason they travel.
/// </para>
/// <para>
/// It lives here rather than beside the local readers for the reason its twin
/// does: producing an <see cref="AgentSessionActivity"/> from a
/// <c>SessionRecordEntry</c> needs both contexts' published surfaces, and
/// <c>ModuleBoundaryTests</c> forbids <c>Backlog.Modules.Sessions.UI</c>
/// referencing <c>Backlog.Modules.Sync.Abstractions</c>. The join is pushed down
/// into an infrastructure adapter that may see both.
/// </para>
/// </summary>
public sealed class ReplicatedAgentActivitySource : IAgentActivitySource
{
    private readonly IReplicatedSessionStore _store;
    private readonly IAppFeatureSettings _features;

    /// <summary>No <see cref="TimeProvider"/>, for the reason the local source has
    /// none: this reads recorded instants against a horizon the caller states, and
    /// never asks what time it is.</summary>
    public ReplicatedAgentActivitySource(IReplicatedSessionStore store, IAppFeatureSettings features)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(features);

        _store = store;
        _features = features;
    }

    /// <summary>
    /// Every record this device has been told about that carries activity inside
    /// the horizon, as activity records.
    /// <para>
    /// <strong>The flag is read here and not only by the loop</strong>, exactly as
    /// the session twin reads it: switching sync off has to take the other
    /// machines' figures off the Dashboard, not merely stop new ones arriving. The
    /// cache is left on disk, so switching it back on shows what was already there.
    /// </para>
    /// <para>
    /// <see cref="AgentActivityLog.Unreadable"/> is empty — a feed this device
    /// could not read is a failed cycle the worker reports, not a source the
    /// surface should describe as broken. <see cref="AgentActivityLog.Subagents"/>
    /// is empty because sub-agent activity does not travel: a concurrency figure
    /// stays a figure about this machine, and an empty list here is that fact
    /// rather than a gap. <see cref="AgentActivityLog.IdleAfter"/> is
    /// <see cref="TimeSpan.Zero"/>, which means "no opinion" and not "zero
    /// minutes": the fold that produced these intervals ran on the pushing machine
    /// with that machine's threshold, which is assumed to be the same one this
    /// build folds with rather than carried as a thirteenth field. The composite
    /// that merges this source with the local one skips a zero and names the local
    /// source's number instead.
    /// </para>
    /// <para>
    /// Synchronous work behind an async signature, and deliberately not wrapped in
    /// <see cref="Task.Run"/>: the store holds its records in memory, and the port
    /// is async because the local reader has files to open.
    /// </para>
    /// </summary>
    public Task<AgentActivityLog> GetActivityAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        if (!_features.IsEnabled(SyncFeatures.Sync))
        {
            return Task.FromResult(new AgentActivityLog([], [], since, TimeSpan.Zero));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var sessions = _store.Current.Entries
            .Select(entry => SessionRecordMapping.ToActivity(entry, since))
            .OfType<AgentSessionActivity>()
            .ToList();

        return Task.FromResult(new AgentActivityLog(sessions, [], since, TimeSpan.Zero));
    }
}
