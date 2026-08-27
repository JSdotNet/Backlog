using System.Text.Json;

using Backlog.Modules.Knowledge.Abstractions;
using Backlog.UI.Components.Knowledge;

namespace Backlog.Desktop.UI.Knowledge;

public sealed class TechnologyKnowledgeService(IKnowledgeFolderSource source)
{
    public event Action? Changed
    {
        add => source.Changed += value;
        remove => source.Changed -= value;
    }

    public Task<TechnologyKnowledgeView> ReadAsync(string? repositoryAlias = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var location = source.Resolve(".tech", repositoryAlias);
        if (!location.Available || location.FullPath is null)
        {
            return Task.FromResult(TechnologyKnowledgeView.Unavailable(location));
        }

        try
        {
            return Task.FromResult(TechnologyKnowledgeReader.Read(location));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Task.FromResult(TechnologyKnowledgeView.Unavailable(
                location with { Message = $"Technology knowledge could not be read: {ex.Message}" }));
        }
    }

    public Task UpdateStatusAsync(string? repositoryAlias, string itemPath, string status, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(itemPath)) throw new ArgumentException("Knowledge item path is required.", nameof(itemPath));
        if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

        var location = source.Resolve(".tech", repositoryAlias);
        if (!location.Available) throw new InvalidOperationException(location.Message ?? "Technology knowledge is unavailable.");
        if (location.FullPath is null) throw new InvalidOperationException("Technology knowledge folder path is unavailable.");

        KnowledgeMarkdownStatusWriter.UpdateStatus(location.FullPath, itemPath, ".tech/", status);
        return Task.CompletedTask;
    }
}

public sealed record TechnologyKnowledgeView(
    KnowledgeFolderLocation Location,
    string Title,
    string Summary,
    IReadOnlyList<TechnologyLayer> Layers,
    IReadOnlyList<TechnologyRelationship> Relationships,
    IReadOnlyList<TechnologyKnowledgeDiagram> Diagrams,
    TechnologyGraphData Graph,
    TechnologyGraphStats Stats)
{
    public bool Available => Location.Available;

    public static TechnologyKnowledgeView Unavailable(KnowledgeFolderLocation location) => new(
        location,
        "Technology knowledge",
        location.Message ?? "Technology knowledge is unavailable.",
        [],
        [],
        [],
        TechnologyGraphData.Empty,
        TechnologyGraphStats.Empty);
}

public sealed record TechnologyLayer(
    string FileName,
    string Title,
    KnowledgeMetadata Metadata,
    IReadOnlyList<TechnologyNode> Nodes)
{
    public int DependencyCount => Nodes.Sum(node => node.DependsOn.Count);
}

public sealed record TechnologyNode(
    string Id,
    string Label,
    string LayerFileName,
    string LayerTitle,
    KnowledgeMetadata Metadata,
    string Description,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> Related,
    IReadOnlyList<string> Alternatives)
{
    public string Status => Metadata.Status ?? "unknown";

    public string Kind => Metadata.Kind ?? "technology";
}

public sealed record TechnologyRelationship(string FromId, string FromLabel, string ToId, string ToLabel);

public sealed record TechnologyGraphData(IReadOnlyList<TechnologyGraphNode> Nodes, IReadOnlyList<TechnologyGraphEdge> Edges)
{
    public static TechnologyGraphData Empty { get; } = new([], []);
}

/// <summary>
/// One technology as the atlas draws it.
///
/// <para>The first six fields are what the graph has always carried. The rest are
/// what a picture needs and a lane view never did: where a node sits in the
/// reading order, how many things lean on it, and which tone its status wears.
/// They are computed here because every one of them is a property of the
/// knowledge rather than of the viewport — a renderer that derived them would be
/// a second reading of <c>.tech</c>, and the two would drift.</para>
///
/// <para>They carry defaults so the six-argument shape still compiles: a caller
/// that only wants a node to name a technology should not have to know how the
/// atlas lays one out.</para>
/// </summary>
/// <param name="LayerFileName">The layer file this node was read from, empty for a boundary node.</param>
/// <param name="LayerIndex">Position in the folder's committed reading order; <c>-1</c> for a boundary node.</param>
/// <param name="OrdinalInLayer">Document order within the layer — the atlas's tie-break, so the same graph draws the same picture twice.</param>
/// <param name="InDegree">How many technologies depend on this one. Sizes the node.</param>
/// <param name="OutDegree">How many technologies this one depends on.</param>
/// <param name="ToneSlug">The status badge modifier this node's status wears, through its folder's own vocabulary.</param>
/// <param name="IsFoundation">Nothing in this project sits below it — no outgoing edge, per <c>.tech/technology-graph.md</c>.</param>
/// <param name="IsBoundary">Documented in another knowledge folder; this atlas shows it because something here depends on it.</param>
public sealed record TechnologyGraphNode(
    string Id,
    string Label,
    string Layer,
    string Kind,
    string Status,
    string Description,
    string LayerFileName = "",
    int LayerIndex = -1,
    int OrdinalInLayer = 0,
    int InDegree = 0,
    int OutDegree = 0,
    string ToneSlug = "",
    bool IsFoundation = false,
    bool IsBoundary = false);

public sealed record TechnologyGraphEdge(string Id, string Source, string Target, string Label);

public sealed record TechnologyKnowledgeDiagram(string Title, string Language, string Source);

public sealed record TechnologyGraphStats(int Nodes, int Edges, IReadOnlyDictionary<string, int> NodesByStatus)
{
    public static TechnologyGraphStats Empty { get; } = new(0, 0, new Dictionary<string, int>());
}

public sealed record KnowledgeMetadata(
    string? Status,
    string? Kind,
    string? Version,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> Related,
    IReadOnlyList<string> Alternatives)
{
    public static KnowledgeMetadata Empty { get; } = new(null, null, null, [], [], []);
}

internal static class TechnologyKnowledgeReader
{
    public static TechnologyKnowledgeView Read(KnowledgeFolderLocation location)
    {
        var folderPath = location.FullPath ?? throw new InvalidOperationException("A technology folder path is required.");
        var rootPath = Path.Combine(folderPath, "technology-graph.md");

        if (!File.Exists(rootPath))
        {
            return TechnologyKnowledgeView.Unavailable(location with
            {
                Message = $"Technology graph root was not found at {rootPath}."
            });
        }

        var root = TechnologyMarkdownParser.Parse(rootPath, File.ReadAllText(rootPath));
        var files = OrderedLayerFiles(folderPath, KnowledgeReadingOrder.ForFolder(folderPath));
        var documents = files.Select(path => TechnologyMarkdownParser.Parse(path, File.ReadAllText(path))).ToList();

        var layers = documents.Select(document => ToLayer(document)).ToList();
        var nodes = layers.SelectMany(layer => layer.Nodes).ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        var relationships = layers
            .SelectMany(layer => layer.Nodes)
            .SelectMany(node => node.DependsOn.Select(target => ToRelationship(node, target, nodes)))
            .ToList();

        var diagrams = root.Diagrams.Count == 0
            ? documents.SelectMany(document => document.Diagrams).ToList()
            : root.Diagrams;

        var index = ReadIndex(Path.Combine(folderPath, "_meta", "graph.json"));

        return new TechnologyKnowledgeView(
            location,
            root.Title.Length == 0 ? "Technology graph" : root.Title,
            root.Summary,
            layers,
            relationships,
            diagrams,
            ToGraph(layers, relationships, index.Boundary),
            index.Stats);
    }

    /// <summary>
    /// The layer files in reading order. <paramref name="order"/> is the folder's
    /// committed reading order, which leads with <c>technology-graph.md</c> — the
    /// root document is the graph itself, not a layer of it, so it is dropped here
    /// exactly as the alphabetical fallback below drops it.
    /// </summary>
    private static IReadOnlyList<string> OrderedLayerFiles(string folderPath, IReadOnlyList<string> order)
    {
        var ordered = order
            .Where(file => file.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Where(file => !string.Equals(file, "technology-graph.md", StringComparison.OrdinalIgnoreCase))
            .Select(file => Path.Combine(folderPath, file))
            .Where(File.Exists)
            .ToList();

        if (ordered.Count > 0) return ordered;

        return Directory.GetFiles(folderPath, "*.md")
            .Where(path => !string.Equals(Path.GetFileName(path), "technology-graph.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static TechnologyLayer ToLayer(TechnologyMarkdownDocument document)
    {
        var fileName = Path.GetFileName(document.Path);
        var title = document.Title.Length == 0 ? Path.GetFileNameWithoutExtension(fileName) : document.Title;

        return new TechnologyLayer(
            fileName,
            title,
            document.Metadata,
            [.. document.Chapters.Select(chapter => ToNode(fileName, title, chapter))]);
    }

    private static TechnologyNode ToNode(string fileName, string layerTitle, TechnologyMarkdownChapter chapter)
    {
        var id = $".tech/{fileName}#{Slug(chapter.Title)}";

        return new TechnologyNode(
            id,
            chapter.Title,
            fileName,
            layerTitle,
            chapter.Metadata,
            chapter.Summary,
            chapter.Metadata.DependsOn,
            chapter.Metadata.Related,
            chapter.Metadata.Alternatives);
    }

    private static TechnologyRelationship ToRelationship(
        TechnologyNode source,
        string target,
        IReadOnlyDictionary<string, TechnologyNode> nodes)
    {
        var label = nodes.TryGetValue(target, out var node)
            ? node.Label
            : target[(target.LastIndexOf('#') + 1)..].Replace('-', ' ');

        return new TechnologyRelationship(source.Id, source.Label, target, label);
    }

    /// <summary>
    /// The parsed layers as one graph.
    ///
    /// <para>Degrees are counted here, from the relationship list, in the direction
    /// the metadata means: a <c>depends-on</c> entry is an edge out of the node that
    /// declares it and into the node it names. In-degree is what sizes a node in the
    /// atlas, so counting it anywhere else would be a second answer to the same
    /// question.</para>
    /// </summary>
    private static TechnologyGraphData ToGraph(
        IReadOnlyList<TechnologyLayer> layers,
        IReadOnlyList<TechnologyRelationship> relationships,
        IReadOnlyDictionary<string, TechnologyBoundaryNode> boundary)
    {
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var outDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var relationship in relationships)
        {
            outDegree[relationship.FromId] = outDegree.GetValueOrDefault(relationship.FromId) + 1;
            inDegree[relationship.ToId] = inDegree.GetValueOrDefault(relationship.ToId) + 1;
        }

        var techStatuses = KnowledgeStatus.Vocabulary(KnowledgeFolder.Tech);
        var graphNodes = new Dictionary<string, TechnologyGraphNode>(StringComparer.OrdinalIgnoreCase);
        var orderedNodes = new List<TechnologyGraphNode>();

        for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            var layer = layers[layerIndex];

            for (var ordinal = 0; ordinal < layer.Nodes.Count; ordinal++)
            {
                var node = layer.Nodes[ordinal];
                var outgoing = outDegree.GetValueOrDefault(node.Id);

                var graphNode = new TechnologyGraphNode(
                    node.Id,
                    node.Label,
                    layer.Title,
                    node.Kind,
                    node.Status,
                    node.Description,
                    layer.FileName,
                    layerIndex,
                    ordinal,
                    inDegree.GetValueOrDefault(node.Id),
                    outgoing,
                    techStatuses.SlugFor(node.Status),
                    // `.tech/technology-graph.md` calls a node with no outgoing edge a
                    // foundation: nothing in this project sits below it.
                    outgoing == 0,
                    false);

                graphNodes[node.Id] = graphNode;
                orderedNodes.Add(graphNode);
            }
        }

        // A `depends-on` target with no chapter of its own is documented, just not
        // here — an .arc42 chapter, a .domain aggregate. It still belongs in the
        // picture, because something in `.tech` leans on it.
        //
        // `_meta/graph.json` already indexes those: a scoped graph carries every
        // out-of-scope node an in-scope one references, flagged and complete. So the
        // label, the folder and the status are read from the index rather than
        // reverse-engineered out of the slug, and the node answers to its own
        // folder's status vocabulary rather than to this one's.
        var boundaryOrdinal = 0;

        foreach (var relationship in relationships)
        {
            if (graphNodes.ContainsKey(relationship.ToId)) continue;

            var known = boundary.GetValueOrDefault(relationship.ToId);
            var folder = KnowledgeFolders.FromPath(relationship.ToId);
            var status = known?.Status ?? string.Empty;

            var graphNode = new TechnologyGraphNode(
                relationship.ToId,
                known?.Label ?? relationship.ToLabel,
                known?.Folder ?? "External reference",
                "external",
                // An absent status is left absent. A boundary node whose index entry
                // carries no status has one fact missing, not a status of "unknown".
                status.Length == 0 ? "unknown" : status,
                string.Empty,
                string.Empty,
                -1,
                boundaryOrdinal++,
                inDegree.GetValueOrDefault(relationship.ToId),
                0,
                status.Length == 0 ? string.Empty : KnowledgeStatus.Vocabulary(folder).SlugFor(status),
                // Not a foundation. A foundation is a technology this project chose to
                // sit on; a boundary node is a chapter somewhere else that happens to be
                // referenced, and calling it one would put .arc42 in the technology stack.
                false,
                true);

            graphNodes[relationship.ToId] = graphNode;
            orderedNodes.Add(graphNode);
        }

        var edges = relationships
            .Select((relationship, index) => new TechnologyGraphEdge(
                $"edge-{index + 1}",
                relationship.FromId,
                relationship.ToId,
                "depends on"))
            .ToList();

        return new TechnologyGraphData(orderedNodes, edges);
    }

    /// <summary>
    /// The derived index beside the Markdown, read once for the two things it
    /// answers: the folder's own counts, and the out-of-scope nodes that
    /// <c>depends-on</c> references reach.
    ///
    /// <para>Absence is tolerated at every level. The index is generated, so a
    /// checkout that has not run the generator yet is a normal state, not a broken
    /// one — the graph is still readable from the Markdown alone, just with
    /// boundary nodes named from their slugs.</para>
    /// </summary>
    private static TechnologyKnowledgeIndex ReadIndex(string path)
    {
        if (!File.Exists(path)) return TechnologyKnowledgeIndex.Empty;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        return new TechnologyKnowledgeIndex(ReadStats(root), ReadBoundary(root));
    }

    private static TechnologyGraphStats ReadStats(JsonElement root)
    {
        if (!root.TryGetProperty("stats", out var stats)) return TechnologyGraphStats.Empty;

        var nodes = stats.TryGetProperty("nodes", out var nodesElement) ? nodesElement.GetInt32() : 0;
        var edges = stats.TryGetProperty("edges", out var edgesElement) ? edgesElement.GetInt32() : 0;
        var statuses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (stats.TryGetProperty("nodesByStatus", out var byStatus))
        {
            foreach (var status in byStatus.EnumerateObject())
            {
                statuses[status.Name] = status.Value.GetInt32();
            }
        }

        return new TechnologyGraphStats(nodes, edges, statuses);
    }

    /// <summary>
    /// The index's out-of-scope entries, keyed by the same reference string a
    /// <c>depends-on</c> field carries. Anything in scope is skipped: those nodes
    /// are parsed from the Markdown itself, which is the authority, and letting the
    /// derived file answer for them would make a stale index able to contradict the
    /// document it was generated from.
    /// </summary>
    private static IReadOnlyDictionary<string, TechnologyBoundaryNode> ReadBoundary(JsonElement root)
    {
        if (!root.TryGetProperty("elements", out var elements)) return TechnologyKnowledgeIndex.NoBoundary;
        if (!elements.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return TechnologyKnowledgeIndex.NoBoundary;
        }

        var boundary = new Dictionary<string, TechnologyBoundaryNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in nodes.EnumerateArray())
        {
            if (!element.TryGetProperty("data", out var data)) continue;
            if (!data.TryGetProperty("outOfScope", out var outOfScope) || !outOfScope.ValueKind.Equals(JsonValueKind.True)) continue;
            if (!data.TryGetProperty("id", out var id) || id.GetString() is not { Length: > 0 } reference) continue;

            boundary[reference] = new TechnologyBoundaryNode(
                data.TryGetProperty("label", out var label) ? label.GetString() ?? string.Empty : string.Empty,
                data.TryGetProperty("folder", out var folder) ? FolderTitle(folder.GetString()) : string.Empty,
                data.TryGetProperty("status", out var status) ? status.GetString() ?? string.Empty : string.Empty);
        }

        return boundary;
    }

    /// <summary>The index writes a folder as the bare word (<c>arc42</c>); the
    /// atlas prints it as the cluster's name, so it is titled here rather than in
    /// the renderer, which has no business knowing what the knowledge folders are
    /// called.</summary>
    private static string FolderTitle(string? folder) => folder?.ToLowerInvariant() switch
    {
        "arc42" => "Architecture",
        "domain" => "Domain",
        "design" => "Design",
        "backlog" => "Backlog",
        "tech" => "Technology",
        null or "" => "External reference",
        _ => char.ToUpperInvariant(folder[0]) + folder[1..]
    };

    /// <summary>
    /// The heading anchor a <c>depends-on</c> reference names.
    ///
    /// <para>This is GitHub's anchor algorithm, and it has to be exactly that:
    /// lowercase, drop punctuation, then turn each remaining whitespace character
    /// into a hyphen — without collapsing runs. Every reference in <c>.tech</c> and
    /// every id in <c>_meta</c> is written by the generator in
    /// <c>.github/tools/knowledge-meta</c>, which uses this rule, so a reader that
    /// used any other rule would compute ids the repository does not use.</para>
    ///
    /// <para>It did. Mapping every non-alphanumeric to a hyphen and collapsing runs
    /// turns <c>ASP.NET Core Minimal APIs</c> into <c>asp-net-core-minimal-apis</c>,
    /// where the repository says <c>aspnet-core-minimal-apis</c> — so two
    /// <c>depends-on</c> edges missed a chapter sitting in the same file, and the
    /// graph invented an external placeholder for a technology it had already
    /// parsed. Punctuation is dropped, not replaced.</para>
    /// </summary>
    private static string Slug(string heading)
    {
        var builder = new System.Text.StringBuilder(heading.Length);

        foreach (var ch in heading.Trim().ToLowerInvariant())
        {
            // `\w` in the generator's expression, which is ASCII word characters
            // plus the underscore.
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
            {
                builder.Append(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                builder.Append('-');
            }
        }

        return builder.ToString();
    }
}

/// <summary>What a boundary node's own folder says about it, read from the derived
/// index. Only the three facts the atlas prints: the rest of that chapter belongs to
/// the folder that owns it.</summary>
internal sealed record TechnologyBoundaryNode(string Label, string Folder, string Status);

/// <summary>The two answers <c>_meta/graph.json</c> holds, read together because it
/// is one file and opening it twice would be two chances to disagree.</summary>
internal sealed record TechnologyKnowledgeIndex(
    TechnologyGraphStats Stats,
    IReadOnlyDictionary<string, TechnologyBoundaryNode> Boundary)
{
    public static readonly IReadOnlyDictionary<string, TechnologyBoundaryNode> NoBoundary =
        new Dictionary<string, TechnologyBoundaryNode>(StringComparer.OrdinalIgnoreCase);

    public static TechnologyKnowledgeIndex Empty { get; } = new(TechnologyGraphStats.Empty, NoBoundary);
}

internal sealed record TechnologyMarkdownDocument(
    string Path,
    string Title,
    KnowledgeMetadata Metadata,
    string Summary,
    IReadOnlyList<TechnologyMarkdownChapter> Chapters,
    IReadOnlyList<TechnologyKnowledgeDiagram> Diagrams);

internal sealed record TechnologyMarkdownChapter(string Title, KnowledgeMetadata Metadata, string Summary);

internal static class TechnologyMarkdownParser
{
    public static TechnologyMarkdownDocument Parse(string path, string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var title = string.Empty;
        var metadata = KnowledgeMetadata.Empty;
        var summary = new List<string>();
        var chapters = new List<TechnologyMarkdownChapter>();
        var diagrams = new List<TechnologyKnowledgeDiagram>();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var marker = line.TrimStart();

            if (title.Length == 0 && marker.StartsWith("# ", StringComparison.Ordinal))
            {
                title = marker[2..].Trim();
                continue;
            }

            if (marker.StartsWith("## ", StringComparison.Ordinal))
            {
                var (chapter, next) = ReadChapter(lines, index);
                chapters.Add(chapter);
                index = next - 1;
                continue;
            }

            if (marker.StartsWith("```meta", StringComparison.OrdinalIgnoreCase))
            {
                var (block, next) = ReadFence(lines, index);
                metadata = ParseMetadata(block);
                index = next;
                continue;
            }

            if (marker.StartsWith("```", StringComparison.Ordinal))
            {
                var language = marker[3..].Trim();
                var (block, next) = ReadFence(lines, index);
                if (language.Length > 0)
                {
                    diagrams.Add(new TechnologyKnowledgeDiagram(title.Length == 0 ? Path.GetFileName(path) : title, language, block));
                }

                index = next;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(line) || summary.Count > 0)
            {
                summary.Add(line);
            }
        }

        return new TechnologyMarkdownDocument(
            path,
            title,
            metadata,
            CleanSummary(summary),
            chapters,
            diagrams);
    }

    private static (TechnologyMarkdownChapter Chapter, int Next) ReadChapter(string[] lines, int start)
    {
        var title = lines[start].TrimStart()[3..].Trim();
        var metadata = KnowledgeMetadata.Empty;
        var body = new List<string>();

        var index = start + 1;
        while (index < lines.Length)
        {
            var line = lines[index];
            var marker = line.TrimStart();
            if (marker.StartsWith("## ", StringComparison.Ordinal)) break;

            if (marker.StartsWith("```meta", StringComparison.OrdinalIgnoreCase))
            {
                var (block, next) = ReadFence(lines, index);
                metadata = ParseMetadata(block);
                index = next + 1;
                continue;
            }

            if (marker.StartsWith("```", StringComparison.Ordinal))
            {
                var (_, next) = ReadFence(lines, index);
                index = next + 1;
                continue;
            }

            body.Add(line);
            index++;
        }

        return (new TechnologyMarkdownChapter(title, metadata, CleanSummary(body)), index);
    }

    private static (string Block, int ClosingLine) ReadFence(string[] lines, int start)
    {
        var body = new List<string>();
        var index = start + 1;

        while (index < lines.Length)
        {
            if (lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                return (string.Join('\n', body), index);
            }

            body.Add(lines[index]);
            index++;
        }

        return (string.Join('\n', body), lines.Length - 1);
    }

    private static KnowledgeMetadata ParseMetadata(string block)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in block.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;

            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return new KnowledgeMetadata(
            ReadString(values, "status"),
            ReadString(values, "kind"),
            ReadString(values, "version"),
            ReadList(values, "depends-on"),
            ReadList(values, "related"),
            ReadList(values, "alternatives"));
    }

    private static string? ReadString(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static IReadOnlyList<string> ReadList(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) return [];

        var normalized = value.Trim();
        if (normalized.StartsWith("[", StringComparison.Ordinal) && normalized.EndsWith("]", StringComparison.Ordinal))
        {
            normalized = normalized[1..^1];
        }

        return normalized
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim().Trim('"'))
            .Where(item => item.Length > 0)
            .ToArray();
    }

    private static string CleanSummary(IEnumerable<string> lines)
    {
        var selected = lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal))
            .Take(2)
            .ToList();

        return selected.Count == 0 ? string.Empty : string.Join(" ", selected);
    }
}

