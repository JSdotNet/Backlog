using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Roadmap Planning's <see cref="IRoadmapItemRollup"/> by reading the two
/// contexts a roadmap item gathers from: the backlog, through
/// <see cref="ITaskItems"/>, and the knowledge chapters, through the generated
/// <c>_meta/graph.json</c> beside the plan.
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

    private IReadOnlyList<KnowledgeGraphNode> ReadGraphNodes()
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
}
