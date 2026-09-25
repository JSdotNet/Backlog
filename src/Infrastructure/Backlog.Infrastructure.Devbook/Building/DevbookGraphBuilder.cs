using System.Globalization;

namespace Backlog.Infrastructure.Devbook.Building;

/// <summary>
/// The reference graph, as the devbook generator's <c>buildGraph</c> derives it
/// from the <c>meta</c> blocks: one node per file, one per heading that carries a
/// block, one per structural heading something cites, and one per reference that
/// points outside the devbook folders; <c>contains</c> edges down the heading
/// hierarchy, and one edge per <c>depends-on</c>, <c>feature-flag</c>,
/// <c>setting</c> and <c>related</c> entry.
///
/// <para>Only what the database stores is ported. The generator also lints every
/// block and counts open review notes; neither reaches a column, and the lint is
/// the checker's to report (local ADR 0015). A reference that resolves to nothing
/// inside the devbook folders draws no edge, as there — reporting it is the
/// checker's job too.</para>
/// </summary>
internal static class DevbookGraphBuilder
{
    /// <summary>Metadata field → the edge type a reference in it draws.</summary>
    private static readonly (string Field, string EdgeType)[] ReferenceFields =
    [
        ("depends-on", "depends-on"),
        ("feature-flag", "gated-by"),
        ("setting", "configured-by"),
        ("related", "related")
    ];

    public static DevbookGraph Build(string repositoryRoot, DevbookBuildLayout layout)
    {
        var nodes = new List<DevbookGraphNode>();
        var byId = new Dictionary<string, DevbookGraphNode>(StringComparer.Ordinal);
        var edges = new List<DevbookGraphEdge>();
        var headingIndex = new Dictionary<string, (DevbookGraphNode Node, string Parent)>(StringComparer.Ordinal);

        void Add(DevbookGraphNode node)
        {
            nodes.Add(node);
            byId[node.Id] = node;
        }

        foreach (var folder in layout.Folders)
        {
            foreach (var relativePath in DevbookBuildFiles.Markdown(repositoryRoot, folder, skipGenerated: false))
            {
                var folderKind = layout.FolderKindForPath(relativePath);
                var document = DevbookMarkdown.Parse(DevbookBuildFiles.ReadText(repositoryRoot, relativePath));

                var fileMeta = document.Chapters.FirstOrDefault(chapter => chapter.Level == 1)?.Meta;
                var fileNode = new DevbookGraphNode(relativePath, "file")
                {
                    Label = FileLabel(document.FileTitle ?? Path.GetFileNameWithoutExtension(relativePath), DevbookBuildLayout.ResolveType(folderKind, fileMeta)),
                    Folder = folderKind,
                    Path = relativePath
                };
                ApplyMeta(fileNode, fileMeta, folderKind);
                Add(fileNode);

                var ancestors = new List<(int Level, string Id)> { (1, relativePath) };

                foreach (var chapter in document.Chapters)
                {
                    if (chapter.Level == 1) continue;

                    while (ancestors.Count > 0 && ancestors[^1].Level >= chapter.Level) ancestors.RemoveAt(ancestors.Count - 1);
                    var parentId = ancestors.Count > 0 ? ancestors[^1].Id : relativePath;
                    var id = $"{relativePath}#{chapter.Slug}";

                    // The first heading keeps an anchor; a later one that slugs
                    // the same is dropped, as GitHub would suffix it.
                    if (headingIndex.ContainsKey(id)) continue;

                    headingIndex[id] = (new DevbookGraphNode(id, "heading")
                    {
                        Label = chapter.Text,
                        Folder = folderKind,
                        Path = relativePath,
                        Slug = chapter.Slug,
                        Level = chapter.Level,
                        Line = chapter.Line
                    }, parentId);

                    if (chapter.Meta is null) continue;

                    var node = new DevbookGraphNode(id, "chapter")
                    {
                        Label = chapter.Text,
                        Folder = folderKind,
                        Path = relativePath,
                        Slug = chapter.Slug,
                        Level = chapter.Level,
                        Line = chapter.Line
                    };
                    ApplyMeta(node, chapter.Meta, folderKind);
                    Add(node);
                    ancestors.Add((chapter.Level, id));

                    edges.Add(new DevbookGraphEdge($"contains:{parentId}->{id}", "contains", parentId, id));
                }
            }
        }

        // Resolved after every node exists, so a reference may point forward.
        // Iterates by index because a cited heading or an external target is
        // appended as it is found, exactly as the generator's Map iteration does.
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            foreach (var (field, edgeType) in ReferenceFields)
            {
                if (!node.References.TryGetValue(field, out var references)) continue;

                foreach (var reference in references)
                {
                    if (!byId.ContainsKey(reference))
                    {
                        if (headingIndex.TryGetValue(reference, out var heading))
                        {
                            Add(heading.Node);
                            edges.Add(new DevbookGraphEdge($"contains:{heading.Parent}->{reference}", "contains", heading.Parent, reference));
                        }
                        else
                        {
                            var targetPath = reference.Split('#')[0];
                            if (layout.FolderKindForPath(targetPath) is not null) continue;

                            Add(new DevbookGraphNode(reference, "external") { Label = reference, Path = targetPath });
                        }
                    }

                    edges.Add(new DevbookGraphEdge($"{edgeType}:{node.Id}->{reference}", edgeType, node.Id, reference));
                }
            }
        }

        return new DevbookGraph(nodes, edges);
    }

    private static void ApplyMeta(DevbookGraphNode node, IReadOnlyDictionary<string, object?>? meta, string? folder)
    {
        if (meta is null) return;

        var declared = meta.GetValueOrDefault("status");
        node.Status = declared is not null ? Scalar(declared) : DevbookBuildLayout.RestingStatusFor(folder);

        if (DevbookBuildLayout.ResolveType(folder, meta) is { } type) node.Kind = Scalar(type);
        if (meta.GetValueOrDefault("version") is { } version) node.Version = Scalar(version);
        if (meta.GetValueOrDefault("issue") is { } issue) node.Issue = Scalar(issue);

        if (meta.GetValueOrDefault("effort") is string effort
            && effort.Length > 0
            && effort.All(char.IsAsciiDigit)
            && long.TryParse(effort, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            node.Effort = parsed;
        }

        foreach (var (field, _) in ReferenceFields)
        {
            var references = DevbookMarkdown.AsList(meta.GetValueOrDefault(field));
            if (references.Count > 0) node.References[field] = references;
        }

        // The list attributes the database keeps as rows, in the writer's order:
        // `feature-flag` and `roadmap` only when non-empty (the generator's
        // asList), `aliases` and `alternatives` whenever stated.
        if (node.References.TryGetValue("feature-flag", out var flags)) node.Attributes.Add(("feature-flag", flags));

        var roadmap = DevbookMarkdown.AsList(meta.GetValueOrDefault("roadmap"));
        if (roadmap.Count > 0) node.Attributes.Add(("roadmap", roadmap));

        foreach (var name in (string[])["aliases", "alternatives"])
        {
            if (meta.GetValueOrDefault(name) is { } value) node.Attributes.Add((name, DevbookMarkdown.AsList(value)));
        }
    }

    /// <summary>A file's label: its title, and its type in parentheses when the
    /// title does not already say it.</summary>
    private static string FileLabel(string title, object? type)
    {
        if (type is null) return title;
        var text = Scalar(type);
        return type is string && DevbookMarkdown.Slugify(title) == text ? title : $"{title} ({text})";
    }

    /// <summary>A value as JavaScript's <c>String(value)</c> writes it: a list
    /// joins with commas.</summary>
    private static string Scalar(object value) =>
        value is IReadOnlyList<string> list ? string.Join(',', list) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
}

internal sealed record DevbookGraph(IReadOnlyList<DevbookGraphNode> Nodes, IReadOnlyList<DevbookGraphEdge> Edges);

internal sealed class DevbookGraphNode(string id, string type)
{
    public string Id { get; } = id;
    public string Type { get; } = type;
    public string? Label { get; init; }
    public string? Folder { get; init; }
    public string? Path { get; init; }
    public string? Slug { get; init; }
    public int? Level { get; init; }
    public int? Line { get; init; }
    public string? Status { get; set; }
    public long? Effort { get; set; }
    public string? Kind { get; set; }
    public string? Version { get; set; }
    public string? Issue { get; set; }

    /// <summary>Reference fields, by field name, in the order they were written.</summary>
    public Dictionary<string, IReadOnlyList<string>> References { get; } = new(StringComparer.Ordinal);

    /// <summary>The <c>node_attribute</c> rows, one list per attribute name.</summary>
    public List<(string Name, IReadOnlyList<string> Values)> Attributes { get; } = [];
}

internal sealed record DevbookGraphEdge(string Id, string Type, string Source, string Target);
