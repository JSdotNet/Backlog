using System.Text.Json;

using Backlog.Modules.Knowledge.Abstractions;
using Backlog.UI.Components.Knowledge;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// The knowledge folders as a map, read from the graphs the metadata generator
/// already writes.
///
/// <para>Nothing here parses Markdown. Every knowledge folder carries a
/// <c>_meta/graph.json</c> — nodes with a label, a folder, a status and a path,
/// edges with a kind — and the repository root carries one spanning all of them.
/// Those files are the whole input. Re-deriving the same graph by reading the
/// documents would be a second answer to a question that already has one, and it
/// would disagree the first time the generator changed.</para>
///
/// <para>That also decides what the atlas can honestly show: it is as fresh as
/// the last generator run. A folder whose index has not been regenerated draws
/// the graph as it was, which is why an absent index says so rather than falling
/// back to something half-parsed.</para>
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
        var graphPath = ResolveGraphPath(scope, repositoryAlias);

        if (graphPath is null)
        {
            return KnowledgeAtlasGraph.Unavailable(scope, $"{scope.Label} is not available for this repository.");
        }

        if (!File.Exists(graphPath))
        {
            return KnowledgeAtlasGraph.Unavailable(
                scope,
                "This atlas is drawn from the generated knowledge index, which has not been written yet. Run the knowledge metadata tool to build it.");
        }

        return KnowledgeAtlasReader.Read(scope, File.ReadAllText(graphPath));
    }

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

    public static KnowledgeAtlasGraph Read(KnowledgeAtlasScope scope, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("elements", out var elements))
        {
            return KnowledgeAtlasGraph.Unavailable(scope, "The knowledge index has no graph in it.");
        }

        var entries = ReadEntries(elements);
        if (entries.Count == 0)
        {
            return KnowledgeAtlasGraph.Unavailable(scope, "The knowledge index names no documents yet.");
        }

        var edges = ReadEdges(elements, entries);
        var groups = AssignGroups(entries, scope, elements);

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

    private static List<Entry> ReadEntries(JsonElement elements)
    {
        var entries = new List<Entry>();

        if (!elements.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return entries;
        }

        foreach (var element in nodes.EnumerateArray())
        {
            if (!element.TryGetProperty("data", out var data)) continue;
            if (Text(data, "id") is not { Length: > 0 } id) continue;

            entries.Add(new Entry(
                id,
                Text(data, "label") is { Length: > 0 } label ? label : id,
                Text(data, "folder"),
                Text(data, "type") is { Length: > 0 } type ? type : "chapter",
                Text(data, "status"),
                Text(data, "path"),
                data.TryGetProperty("outOfScope", out var outOfScope) && outOfScope.ValueKind == JsonValueKind.True));
        }

        return entries;
    }

    private static List<KnowledgeAtlasEdge> ReadEdges(JsonElement elements, List<Entry> entries)
    {
        var known = entries.Select(entry => entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var edges = new List<KnowledgeAtlasEdge>();

        if (!elements.TryGetProperty("edges", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return edges;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in items.EnumerateArray())
        {
            if (!element.TryGetProperty("data", out var data)) continue;

            var kind = Text(data, "type");
            if (!DrawnEdgeKinds.Contains(kind, StringComparer.OrdinalIgnoreCase)) continue;

            var from = Text(data, "source");
            var to = Text(data, "target");
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
    private static Dictionary<string, string> AssignGroups(List<Entry> entries, KnowledgeAtlasScope scope, JsonElement elements)
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

        if (elements.TryGetProperty("edges", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in items.EnumerateArray())
            {
                if (!element.TryGetProperty("data", out var data)) continue;
                if (!string.Equals(Text(data, "type"), "contains", StringComparison.OrdinalIgnoreCase)) continue;

                var parent = Text(data, "source");
                var child = Text(data, "target");
                if (parent.Length > 0 && child.Length > 0) fileOf[child] = parent;
            }
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
