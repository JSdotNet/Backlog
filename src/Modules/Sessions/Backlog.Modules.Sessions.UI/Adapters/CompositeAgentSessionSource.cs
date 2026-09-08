using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UI.Adapters;

/// <summary>
/// Every source of sessions this host composed, answered as one catalog.
/// <para>
/// It exists because a session list stopped having one source. This machine's own
/// readers answer for the environment the app is running in, and the sync slice
/// answers for the environments that reported theirs; both are implementations of
/// <see cref="IAgentSessionSource"/>, which is what that port was shaped for.
/// Merging here rather than in each surface is what makes
/// <c>SessionsPane</c> and the Dashboard's sessions part show the same list
/// without either of them learning that there is more than one place a session can
/// come from.
/// </para>
/// <para>
/// <strong>It lives beside the local readers and takes nothing but this module's
/// own abstractions.</strong> That is the whole of what merging two catalogs
/// needs, and it is the reason this half of the composition is here while the
/// replicated source is in <c>Backlog.Infrastructure.Sync</c>: producing a session
/// from a wire record needs the Sync context's published surface, and
/// <c>ModuleBoundaryTests</c> forbids a module UI consuming another module's
/// abstractions. Splitting it this way leaves each half where its dependencies
/// already point, and creates no reference in either direction between the two
/// projects.
/// </para>
/// <para>
/// <strong>The merge is honest about what it could not tell you.</strong>
/// <c>Unreadable</c> is the union and <c>Discovered</c> is the sum, so a source
/// that had to cap its answer keeps the list visibly capped
/// (<see cref="AgentSessionCatalog.Capped"/>) and a folder that could not be read
/// is still named. Taking the maximum of the discovered counts, or reporting only
/// the sources that answered, would each turn a partial list into what reads as a
/// complete one — and <c>.domain/sessions/features.md</c> makes saying how much was
/// left out the pane's job, which it cannot do from a number that has already been
/// rounded down.
/// </para>
/// </summary>
internal sealed class CompositeAgentSessionSource : IAgentSessionSource
{
    private readonly IReadOnlyList<IAgentSessionSource> _sources;

    internal CompositeAgentSessionSource(IReadOnlyList<IAgentSessionSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        _sources = sources;
    }

    /// <summary>
    /// Every source's answer, concatenated.
    /// <para>
    /// Sequentially rather than in parallel. The sources are a handful of file
    /// reads, and running them together would buy milliseconds at the cost of a
    /// non-deterministic order in a list whose order the grouping then has to
    /// impose anyway — <see cref="AgentSessionGroups"/> sorts by last activity, so
    /// nothing downstream depends on this order, and a test that could not predict
    /// it would have to sort before asserting.
    /// </para>
    /// <para>
    /// A source that throws is not caught here. Each source already decides what
    /// counts as unreadable for it — the local one names the agent folder it could
    /// not open and carries on — so an exception reaching this point is a fault in
    /// a source rather than a fact about the machine, and swallowing it would hide
    /// the one thing worth seeing.
    /// </para>
    /// </summary>
    public async Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = new List<AgentSession>();
        var unreadable = new List<string>();
        var discovered = 0;

        foreach (var source in _sources)
        {
            var catalog = await source.GetSessionsAsync(cancellationToken).ConfigureAwait(false);

            sessions.AddRange(catalog.Sessions);
            discovered += catalog.Discovered;

            foreach (var name in catalog.Unreadable)
            {
                // A union rather than a concatenation: two sources naming the same
                // folder is one folder that could not be read, and a surface
                // saying so twice reads as two separate problems.
                if (!unreadable.Contains(name, StringComparer.Ordinal))
                {
                    unreadable.Add(name);
                }
            }
        }

        return new AgentSessionCatalog(sessions, unreadable, discovered);
    }
}
