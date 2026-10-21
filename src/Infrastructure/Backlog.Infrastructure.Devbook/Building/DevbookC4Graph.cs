using System.Text.RegularExpressions;

using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Devbook.Building;

/// <summary>
/// The arc42 folder's C4 model in the reference graph, as devbook's
/// <c>c4-graph.mjs</c> puts it there (contract 33): every element of
/// <c>.devbook/arc42/workspace.dsl</c> an <c>element</c> node and every view a
/// <c>view</c> node; an element's parent <c>contains</c> it and a relationship is a
/// <c>relationship</c> edge; a section embedding <c>![…](c4:&lt;key&gt;)</c> has an
/// <c>embeds</c> edge to the view, and a block's <c>c4</c> and <c>c4-touches</c>
/// entries an edge of that name to each element they name.
///
/// <para>The checks <c>c4-graph.mjs</c> runs beside this — a description per
/// element, a view embedded once and in its own chapter, a <c>code</c> folder that
/// exists — are the checker's, and draw nothing.</para>
/// </summary>
internal static partial class DevbookC4Graph
{
    /// <summary>The one workspace, repository-relative.</summary>
    public const string WorkspacePath = DevbookFolderSetting.DevbookRoot + "/arc42/workspace.dsl";

    /// <summary>The fields whose entries name workspace elements, in the generator's
    /// order (metadata.mjs <c>C4_FIELDS</c>).</summary>
    public static readonly string[] Fields = ["c4", "c4-touches"];

    public static string ElementNodeId(string id) => $"c4:{id}";

    public static string ViewNodeId(string key) => $"c4-view:{key}";

    /// <summary>One indexed document, as the graph build read it.</summary>
    public sealed record Document(string RelativePath, string Markdown, IReadOnlyList<DevbookParsedChapter> Chapters);

    /// <summary>
    /// The nodes and edges the C4 model adds. <paramref name="nodes"/> is every node
    /// the graph holds so far, in order, and <paramref name="byId"/> the same by id:
    /// an embed belongs to the innermost heading that is one of them.
    /// </summary>
    public static (IReadOnlyList<DevbookGraphNode> Nodes, IReadOnlyList<DevbookGraphEdge> Edges) Build(
        string repositoryRoot,
        DevbookBuildLayout layout,
        IReadOnlyList<Document> documents,
        IReadOnlyList<DevbookGraphNode> nodes,
        IReadOnlyDictionary<string, DevbookGraphNode> byId)
    {
        var added = new List<DevbookGraphNode>();
        var edges = new List<DevbookGraphEdge>();

        // The generator reads `.devbook/arc42/workspace.dsl` alone; a root layout
        // has no path it would look at.
        var workspacePath = Path.Combine(repositoryRoot, WorkspacePath.Replace('/', Path.DirectorySeparatorChar));
        var workspace = layout.IsDevbookLayout && !layout.IsBrain && File.Exists(workspacePath)
            ? TryRead(repositoryRoot)
            : null;
        if (workspace is null) return (added, edges);

        var elements = new Dictionary<string, C4Element>(StringComparer.Ordinal);
        var addedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in workspace.Elements)
        {
            if (!element.Anonymous) elements.TryAdd(element.Id.ToLowerInvariant(), element);

            var id = ElementNodeId(element.Id);
            if (byId.ContainsKey(id) || !addedIds.Add(id)) continue;

            added.Add(new DevbookGraphNode(id, "element")
            {
                Label = element.Name ?? element.Id,
                Folder = "arc42",
                Path = element.File,
                Line = element.Line,
                Kind = element.Kind
            });
        }

        foreach (var element in workspace.Elements)
        {
            if (element.Parent is null) continue;
            var source = ElementNodeId(element.Parent);
            var target = ElementNodeId(element.Id);
            edges.Add(new DevbookGraphEdge($"contains:{source}->{target}", "contains", source, target));
        }

        // A second relationship between one pair is numbered: `#2`, `#3`, ….
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var relationship in workspace.Relationships)
        {
            var source = ElementNodeId(relationship.Source);
            var target = ElementNodeId(relationship.Destination);
            var id = $"relationship:{source}->{target}";
            var n = seen.GetValueOrDefault(id) + 1;
            seen[id] = n;
            edges.Add(new DevbookGraphEdge(n == 1 ? id : $"{id}#{n}", "relationship", source, target));
        }

        var views = new Dictionary<string, C4View>(StringComparer.Ordinal);
        foreach (var view in workspace.Views)
        {
            // No key, or a key an earlier view has: the reader reported it.
            if (view.Key is null || !views.TryAdd(view.Key.ToLowerInvariant(), view)) continue;

            added.Add(new DevbookGraphNode(ViewNodeId(view.Key), "view")
            {
                Label = view.Title ?? view.Key,
                Folder = "arc42",
                Path = view.File,
                Line = view.Line,
                Kind = view.Kind
            });
        }

        // ── embeds ──
        var embedded = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            foreach (var (key, trail) in Embeds(document.Markdown, document.Chapters))
            {
                if (!views.TryGetValue(key.ToLowerInvariant(), out var view)) continue;

                // The section is the innermost heading that is a node; a
                // structural heading under it, or none, falls back to the file.
                var source = document.RelativePath;
                for (var k = trail.Count - 1; k >= 0; k--)
                {
                    var heading = trail[k];
                    var id = heading.Level == 1 ? document.RelativePath : $"{document.RelativePath}#{heading.Slug}";
                    if (byId.ContainsKey(id))
                    {
                        source = id;
                        break;
                    }
                }

                var count = embedded.GetValueOrDefault(view.Key!) + 1;
                embedded[view.Key!] = count;
                var target = ViewNodeId(view.Key!);
                edges.Add(new DevbookGraphEdge($"embeds:{source}->{target}{(count > 1 ? $"#{count}" : string.Empty)}", "embeds", source, target));
            }
        }

        // ── fields ──
        foreach (var node in nodes)
        {
            if (node.Folder is not ("arc42" or "domain")) continue;

            foreach (var field in Fields)
            {
                if (!node.C4Fields.TryGetValue(field, out var entries)) continue;

                foreach (var entry in entries.Distinct(StringComparer.Ordinal))
                {
                    if (!elements.TryGetValue(entry.ToLowerInvariant(), out var element)) continue;
                    var target = ElementNodeId(element.Id);
                    edges.Add(new DevbookGraphEdge($"{field}:{node.Id}->{target}", field, node.Id, target));
                }
            }
        }

        return (added, edges);
    }

    /// <summary>The workspace, or null when it cannot be read — which the
    /// generator reports, and draws nothing for.</summary>
    private static C4Reading? TryRead(string repositoryRoot)
    {
        try
        {
            return DevbookC4Workspace.Read(repositoryRoot, WorkspacePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Every <c>c4:</c> image embed in one document outside a fence, an inline code
    /// span and an HTML comment: its key, and the headings it sits under, innermost
    /// last (c4-graph.mjs <c>c4Embeds</c>).
    /// </summary>
    internal static IEnumerable<(string Key, IReadOnlyList<DevbookParsedChapter> Trail)> Embeds(string markdown, IReadOnlyList<DevbookParsedChapter> chapters)
    {
        var lines = LineBreak().Split(markdown);
        var headings = chapters.OrderBy(chapter => chapter.Line).ToList();
        string? fence = null;
        var comment = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // An HTML comment hides what it holds, across lines too.
            if (comment)
            {
                var close = line.IndexOf("-->", StringComparison.Ordinal);
                if (close < 0) continue;
                line = line[(close + 3)..];
                comment = false;
            }

            var marker = FenceMarker().Match(line);
            if (fence is not null)
            {
                if (marker.Success && marker.Groups[1].Value[0] == fence[0] && marker.Groups[1].Value.Length >= fence.Length) fence = null;
                continue;
            }

            if (marker.Success)
            {
                fence = marker.Groups[1].Value;
                continue;
            }

            // An embed inside an inline code span is an example, not an embed.
            var prose = InlineComment().Replace(CodeSpan().Replace(line, string.Empty), string.Empty);
            var open = prose.IndexOf("<!--", StringComparison.Ordinal);
            if (open >= 0)
            {
                prose = prose[..open];
                comment = true;
            }

            foreach (Match match in C4Embed().Matches(prose))
            {
                var lineNumber = i + 1;
                var trail = new List<DevbookParsedChapter>();
                foreach (var heading in headings)
                {
                    if (heading.Line > lineNumber) break;
                    while (trail.Count > 0 && trail[^1].Level >= heading.Level) trail.RemoveAt(trail.Count - 1);
                    trail.Add(heading);
                }

                yield return (match.Groups[1].Value, trail);
            }
        }
    }

    [GeneratedRegex(@"\r?\n")]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"^\s*(`{3,}|~{3,})")]
    private static partial Regex FenceMarker();

    [GeneratedRegex(@"(`+)[\s\S]*?\1")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"<!--[\s\S]*?-->")]
    private static partial Regex InlineComment();

    [GeneratedRegex(@"!\[[^\]]*\]\(\s*<?c4:([^)\s>]*)>?(?:\s+""[^""]*"")?\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex C4Embed();
}
