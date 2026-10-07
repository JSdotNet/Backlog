namespace Backlog.Modules.Roadmap.Services;

/// <summary>
/// One writer of the plan at a time, from its load to its save.
/// <para>
/// The plan is loaded whole and saved whole (<see cref="IRoadmapPlanRepository"/>), so two
/// writers that overlap lose the first one's edit: the second saves the plan it loaded
/// before the first saved. A writer that awaits between the two — the keep-up writer and
/// the importer both gather the backlog and read the paces — leaves that window wide open,
/// and the keep-up writer is started by a task write, which is exactly what an import or an
/// agent does just before it writes the plan. Every command that loads and saves the plan
/// enters here first; reads do not.
/// </para>
/// <para>
/// A plan pulled from another device enters too (<see cref="RoadmapReplication"/>): written
/// while a writer is between its load and its save, it would be saved over from the copy
/// that writer loaded before it arrived. No writer reaches the pull while it holds the gate
/// — the keep-up writer awaits the catch-up before it enters — so neither waits on the other.
/// </para>
/// <para>
/// A singleton, for the same reason as <see cref="RoadmapPlanChanges"/>: the writers are
/// resolved in different scopes — the band's screen, Tasks' Import, the MCP host — and a
/// gate per scope would guard nothing. It guards this process only; another device's save
/// still lands whole over this one's (local ADR 0018, last write wins).
/// </para>
/// </summary>
public sealed class RoadmapPlanGate
{
    // Never disposed: it lives as long as the host, and no wait handle is ever asked of it.
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Waits until no other writer holds the plan, then holds it until the
    /// returned handle is disposed. Not re-entrant: a writer never calls another.</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Held(_gate);
    }

    private sealed class Held(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }
}
