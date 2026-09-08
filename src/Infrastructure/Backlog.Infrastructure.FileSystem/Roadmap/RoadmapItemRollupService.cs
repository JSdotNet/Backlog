using Backlog.Infrastructure.Knowledge;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Roadmap Planning's <see cref="IRoadmapItemRollup"/> by reading the two
/// contexts a roadmap item gathers from: the backlog, through
/// <see cref="ITaskItems"/>, and the knowledge chapters, through the generated
/// knowledge index beside the plan - <c>_meta/knowledge.db</c>, or the committed
/// <c>_meta/graph.json</c> where no database has been built.
/// <para>
/// The join lives here because only an adapter may see both. The band asks its own
/// module; this reads the backlog and the graph and hands back the rolled-up result
/// through <see cref="RoadmapItemRollupBuilder"/>, which owns the arithmetic.
/// </para>
/// </summary>
public sealed class RoadmapItemRollupService : IRoadmapItemRollup
{
    private readonly ITaskItems _entries;
    private readonly Func<string> _rootDirectory;

    /// <param name="entries">The backlog port, read for entries linked or tagged.</param>
    /// <param name="rootDirectory">Where the storage root is right now — read per
    /// call rather than pinned, so pointing the app at another folder takes effect
    /// without a restart, the same as the plan repository.</param>
    public RoadmapItemRollupService(ITaskItems entries, Func<string> rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(rootDirectory);
        _entries = entries;
        _rootDirectory = rootDirectory;
    }

    public async Task<RoadmapItemRollupDto> GatherAsync(RoadmapItemDto item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var backlog = await _entries.ListAsync(cancellationToken);
        var knowledge = ReadGraphNodes();

        return RoadmapItemRollupBuilder.Build(item, backlog, knowledge);
    }

    private IReadOnlyList<KnowledgeGraphNode> ReadGraphNodes() => ReadDatabaseNodes() ?? ReadGraphJsonNodes();

    /// <summary>
    /// The nodes out of the generated <c>_meta/knowledge.db</c>, or
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
        using var database = KnowledgeDatabase.TryOpen(KnowledgeDatabaseLocation.ForRepositoryRoot(_rootDirectory()));
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
        var path = Path.Combine(_rootDirectory(), "_meta", "graph.json");
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
