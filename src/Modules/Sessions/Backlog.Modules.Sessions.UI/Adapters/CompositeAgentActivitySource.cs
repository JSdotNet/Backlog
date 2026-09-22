using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UI.Adapters;

/// <summary>
/// Every source of activity this host composed, answered as one log.
/// <para>
/// <see cref="CompositeAgentSessionSource"/>'s twin, for the second port, and
/// there for the same reason: activity stopped having one source the day another
/// machine's runs and waits started arriving on its session records. This
/// machine's own reader folds them out of transcripts; the sync slice answers
/// with the ones that travelled. Merging here rather than in the Dashboard's
/// adapter is what lets that adapter go on injecting one port and never learn
/// that there is more than one place an interval can come from.
/// </para>
/// <para>
/// <strong>It lives beside the local reader and takes nothing but this module's
/// own abstractions</strong>, for the reason its twin gives: merging two logs
/// needs only the port, while producing an activity record from a wire record
/// needs the Sync context's published surface, which
/// <c>ModuleBoundaryTests</c> forbids this project from referencing. Each half
/// sits where its dependencies already point.
/// </para>
/// <para>
/// <strong>The merge is honest about what it could not tell you.</strong>
/// <c>Unreadable</c> is the union, so an agent folder one source could not open
/// is still named after the merge; the sessions and the subagents are
/// concatenated, never deduplicated, because two sources never describe the same
/// session — a record this device pushed comes back stamped with its own machine
/// id and is dropped before it reaches a store, so the replicated source cannot
/// hold a session the local one also folded.
/// </para>
/// <para>
/// <strong><c>IdleAfter</c> is the first source with an opinion, and zero is not
/// an opinion.</strong> The threshold that ends a run is a judgement the local
/// reader made and the sentence on screen names it; the replicated source cannot
/// know what threshold the pushing machine folded with and says so with
/// <see cref="TimeSpan.Zero"/>. Taking the first non-zero answer names the local
/// number whichever order the contributors were registered in, which is the
/// property the keyed collection cannot promise on its own. A host with no local
/// reader gets zero, which is the truth: nothing here folded anything.
/// </para>
/// </summary>
internal sealed class CompositeAgentActivitySource : IAgentActivitySource
{
    private readonly IReadOnlyList<IAgentActivitySource> _sources;

    internal CompositeAgentActivitySource(IReadOnlyList<IAgentActivitySource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        _sources = sources;
    }

    /// <summary>
    /// Every source's answer, concatenated, under the horizon that was asked for.
    /// <para>
    /// Sequentially rather than in parallel, and with nothing caught, for the
    /// reasons the session composite gives: the order downstream is imposed by
    /// the sweep and not by this list, and a source that throws is a fault in a
    /// source rather than a fact about the machine.
    /// </para>
    /// <para>
    /// <c>Since</c> is the horizon the caller stated rather than any source's
    /// echo of it. Every source was asked the same horizon, so the two agree on a
    /// well-behaved day; on any other day a surface reporting a floor should
    /// report the one it asked for.
    /// </para>
    /// </summary>
    public async Task<AgentActivityLog> GetActivityAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        var sessions = new List<AgentSessionActivity>();
        var subagents = new List<SubagentActivity>();
        var unreadable = new List<string>();
        var idleAfter = TimeSpan.Zero;

        foreach (var source in _sources)
        {
            var log = await source.GetActivityAsync(since, cancellationToken).ConfigureAwait(false);

            sessions.AddRange(log.Sessions);
            subagents.AddRange(log.Subagents);

            foreach (var name in log.Unreadable)
            {
                // A union rather than a concatenation: two sources naming the same
                // folder is one folder that could not be read.
                if (!unreadable.Contains(name, StringComparer.Ordinal))
                {
                    unreadable.Add(name);
                }
            }

            if (idleAfter == TimeSpan.Zero && log.IdleAfter != TimeSpan.Zero)
            {
                idleAfter = log.IdleAfter;
            }
        }

        return new AgentActivityLog(sessions, unreadable, since, idleAfter)
        {
            Subagents = subagents
        };
    }
}
