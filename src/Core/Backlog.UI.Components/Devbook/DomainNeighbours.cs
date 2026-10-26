using System.Text.RegularExpressions;

using Backlog.SharedKernel.Markdown;

namespace Backlog.UI.Components.Devbook;

/// <summary>One neighbour of a bounded context as the Domain view's context page
/// draws it (local ADR 0040): who it is and why the two meet, in the words of the
/// dependency table's row, with the row's pattern left off.</summary>
/// <param name="Name">The first cell as plain text, without an <c>(external)</c>
/// mark: <c>Devbook</c>, <c>GitHub</c>. Outside systems on one side that share a
/// contract are one card, their names joined: <c>GitHub · Copilot CLI</c>.</param>
/// <param name="Line">The first sentence of the row's "Why" or "What it relies
/// on" cell as plain text, cut to a card's phrase by <see cref="DomainNeighbours.Phrase"/>.</param>
/// <param name="Context">The folder of the context the first cell links to —
/// <c>devbook</c> for <c>../devbook/domain.md#knowledge-note</c> — or
/// <see langword="null"/> when it links to none.</param>
/// <param name="External">Whether the row names something that is not one of the
/// domain folder's contexts: its first cell says <c>(external)</c>, or links to no
/// context at all — a package registry, a cloud service, shared code.</param>
public sealed record DomainNeighbour(string Name, string Line, string? Context, bool External)
{
    /// <summary>The whole first sentence <see cref="Line"/> was cut from, which the
    /// card offers as its tooltip.</summary>
    public string Sentence { get; init; } = Line;

    /// <summary>Whether <see cref="Line"/> leaves words of <see cref="Sentence"/>
    /// out, rather than only its full stop.</summary>
    public bool Cut => !string.Equals(Sentence.Trim().TrimEnd('.').TrimEnd(), Line, StringComparison.Ordinal);

    /// <summary>Whether the context map's Subdomain landscape calls the neighbour
    /// Core.</summary>
    public bool Core { get; init; }

    /// <summary>The reference that opens the neighbour's own context page, or
    /// <see langword="null"/> when the row names no context.</summary>
    public string? Reference => Context is null ? null : $".devbook/domain/{Context}/context.md";
}

/// <summary>How the Domain view names a neighbour that is one of its contexts: as
/// its <c>context.md</c> heads it, and whether the landscape calls it Core.</summary>
public sealed record DomainNeighbourContext(string Name, bool Core);

/// <summary>
/// A bounded context's neighbours, read from its dependency tables: the
/// <c>## Outbound dependencies</c> and <c>## Inbound dependents</c> tables of
/// <c>dependencies.md</c>, or the same tables under the <c>## Dependencies</c>
/// chapter of <c>context.md</c>. Each neighbour stands by the way information goes
/// between the two (<see cref="DomainNeighbours.Read"/>).
/// </summary>
/// <param name="ListensTo">The neighbours that supply this context: upstream.</param>
/// <param name="ListenTo">The neighbours this context supplies: downstream.</param>
/// <param name="HandInHand">Every Partnership neighbour, one card each, standing on
/// neither side.</param>
public sealed record DomainNeighbourhood(
    IReadOnlyList<DomainNeighbour> ListensTo,
    IReadOnlyList<DomainNeighbour> ListenTo,
    IReadOnlyList<DomainNeighbour> HandInHand)
{
    public static DomainNeighbourhood Empty { get; } = new([], [], []);

    /// <summary>Whether the tables name no neighbour at all.</summary>
    public bool IsEmpty => ListensTo.Count == 0 && ListenTo.Count == 0 && HandInHand.Count == 0;

    /// <summary>The neighbourhood with every neighbour that is one of the domain's
    /// contexts named as its folder's <c>context.md</c> heads it — <c>Monitoring &amp;
    /// Dashboard</c> for a row that says <c>Monitoring</c> — and marked Core when the
    /// landscape calls it so. <paramref name="contexts"/> is keyed by folder.</summary>
    public DomainNeighbourhood Describe(IReadOnlyDictionary<string, DomainNeighbourContext> contexts)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        return new([.. ListensTo.Select(Named)], [.. ListenTo.Select(Named)], [.. HandInHand.Select(Named)]);

        DomainNeighbour Named(DomainNeighbour neighbour) =>
            neighbour.Context is { } slug && contexts.TryGetValue(slug, out var context)
                ? neighbour with { Name = context.Name.Length > 0 ? context.Name : neighbour.Name, Core = context.Core }
                : neighbour;
    }
}

/// <summary>One row of a context's dependency tables.</summary>
/// <param name="Neighbour">The row as a Neighbours card draws it.</param>
/// <param name="Outbound">Whether the row is in the outbound table — a context
/// this one depends on — rather than the inbound one.</param>
/// <param name="Partnership">Whether the row's pattern is a Partnership.</param>
/// <param name="Reason">The whole "Why" or "What it relies on" cell as plain text.</param>
/// <param name="Pattern">The "DDD pattern" cell as plain text, or the empty string.</param>
/// <param name="Contract">The "Contract" cell as plain text, or the empty string.</param>
public sealed record DomainNeighbourRow(
    DomainNeighbour Neighbour,
    bool Outbound,
    bool Partnership,
    string Reason,
    string Pattern = "",
    string Contract = "");

/// <summary>Reads <see cref="DomainNeighbourhood"/> out of a dependency table.</summary>
public static partial class DomainNeighbours
{
    /// <summary>The longest a card's phrase runs before it is cut.</summary>
    internal const int PhraseLength = 64;

    private static readonly string[] ClauseBreaks = ["; ", " — ", " – ", ": ", ", ", " ("];

    /// <summary>The neighbours the dependency tables in <paramref name="markdown"/>
    /// name. <paramref name="path"/> is the file's path as the host spells it, so a
    /// first cell's relative link resolves to the context folder it names.</summary>
    /// <remarks>
    /// A neighbour stands by the way information goes between the two, so the
    /// arrows agree with the legend. The pattern's role mark decides —
    /// <c>(Inbox = supplier)</c>, <c>(Tasks = customer)</c>, <c>(Sync = conformist)</c>
    /// — whether it names the neighbour or this context: a supplier is upstream of
    /// a customer or a conformist. A row without one stands by its table: outbound
    /// upstream, inbound downstream. A Partnership neighbour stands only below,
    /// whatever its other rows say; a neighbour met twice on one side is one card,
    /// the first row's; and outside systems on one side that share a contract are
    /// one card.
    /// </remarks>
    public static DomainNeighbourhood Read(string? markdown, string path)
    {
        var rows = Rows(markdown, path);
        if (rows.Count == 0) return DomainNeighbourhood.Empty;

        var partners = new List<DomainNeighbour>();
        foreach (var row in rows.Where(row => row.Partnership))
        {
            if (!partners.Any(n => Alike(n, row.Neighbour))) partners.Add(row.Neighbour);
        }

        var upstream = new List<DomainNeighbourRow>();
        var downstream = new List<DomainNeighbourRow>();
        foreach (var row in rows.Where(row => !row.Partnership))
        {
            if (partners.Any(n => Alike(n, row.Neighbour))) continue;
            var side = Upstream(row) ? upstream : downstream;
            if (!side.Any(seen => Alike(seen.Neighbour, row.Neighbour))) side.Add(row);
        }

        return new DomainNeighbourhood(Merged(upstream), Merged(downstream), partners);
    }

    /// <summary>A reason's first sentence cut to a card's phrase, as the design's
    /// cards word a relation: without its full stop, at its first clause break when
    /// the sentence runs past <see cref="PhraseLength"/> characters, else at the last
    /// whole word that fits, with an ellipsis.</summary>
    public static string Phrase(string? sentence)
    {
        var text = (sentence ?? string.Empty).Trim();
        if (text.EndsWith('.') && !text.EndsWith("..", StringComparison.Ordinal)) text = text[..^1].TrimEnd();
        if (text.Length <= PhraseLength) return text;

        var cut = -1;
        foreach (var mark in ClauseBreaks)
        {
            var at = text.IndexOf(mark, StringComparison.Ordinal);
            if (at >= 20 && at <= PhraseLength && (cut < 0 || at < cut)) cut = at;
        }
        if (cut > 0) return text[..cut].TrimEnd();

        var space = text.LastIndexOf(' ', PhraseLength - 1);
        var head = space > 0 ? text[..space] : text[..(PhraseLength - 1)];
        return head.TrimEnd(',', ';', ':', ' ', '—', '–') + "…";
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether two rows name the same neighbour: by name, or by the context
    /// folder both link to.</summary>
    private static bool Alike(DomainNeighbour a, DomainNeighbour b) =>
        Same(a.Name, b.Name) || (a.Context is { } folder && b.Context is { } other && Same(folder, other));

    /// <summary>Whether information comes into this context from the row's
    /// neighbour: its role mark says so, else its table.</summary>
    private static bool Upstream(DomainNeighbourRow row)
    {
        if (Role().Match(row.Pattern) is { Success: true } role)
        {
            var party = role.Groups["party"].Value.Trim();
            var supplier = role.Groups["role"].Value.Equals("supplier", StringComparison.OrdinalIgnoreCase);
            var isNeighbour = Same(party, row.Neighbour.Name)
                || (row.Neighbour.Context is { } slug && Same(party.Replace(' ', '-'), slug));
            return isNeighbour ? supplier : !supplier;
        }
        return row.Outbound;
    }

    /// <summary>One side's cards, with outside systems that share a contract
    /// joined into the first one's card.</summary>
    private static List<DomainNeighbour> Merged(List<DomainNeighbourRow> side)
    {
        var cards = new List<(DomainNeighbour Card, string Contract)>();
        foreach (var row in side)
        {
            var at = row.Neighbour.External && row.Contract.Length > 0
                ? cards.FindIndex(c => c.Card.External && Same(c.Contract, row.Contract))
                : -1;
            if (at >= 0)
            {
                var (card, contract) = cards[at];
                cards[at] = (card with { Name = $"{card.Name} · {row.Neighbour.Name}" }, contract);
                continue;
            }
            cards.Add((row.Neighbour, row.Contract));
        }
        return [.. cards.Select(c => c.Card)];
    }

    /// <summary>Every row of the dependency tables in <paramref name="markdown"/>, in
    /// the order written: the outbound table's, then the inbound table's, each with
    /// its whole reason cell as plain text. The context map reads a line's wording
    /// from these (<see cref="Diagrams.ContextMaps"/>).</summary>
    public static IReadOnlyList<DomainNeighbourRow> Rows(string? markdown, string path)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];

        var rows = new List<DomainNeighbourRow>();
        string? side = null;
        var table = new List<string>();
        MarkdownFence? fence = null;

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            // A fence is read by CommonMark's rule, so a table quoted in a sample is not a table.
            if (fence is { } open)
            {
                if (open.IsClosedBy(raw)) fence = null;
                continue;
            }
            if (MarkdownFence.Open(raw) is { } opened)
            {
                fence = opened;
                continue;
            }

            var line = raw.Trim();

            if (HeadingLine().IsMatch(line))
            {
                Flush(side, table, path, rows);
                var heading = line.TrimStart('#').Trim();
                side = heading.Contains("outbound", StringComparison.OrdinalIgnoreCase) ? "out"
                    : heading.Contains("inbound", StringComparison.OrdinalIgnoreCase) ? "in"
                    : null;
                continue;
            }

            if (line.StartsWith('|')) table.Add(line);
            else if (table.Count > 0) Flush(side, table, path, rows);
        }
        Flush(side, table, path, rows);

        // Outbound first, whatever order the file wrote the two tables in.
        return [.. rows.Where(row => row.Outbound), .. rows.Where(row => !row.Outbound)];
    }

    private static void Flush(string? side, List<string> table, string path, List<DomainNeighbourRow> rows)
    {
        if (side is not null && table.Count > 2)
        {
            var header = Cells(table[0]);
            var pattern = header.FindIndex(cell => cell.Contains("pattern", StringComparison.OrdinalIgnoreCase));
            var reason = header.FindIndex(cell =>
                cell.Contains("why", StringComparison.OrdinalIgnoreCase) || cell.Contains("relies on", StringComparison.OrdinalIgnoreCase));
            if (reason < 0) reason = header.Count - 1;
            var contract = header.FindIndex(cell => cell.Contains("contract", StringComparison.OrdinalIgnoreCase));

            // Row 1 is the |---| separator.
            foreach (var row in table.Skip(2).Select(Cells))
            {
                // A table that lists nobody says so with a placeholder row.
                if (row.Count == 0 || Placeholder().IsMatch(DomainUnits.Plain(row[0]))) continue;

                var partnership = pattern >= 0 && pattern < row.Count
                    && row[pattern].TrimStart().StartsWith("Partnership", StringComparison.OrdinalIgnoreCase);
                var reasonCell = reason < row.Count ? row[reason] : string.Empty;
                var patternCell = pattern >= 0 && pattern < row.Count ? DomainUnits.Plain(row[pattern]) : string.Empty;
                var contractCell = contract >= 0 && contract < row.Count ? DomainUnits.Plain(row[contract]) : string.Empty;
                rows.Add(new DomainNeighbourRow(
                    Neighbour(row[0], reasonCell, path), side == "out", partnership, DomainUnits.Plain(reasonCell), patternCell, contractCell));
            }
        }
        table.Clear();
    }

    private static DomainNeighbour Neighbour(string nameCell, string reasonCell, string path)
    {
        var name = DomainUnits.Plain(nameCell);
        var marked = External().IsMatch(name);
        if (marked) name = External().Replace(name, string.Empty).Trim();

        string? context = null;
        if (!marked && Link().Match(nameCell) is { Success: true } link && !Scheme().IsMatch(link.Groups[1].Value))
        {
            var target = DomainUnits.Resolve(link.Groups[1].Value, path);
            var hash = target.IndexOf('#');
            if (hash >= 0) target = target[..hash];
            var slash = target.IndexOf('/');
            if (slash > 0 && target.EndsWith(".md", StringComparison.Ordinal)) context = target[..slash];
        }

        var sentence = DomainUnits.FirstSentence(reasonCell);
        return new DomainNeighbour(name, Phrase(sentence), context, marked || context is null) { Sentence = sentence };
    }

    /// <summary>A table row's cells, trimmed, without the outer pipes. A pipe
    /// escaped as <c>\|</c> or inside a code span stays in its cell.</summary>
    internal static List<string> Cells(string row)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        var code = false;
        var text = row.Trim();
        if (text.StartsWith('|')) text = text[1..];
        if (text.EndsWith('|') && !text.EndsWith("\\|", StringComparison.Ordinal)) text = text[..^1];

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] == '|')
            {
                current.Append('|');
                i++;
                continue;
            }
            if (c == '`') code = !code;
            if (c == '|' && !code)
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        cells.Add(current.ToString().Trim());
        return cells;
    }

    /// <summary>A pattern's role mark: <c>(Inbox = supplier)</c>, <c>(Technology
    /// Stack = customer of scans)</c>, <c>(Sync = conformist)</c>.</summary>
    [GeneratedRegex(@"\(\s*(?<party>[^=()]+?)\s*=\s*(?<role>supplier|customer|conformist)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Role();

    [GeneratedRegex(@"\(\s*external\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex External();

    [GeneratedRegex(@"\[[^\]]*\]\(([^)\s]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase)]
    private static partial Regex Scheme();

    [GeneratedRegex(@"^#{1,6}[ \t]")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^(?:|none|n/?a|-+|—|–)$", RegexOptions.IgnoreCase)]
    private static partial Regex Placeholder();
}
