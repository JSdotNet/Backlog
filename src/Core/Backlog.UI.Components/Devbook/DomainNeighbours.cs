using System.Text.RegularExpressions;

using Backlog.SharedKernel.Markdown;

namespace Backlog.UI.Components.Devbook;

/// <summary>One neighbour of a bounded context as the Domain view's Neighbours tab
/// draws it (local ADR 0036): who it is and why the two meet, in the words of the
/// dependency table's row, with the row's pattern left off.</summary>
/// <param name="Name">The first cell as plain text, without an <c>(external)</c>
/// mark: <c>Devbook</c>, <c>GitHub</c>.</param>
/// <param name="Line">The first sentence of the row's "Why" or "What it relies
/// on" cell, as plain text.</param>
/// <param name="Context">The folder of the context the first cell links to —
/// <c>devbook</c> for <c>../devbook/domain.md#knowledge-note</c> — or
/// <see langword="null"/> when it links to none.</param>
/// <param name="External">Whether the row names something that is not one of the
/// domain folder's contexts: its first cell says <c>(external)</c>, or links to no
/// context at all — a package registry, a cloud service, shared code.</param>
public sealed record DomainNeighbour(string Name, string Line, string? Context, bool External)
{
    /// <summary>The reference that opens the neighbour's own context page, or
    /// <see langword="null"/> when the row names no context.</summary>
    public string? Reference => Context is null ? null : $".devbook/domain/{Context}/context.md";
}

/// <summary>
/// A bounded context's neighbours, read from its dependency tables: the
/// <c>## Outbound dependencies</c> and <c>## Inbound dependents</c> tables of
/// <c>dependencies.md</c>, or the same tables under the <c>## Dependencies</c>
/// chapter of <c>context.md</c>.
/// </summary>
/// <param name="ListensTo">The outbound rows that are not a Partnership: the
/// contexts this one listens to.</param>
/// <param name="ListenTo">The inbound rows that are not a Partnership: the
/// contexts that listen to this one.</param>
/// <param name="HandInHand">Every Partnership row, outbound and inbound, one card
/// per neighbour.</param>
public sealed record DomainNeighbourhood(
    IReadOnlyList<DomainNeighbour> ListensTo,
    IReadOnlyList<DomainNeighbour> ListenTo,
    IReadOnlyList<DomainNeighbour> HandInHand)
{
    public static DomainNeighbourhood Empty { get; } = new([], [], []);

    /// <summary>Whether the tables name no neighbour at all.</summary>
    public bool IsEmpty => ListensTo.Count == 0 && ListenTo.Count == 0 && HandInHand.Count == 0;
}

/// <summary>One row of a context's dependency tables.</summary>
/// <param name="Neighbour">The row as a Neighbours card draws it.</param>
/// <param name="Outbound">Whether the row is in the outbound table — a context
/// this one depends on — rather than the inbound one.</param>
/// <param name="Partnership">Whether the row's pattern is a Partnership.</param>
/// <param name="Reason">The whole "Why" or "What it relies on" cell as plain text.</param>
public sealed record DomainNeighbourRow(DomainNeighbour Neighbour, bool Outbound, bool Partnership, string Reason);

/// <summary>Reads <see cref="DomainNeighbourhood"/> out of a dependency table.</summary>
public static partial class DomainNeighbours
{
    /// <summary>The neighbours the dependency tables in <paramref name="markdown"/>
    /// name. <paramref name="path"/> is the file's path as the host spells it, so a
    /// first cell's relative link resolves to the context folder it names.</summary>
    public static DomainNeighbourhood Read(string? markdown, string path)
    {
        var rows = Rows(markdown, path);
        if (rows.Count == 0) return DomainNeighbourhood.Empty;

        // A neighbour met twice on one side is one card: the first row's.
        static List<DomainNeighbourRow> Distinct(IEnumerable<DomainNeighbourRow> side)
        {
            var kept = new List<DomainNeighbourRow>();
            foreach (var row in side)
            {
                if (kept.Any(seen => string.Equals(seen.Neighbour.Name, row.Neighbour.Name, StringComparison.OrdinalIgnoreCase))) continue;
                kept.Add(row);
            }
            return kept;
        }

        var outbound = Distinct(rows.Where(row => row.Outbound));
        var inbound = Distinct(rows.Where(row => !row.Outbound));

        var handInHand = new List<DomainNeighbour>();
        foreach (var row in outbound.Concat(inbound))
        {
            if (row.Partnership && !handInHand.Any(n => string.Equals(n.Name, row.Neighbour.Name, StringComparison.OrdinalIgnoreCase)))
                handInHand.Add(row.Neighbour);
        }

        return new DomainNeighbourhood(
            [.. outbound.Where(row => !row.Partnership).Select(row => row.Neighbour)],
            [.. inbound.Where(row => !row.Partnership).Select(row => row.Neighbour)],
            handInHand);
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

            // Row 1 is the |---| separator.
            foreach (var row in table.Skip(2).Select(Cells))
            {
                // A table that lists nobody says so with a placeholder row.
                if (row.Count == 0 || Placeholder().IsMatch(DomainUnits.Plain(row[0]))) continue;

                var partnership = pattern >= 0 && pattern < row.Count
                    && row[pattern].TrimStart().StartsWith("Partnership", StringComparison.OrdinalIgnoreCase);
                var reasonCell = reason < row.Count ? row[reason] : string.Empty;
                rows.Add(new DomainNeighbourRow(Neighbour(row[0], reasonCell, path), side == "out", partnership, DomainUnits.Plain(reasonCell)));
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

        return new DomainNeighbour(name, DomainUnits.FirstSentence(reasonCell), context, marked || context is null);
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
