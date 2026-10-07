using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Roadmap Planning's <see cref="IRoadmapItemRollup"/> by reading the two
/// contexts a roadmap item gathers from: the backlog, through
/// <see cref="ITaskItems"/>, and the knowledge chapters, through the generated
/// knowledge index beside the plan - the devbook database, or the committed
/// <c>_meta/graph.json</c> where no database has been built.
/// <para>
/// The join lives here because only an adapter may see both. The band asks its own
/// module; this reads the backlog and the graph and hands back the rolled-up result
/// through <see cref="RoadmapItemRollupBuilder"/>, which owns the arithmetic.
/// </para>
/// <para>
/// A third context is read only when it can add something: the AI sessions linked to a
/// gathered entry, through <see cref="IAgentSessionSource"/>, to date an entry that
/// carries no <c>started:</c> or <c>completed:</c> date of its own. When no gathered
/// entry needs that, the sessions are not read at all.
/// </para>
/// </summary>
public sealed class RoadmapItemRollupService : IRoadmapItemRollup
{
    private readonly Func<ITaskItems> _entries;
    private readonly Func<string> _rootDirectory;
    private readonly IAgentSessionSource? _sessions;
    private readonly TimeProvider _time;

    /// <param name="entries">The backlog port, read for entries linked or tagged.</param>
    /// <param name="rootDirectory">Where the storage root is right now — read per
    /// call rather than pinned, so pointing the app at another folder takes effect
    /// without a restart, the same as the plan repository.</param>
    /// <param name="sessions">Where the sessions linked to an entry are read from, or
    /// null in a host that has none — entries are then dated by Tasks alone.</param>
    /// <param name="time">The clock the session horizon is measured from.</param>
    public RoadmapItemRollupService(
        ITaskItems entries,
        Func<string> rootDirectory,
        IAgentSessionSource? sessions = null,
        TimeProvider? time = null)
        : this(Pinned(entries), rootDirectory, sessions, time)
    {
    }

    /// <param name="entries">The backlog port, resolved per call: the backlog's plan
    /// import reaches the roadmap importer, which gathers through this, so a host that
    /// took the backlog in the constructor would build a loop its scope cannot finish.</param>
    /// <param name="rootDirectory">Where the storage root is right now.</param>
    /// <param name="sessions">Where the sessions linked to an entry are read from, or
    /// null in a host that has none.</param>
    /// <param name="time">The clock the session horizon is measured from.</param>
    public RoadmapItemRollupService(
        Func<ITaskItems> entries,
        Func<string> rootDirectory,
        IAgentSessionSource? sessions = null,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(rootDirectory);
        _entries = entries;
        _rootDirectory = rootDirectory;
        _sessions = sessions;
        _time = time ?? TimeProvider.System;
    }

    private static Func<ITaskItems> Pinned(ITaskItems entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return () => entries;
    }

    public async Task<RoadmapItemRollupDto> GatherAsync(RoadmapItemDto item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var backlog = await _entries().ListAsync(cancellationToken).ConfigureAwait(false);
        var knowledge = ReadGraphNodes();
        var sessions = await ReadSessionsAsync(
            backlog.Where(entry => RoadmapItemRollupBuilder.Gathers(item, entry)),
            cancellationToken).ConfigureAwait(false);

        return RoadmapItemRollupBuilder.Build(item, backlog, knowledge, sessions);
    }

    /// <summary>
    /// Every item of the plan, off one read of the backlog and one of the knowledge
    /// graph — the two reads this method exists to make once instead of per item.
    /// The arithmetic and the joining still belong to
    /// <see cref="RoadmapItemRollupBuilder"/>; all this adds is that both sources are
    /// read before any item is answered.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, RoadmapItemRollupDto>> GatherPlanAsync(
        RoadmapPlanDto plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var backlog = await _entries().ListAsync(cancellationToken).ConfigureAwait(false);
        var knowledge = ReadGraphNodes();
        var sessions = await ReadSessionsAsync(
            backlog.Where(entry => plan.Items.Any(item => RoadmapItemRollupBuilder.Gathers(item, entry))),
            cancellationToken).ConfigureAwait(false);

        return RoadmapItemRollupBuilder.BuildPlan(plan, backlog, knowledge, sessions);
    }

    /// <summary>
    /// The sessions linked to the <paramref name="gathered"/> entries that need one to
    /// be dated, by id — or <see langword="null"/> when none does, so a plan whose work
    /// is dated by Tasks never pays for a session read.
    /// <para>
    /// A horizon reading rather than the inventory's newest-per-agent list: a capped
    /// list would silently lose an older entry's sessions. The horizon reaches back as
    /// far as the Sessions context promises to keep records, and further when an
    /// entry needing a date was filed before that — a session linked to an entry does
    /// not end before the entry exists.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyDictionary<string, RoadmapSessionActivity>?> ReadSessionsAsync(
        IEnumerable<TaskItemDto> gathered,
        CancellationToken cancellationToken)
    {
        if (_sessions is null) return null;

        var needy = gathered.Where(RoadmapItemRollupBuilder.NeedsSessionDates).ToList();
        if (needy.Count == 0) return null;

        var wanted = new HashSet<string>(needy.SelectMany(RoadmapItemRollupBuilder.SessionIds), StringComparer.OrdinalIgnoreCase);

        var horizon = _time.GetUtcNow() - AgentSessionLimits.History;
        foreach (var entry in needy)
        {
            if (entry.CreatedAt is { } created && created < horizon) horizon = created;
        }

        var catalog = await _sessions.GetSessionsAsync(AgentSessionQuery.Since(horizon), cancellationToken).ConfigureAwait(false);

        var found = new Dictionary<string, RoadmapSessionActivity>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in catalog.Sessions)
        {
            if (!wanted.Contains(session.Id)) continue;

            // One session can arrive twice — recorded here and replicated from another
            // machine. Merged to the widest stretch either copy saw.
            var activity = new RoadmapSessionActivity(session.StartedAt, session.LastActivityAt);
            found[session.Id] = found.TryGetValue(session.Id, out var seen) ? Widest(seen, activity) : activity;
        }

        return found;
    }

    private static RoadmapSessionActivity Widest(RoadmapSessionActivity one, RoadmapSessionActivity other) =>
        new(
            one.EarliestAt < other.EarliestAt ? one.EarliestAt : other.EarliestAt,
            one.LastActivityAt > other.LastActivityAt ? one.LastActivityAt : other.LastActivityAt);

    private IReadOnlyList<KnowledgeGraphNode> ReadGraphNodes() => ReadDatabaseNodes() ?? ReadGraphJsonNodes();

    /// <summary>
    /// The nodes out of the generated devbook database, or
    /// <see langword="null"/> when there is none to read.
    ///
    /// <para>Local ADR 0004 imagines this saving an 836 KB parse to total a few
    /// hundred effort values. Be precise about what it actually saves here, because
    /// the second half of that sentence is not true of this corpus: no chapter in it
    /// registers <c>effort</c> or names a <c>roadmap</c> item, so both the parse and
    /// the query correctly return nothing to total. The move is worth making because
    /// it is a strictly cheaper way to reach the same empty answer — a few hundred
    /// indexed rows instead of a whole document deserialized to find that none of
    /// them carries the two fields being asked for — and not because it recovers a
    /// number that was being missed.</para>
    ///
    /// <para>Every node is returned, not just the ones with effort or a roadmap tag.
    /// A roadmap item also gathers chapters it references directly, by the same
    /// <c>&lt;path&gt;#&lt;slug&gt;</c> string, and a node filtered out here would
    /// stop resolving.</para>
    /// </summary>
    private IReadOnlyList<KnowledgeGraphNode>? ReadDatabaseNodes()
    {
        using var database = DevbookDatabase.TryOpen(DevbookDatabaseLocation.ForRepositoryRoot(_rootDirectory()));
        if (database is null) return null;

        var roadmap = database.Attributes(RoadmapAttribute);

        return database.Nodes()
            .Select(node => new KnowledgeGraphNode(
                node.Id,
                string.IsNullOrWhiteSpace(node.Label) ? node.Id : node.Label,
                node.Effort is { } effort && effort >= 0 ? effort : null,
                roadmap.Contains(node.Id) ? roadmap[node.Id].ToList() : []))
            .ToList();
    }

    private IReadOnlyList<KnowledgeGraphNode> ReadGraphJsonNodes()
    {
        // Beside the folders it indexes: .devbook/_meta in the devbook layout.
        var root = _rootDirectory();
        var path = Path.Combine(DevbookLayout.MetaDirectoryFor(root, DevbookLayout.UsesDevbookLayout(root)), "graph.json");
        if (!File.Exists(path)) return [];

        try
        {
            return KnowledgeGraph.Parse(File.ReadAllText(path));
        }
        catch (IOException)
        {
            // A graph the OS is holding open, or one that has just been swapped out
            // from under the read, is not a reason to fail a rollup: it is read again
            // next time the band gathers.
            return [];
        }
    }

    /// <summary>The list-valued <c>meta</c> field a chapter names its roadmap items
    /// in, as the writer files it in <c>node_attribute</c>.</summary>
    private const string RoadmapAttribute = "roadmap";
}
