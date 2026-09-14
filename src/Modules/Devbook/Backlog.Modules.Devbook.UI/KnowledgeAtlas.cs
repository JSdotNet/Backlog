using System.Text.Json;

using Backlog.Infrastructure.Knowledge;
using Backlog.Modules.Knowledge.Abstractions;
using Backlog.UI.Components.Knowledge;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// The knowledge folders as a map, read from the graph the metadata generator
/// already derives.
///
/// <para>Nothing here parses Markdown. The generated <c>_meta/knowledge.db</c>
/// holds the whole repository's graph in one table — nodes with a label, a
/// folder, a status and a path, edges with a kind — and a scope is a filter over
/// it rather than a file of its own, which is what local ADR 0004 changed. A
/// repository that still has the committed <c>_meta/graph.json</c> and no database
/// is read from that instead; the map is the same either way. Re-deriving the same
/// graph by reading the documents would be a second answer to a question that
/// already has one, and it would disagree the first time the generator changed.</para>
///
/// <para>That also decides what the atlas can honestly show: it is as fresh as
/// the last generator run. A folder whose index has not been regenerated draws
/// the graph as it was, which is why an absent index says so rather than falling
/// back to something half-parsed. It is the one knowledge consumer that does not
/// degrade to Markdown, and that is deliberate — a map of the references between
/// chapters cannot be drawn from the handful of files on screen.</para>
/// </summary>
public sealed class KnowledgeAtlasService(IKnowledgeFolderSource source)
{
    public event Action? Changed
    {
        add => source.Changed += value;
        remove => source.Changed -= value;
    }

    public Task<KnowledgeAtlasGraph> ReadAsync(KnowledgeAtlasScope scope, string? repositoryAlias = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return Task.FromResult(Read(scope, repositoryAlias));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Task.FromResult(KnowledgeAtlasGraph.Unavailable(scope, $"The {scope.Label} atlas could not be read: {ex.Message}"));
        }
    }

    private KnowledgeAtlasGraph Read(KnowledgeAtlasScope scope, string? repositoryAlias)
    {
        if (ReadFromDatabase(scope, repositoryAlias) is { } fromDatabase) return fromDatabase;

        var graphPath = ResolveGraphPath(scope, repositoryAlias);

        if (graphPath is null)
        {
            return KnowledgeAtlasGraph.Unavailable(scope, $"{scope.Label} is not available for this repository.");
        }

        if (!File.Exists(graphPath))
        {
            // The same words search uses, from the same place, because this is the
            // same absence. It said "run the knowledge metadata tool" until ADR 0004
            // moved the layer into the database: that tool still writes
            // `_meta/*.json`, but nothing reads them any more, so following the
            // advice would have left the atlas exactly as empty as it found it.
            return KnowledgeAtlasGraph.Unavailable(scope, KnowledgeRetrieval.UnavailableMessage("This atlas"));
        }

        return KnowledgeAtlasReader.Read(scope, File.ReadAllText(graphPath));
    }

    /// <summary>
    /// The atlas out of the generated database, or <see langword="null"/> when
    /// this repository has none — in which case the scoped <c>graph.json</c> beside
    /// the folders is tried next, and after that the reader says the index has not
    /// been written.
    ///
    /// <para>The graph is stored once and unprojected, so the scope is applied
    /// here: <see cref="KnowledgeScopeProjection"/> is the reading-side restatement
    /// of the same rule that produced the scoped JSON files, boundary nodes
    /// included. A scope with no rows — <c>instructions</c>, which is not a
    /// knowledge folder and never had a graph — answers null rather than an empty
    /// map, so the message a reader sees is still about the index rather than about
    /// the scope.</para>
    /// </summary>
    private KnowledgeAtlasGraph? ReadFromDatabase(KnowledgeAtlasScope scope, string? repositoryAlias)
    {
        using var database = OpenDatabase(repositoryAlias);
        if (database is null) return null;

        var projection = KnowledgeScopeProjection.Project(
            scope.FolderKey ?? KnowledgeDatabaseSchema.RepositoryScope,
            database.Nodes(),
            database.Edges());

        if (projection.Nodes.Count == 0) return null;

        var nodes = projection.Nodes
            .Select(node => new KnowledgeAtlasSourceNode(
                node.Id,
                node.Label ?? string.Empty,
                node.Folder ?? string.Empty,
                node.Type,
                node.Status ?? string.Empty,
                node.Path ?? string.Empty,
                node.OutOfScope))
            .ToList();

        var edges = projection.Edges
            .Select(edge => new KnowledgeAtlasSourceEdge(edge.Type, edge.Source, edge.Target))
            .ToList();

        return KnowledgeAtlasReader.Read(scope, nodes, edges);
    }

    /// <summary>
    /// The database for this repository, found through the same port every other
    /// knowledge read goes through: a knowledge folder resolves to a path, and the
    /// database sits at the repository root above it.
    ///
    /// <para>Every enabled folder is tried rather than one being nominated,
    /// because <c>instructions</c> resolves into <c>.github</c> — a directory
    /// deeper than the folders at the root — and going up from it lands somewhere
    /// with no database in it. That is the same reason
    /// <see cref="ResolveGraphPath"/> tries them all. The walk itself moved to
    /// <see cref="KnowledgeDatabaseSource"/> when retrieval became a second
    /// consumer of it: two copies of "which folder do I go up from" is exactly the
    /// drift <see cref="KnowledgeDatabaseLocation"/> exists to prevent one level
    /// down.</para>
    /// </summary>
    private KnowledgeDatabase? OpenDatabase(string? repositoryAlias) =>
        KnowledgeDatabaseSource.TryOpen(source, repositoryAlias);

    /// <summary>
    /// Where the scope's graph file is.
    ///
    /// <para>A folder scope resolves through the port like any other knowledge
    /// area. The whole-repository scope has no folder of its own, so it is found
    /// beside the folders: the knowledge folders sit at the repository root and
    /// the root index sits with them. Resolving through a folder rather than
    /// asking for a repository path keeps this on the port that already exists —
    /// the one that knows a folder can be turned off, or moved.</para>
    /// </summary>
    private string? ResolveGraphPath(KnowledgeAtlasScope scope, string? repositoryAlias)
    {
        if (scope.FolderKey is { Length: > 0 } folderKey)
        {
            var location = source.Resolve(folderKey, repositoryAlias);
            return location is { Available: true, FullPath: { Length: > 0 } path }
                ? Path.Combine(path, "_meta", "graph.json")
                : null;
        }

        // The whole-repository index sits beside the knowledge folders, so it is
        // found by going up from one of them. Which one matters: `instructions`
        // resolves into `.github`, which is a directory deeper than the folders
        // that live at the root, and going up from it lands somewhere with no
        // index in it. Rather than encode which keys are root-level, every
        // candidate is tried and the first that actually has the file wins.
        string? fallback = null;

        foreach (var setting in source.Folders(repositoryAlias))
        {
            if (!setting.Enabled) continue;

            var location = source.Resolve(setting.Key, repositoryAlias);
            if (location is not { Available: true, FullPath: { Length: > 0 } path }) continue;

            var root = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(root)) continue;

            var candidate = Path.Combine(root, "_meta", "graph.json");
            if (File.Exists(candidate)) return candidate;

            fallback ??= candidate;
        }

        // Nothing had one. Handing back a candidate rather than null is what makes
        // the reader say the index has not been generated, which is the true
        // reason, instead of saying the scope is unavailable, which is not.
        if (fallback is not null) return fallback;

        return null;
    }
}

/// <summary>
/// One reading of the knowledge base: a single folder, or all of it.
/// </summary>
/// <param name="Key">Stable identity, and what a host stores when it remembers a choice.</param>
/// <param name="Label">The scope's name, as a reader sees it.</param>
/// <param name="FolderKey">The knowledge folder key, or <see langword="null"/> for the whole repository.</param>
public sealed record KnowledgeAtlasScope(string Key, string Label, string? FolderKey)
{
    /// <summary>Every knowledge folder at once, read from the repository's own
    /// root index.</summary>
    public static KnowledgeAtlasScope All { get; } = new("all", "All knowledge", null);

    /// <summary>The scope for one knowledge area, named as that area is named in
    /// the section strip so the two cannot drift apart.</summary>
    public static KnowledgeAtlasScope ForArea(KnowledgeArea area) =>
        new(area.Key, area.Label, KnowledgeAtlasFolders.KeyFor(area.Key));
}

/// <summary>The folder key behind a section key. The section strip says
/// <c>arc42</c>; the folder settings say <c>.arc42</c>; the two have to be
/// mapped somewhere and this is the only place that needs it.</summary>
internal static class KnowledgeAtlasFolders
{
    public static string? KeyFor(string areaKey) => areaKey switch
    {
        "instructions" => "instructions",
        "domain" => ".domain",
        "arc42" => ".arc42",
        "tech" => ".tech",
        "design" => ".design",
        _ => null
    };
}

public sealed record KnowledgeAtlasNode(
    string Id,
    string Label,
    string Folder,
    string Kind,
    string Status,
    string ToneSlug,
    string Path,
    string Group,
    int GroupIndex,
    int Ordinal,
    int InDegree,
    int OutDegree,
    bool OutOfScope);

public sealed record KnowledgeAtlasEdge(string Source, string Target, string Kind);

public sealed record KnowledgeAtlasGraph(
    KnowledgeAtlasScope Scope,
    bool Available,
    string? Message,
    IReadOnlyList<KnowledgeAtlasNode> Nodes,
    IReadOnlyList<KnowledgeAtlasEdge> Edges)
{
    public static KnowledgeAtlasGraph Unavailable(KnowledgeAtlasScope scope, string message) =>
        new(scope, false, message, [], []);

    /// <summary>How many groups the nodes fall into — the clusters a reader sees.</summary>
    public int GroupCount => Nodes.Select(node => node.Group).Distinct(StringComparer.Ordinal).Count();
}

/// <summary>One node as either source hands it over: a row of <c>node</c>, or a
/// <c>data</c> object out of a scoped <c>graph.json</c>. Deliberately the smaller
/// of the two vocabularies — what the map draws with, not everything the graph
/// carries.</summary>
internal sealed record KnowledgeAtlasSourceNode(
    string Id,
    string Label,
    string Folder,
    string Type,
    string Status,
    string Path,
    bool OutOfScope);

/// <summary>One edge as either source hands it over, before the atlas decides
/// whether it is drawn.</summary>
internal sealed record KnowledgeAtlasSourceEdge(string Type, string Source, string Target);

internal static class KnowledgeAtlasReader
{
    /// <summary>
    /// The edge kinds the atlas draws.
    ///
    /// <para><c>contains</c> is deliberately not among them. It is the hierarchy —
    /// a file containing its chapters — and drawing it would put an edge between
    /// every chapter and its own file, which is most of the graph and says nothing
    /// a reader cannot already see from the clustering. It is used to work out
    /// which file a chapter belongs to and then dropped.</para>
    /// </summary>
    private static readonly string[] DrawnEdgeKinds = ["related", "depends-on", "implements"];

    /// <summary>
    /// The atlas from a scoped <c>_meta/graph.json</c> — the fallback rung, for a
    /// repository that still has the committed artifacts and no database.
    /// </summary>
    public static KnowledgeAtlasGraph Read(KnowledgeAtlasScope scope, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("elements", out var elements))
        {
            return KnowledgeAtlasGraph.Unavailable(scope, "The knowledge index has no graph in it.");
        }

        return Read(scope, ReadEntries(elements), ReadRawEdges(elements));
    }

    /// <summary>
    /// The atlas from graph rows, which is what the generated database hands back
    /// once <c>KnowledgeScopeProjection</c> has applied the scope.
    ///
    /// <para>The two sources meet here rather than each drawing their own map. What
    /// the map is — which edges are drawn, how nodes cluster, what a degree counts
    /// — is one set of decisions, and they were only ever expressed against the
    /// JSON because the JSON was the only source. <paramref name="rawEdges"/> arrives
    /// unfiltered, including <c>contains</c>, because the hierarchy is what decides
    /// a chapter's cluster before it is dropped.</para>
    /// </summary>
    public static KnowledgeAtlasGraph Read(
        KnowledgeAtlasScope scope,
        IReadOnlyList<KnowledgeAtlasSourceNode> sourceNodes,
        IReadOnlyList<KnowledgeAtlasSourceEdge> rawEdges)
    {
        var entries = sourceNodes
            .Select(node => new Entry(
                node.Id,
                node.Label is { Length: > 0 } label ? label : node.Id,
                node.Folder,
                node.Type is { Length: > 0 } type ? type : "chapter",
                node.Status,
                node.Path,
                node.OutOfScope))
            .ToList();

        if (entries.Count == 0)
        {
            return KnowledgeAtlasGraph.Unavailable(scope, "The knowledge index names no documents yet.");
        }

        var edges = DrawnEdges(rawEdges, entries);
        var groups = AssignGroups(entries, scope, rawEdges);

        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var outDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in edges)
        {
            outDegree[edge.Source] = outDegree.GetValueOrDefault(edge.Source) + 1;
            inDegree[edge.Target] = inDegree.GetValueOrDefault(edge.Target) + 1;
        }

        var groupIndexes = groups.Values
            .Distinct(StringComparer.Ordinal)
            .OrderBy(group => group, StringComparer.OrdinalIgnoreCase)
            .Select((group, index) => (group, index))
            .ToDictionary(pair => pair.group, pair => pair.index, StringComparer.Ordinal);

        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        var nodes = new List<KnowledgeAtlasNode>(entries.Count);

        foreach (var entry in entries)
        {
            var group = groups[entry.Id];
            var ordinal = ordinals.GetValueOrDefault(group);
            ordinals[group] = ordinal + 1;

            var folder = KnowledgeFolders.FromPath(entry.Path.Length > 0 ? entry.Path : entry.Id);

            nodes.Add(new KnowledgeAtlasNode(
                entry.Id,
                entry.Label,
                FolderTitle(entry.Folder),
                entry.Type,
                entry.Status,
                // Each folder answers to its own status vocabulary, which matters
                // most in the whole-repository scope: `active` is a real status in
                // .arc42 and no status at all in .tech, and one shared list would
                // have to be wrong about one of them.
                entry.Status.Length == 0 ? string.Empty : KnowledgeStatus.Vocabulary(folder).SlugFor(entry.Status),
                entry.Path,
                group,
                groupIndexes[group],
                ordinal,
                inDegree.GetValueOrDefault(entry.Id),
                outDegree.GetValueOrDefault(entry.Id),
                entry.OutOfScope));
        }

        return new KnowledgeAtlasGraph(scope, true, null, nodes, edges);
    }

    private static List<KnowledgeAtlasSourceNode> ReadEntries(JsonElement elements)
    {
        var entries = new List<KnowledgeAtlasSourceNode>();

        if (!elements.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return entries;
        }

        foreach (var element in nodes.EnumerateArray())
        {
            if (!element.TryGetProperty("data", out var data)) continue;
            if (Text(data, "id") is not { Length: > 0 } id) continue;

            entries.Add(new KnowledgeAtlasSourceNode(
                id,
                Text(data, "label"),
                Text(data, "folder"),
                Text(data, "type"),
                Text(data, "status"),
                Text(data, "path"),
                data.TryGetProperty("outOfScope", out var outOfScope) && outOfScope.ValueKind == JsonValueKind.True));
        }

        return entries;
    }

    private static List<KnowledgeAtlasSourceEdge> ReadRawEdges(JsonElement elements)
    {
        var edges = new List<KnowledgeAtlasSourceEdge>();

        if (!elements.TryGetProperty("edges", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return edges;
        }

        foreach (var element in items.EnumerateArray())
        {
            if (!element.TryGetProperty("data", out var data)) continue;

            edges.Add(new KnowledgeAtlasSourceEdge(Text(data, "type"), Text(data, "source"), Text(data, "target")));
        }

        return edges;
    }

    private static List<KnowledgeAtlasEdge> DrawnEdges(IReadOnlyList<KnowledgeAtlasSourceEdge> items, List<Entry> entries)
    {
        var known = entries.Select(entry => entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var edges = new List<KnowledgeAtlasEdge>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var kind = item.Type;
            if (!DrawnEdgeKinds.Contains(kind, StringComparer.OrdinalIgnoreCase)) continue;

            var from = item.Source;
            var to = item.Target;
            if (from.Length == 0 || to.Length == 0) continue;

            // An edge to something the index does not describe cannot be drawn:
            // there is no second end to draw it to.
            if (!known.Contains(from) || !known.Contains(to)) continue;

            // One line per pair, whichever way round it was written and whatever
            // kind it is. `related` is written on both chapters, so a reciprocal
            // pair arrives twice; and two documents that both relate to and depend
            // on each other would otherwise be drawn twice, on top of themselves.
            // The map says "these two are connected" — which kind of connection it
            // is, is a fact about the pair rather than a second line.
            var pair = string.CompareOrdinal(from, to) <= 0 ? from + "|" + to : to + "|" + from;
            if (!seen.Add(pair)) continue;

            edges.Add(new KnowledgeAtlasEdge(from, to, kind));
        }

        return edges;
    }

    /// <summary>
    /// Which cluster each node sits in.
    ///
    /// <para>Whole-repository: the folder, so the five knowledge areas read as
    /// five regions of one map.</para>
    ///
    /// <para>One folder: the directory under it when the file is in one — a
    /// bounded context in <c>.domain</c>, the ADR shelf in <c>.arc42</c> — and the
    /// document itself when it is not. That is uniform across folders shaped very
    /// differently: <c>.domain</c> is thirteen contexts of six files each, and
    /// <c>.design</c> is nine documents in a row, and both come out as a
    /// comparable handful of clusters without a rule that has to know which is
    /// which.</para>
    /// </summary>
    private static Dictionary<string, string> AssignGroups(
        List<Entry> entries,
        KnowledgeAtlasScope scope,
        IReadOnlyList<KnowledgeAtlasSourceEdge> rawEdges)
    {
        var groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (scope.FolderKey is null)
        {
            foreach (var entry in entries)
            {
                groups[entry.Id] = FolderTitle(entry.Folder);
            }

            return groups;
        }

        // A chapter's file is what its `contains` edge names. Falling back to the
        // path covers a node the generator did not connect.
        var fileOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in rawEdges)
        {
            if (!string.Equals(edge.Type, "contains", StringComparison.OrdinalIgnoreCase)) continue;
            if (edge.Source.Length > 0 && edge.Target.Length > 0) fileOf[edge.Target] = edge.Source;
        }

        var labels = entries.ToDictionary(entry => entry.Id, entry => entry.Label, StringComparer.OrdinalIgnoreCase);
        var paths = entries.ToDictionary(entry => entry.Id, entry => entry.Path, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var fileId = fileOf.GetValueOrDefault(entry.Id, entry.Id);
            var path = paths.GetValueOrDefault(fileId, entry.Path);
            var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

            groups[entry.Id] = segments.Length > 2
                ? Titleise(segments[1])
                : labels.GetValueOrDefault(fileId, entry.Label);
        }

        return groups;
    }

    private static string Text(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>The index writes a folder as the bare word; a reader sees the
    /// section's own name.</summary>
    private static string FolderTitle(string? folder) => folder?.ToLowerInvariant() switch
    {
        "arc42" => "Architecture",
        "domain" => "Domain",
        "design" => "Design",
        "backlog" => "Backlog",
        "tech" => "Technology",
        null or "" => "Elsewhere",
        _ => Titleise(folder)
    };

    private static string Titleise(string value) =>
        string.Join(' ', value
            .Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    private sealed record Entry(
        string Id,
        string Label,
        string Folder,
        string Type,
        string Status,
        string Path,
        bool OutOfScope);
}
