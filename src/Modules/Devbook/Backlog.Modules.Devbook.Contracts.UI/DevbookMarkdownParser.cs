using System.Text.RegularExpressions;

using Backlog.Infrastructure.Devbook.Documents;
using Backlog.SharedKernel.Devbook;
using Backlog.SharedKernel.Markdown;
using Backlog.SharedKernel.Metadata;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Reads an arc42 chapter into the panel's model: the blocks, every heading with the
/// <c>meta</c> fence under it, the diagrams, and a decision record's history.
/// <para>
/// The Markdown is read by <see cref="MarkdownPreview.ParseDocument"/>, the shared
/// block parser, and mapped onto <see cref="DevbookBlock"/>; a <c>meta</c> fence by
/// <see cref="MetadataReader"/>. Fences follow CommonMark, so a devbook
/// <c>annotation</c> note quoting a code sample is one block, and a heading inside a
/// fence is the fence's.
/// </para>
/// </summary>
public static partial class DevbookMarkdownParser
{
    [GeneratedRegex(@"^(#{1,6})[ \t]+(.+)$")]
    private static partial Regex HeadingRegex();

    public static DevbookDocument Parse(string path, string markdown)
    {
        var text = DevbookMarkdownBlocks.Normalize(markdown);
        var lines = text.Split('\n');
        var parsed = MarkdownPreview.ParseDocument(text);
        var blocks = new List<DevbookBlock>();
        var headings = new List<DevbookHeadingSummary>();
        var title = Path.GetFileNameWithoutExtension(path);
        var documentMeta = DevbookMeta.Empty;
        var diagramCount = 0;
        string? section = null;
        var history = new List<DevbookHistoryRow>();

        for (var index = 0; index < parsed.Count; index++)
        {
            switch (parsed[index])
            {
                case MdHeading heading:
                {
                    var level = heading.Level;
                    var headingText = DevbookMarkdownBlocks.HeadingText(lines, heading);
                    var next = index + 1;
                    var metadata = DevbookMarkdownBlocks.TakeMeta(parsed, ref next) is { } record ? ToMeta(record) : DevbookMeta.Empty;
                    index = next - 1;

                    if (level == 1)
                    {
                        title = headingText;
                        documentMeta = metadata;
                    }

                    if (level <= 2) section = level == 2 ? headingText : null;

                    blocks.Add(new DevbookHeadingBlock(level, headingText, metadata));
                    headings.Add(new DevbookHeadingSummary(level, headingText, metadata));
                    break;
                }

                case MdCode code when IsDiagramLanguage(code.Language):
                    diagramCount++;
                    blocks.Add(new DevbookDiagramBlock(code.Language, code.Text, DiagramTitle(code.Text, diagramCount)));
                    break;

                // A devbook `annotation` note stays a code record, and
                // DevbookMarkdownView draws it as one.
                case MdCode code:
                    blocks.Add(new DevbookCodeBlock(code.Language, code.Text));
                    break;

                case MdTable table:
                {
                    var tableLines = DevbookMarkdownBlocks.SourceLines(lines, table.Source)
                        .Where((_, row) => row != 1)
                        .Select(line => line.Trim())
                        .ToList();
                    blocks.Add(new DevbookTableBlock([.. tableLines.Select(ParseTableRow)]));

                    if (history.Count == 0 && string.Equals(section, "History", StringComparison.OrdinalIgnoreCase))
                    {
                        history.AddRange(HistoryRows(tableLines));
                    }

                    break;
                }

                case MdParagraph paragraph:
                    blocks.Add(new DevbookParagraphBlock(paragraph.Content));
                    break;

                case MdQuote quote:
                    blocks.Add(new DevbookQuoteBlock(quote.Content));
                    break;

                case MdList list:
                    blocks.Add(new DevbookListBlock(list.Ordered, DevbookMarkdownBlocks.FlatItems(list)));
                    break;

                case MdFootnotes notes:
                    blocks.Add(new DevbookListBlock(true, DevbookMarkdownBlocks.NoteItems(notes)));
                    break;

                case MdDivider:
                    blocks.Add(new DevbookDividerBlock());
                    break;
            }
        }

        return new DevbookDocument(path, title, documentMeta, blocks, headings, diagramCount) { History = history };
    }

    /// <summary>
    /// A <c>## History</c> table's rows, when its header is the one
    /// <c>devbook-arc42.md</c> fixes — <c>| Date | Change | Why |</c> — and nothing
    /// otherwise. The header is fixed so a reader can list a concern's decisions across
    /// every record; a table with another one is a record written before it was, and
    /// guessing which column holds the date would put a reason where a date belongs.
    /// </summary>
    private static IEnumerable<DevbookHistoryRow> HistoryRows(IReadOnlyList<string> tableLines)
    {
        if (tableLines.Count == 0) return [];

        var header = TableCells(tableLines[0]);
        if (header.Length != 3
            || !string.Equals(header[0], "Date", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(header[1], "Change", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(header[2], "Why", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return tableLines
            .Skip(1)
            .Select(TableCells)
            .Where(cells => cells.Length == 3)
            .Select(cells => new DevbookHistoryRow(cells[0], cells[1], cells[2]))
            .ToList();
    }

    /// <summary>A table row's cells, split on the pipes that are not escaped: GFM
    /// writes a literal pipe in a cell as <c>\|</c>, inside a code span too.</summary>
    private static string[] TableCells(string line)
    {
        var row = line.Trim();
        if (row.StartsWith('|')) row = row[1..];
        if (row.EndsWith('|') && !row.EndsWith(@"\|", StringComparison.Ordinal)) row = row[..^1];

        return [.. UnescapedPipe().Split(row).Select(cell => cell.Replace(@"\|", "|", StringComparison.Ordinal).Trim())];
    }

    [GeneratedRegex(@"(?<!\\)\|")]
    private static partial Regex UnescapedPipe();

    /// <summary>
    /// The panel's fields from one <c>meta</c> fence, as <see cref="MetadataReader"/>
    /// reads it. <c>related</c> keeps every entry as written — the addresses first,
    /// then any the reader could not address — and the C4 fields, which the shared
    /// record does not model, come from its <see cref="MetadataRecord.Extra"/>.
    /// </summary>
    private static DevbookMeta ToMeta(MetadataRecord record)
    {
        static IReadOnlyList<string> Extra(MetadataRecord record, string key) =>
            record.Extra.TryGetValue(key, out var values) ? values : [];

        static IReadOnlyList<string> Distinct(IEnumerable<string> values) =>
            values.Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToList();

        return new DevbookMeta(record.Status, Distinct([.. record.Related.Select(reference => reference.Raw), .. Extra(record, "related")]))
        {
            C4 = Distinct(Extra(record, "c4")),
            C4Touches = Distinct(Extra(record, "c4-touches")),
            Code = Distinct(Extra(record, "code")),
            Severity = Extra(record, "severity") is [var severity, ..] && severity.Length > 0 ? severity : null,
        };
    }

    /// <summary>
    /// Every heading of a document with the <c>meta</c> fence under it, and nothing
    /// else — the cheap read for a caller that wants the fields of a whole folder
    /// without drawing a word of it. Fences are skipped by CommonMark's rule, as
    /// <see cref="Parse"/> skips them, so a heading or a <c>meta</c> fence quoted in an
    /// <c>annotation</c> note is not read as the chapter's.
    /// </summary>
    public static IReadOnlyList<DevbookHeadingSummary> ReadHeadings(string markdown)
    {
        var lines = DevbookMarkdownBlocks.Normalize(markdown).Split('\n');
        var headings = new List<DevbookHeadingSummary>();

        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();

            if (MarkdownFence.Open(trimmed) is { } fence)
            {
                for (index++; index < lines.Length && !fence.IsClosedBy(lines[index]); index++)
                {
                }

                continue;
            }

            var heading = HeadingRegex().Match(trimmed);
            if (!heading.Success) continue;

            var metadata = DevbookMeta.Empty;
            if (MetadataFence.At(lines, index + 1) is { } meta)
            {
                metadata = ToMeta(meta.Record);
                index = meta.End - 1;
            }

            headings.Add(new DevbookHeadingSummary(heading.Groups[1].Value.Length, heading.Groups[2].Value.Trim(), metadata));
        }

        return headings;
    }

    private static bool IsDiagramLanguage(string language) =>
        language.Equals("mermaid", StringComparison.OrdinalIgnoreCase) ||
        language.StartsWith("c4", StringComparison.OrdinalIgnoreCase);

    private static string DiagramTitle(string text, int number)
    {
        var title = text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith("title ", StringComparison.OrdinalIgnoreCase));
        return title is null ? $"Architecture diagram {number}" : title["title ".Length..].Trim();
    }

    private static IReadOnlyList<MdInline> ParseTableRow(string line) =>
        MarkdownPreview.ParseInlines(string.Join(" | ", line.Trim().Trim('|').Split('|', StringSplitOptions.TrimEntries)));
}