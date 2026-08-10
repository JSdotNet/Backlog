using System.Text.RegularExpressions;

namespace Backlog.Desktop.UI.Services;

public sealed class KnowledgeBacklog(KnowledgeFolderSource source)
{
    public Task<BacklogKnowledgeView> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var location = source.Resolve(".backlog");
        if (!location.Available || location.FullPath is null)
        {
            return Task.FromResult(BacklogKnowledgeView.NotConfigured(location.Message ?? "Repository .backlog knowledge is unavailable."));
        }

        var repositoryRoot = location.Repository?.CloneDirectory ?? location.FullPath;
        var files = Directory.EnumerateFiles(location.FullPath, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(file => BacklogKnowledgeParser.ParseFile(file, repositoryRoot))
            .Where(file => file.Items.Count > 0 || file.Metadata.Count > 0)
            .ToList();

        return Task.FromResult(BacklogKnowledgeView.Ready(
            location.Repository?.FullName ?? "Configured repository",
            location.FullPath,
            files));
    }
}

public sealed record BacklogKnowledgeView(
    string? RepositoryFullName,
    string? Directory,
    IReadOnlyList<BacklogConcernKnowledge> Concerns,
    string? Message,
    bool IsMissing)
{
    public bool IsReady => Message is null;

    public static BacklogKnowledgeView Ready(string repositoryFullName, string directory, IReadOnlyList<BacklogConcernKnowledge> concerns) =>
        new(repositoryFullName, directory, concerns, null, false);

    public static BacklogKnowledgeView NotConfigured(string message) =>
        new(null, null, [], message, false);

    public static BacklogKnowledgeView Missing(string repositoryFullName, string directory) =>
        new(repositoryFullName, directory, [], $"No .backlog folder was found at {directory}.", true);
}

public sealed record BacklogConcernKnowledge(
    string Title,
    string RelativePath,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<BacklogWorkItemKnowledge> Items);

public sealed record BacklogWorkItemKnowledge(
    string Title,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<MdBlock> Description,
    IReadOnlyList<BacklogWorkItemKnowledge> SubItems);

public static class BacklogKnowledgeParser
{
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})[ \t]+(.+?)\s*$", RegexOptions.Compiled);

    public static BacklogConcernKnowledge ParseFile(string path, string repositoryRoot)
    {
        var lines = File.ReadAllText(path).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var title = Path.GetFileNameWithoutExtension(path);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<BacklogWorkItemKnowledge>();

        for (var index = 0; index < lines.Length; index++)
        {
            var heading = HeadingRegex.Match(lines[index]);
            if (!heading.Success) continue;

            var level = heading.Groups[1].Value.Length;
            if (level == 1)
            {
                title = heading.Groups[2].Value.Trim();
                var metaStart = index + 1;
                metadata = ReadMetadata(lines, ref metaStart);
                index = metaStart - 1;
            }
            else if (level == 2)
            {
                var itemStart = index;
                var next = FindNextHeading(lines, itemStart + 1, maxLevel: 2);
                items.Add(ParseItem(lines, itemStart, next));
                index = next - 1;
            }
        }

        return new BacklogConcernKnowledge(title, RelativePath(path, repositoryRoot), metadata, items);
    }

    private static BacklogWorkItemKnowledge ParseItem(string[] lines, int headingIndex, int endIndex)
    {
        var heading = HeadingRegex.Match(lines[headingIndex]);
        var title = heading.Groups[2].Value.Trim();
        var cursor = headingIndex + 1;
        var metadata = ReadMetadata(lines, ref cursor);
        var descriptionEnd = FindNextHeading(lines, cursor, maxLevel: 3, upperBound: endIndex);
        var description = MarkdownPreview.Parse(Slice(lines, cursor, descriptionEnd));
        var subItems = new List<BacklogWorkItemKnowledge>();

        for (var index = descriptionEnd; index < endIndex; index++)
        {
            var subHeading = HeadingRegex.Match(lines[index]);
            if (!subHeading.Success || subHeading.Groups[1].Value.Length != 3) continue;

            var next = FindNextHeading(lines, index + 1, maxLevel: 3, upperBound: endIndex);
            subItems.Add(ParseItem(lines, index, next));
            index = next - 1;
        }

        return new BacklogWorkItemKnowledge(title, metadata, description, subItems);
    }

    private static Dictionary<string, string> ReadMetadata(string[] lines, ref int index)
    {
        while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index])) index++;

        if (index >= lines.Length || !lines[index].Trim().Equals("```meta", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        index++;
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;
        while (index < lines.Length && !lines[index].Trim().Equals("```", StringComparison.Ordinal))
        {
            var rawLine = lines[index];
            var line = rawLine.Trim();
            var separator = line.IndexOf(':');
            if (separator > 0 && !line.StartsWith("-", StringComparison.Ordinal))
            {
                currentKey = line[..separator].Trim();
                metadata[currentKey] = line[(separator + 1)..].Trim();
            }
            else if (currentKey is not null && line.StartsWith("- ", StringComparison.Ordinal))
            {
                metadata[currentKey] = AppendMetadataValue(metadata[currentKey], line[2..].Trim());
            }
            else if (currentKey is not null && char.IsWhiteSpace(rawLine.FirstOrDefault()) && line.Length > 0)
            {
                metadata[currentKey] = AppendMetadataValue(metadata[currentKey], line);
            }

            index++;
        }

        if (index < lines.Length) index++;
        return metadata;
    }

    private static string AppendMetadataValue(string existing, string next) =>
        string.IsNullOrWhiteSpace(existing) ? next : $"{existing}, {next}";
    private static int FindNextHeading(string[] lines, int start, int maxLevel, int? upperBound = null)
    {
        var end = upperBound ?? lines.Length;
        for (var index = start; index < end; index++)
        {
            var heading = HeadingRegex.Match(lines[index]);
            if (heading.Success && heading.Groups[1].Value.Length <= maxLevel) return index;
        }

        return end;
    }

    private static string Slice(string[] lines, int start, int end) =>
        start >= end ? string.Empty : string.Join('\n', lines[start..end]).Trim();

    private static string RelativePath(string path, string repositoryRoot)
    {
        var relative = Path.GetRelativePath(repositoryRoot, path);
        return relative.StartsWith(".", StringComparison.Ordinal) ? relative : $".{Path.DirectorySeparatorChar}{relative}";
    }
}
