using System.Text.RegularExpressions;

using Backlog.Modules.Knowledge.Abstractions;

namespace Backlog.Desktop.UI.Knowledge;

public sealed class DomainKnowledgeStore
{
    private readonly IKnowledgeFolderSource source;

    public DomainKnowledgeStore(IKnowledgeFolderSource source)
    {
        this.source = source;
    }

    private static readonly Regex Heading = new("^(#{1,6})[ \\t]+(.+?)\\s*$", RegexOptions.Compiled);
    private static readonly Regex Fence = new("^```(?<lang>[A-Za-z0-9_-]*)\\s*$", RegexOptions.Compiled);
    private static readonly Regex KnowledgeLink = new("\\.(?:domain|arc42|backlog|tech|design)/[^\\s)`>,]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string[] PreferredContextFiles = ["domain.md", "index.md", "features.md", "model.md", "flow.md", "dependencies.md", "naming.md"];

public Task<DomainKnowledgeView> LoadAsync(string? repositoryAlias = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var location = source.Resolve(".domain", repositoryAlias);
        if (!location.Available || location.FullPath is null)
        {
            return Task.FromResult(DomainKnowledgeView.Unavailable(location.Message ?? "Domain knowledge is unavailable."));
        }

        var root = location.FullPath;
        var contextMapPath = Path.Combine(root, "context-map.md");
        if (!File.Exists(contextMapPath)) return Task.FromResult(DomainKnowledgeView.Unavailable($"Domain knowledge folder at {root} has no context-map.md."));

        var contextMap = ReadDocument(contextMapPath, root, DomainKnowledgeDocumentKind.ContextMap);
        var contexts = ReadContexts(root, ReadOrder(contextMap.Metadata).ToList());
        return Task.FromResult(new DomainKnowledgeView(location.ScopeLabel ?? "storage", location.RootPath ?? root, root, null, contextMap, contexts));
    }

    public Task UpdateStatusAsync(string? repositoryAlias, string itemPath, string status, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(itemPath)) throw new ArgumentException("Knowledge item path is required.", nameof(itemPath));
        if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

        var location = source.Resolve(".domain", repositoryAlias);
        if (!location.Available) throw new InvalidOperationException(location.Message ?? "Domain knowledge is unavailable.");
        if (location.FullPath is null) throw new InvalidOperationException("Domain knowledge folder path is unavailable.");

        KnowledgeMarkdownStatusWriter.UpdateStatus(location.FullPath, itemPath, ".domain/", status);
        return Task.CompletedTask;
    }
    private static IReadOnlyList<DomainKnowledgeContext> ReadContexts(string root, IReadOnlyList<string> orderedSlugs)
    {
        var dirs = Directory.EnumerateDirectories(root)
            .Where(p => !Path.GetFileName(p).StartsWith('_')).Select(p => new { Slug = Path.GetFileName(p), Path = p })
            .Where(item => !string.IsNullOrWhiteSpace(item.Slug))
            .ToDictionary(item => item.Slug!, item => item.Path, StringComparer.OrdinalIgnoreCase);
        return [.. orderedSlugs.Concat(dirs.Keys.Order(StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Where(dirs.ContainsKey).Select(slug => ReadContext(slug, dirs[slug], root))];
    }

    private static DomainKnowledgeContext ReadContext(string slug, string path, string root)
    {
        var docs = EnumerateContextDocuments(path).Select(p => ReadDocument(p, root, KindFromFile(Path.GetFileName(p)))).ToList();
        var domain = docs.FirstOrDefault(d => d.Kind == DomainKnowledgeDocumentKind.Domain) ?? docs.FirstOrDefault();
        var name = domain?.Title.Replace("Domain: ", string.Empty, StringComparison.OrdinalIgnoreCase) ?? Humanize(slug);
        return new DomainKnowledgeContext(slug, name, domain?.Status ?? "none", docs);
    }

    private static IReadOnlyList<string> EnumerateContextDocuments(string path)
    {
        var preferredOrder = PreferredContextFiles
            .Select((file, index) => new { file, index })
            .ToDictionary(item => item.file, item => item.index, StringComparer.OrdinalIgnoreCase);

        return [.. Directory.EnumerateFiles(path, "*.md", SearchOption.TopDirectoryOnly)
            .Where(file => !Path.GetFileName(file).StartsWith('_'))
            .OrderBy(file => preferredOrder.TryGetValue(Path.GetFileName(file), out var index) ? index : int.MaxValue)
            .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)];
    }

    private static DomainKnowledgeDocument ReadDocument(string path, string root, DomainKnowledgeDocumentKind kind)
    {
        var relative = ".domain/" + Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
        var lines = File.ReadAllText(path).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var title = Path.GetFileName(path);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sections = new List<DomainKnowledgeSection>();
        var intro = new List<string>();
        var i = 0;
        while (i < lines.Length)
        {
            var match = Heading.Match(lines[i]);
            if (match.Success && match.Groups[1].Value.Length == 1)
            {
                title = match.Groups[2].Value.Trim();
                i++;
                metadata = ReadMeta(lines, ref i);
                break;
            }
            i++;
        }

        var sectionStart = -1;
        while (i < lines.Length)
        {
            var match = Heading.Match(lines[i]);
            if (match.Success && match.Groups[1].Value.Length == 2)
            {
                if (sectionStart >= 0) sections.Add(ReadSection(lines, sectionStart, i, relative));
                sectionStart = i;
            }
            else if (sectionStart < 0)
            {
                intro.Add(lines[i]);
            }
            i++;
        }
        if (sectionStart >= 0) sections.Add(ReadSection(lines, sectionStart, lines.Length, relative));

        var diagrams = new List<DomainKnowledgeDiagram>();
        CollectDiagrams(intro, title, diagrams);
        diagrams.AddRange(sections.SelectMany(s => s.Diagrams));
        var links = new SortedSet<string>(sections.SelectMany(s => s.Links), StringComparer.OrdinalIgnoreCase);
        foreach (var link in metadata.Values.SelectMany(FindLinks)) links.Add(link);
        foreach (var link in FindLinks(string.Join('\n', intro))) links.Add(link);

        return new DomainKnowledgeDocument(relative, title, kind, Status(metadata), metadata, Quote(intro), diagrams, sections, [.. links]);
    }
    private static DomainKnowledgeSection ReadSection(string[] lines, int start, int end, string documentPath)
    {
        var heading = Heading.Match(lines[start]);
        var title = heading.Success ? heading.Groups[2].Value.Trim() : "Section";
        var level = heading.Success ? heading.Groups[1].Value.Length : 2;
        var index = start + 1;
        var metadata = ReadMeta(lines, ref index);
        var body = lines[index..end].ToList();
        var diagrams = new List<DomainKnowledgeDiagram>();
        CollectDiagrams(body, title, diagrams);
        var readable = WithoutFences(body);
        var links = new SortedSet<string>(metadata.Values.SelectMany(FindLinks), StringComparer.OrdinalIgnoreCase);
        foreach (var link in FindLinks(string.Join('\n', readable))) links.Add(link);
        return new DomainKnowledgeSection(title, level, Status(metadata), metadata, Excerpt(readable), diagrams, [.. links], $"{documentPath}#{Slug(title)}");
    }


    private static Dictionary<string, string> ReadMeta(string[] lines, ref int index)
    {
        while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index])) index++;
        if (index >= lines.Length || !string.Equals(lines[index].Trim(), "```meta", StringComparison.OrdinalIgnoreCase)) return [];
        index++;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;
        while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            var rawLine = lines[index];
            var line = rawLine.Trim();
            var sep = line.IndexOf(':');
            if (sep > 0 && !line.StartsWith("-", StringComparison.Ordinal))
            {
                currentKey = line[..sep].Trim();
                result[currentKey] = line[(sep + 1)..].Trim();
            }
            else if (currentKey is not null && line.StartsWith("- ", StringComparison.Ordinal))
            {
                result[currentKey] = AppendMetadataValue(result[currentKey], line[2..].Trim());
            }
            else if (currentKey is not null && char.IsWhiteSpace(rawLine.FirstOrDefault()) && line.Length > 0)
            {
                result[currentKey] = AppendMetadataValue(result[currentKey], line);
            }
            index++;
        }
        if (index < lines.Length) index++;
        while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index])) index++;
        return result;
    }

    private static string AppendMetadataValue(string existing, string next) =>
        string.IsNullOrWhiteSpace(existing) ? next : $"{existing}, {next}";

    private static void CollectDiagrams(IReadOnlyList<string> lines, string title, ICollection<DomainKnowledgeDiagram> diagrams)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var fence = Fence.Match(lines[i].Trim());
            if (!fence.Success) continue;
            var lang = fence.Groups["lang"].Value;
            var code = new List<string>();
            i++;
            while (i < lines.Count && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal)) code.Add(lines[i++]);
            if (string.Equals(lang, "mermaid", StringComparison.OrdinalIgnoreCase)) diagrams.Add(new DomainKnowledgeDiagram(title, MermaidKind(code), string.Join('\n', code), "mermaid"));
        }
    }

    private static IReadOnlyList<string> WithoutFences(IReadOnlyList<string> lines)
    {
        var output = new List<string>();
        var fenced = false;
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { fenced = !fenced; continue; }
            if (!fenced) output.Add(line);
        }
        return output;
    }

    private static IReadOnlyList<string> FindLinks(string text) => [.. KnowledgeLink.Matches(text).Select(m => m.Value.TrimEnd('.', ',', ';', ']')).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static IEnumerable<string> ReadOrder(IReadOnlyDictionary<string, string> metadata)
    {
        if (!metadata.TryGetValue("order", out var order)) yield break;
        foreach (var value in order.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) yield return value.Trim('"', '\'');
    }

    private static string Quote(IEnumerable<string> lines) => string.Join(" ", lines.Select(l => l.Trim()).Where(l => l.StartsWith('>')).Select(l => l.TrimStart('>').Trim()).Where(l => l.Length > 0).Take(2));

    private static string Excerpt(IEnumerable<string> lines) => string.Join(" ", lines.Select(l => l.Trim()).Where(l => l.Length > 0).Where(l => !l.StartsWith('|')).Where(l => !l.StartsWith('#')).Select(l => l.StartsWith('>') ? l.TrimStart('>').Trim() : l.TrimStart('-', '*').Trim()).Where(l => l.Length > 0).Take(3));

    private static string Status(IReadOnlyDictionary<string, string> metadata) => metadata.TryGetValue("status", out var status) && !string.IsNullOrWhiteSpace(status) ? status.Trim().ToLowerInvariant() : "none";

    private static DomainKnowledgeDocumentKind KindFromFile(string file) => file.ToLowerInvariant() switch
    {
        "domain.md" => DomainKnowledgeDocumentKind.Domain,
        "index.md" => DomainKnowledgeDocumentKind.Other,
        "features.md" => DomainKnowledgeDocumentKind.Features,
        "model.md" => DomainKnowledgeDocumentKind.Model,
        "flow.md" => DomainKnowledgeDocumentKind.Flow,
        "dependencies.md" => DomainKnowledgeDocumentKind.Dependencies,
        "naming.md" => DomainKnowledgeDocumentKind.Naming,
        _ => DomainKnowledgeDocumentKind.Other
    };

    private static string MermaidKind(IReadOnlyList<string> lines)
    {
        var first = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "mermaid";
        if (first.StartsWith("classDiagram", StringComparison.OrdinalIgnoreCase)) return "domain model";
        if (first.StartsWith("flowchart", StringComparison.OrdinalIgnoreCase) || first.StartsWith("graph", StringComparison.OrdinalIgnoreCase)) return "context map";
        if (first.StartsWith("stateDiagram", StringComparison.OrdinalIgnoreCase)) return "state flow";
        if (first.StartsWith("sequenceDiagram", StringComparison.OrdinalIgnoreCase)) return "sequence flow";
        return "mermaid";
    }

    private static string Humanize(string slug) => string.Join(' ', slug.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    private static string Slug(string heading) => Regex.Replace(new string(heading.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()), "-+", "-").Trim('-');
}

public sealed record DomainKnowledgeView(string RepositoryLabel, string RepositoryRoot, string RootPath, string? Error, DomainKnowledgeDocument ContextMap, IReadOnlyList<DomainKnowledgeContext> Contexts)
{
    public bool IsReady => Error is null;
    public static DomainKnowledgeView Unavailable(string error) => new(string.Empty, string.Empty, string.Empty, error, DomainKnowledgeDocument.Empty, []);
}

public sealed record DomainKnowledgeContext(string Slug, string DisplayName, string Status, IReadOnlyList<DomainKnowledgeDocument> Documents);

public sealed record DomainKnowledgeDocument(string Path, string Title, DomainKnowledgeDocumentKind Kind, string Status, IReadOnlyDictionary<string, string> Metadata, string Summary, IReadOnlyList<DomainKnowledgeDiagram> Diagrams, IReadOnlyList<DomainKnowledgeSection> Sections, IReadOnlyList<string> Links)
{
    public static DomainKnowledgeDocument Empty { get; } = new(string.Empty, string.Empty, DomainKnowledgeDocumentKind.Other, "none", new Dictionary<string, string>(), string.Empty, [], [], []);
    public string KindLabel => Kind switch
    {
        DomainKnowledgeDocumentKind.ContextMap => "Strategic context map",
        DomainKnowledgeDocumentKind.Domain => "Domain model narrative",
        DomainKnowledgeDocumentKind.Features => "Features",
        DomainKnowledgeDocumentKind.Model => "Structural model",
        DomainKnowledgeDocumentKind.Flow => "Flow",
        DomainKnowledgeDocumentKind.Dependencies => "Dependencies",
        DomainKnowledgeDocumentKind.Naming => "Ubiquitous language",
        _ => "Domain document"
    };
}

public sealed record DomainKnowledgeSection(string Title, int Level, string Status, IReadOnlyDictionary<string, string> Metadata, string Excerpt, IReadOnlyList<DomainKnowledgeDiagram> Diagrams, IReadOnlyList<string> Links, string Anchor);
public sealed record DomainKnowledgeDiagram(string Title, string Kind, string Source, string Language);

public enum DomainKnowledgeDocumentKind
{
    ContextMap,
    Domain,
    Features,
    Model,
    Flow,
    Dependencies,
    Naming,
    Other
}
