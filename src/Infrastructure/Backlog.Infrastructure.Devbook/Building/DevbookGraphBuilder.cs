using System.Globalization;
using System.Text.RegularExpressions;

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
internal static partial class DevbookGraphBuilder
{
    /// <summary>The scheme a link into a repository devbook starts with (local
    /// ADR 0027 §6).</summary>
    public const string RepoScheme = "repo:";

    /// <summary>The edge a <c>repo:</c> link in a Brain note's prose draws.</summary>
    public const string RepoLinkEdge = "repo-link";

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
        var documents = new List<DevbookC4Graph.Document>();

        void Add(DevbookGraphNode node)
        {
            nodes.Add(node);
            byId[node.Id] = node;
        }

        foreach (var folder in layout.Folders)
        {
            foreach (var relativePath in DevbookBuildFiles.Markdown(repositoryRoot, folder, skipGenerated: false, layout.IsBrain))
            {
                var folderKind = layout.FolderKindForPath(relativePath);
                var conventionFolder = layout.ConventionFolderForPath(relativePath);

                // A cloud-only file is a node by its name alone until it is
                // hydrated (DevbookBuildFiles.IsCloudOnly).
                var markdown = DevbookBuildFiles.ReadHydratedText(repositoryRoot, relativePath);
                var document = markdown is null ? new DevbookParsedDocument(null, null, []) : DevbookMarkdown.Parse(markdown);

                if (markdown is not null) documents.Add(new DevbookC4Graph.Document(relativePath, markdown, document.Chapters));

                var fileMeta = document.Chapters.FirstOrDefault(chapter => chapter.Level == 1)?.Meta;
                var fileNode = new DevbookGraphNode(relativePath, "file")
                {
                    Label = FileLabel(document.FileTitle ?? Path.GetFileNameWithoutExtension(relativePath), DevbookBuildLayout.ResolveType(conventionFolder, fileMeta)),
                    Folder = folderKind,
                    Path = relativePath
                };
                ApplyMeta(fileNode, fileMeta, conventionFolder, DevbookBuildLayout.IsDebtRecord(layout, relativePath, fileMeta));
                if (layout.IsBrain) ApplyBrainFields(fileNode, fileMeta, folderKind);
                Add(fileNode);

                var ancestors = new List<(int Level, string Id)> { (1, relativePath) };

                // The node each heading's slice belongs to, for the Brain's prose
                // links: the heading itself when it is a chapter, else the
                // chapter it sits in, else the file.
                var owners = new List<(int Line, string Id)>();

                foreach (var chapter in document.Chapters)
                {
                    if (chapter.Level == 1)
                    {
                        owners.Add((chapter.Line, relativePath));
                        continue;
                    }

                    while (ancestors.Count > 0 && ancestors[^1].Level >= chapter.Level) ancestors.RemoveAt(ancestors.Count - 1);
                    var parentId = ancestors.Count > 0 ? ancestors[^1].Id : relativePath;
                    var id = $"{relativePath}#{chapter.Slug}";

                    // The first heading keeps an anchor; a later one that slugs
                    // the same is dropped, as GitHub would suffix it.
                    if (headingIndex.ContainsKey(id))
                    {
                        owners.Add((chapter.Line, parentId));
                        continue;
                    }

                    headingIndex[id] = (new DevbookGraphNode(id, "heading")
                    {
                        Label = chapter.Text,
                        Folder = folderKind,
                        Path = relativePath,
                        Slug = chapter.Slug,
                        Level = chapter.Level,
                        Line = chapter.Line
                    }, parentId);

                    if (chapter.Meta is null)
                    {
                        owners.Add((chapter.Line, parentId));
                        continue;
                    }

                    var node = new DevbookGraphNode(id, "chapter")
                    {
                        Label = chapter.Text,
                        Folder = folderKind,
                        Path = relativePath,
                        Slug = chapter.Slug,
                        Level = chapter.Level,
                        Line = chapter.Line
                    };
                    ApplyMeta(node, chapter.Meta, conventionFolder);
                    if (layout.IsBrain) ApplyBrainFields(node, chapter.Meta, area: null);
                    Add(node);
                    ancestors.Add((chapter.Level, id));
                    owners.Add((chapter.Line, id));

                    edges.Add(new DevbookGraphEdge($"contains:{parentId}->{id}", "contains", parentId, id));
                }

                if (layout.IsBrain && markdown is not null) edges.AddRange(RepoLinkEdges(relativePath, markdown, owners));
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
                        else if (layout.IsBrain && reference.StartsWith(RepoScheme, StringComparison.Ordinal))
                        {
                            // A link into a repository devbook: nothing in a
                            // Brain's database can resolve it, so the edge is
                            // kept and its target is no node.
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

        // The C4 model last, against every node the chapters made (contract 33).
        var (c4Nodes, c4Edges) = DevbookC4Graph.Build(repositoryRoot, layout, documents, [.. nodes], byId);
        foreach (var node in c4Nodes) Add(node);
        edges.AddRange(c4Edges);

        return new DevbookGraph(nodes, edges);
    }

    /// <param name="debtRecord">Whether the block is a debt record's top-level one,
    /// which rests at <c>identified</c> rather than its folder's value.</param>
    private static void ApplyMeta(DevbookGraphNode node, IReadOnlyDictionary<string, object?>? meta, string? folder, bool debtRecord = false)
    {
        if (meta is null) return;

        var declared = meta.GetValueOrDefault("status");
        node.Status = declared is not null ? Scalar(declared) : DevbookBuildLayout.RestingStatusFor(folder, debtRecord);

        if (DevbookBuildLayout.ResolveType(folder, meta) is { } type) node.Kind = Scalar(type);
        if (meta.GetValueOrDefault("version") is { } version) node.Version = Scalar(version);
        if (meta.GetValueOrDefault("issue") is { } issue) node.Issue = Scalar(issue);
        if (meta.GetValueOrDefault("sync") is { } sync) node.Sync = Scalar(sync);

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
        // asList), `aliases` and `alternatives` whenever stated, then `tests`
        // only when non-empty — the test identifiers a requirement or invariant
        // is proved by, which the Domain view reads (local ADR 0040). Only the
        // authored entries: the generator also appends an `e2e:playwright` entry
        // for each `Proved by:` pointer whose scenario page a spec implements,
        // and that derivation is not ported — this corpus has no such spec yet,
        // and DevbookBuilderParityTests goes red the day one lands.
        if (node.References.TryGetValue("feature-flag", out var flags)) node.Attributes.Add(("feature-flag", flags));

        var roadmap = DevbookMarkdown.AsList(meta.GetValueOrDefault("roadmap"));
        if (roadmap.Count > 0) node.Attributes.Add(("roadmap", roadmap));

        foreach (var name in (string[])["aliases", "alternatives"])
        {
            if (meta.GetValueOrDefault(name) is { } value) node.Attributes.Add((name, DevbookMarkdown.AsList(value)));
        }

        var tests = DevbookMarkdown.AsList(meta.GetValueOrDefault("tests"));
        if (tests.Count > 0) node.Attributes.Add(("tests", tests));

        // The workspace elements a block names: edges, drawn once the model is read.
        foreach (var field in DevbookC4Graph.Fields)
        {
            var entries = DevbookMarkdown.AsList(meta.GetValueOrDefault(field));
            if (entries.Count > 0) node.C4Fields[field] = entries;
        }
    }

    /// <summary>
    /// A Brain note's own fields as node attributes: <c>para</c> — the note's
    /// area, from <paramref name="area"/> unless the block overrides it; a chapter
    /// passes no area and carries one only when it says so — and the
    /// <c>repos</c> and <c>tasks</c> it names.
    /// </summary>
    private static void ApplyBrainFields(DevbookGraphNode node, IReadOnlyDictionary<string, object?>? meta, string? area)
    {
        var para = meta?.GetValueOrDefault("para") is { } declared ? Scalar(declared) : area;
        if (!string.IsNullOrEmpty(para)) node.Attributes.Add(("para", [para]));

        foreach (var name in (string[])["repos", "tasks"])
        {
            var values = DevbookMarkdown.AsList(meta?.GetValueOrDefault(name));
            if (values.Count > 0) node.Attributes.Add((name, values));
        }
    }

    /// <summary>
    /// One <c>repo-link</c> edge per <c>repo:</c> link in a note's prose, outside
    /// every fence, from the node whose slice holds it — the last heading at or
    /// above the line in <paramref name="owners"/>, or the file — once per source
    /// and target. <c>tools/devbook/brain.mjs</c>'s <c>repoLinkEdges</c> is the
    /// same walk.
    /// </summary>
    private static IEnumerable<DevbookGraphEdge> RepoLinkEdges(string relativePath, string markdown, List<(int Line, string Id)> owners)
    {
        var lines = DevbookMarkdown.Lines(markdown);
        var fenced = DevbookChapterText.FenceMask(lines);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var owner = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            while (owner < owners.Count && owners[owner].Line <= index + 1) owner++;
            if (fenced[index]) continue;

            var source = owner == 0 ? relativePath : owners[owner - 1].Id;
            foreach (Match match in RepoLink().Matches(lines[index]))
            {
                var target = match.Groups[1].Value;
                var id = $"{RepoLinkEdge}:{source}->{target}";
                if (seen.Add(id)) yield return new DevbookGraphEdge(id, RepoLinkEdge, source, target);
            }
        }
    }

    [GeneratedRegex(@"\]\(\s*<?(repo:[^\s)>]+)>?")]
    private static partial Regex RepoLink();

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

    /// <summary>The <c>sync</c> direction the block states, as written.</summary>
    public string? Sync { get; set; }

    /// <summary>Reference fields, by field name, in the order they were written.</summary>
    public Dictionary<string, IReadOnlyList<string>> References { get; } = new(StringComparer.Ordinal);

    /// <summary>The <c>node_attribute</c> rows, one list per attribute name.</summary>
    public List<(string Name, IReadOnlyList<string> Values)> Attributes { get; } = [];

    /// <summary>The <c>c4</c> and <c>c4-touches</c> entries, as written: no row of
    /// their own, the source of an edge each to the element they name.</summary>
    public Dictionary<string, IReadOnlyList<string>> C4Fields { get; } = new(StringComparer.Ordinal);
}

internal sealed record DevbookGraphEdge(string Id, string Type, string Source, string Target);
