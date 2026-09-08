using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.Sync.Sessions;

/// <summary>
/// The sessions other environments have reported, answered as a second
/// implementation of <see cref="IAgentSessionSource"/>.
/// <para>
/// This is the widening <c>LocalAgentSessionSource</c>'s own doc comment
/// anticipates in as many words — "sessions from another environment arrive when
/// that environment reports them, and this source is deliberately not the thing
/// that would have to be widened for that: it answers a port, and a second
/// implementation of that port can answer for a fleet". It is that second
/// implementation, and the local reader did not change to make room for it.
/// </para>
/// <para>
/// <strong>It lives here rather than beside the local readers, and the boundary
/// tests are why.</strong> Producing an <see cref="AgentSession"/> from a
/// <c>SessionRecordEntry</c> needs both contexts' published surfaces, and
/// <c>ModuleBoundaryTests.A_module_ui_asks_only_its_own_modules_published_surface</c>
/// forbids <c>Backlog.Modules.Sessions.UI</c> referencing
/// <c>Backlog.Modules.Sync.Abstractions</c>. That rule's own remedy is the one
/// taken here: push the join down into an infrastructure adapter, which may see
/// both contexts, and leave each screen asking only its own module.
/// </para>
/// <para>
/// It reads a store and never the network. The pull loop is
/// <see cref="SessionSyncSession"/>, on a timer; this answers from what that loop
/// last wrote, so opening the pane costs a file read and works on a laptop with
/// no signal. Every reading re-reads, which is what makes the pane's refresh
/// button mean something.
/// </para>
/// </summary>
public sealed class ReplicatedAgentSessionSource : IAgentSessionSource
{
    /// <summary>What the surface is told could not be read when the feature is on
    /// and the cache is unreadable. Not used today — an unreadable cache reads as
    /// empty and the next pull refills what the cursor has not yet passed — and
    /// named here so that the day it is, it is one word rather than one per
    /// caller.</summary>
    internal const string SourceName = "Replicated sessions";

    private readonly IReplicatedSessionStore _store;
    private readonly IAppFeatureSettings _features;
    private readonly TimeProvider _time;

    public ReplicatedAgentSessionSource(
        IReplicatedSessionStore store,
        IAppFeatureSettings features,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(time);

        _store = store;
        _features = features;
        _time = time;
    }

    /// <summary>
    /// Every record this device has been told about, as sessions.
    /// <para>
    /// <strong>The flag is read here and not only by the loop.</strong> Switching
    /// session sync off has to take the other machines' rows off the screen, not
    /// merely stop new ones arriving — a feature that kept showing what it
    /// gathered while it was on would be a switch that does not switch anything a
    /// person can see. The cache is left on disk rather than deleted, so switching
    /// it back on shows what was already there instead of waiting for a cycle.
    /// </para>
    /// <para>
    /// Synchronous work behind an async signature, and deliberately not wrapped in
    /// <see cref="Task.Run"/>: the store holds its records in memory and the port
    /// is async because the local reader has files to open. Nothing is gained by
    /// moving a dictionary walk to the thread pool.
    /// </para>
    /// </summary>
    public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        if (!_features.IsEnabled(SyncFeatures.SessionSync))
        {
            return Task.FromResult(AgentSessionCatalog.Empty);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var held = _store.Current;
        var now = _time.GetUtcNow();

        var sessions = held.Entries
            .Select(entry => SessionRecordMapping.ToSession(entry, now))
            .ToList();

        return Task.FromResult(new AgentSessionCatalog(
            sessions,
            // Nothing is named unreadable. A feed this device could not read is a
            // failed cycle the worker reports, not a source the pane should
            // describe as broken — the records it already holds are as readable as
            // they ever were.
            [],
            // What the cap discarded is added back in, because Discovered means
            // "how many there were before the cap" and a store that under-reported
            // it would let a truncated list render as the whole history.
            sessions.Count + held.Dropped));
    }
}
