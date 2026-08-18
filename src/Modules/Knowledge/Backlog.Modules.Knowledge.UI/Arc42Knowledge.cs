using System.Text.Json;
using System.Text.RegularExpressions;

using Backlog.UI.Components.Markdown;

using Backlog.Modules.Knowledge.Abstractions;

namespace Backlog.Desktop.UI.Knowledge;

public sealed class Arc42KnowledgeStore(IKnowledgeFolderSource source)
{
    public event Action? Changed
    {
        add => source.Changed += value;
        remove => source.Changed -= value;
    }

    public Task<Arc42KnowledgeCatalog> LoadAsync(string? repositoryAlias = null)
    {
        var location = source.Resolve(".arc42", repositoryAlias);
        if (!location.Available || location.FullPath is null)
        {
            return Task.FromResult(Arc42KnowledgeCatalog.Missing(location.RootPath ?? location.FullPath ?? string.Empty));
        }

        return Arc42KnowledgeReader.LoadFolderAsync(location.FullPath);
    }

    public Task UpdateStatusAsync(string? repositoryAlias, string itemPath, string status, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(itemPath)) throw new ArgumentException("Knowledge item path is required.", nameof(itemPath));
        if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

        var location = source.Resolve(".arc42", repositoryAlias);
        if (!location.Available) throw new InvalidOperationException(location.Message ?? "Architecture knowledge is unavailable.");
        if (location.FullPath is null) throw new InvalidOperationException("Architecture knowledge folder path is unavailable.");

        KnowledgeMarkdownStatusWriter.UpdateStatus(location.FullPath, itemPath, ".arc42/", status);
        return Task.CompletedTask;
    }
}

public static class Arc42KnowledgeReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task<Arc42KnowledgeCatalog> LoadAsync(string rootDirectory) =>
        LoadFolderAsync(Path.Combine(rootDirectory, ".arc42"));

    public static async Task<Arc42KnowledgeCatalog> LoadFolderAsync(string arc42Directory)
    {
        var rootDirectory = Directory.GetParent(arc42Directory)?.FullName ?? arc42Directory;
        if (!Directory.Exists(arc42Directory))
        {
            return Arc42KnowledgeCatalog.Missing(rootDirectory);
        }

        var indexPath = Path.Combine(arc42Directory, "_meta", "index.json");
        var documentPaths = File.Exists(indexPath)
            ? await ReadIndexedPathsAsync(indexPath, rootDirectory)
            : Directory.EnumerateFiles(arc42Directory, "*.md", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetRelativePath(rootDirectory, path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

        var documents = new List<KnowledgeDocument>();
        foreach (var relativePath in documentPaths)
        {
            var fullPath = Path.Combine(rootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath)) continue;

            var markdown = await File.ReadAllTextAsync(fullPath);
            documents.Add(KnowledgeMarkdownParser.Parse(relativePath.Replace('\\', '/'), markdown));
        }

        return new Arc42KnowledgeCatalog(rootDirectory, true, documents);
    }

    private static async Task<List<string>> ReadIndexedPathsAsync(string indexPath, string rootDirectory)
    {
        await using var stream = File.OpenRead(indexPath);
        var index = await JsonSerializer.DeserializeAsync<KnowledgeIndex>(stream, JsonOptions);
        var paths = new List<string>();

        if (index?.Entries is not null)
        {
            CollectMarkdownPaths(index.Entries, paths);
        }

        return paths
            .Where(path => File.Exists(Path.Combine(rootDirectory, path.Replace('/', Path.DirectorySeparatorChar))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CollectMarkdownPaths(IEnumerable<KnowledgeIndexEntry> entries, List<string> paths)
    {
        foreach (var entry in entries)
        {
            if (string.Equals(entry.Type, "file", StringComparison.OrdinalIgnoreCase) && entry.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(entry.Path);
            }

            if (entry.Children is { Count: > 0 })
            {
                CollectMarkdownPaths(entry.Children, paths);
            }
        }
    }

    private sealed class KnowledgeIndex
    {
        public List<KnowledgeIndexEntry>? Entries { get; set; }
    }

    private sealed class KnowledgeIndexEntry
    {
        public string Type { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public List<KnowledgeIndexEntry>? Children { get; set; }
    }
}

public sealed record Arc42KnowledgeCatalog(string RootDirectory, bool Exists, IReadOnlyList<KnowledgeDocument> Documents)
{
    public static Arc42KnowledgeCatalog Missing(string rootDirectory) => new(rootDirectory, false, []);

    public int DiagramCount => Documents.Sum(document => document.DiagramCount);

    public int DecisionRecordCount => Documents.Count(document => IsDecisionRecord(document.Path));

    public static bool IsDecisionRecord(string path) =>
        path.StartsWith(".arc42/adr/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(".arc42/tdr/", StringComparison.OrdinalIgnoreCase);
}

public sealed record KnowledgeDocument(
    string Path,
    string Title,
    KnowledgeMeta Metadata,
    IReadOnlyList<KnowledgeBlock> Blocks,
    IReadOnlyList<KnowledgeHeadingSummary> Headings,
    int DiagramCount)
{
    public string Status => Metadata.Status;

    public IReadOnlyList<KnowledgeBlock> ContentBlocks => Blocks.FirstOrDefault() is KnowledgeHeadingBlock { Level: 1 } heading
        && string.Equals(heading.Text, Title, StringComparison.Ordinal)
            ? Blocks.Skip(1).ToList()
            : Blocks;
}

public sealed record KnowledgeMeta(string Status, IReadOnlyList<string> Related)
{
    public static KnowledgeMeta Empty { get; } = new("draft", []);
}

public sealed record KnowledgeHeadingSummary(int Level, string Text, KnowledgeMeta Metadata);

public abstract record KnowledgeBlock;

public sealed record KnowledgeHeadingBlock(int Level, string Text, KnowledgeMeta Metadata) : KnowledgeBlock;

public sealed record KnowledgeParagraphBlock(IReadOnlyList<MdInline> Content) : KnowledgeBlock;

public sealed record KnowledgeListBlock(bool Ordered, IReadOnlyList<IReadOnlyList<MdInline>> Items) : KnowledgeBlock;

public sealed record KnowledgeQuoteBlock(IReadOnlyList<MdInline> Content) : KnowledgeBlock;

public sealed record KnowledgeCodeBlock(string Language, string Text) : KnowledgeBlock;

public sealed record KnowledgeDiagramBlock(string Language, string Text, string Title) : KnowledgeBlock;

public sealed record KnowledgeTableBlock(IReadOnlyList<IReadOnlyList<MdInline>> Rows) : KnowledgeBlock;

public sealed record KnowledgeDividerBlock : KnowledgeBlock;

public static class KnowledgeMarkdownParser
{
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})[ \t]+(.+)$", RegexOptions.Compiled);
    private static readonly Regex UnorderedRegex = new(@"^[ \t]*[-*][ \t]+(.+)$", RegexOptions.Compiled);
    private static readonly Regex OrderedRegex = new(@"^[ \t]*\d+[.)][ \t]+(.+)$", RegexOptions.Compiled);

    public static KnowledgeDocument Parse(string path, string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var blocks = new List<KnowledgeBlock>();
        var headings = new List<KnowledgeHeadingSummary>();
        var paragraph = new List<string>();
        var listItems = new List<IReadOnlyList<MdInline>>();
        bool? orderedList = null;
        var title = Path.GetFileNameWithoutExtension(path);
        var documentMeta = KnowledgeMeta.Empty;
        var diagramCount = 0;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new KnowledgeParagraphBlock(MarkdownPreview.ParseInlines(string.Join(" ", paragraph))));
            paragraph.Clear();
        }

        void FlushList()
        {
            if (listItems.Count == 0) return;
            blocks.Add(new KnowledgeListBlock(orderedList ?? false, [.. listItems]));
            listItems.Clear();
            orderedList = null;
        }

        void FlushAll()
        {
            FlushParagraph();
            FlushList();
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushAll();
                var language = trimmed[3..].Trim();
                var code = new List<string>();
                index++;
                while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    code.Add(lines[index]);
                    index++;
                }

                var text = string.Join('\n', code);
                if (IsDiagramLanguage(language))
                {
                    diagramCount++;
                    blocks.Add(new KnowledgeDiagramBlock(language, text, DiagramTitle(text, diagramCount)));
                }
                else
                {
                    blocks.Add(new KnowledgeCodeBlock(language, text));
                }

                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushAll();
                continue;
            }

            var heading = HeadingRegex.Match(trimmed);
            if (heading.Success)
            {
                FlushAll();
                var level = heading.Groups[1].Value.Length;
                var text = heading.Groups[2].Value.Trim();
                var (metadata, nextIndex) = ReadMetadata(lines, index + 1);
                index = nextIndex;

                if (level == 1)
                {
                    title = text;
                    documentMeta = metadata;
                }

                blocks.Add(new KnowledgeHeadingBlock(level, text, metadata));
                headings.Add(new KnowledgeHeadingSummary(level, text, metadata));
                continue;
            }

            if (IsTableStart(lines, index))
            {
                FlushAll();
                var tableLines = new List<string>();
                while (index < lines.Length && IsTableLine(lines[index]))
                {
                    var row = lines[index].Trim();
                    if (!IsTableSeparator(row)) tableLines.Add(row);
                    index++;
                }

                index--;
                blocks.Add(new KnowledgeTableBlock([.. tableLines.Select(ParseTableRow)]));
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                FlushAll();
                blocks.Add(new KnowledgeQuoteBlock(MarkdownPreview.ParseInlines(trimmed[2..])));
                continue;
            }

            if (trimmed is "---" or "***" or "___")
            {
                FlushAll();
                blocks.Add(new KnowledgeDividerBlock());
                continue;
            }

            var unordered = UnorderedRegex.Match(line);
            if (unordered.Success)
            {
                FlushParagraph();
                if (orderedList is true) FlushList();
                orderedList = false;
                listItems.Add(MarkdownPreview.ParseInlines(unordered.Groups[1].Value));
                continue;
            }

            var ordered = OrderedRegex.Match(line);
            if (ordered.Success)
            {
                FlushParagraph();
                if (orderedList is false) FlushList();
                orderedList = true;
                listItems.Add(MarkdownPreview.ParseInlines(ordered.Groups[1].Value));
                continue;
            }

            FlushList();
            paragraph.Add(trimmed);
        }

        FlushAll();
        return new KnowledgeDocument(path, title, documentMeta, blocks, headings, diagramCount);
    }

    private static (KnowledgeMeta Metadata, int NextIndex) ReadMetadata(string[] lines, int startIndex)
    {
        var index = startIndex;
        while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index])) index++;

        if (index >= lines.Length || !string.Equals(lines[index].Trim(), "```meta", StringComparison.Ordinal))
        {
            return (KnowledgeMeta.Empty, startIndex - 1);
        }

        var metaLines = new List<string>();
        index++;
        while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            metaLines.Add(lines[index].Trim());
            index++;
        }

        return (ParseMetadata(metaLines), index);
    }

    private static KnowledgeMeta ParseMetadata(IEnumerable<string> lines)
    {
        var status = "draft";
        var related = new List<string>();
        var readingRelated = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("status:", StringComparison.OrdinalIgnoreCase))
            {
                status = line["status:".Length..].Trim().Trim('"', '\'');
                readingRelated = false;
            }
            else if (line.StartsWith("related:", StringComparison.OrdinalIgnoreCase))
            {
                related.AddRange(ParseInlineList(line["related:".Length..].Trim()));
                readingRelated = true;
            }
            else if (readingRelated && line.StartsWith("- ", StringComparison.Ordinal))
            {
                related.Add(line[2..].Trim().Trim('"', '\''));
            }
            else
            {
                readingRelated = false;
            }
        }

        return new KnowledgeMeta(status, related.Distinct(StringComparer.Ordinal).ToList());
    }

    private static IEnumerable<string> ParseInlineList(string value)
    {
        if (!value.StartsWith('[') || !value.EndsWith(']'))
        {
            if (!string.IsNullOrWhiteSpace(value)) yield return value.Trim('"', '\'');
            yield break;
        }

        foreach (var item in value[1..^1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            yield return item.Trim().Trim('"', '\'');
        }
    }

    private static bool IsDiagramLanguage(string language) =>
        language.Equals("mermaid", StringComparison.OrdinalIgnoreCase) ||
        language.StartsWith("c4", StringComparison.OrdinalIgnoreCase);

    private static string DiagramTitle(string text, int number)
    {
        var title = text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith("title ", StringComparison.OrdinalIgnoreCase));
        return title is null ? $"Architecture diagram {number}" : title["title ".Length..].Trim();
    }

    private static bool IsTableStart(string[] lines, int index) =>
        index + 1 < lines.Length && IsTableLine(lines[index]) && IsTableSeparator(lines[index + 1].Trim());

    private static bool IsTableLine(string line) => line.Trim().StartsWith('|') && line.Trim().EndsWith('|');

    private static bool IsTableSeparator(string line)
    {
        var cleaned = line.Trim().Trim('|').Replace(" ", string.Empty);
        return cleaned.Length > 0 && cleaned.Split('|').All(cell => cell.Length >= 3 && cell.All(c => c is '-' or ':'));
    }

    private static IReadOnlyList<MdInline> ParseTableRow(string line) =>
        MarkdownPreview.ParseInlines(string.Join(" | ", line.Trim().Trim('|').Split('|', StringSplitOptions.TrimEntries)));
}


