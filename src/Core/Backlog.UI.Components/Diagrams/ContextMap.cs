using System.Text.RegularExpressions;

using Backlog.SharedKernel.Markdown;
using Backlog.UI.Components.Devbook;
using Backlog.UI.Components.Markdown;

namespace Backlog.UI.Components.Diagrams;

/// <summary>One bounded context of a context map (local ADR 0037).</summary>
/// <param name="Id">The context's node id in the map's Mermaid fence — <c>Roadmap</c>
/// for <c>Roadmap[Roadmap Planning]</c> — which the lines name it by.</param>
/// <param name="Name">The node's label: <c>Roadmap Planning</c>.</param>
/// <param name="Kind">The Subdomain landscape table's type in lower case —
/// <c>core</c>, <c>supporting</c>, <c>generic</c> — or <c>supporting</c> when the
/// table does not list the context.</param>
/// <param name="Why">The landscape table's "Why" cell as plain text, or the empty
/// string.</param>
/// <param name="Slug">The context's folder under the domain folder, or
/// <see langword="null"/> when no folder is named for it.</param>
public sealed record ContextMapContext(string Id, string Name, string Kind, string Why, string? Slug)
{
    /// <summary>Whether the landscape table calls the context Core.</summary>
    public bool IsCore => string.Equals(Kind, "core", StringComparison.Ordinal);
}

/// <summary>One line of the map: every Mermaid edge between the same two contexts,
/// drawn once.</summary>
/// <param name="From">The id of the context the pair's first edge leaves from.</param>
/// <param name="To">The id of the context it points at.</param>
/// <param name="BothWays">Whether the pair points both ways: a <c>&lt;--&gt;</c>
/// edge, or one edge each way.</param>
/// <param name="Built">False when every edge of the pair is drawn <c>-.-&gt;</c>,
/// which the map's authors use for a relation that is named and not built.</param>
/// <param name="Wording">The "Why" cell of the matching row in the source context's
/// dependency tables, or the empty string when no row names the other context.</param>
public sealed record ContextMapLine(string From, string To, bool BothWays, bool Built, string Wording)
{
    /// <summary>Whether the line touches the context <paramref name="id"/>.</summary>
    public bool Touches(string id) =>
        string.Equals(From, id, StringComparison.Ordinal) || string.Equals(To, id, StringComparison.Ordinal);
}

/// <summary>A context's dependency tables, as the host found them, which a line's
/// wording is read from.</summary>
/// <param name="Slug">The context's folder: <c>tasks</c>.</param>
/// <param name="Name">The context's name as its folder's <c>context.md</c> heads it.</param>
/// <param name="Dependencies">The markdown holding its dependency tables —
/// <c>dependencies.md</c>, or <c>context.md</c>'s <c>## Dependencies</c> chapter — or
/// <see langword="null"/>.</param>
/// <param name="Path">That markdown's repository path, which the tables' links
/// resolve against.</param>
public sealed record ContextMapSource(string Slug, string Name, string? Dependencies, string Path);

/// <summary>
/// A <c>context-map.md</c> as the Domain view's Context map page reads it (local ADR
/// 0037): the contexts of its Mermaid fence, classified by the Subdomain landscape
/// table, the lines between them, and the published-languages and strategic-rules
/// chapters as written. No pattern name from an edge label is kept.
/// </summary>
/// <param name="Contexts">Every context in reading order: the Subdomain landscape
/// table's rows as written, then each node of the fence the table does not list.</param>
/// <param name="Lines">One per pair of contexts, in the order the pair's first edge
/// is written.</param>
/// <param name="Languages">The published-languages chapter's body, or the empty string.</param>
/// <param name="Rules">The strategic-rules chapter's body, or the empty string.</param>
public sealed record ContextMap(
    IReadOnlyList<ContextMapContext> Contexts,
    IReadOnlyList<ContextMapLine> Lines,
    string Languages,
    string Rules)
{
    public static ContextMap Empty { get; } = new([], [], string.Empty, string.Empty);

    /// <summary>Whether the map names no context at all.</summary>
    public bool IsEmpty => Contexts.Count == 0;

    /// <summary>How many lines touch the context <paramref name="id"/>.</summary>
    public int LinesTouching(string id) => Lines.Count(line => line.Touches(id));

    /// <summary>The context whose id is <paramref name="id"/>, or null.</summary>
    public ContextMapContext? Context(string id) =>
        Contexts.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));

    /// <summary>Whether both ends of <paramref name="line"/> are core contexts.</summary>
    public bool IsCoreLine(ContextMapLine line) =>
        Context(line.From)?.IsCore == true && Context(line.To)?.IsCore == true;

    /// <summary>The line's tooltip and accessible name: who meets whom, in plain
    /// words, then the dependency table's reason.</summary>
    public string Describe(ContextMapLine line)
    {
        var from = Context(line.From)?.Name ?? line.From;
        var to = Context(line.To)?.Name ?? line.To;
        var who = line.BothWays ? $"{from} and {to}" : $"{from} to {to}";
        if (!line.Built) who += " (not built yet)";
        return line.Wording.Length == 0 ? who : $"{who}: {line.Wording}";
    }
}

/// <summary>Reads <see cref="ContextMap"/> out of a <c>context-map.md</c>.</summary>
public static partial class ContextMaps
{
    /// <summary>The map <paramref name="markdown"/> writes. <paramref name="sources"/>
    /// are the domain folder's contexts with their dependency tables: they name each
    /// card's folder and every line's wording.</summary>
    public static ContextMap Read(string? markdown, IReadOnlyList<ContextMapSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (string.IsNullOrWhiteSpace(markdown)) return ContextMap.Empty;

        var chapters = MarkdownChapters.Split(markdown);
        var landscape = Landscape(Chapter(chapters, "subdomain landscape"));
        var mermaid = MermaidFence(Chapter(chapters, "context map") ?? markdown);
        var (nodes, edges) = Graph(mermaid);

        // Reading order: the landscape table's rows as written, then any node the
        // fence declares that the table does not list.
        var contexts = new List<ContextMapContext>();
        foreach (var row in landscape)
        {
            if (contexts.Any(c => Same(c.Name, row.Name))) continue;
            var node = nodes.FirstOrDefault(n => Same(n.Name, row.Name));
            var id = node.Id ?? UnusedId(Slugify(row.Name), nodes, contexts);
            var name = node.Name ?? row.Name;
            contexts.Add(new ContextMapContext(id, name, row.Kind ?? "supporting", row.Why ?? string.Empty, Folder(id, name, sources)?.Slug));
        }
        foreach (var (id, name) in nodes)
        {
            if (contexts.Any(c => c.Id == id)) continue;
            contexts.Add(new ContextMapContext(id, name, "supporting", string.Empty, Folder(id, name, sources)?.Slug));
        }

        var rowsBySlug = new Dictionary<string, IReadOnlyList<DomainNeighbourRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources) rowsBySlug.TryAdd(source.Slug, DomainNeighbours.Rows(source.Dependencies, source.Path));

        var lines = new List<ContextMapLine>();
        foreach (var edge in edges)
        {
            if (string.Equals(edge.From, edge.To, StringComparison.Ordinal)) continue;
            var index = lines.FindIndex(l =>
                (l.From == edge.From && l.To == edge.To) || (l.From == edge.To && l.To == edge.From));
            if (index >= 0)
            {
                var seen = lines[index];
                lines[index] = seen with
                {
                    BothWays = seen.BothWays || edge.BothWays || seen.From != edge.From,
                    Built = seen.Built || edge.Built
                };
                continue;
            }

            var from = contexts.First(c => c.Id == edge.From);
            var to = contexts.First(c => c.Id == edge.To);
            lines.Add(new ContextMapLine(edge.From, edge.To, edge.BothWays, edge.Built, Wording(from, to, rowsBySlug)));
        }

        return new ContextMap(
            contexts,
            lines,
            Body(Chapter(chapters, "published language")),
            Body(Chapter(chapters, "strategic rules")));
    }

    /// <summary>The "Why" of the source context's row naming the other context —
    /// its outbound table first, then its inbound one — and failing that, the other
    /// context's row naming the source.</summary>
    private static string Wording(
        ContextMapContext from,
        ContextMapContext to,
        IReadOnlyDictionary<string, IReadOnlyList<DomainNeighbourRow>> rowsBySlug)
    {
        return Find(from, to) ?? Find(to, from) ?? string.Empty;

        string? Find(ContextMapContext owner, ContextMapContext other)
        {
            if (owner.Slug is null || !rowsBySlug.TryGetValue(owner.Slug, out var rows)) return null;
            return rows.FirstOrDefault(r =>
                r.Reason.Length > 0
                && ((other.Slug is not null && string.Equals(r.Neighbour.Context, other.Slug, StringComparison.OrdinalIgnoreCase))
                    || Same(r.Neighbour.Name, other.Name)))?.Reason;
        }
    }

    private static ContextMapSource? Folder(string id, string name, IReadOnlyList<ContextMapSource> sources)
    {
        var slug = Slugify(name);
        return sources.FirstOrDefault(s => Same(s.Name, name))
            ?? sources.FirstOrDefault(s => string.Equals(s.Slug, id, StringComparison.OrdinalIgnoreCase))
            ?? sources.FirstOrDefault(s => string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase))
            // A folder named for the label's first word: roadmap for Roadmap Planning,
            // monitoring for Monitoring & Dashboard.
            ?? sources.FirstOrDefault(s => slug.StartsWith(s.Slug + "-", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>An id for a context the landscape table lists and the fence does
    /// not, which no node and no other context already uses.</summary>
    private static string UnusedId(string slug, List<(string Id, string Name)> nodes, List<ContextMapContext> contexts)
    {
        var id = slug;
        for (var n = 2; nodes.Any(x => x.Id == id) || contexts.Any(c => c.Id == id); n++) id = $"{slug}-{n}";
        return id;
    }

    private static string? Chapter(IReadOnlyList<MarkdownChapter> chapters, string title) =>
        chapters.FirstOrDefault(c => c.Level == 2 && c.Title.Contains(title, StringComparison.OrdinalIgnoreCase))?.Text;

    /// <summary>A chapter's text without its heading line.</summary>
    private static string Body(string? chapter)
    {
        if (chapter is null) return string.Empty;
        var newline = chapter.IndexOf('\n');
        return newline < 0 ? string.Empty : chapter[(newline + 1)..].Trim('\n');
    }

    private static List<(string Name, string? Kind, string? Why)> Landscape(string? chapter)
    {
        var rows = new List<(string, string?, string?)>();
        if (chapter is null) return rows;

        var table = chapter.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith('|')).ToList();
        if (table.Count < 3) return rows;

        var header = DomainNeighbours.Cells(table[0]);
        var type = header.FindIndex(c => c.Contains("type", StringComparison.OrdinalIgnoreCase));
        var why = header.FindIndex(c => c.Contains("why", StringComparison.OrdinalIgnoreCase));
        foreach (var cells in table.Skip(2).Select(DomainNeighbours.Cells))
        {
            if (cells.Count == 0 || cells[0].Length == 0) continue;
            var kind = type >= 0 && type < cells.Count ? DomainUnits.Plain(cells[type]).ToLowerInvariant() : null;
            var reason = why >= 0 && why < cells.Count ? DomainUnits.Plain(cells[why]) : null;
            rows.Add((DomainUnits.Plain(cells[0]), kind is { Length: > 0 } ? kind : null, reason));
        }
        return rows;
    }

    /// <summary>The first <c>mermaid</c> fence's body in <paramref name="markdown"/>.</summary>
    private static string MermaidFence(string markdown)
    {
        var lines = new List<string>();
        MarkdownFence? fence = null;
        var inMermaid = false;
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (fence is { } open)
            {
                if (open.IsClosedBy(raw))
                {
                    if (inMermaid) return string.Join('\n', lines);
                    fence = null;
                    continue;
                }
                if (inMermaid) lines.Add(raw);
                continue;
            }
            if (MarkdownFence.Open(raw) is { } opened)
            {
                fence = opened;
                inMermaid = raw.Trim().TrimStart('`', '~').Trim().StartsWith("mermaid", StringComparison.OrdinalIgnoreCase);
            }
        }
        return inMermaid ? string.Join('\n', lines) : string.Empty;
    }

    private sealed record Edge(string From, string To, bool BothWays, bool Built);

    private static (List<(string Id, string Name)> Nodes, List<Edge> Edges) Graph(string mermaid)
    {
        var nodes = new List<(string Id, string Name)>();
        var edges = new List<Edge>();

        void Node(string id, string? label)
        {
            var index = nodes.FindIndex(n => n.Id == id);
            var name = label is { Length: > 0 } ? Label(label) : id;
            if (index < 0) nodes.Add((id, name));
            else if (label is { Length: > 0 }) nodes[index] = (id, name);
        }

        foreach (var raw in mermaid.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal)) continue;

            // An edge whose text sits inside the arrow, A -- text --> B, is the same
            // edge as A -->|text| B.
            line = InlineText().Replace(line, m => m.Groups["open"].Value switch
            {
                "-." => "-.->",
                "==" => "==>",
                _ => "-->"
            } + $"|{m.Groups["text"].Value}|");

            if (EdgeLine().Match(line) is { Success: true } edge)
            {
                var from = edge.Groups["from"].Value;
                var to = edge.Groups["to"].Value;
                Node(from, edge.Groups["fromLabel"].Success ? edge.Groups["fromLabel"].Value : null);
                Node(to, edge.Groups["toLabel"].Success ? edge.Groups["toLabel"].Value : null);
                var arrow = edge.Groups["arrow"].Value;
                edges.Add(new Edge(from, to, arrow.StartsWith('<'), !arrow.Contains('.')));
                continue;
            }

            if (NodeLine().Match(line) is { Success: true } node)
            {
                Node(node.Groups["id"].Value, node.Groups["L"].Success ? node.Groups["L"].Value : null);
            }
        }
        return (nodes, edges);
    }

    private static string Label(string label)
    {
        var text = label.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') text = text[1..^1];
        return BreakTag().Replace(text, " ").Trim();
    }

    private static bool Same(string a, string b) =>
        string.Equals(Slugify(a), Slugify(b), StringComparison.Ordinal);

    private static string Slugify(string text) =>
        NotWord().Replace(text.ToLowerInvariant(), "-").Trim('-');

    // A node written bare or with its label inline — Id, Id[Label], Id["Label"],
    // Id(Label), Id([Label]), Id[[Label]], Id((Label)), Id{Label} — each closing on
    // its own bracket, then an optional :::class.
    private const string NodeLabel =
        @"(?:\(\[(?<L>[^\]]*)\]\)|\[\[(?<L>[^\]]*)\]\]|\(\((?<L>[^)]*)\)\)|\{\{(?<L>[^}]*)\}\}"
        + @"|\[(?<L>""[^""]*""|[^\]]*)\]|\((?<L>""[^""]*""|[^)]*)\)|\{(?<L>""[^""]*""|[^}]*)\})?(?::::[\w-]+)?";

    // A directed edge between two nodes: -->, --->, ==>, -.->, -..->, and each with
    // a head at both ends. The edge's own label between pipes is the pattern and the
    // contract, and is never kept. An undirected link (---, -.-) is not a relation
    // the map draws a direction for, and is not read.
    [GeneratedRegex(@"^(?<from>[A-Za-z_][\w-]*)\s*" + NodeLabelFrom + @"\s*(?<arrow><?(?:-{2,}|={2,}|-\.+-)>)\s*(?:\|[^|]*\|\s*)?(?<to>[A-Za-z_][\w-]*)\s*" + NodeLabelTo + @"\s*;?$")]
    private static partial Regex EdgeLine();

    private const string NodeLabelFrom =
        @"(?:\(\[(?<fromLabel>[^\]]*)\]\)|\[\[(?<fromLabel>[^\]]*)\]\]|\(\((?<fromLabel>[^)]*)\)\)|\{\{(?<fromLabel>[^}]*)\}\}"
        + @"|\[(?<fromLabel>""[^""]*""|[^\]]*)\]|\((?<fromLabel>""[^""]*""|[^)]*)\)|\{(?<fromLabel>""[^""]*""|[^}]*)\})?(?::::[\w-]+)?";

    private const string NodeLabelTo =
        @"(?:\(\[(?<toLabel>[^\]]*)\]\)|\[\[(?<toLabel>[^\]]*)\]\]|\(\((?<toLabel>[^)]*)\)\)|\{\{(?<toLabel>[^}]*)\}\}"
        + @"|\[(?<toLabel>""[^""]*""|[^\]]*)\]|\((?<toLabel>""[^""]*""|[^)]*)\)|\{(?<toLabel>""[^""]*""|[^}]*)\})?(?::::[\w-]+)?";

    [GeneratedRegex(@"^(?<id>[A-Za-z_][\w-]*)\s*" + NodeLabel + @"\s*;?$")]
    private static partial Regex NodeLine();

    // A -- text --> B, A -. text .-> B, A == text ==> B.
    [GeneratedRegex(@"(?<open>--|-\.|==)\s+(?<text>[^|>]+?)\s+(?<arrow>-{2,}>|\.+->|={2,}>)")]
    private static partial Regex InlineText();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakTag();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NotWord();
}
