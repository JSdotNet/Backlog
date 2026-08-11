using System.Text.Json;

namespace Backlog.Desktop.UI.Services;

public sealed class TechnologyKnowledgeService(KnowledgeFolderSource source)
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
}

public sealed record TechnologyKnowledgeView(
    KnowledgeFolderLocation Location,
    string Title,
    string Summary,
    IReadOnlyList<TechnologyLayer> Layers,
    IReadOnlyList<TechnologyRelationship> Relationships,
    IReadOnlyList<TechnologyKnowledgeDiagram> Diagrams,
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
    IReadOnlyList<string> Alternatives,
    IReadOnlyList<string> Order)
{
    public static KnowledgeMetadata Empty { get; } = new(null, null, null, [], [], [], []);
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
        var files = OrderedLayerFiles(folderPath, root.Metadata.Order);
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

        return new TechnologyKnowledgeView(
            location,
            root.Title.Length == 0 ? "Technology graph" : root.Title,
            root.Summary,
            layers,
            relationships,
            diagrams,
            ReadStats(Path.Combine(folderPath, "_meta", "graph.json")));
    }

    private static IReadOnlyList<string> OrderedLayerFiles(string folderPath, IReadOnlyList<string> order)
    {
        var ordered = order
            .Where(file => file.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
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

    private static TechnologyGraphStats ReadStats(string path)
    {
        if (!File.Exists(path)) return TechnologyGraphStats.Empty;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("stats", out var stats)) return TechnologyGraphStats.Empty;

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

    private static string Slug(string heading)
    {
        var chars = heading
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
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
            ReadList(values, "alternatives"),
            ReadList(values, "order"));
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

