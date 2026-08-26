using System.Text.RegularExpressions;

using Backlog.UI.Components.Markdown;

using Backlog.Modules.Knowledge.Abstractions;

// Imported rather than aliased, which it was until the shared pair was renamed.
// Both of its old names were taken in this namespace — the arc42 reader grew a
// `KnowledgeMeta` of its own and the technology reader a `KnowledgeMetadata`,
// each predating the shared pair — so an unqualified name here would silently
// have resolved to the neighbour rather than to the library. `MetadataRecord`
// and `MetadataReader` collide with nothing, so the aliases the collision needed
// are gone. Design is still the first of the three readers to read a block
// through the library; moving the other two is their own change.
using Backlog.UI.Components.Metadata;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// Loads the repository's `.design` knowledge folder for the desktop wide-screen
/// knowledge pane. The Markdown files remain canonical; this service only builds
/// a read model for display.
/// </summary>
public sealed class DesignKnowledgeProvider(IKnowledgeFolderSource source)
{
    /// <summary>Re-published from the folder source so an open panel can reload
    /// when the configured folder moves.</summary>
    public event Action? Changed
    {
        add => source.Changed += value;
        remove => source.Changed -= value;
    }

    public Task<DesignKnowledgeModel> LoadAsync(string? repositoryAlias = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var location = source.Resolve(".design", repositoryAlias);
        if (!location.Available || location.FullPath is null)
        {
            return Task.FromResult(DesignKnowledgeModel.Unavailable(location.Message ?? "Design knowledge is unavailable."));
        }

        var folderPath = location.FullPath;
        var files = Directory.EnumerateFiles(folderPath, "*.md", SearchOption.TopDirectoryOnly)
            .Select(path => DesignKnowledgeParser.ParseFile(folderPath, path))
            .ToList();

        if (files.Count == 0)
        {
            return Task.FromResult(DesignKnowledgeModel.Unavailable(
                $"No Markdown design knowledge files were found at {folderPath}."));
        }

        files = OrderFiles(files);
        return Task.FromResult(DesignKnowledgeModel.Available(location.ScopeLabel ?? "storage", folderPath, files));
    }

    /// <summary>
    /// Writes a status into the <c>meta</c> fence under the addressed heading —
    /// the file's own when the path names a file, a chapter's when it carries an
    /// anchor.
    /// <para>
    /// Through the same writer the architecture, domain and technology folders
    /// use, and deliberately: where a status lives in a Markdown file is one fact
    /// about this repository's documentation, not five. All this method adds is
    /// which folder is being written and where that folder currently is.
    /// </para>
    /// </summary>
    public Task UpdateStatusAsync(string? repositoryAlias, string itemPath, string status, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(itemPath)) throw new ArgumentException("Knowledge item path is required.", nameof(itemPath));
        if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

        var location = source.Resolve(".design", repositoryAlias);
        if (!location.Available) throw new InvalidOperationException(location.Message ?? "Design knowledge is unavailable.");
        if (location.FullPath is null) throw new InvalidOperationException("Design knowledge folder path is unavailable.");

        KnowledgeMarkdownStatusWriter.UpdateStatus(location.FullPath, itemPath, ".design/", status);
        return Task.CompletedTask;
    }

    private static List<DesignKnowledgeFile> OrderFiles(List<DesignKnowledgeFile> files)
    {
        var byName = files.ToDictionary(f => f.FileName, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<DesignKnowledgeFile>();

        if (byName.TryGetValue("README.md", out var readme))
        {
            ordered.Add(readme);
            foreach (var fileName in readme.ReadingOrder)
            {
                if (byName.TryGetValue(fileName, out var file) && !ordered.Contains(file))
                {
                    ordered.Add(file);
                }
            }
        }

        ordered.AddRange(files
            .Where(file => !ordered.Contains(file))
            .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase));

        return ordered;
    }
}

public static class DesignKnowledgeParser
{
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})[ \t]+(.+)$", RegexOptions.Compiled);
    private static readonly Regex OrderedListRegex = new(@"^[ \t]*\d+[.)][ \t]+(.+)$", RegexOptions.Compiled);
    private static readonly Regex UnorderedListRegex = new(@"^[ \t]*[-*][ \t]+(.+)$", RegexOptions.Compiled);
    private static readonly Regex TableSeparatorRegex = new(@"^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?$", RegexOptions.Compiled);

    public static DesignKnowledgeFile ParseFile(string folderPath, string path)
    {
        var fileName = Path.GetFileName(path);
        var lines = File.ReadAllText(path).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var index = 0;

        var title = Path.GetFileNameWithoutExtension(path);
        if (index < lines.Length && TryParseHeading(lines[index], out var heading) && heading.Level == 1)
        {
            title = heading.Text;
            index++;
        }

        index = SkipBlank(lines, index);
        var meta = TryParseMeta(lines, ref index);
        index = SkipBlank(lines, index);

        var summaryLines = new List<string>();
        while (index < lines.Length && lines[index].TrimStart().StartsWith(">", StringComparison.Ordinal))
        {
            summaryLines.Add(lines[index].TrimStart().TrimStart('>').TrimStart());
            index++;
        }

        var sections = new List<DesignKnowledgeSection>();
        while (index < lines.Length)
        {
            if (!TryParseHeading(lines[index], out var sectionHeading) || sectionHeading.Level != 2)
            {
                index++;
                continue;
            }

            index++;
            index = SkipBlank(lines, index);
            var sectionMeta = TryParseMeta(lines, ref index);
            var bodyStart = index;

            while (index < lines.Length && (!TryParseHeading(lines[index], out var nextHeading) || nextHeading.Level != 2))
            {
                index++;
            }

            var blocks = ParseBlocks(lines[bodyStart..index]);
            sections.Add(new DesignKnowledgeSection(
                sectionHeading.Text,
                AnchorFor(fileName, sectionHeading.Text),
                sectionMeta,
                blocks));
        }

        return new DesignKnowledgeFile(
            fileName,
            title,
            string.Join(' ', summaryLines).Trim(),
            meta,
            sections,
            ReadReadingOrder(lines));
    }

    /// <summary>
    /// The sibling file names <c>.design/README.md</c> lists in its own fence, which
    /// is the order the pane shows the folder in.
    ///
    /// <para>Read here rather than off the shared record. It is not metadata about
    /// the chapter it sits under — it is a directory listing that happens to be
    /// written in the same fence — so the shared schema does not model it, and a
    /// folder that wants it reads it itself. <c>DomainKnowledgeStore</c> and
    /// <c>TechnologyKnowledge</c> already do the same for their own roots.</para>
    ///
    /// <para>Only the file-level fence is consulted: the first one in the file, and
    /// only when it opens before any <c>##</c> heading. A chapter does not get to
    /// reorder the folder it is in.</para>
    /// </summary>
    private static IReadOnlyList<string> ReadReadingOrder(string[] lines)
    {
        var index = 0;
        while (index < lines.Length && !lines[index].Trim().Equals("```meta", StringComparison.OrdinalIgnoreCase))
        {
            // A `##` before the fence means the file states no block of its own.
            if (lines[index].StartsWith("## ", StringComparison.Ordinal)) return [];
            index++;
        }

        for (index++; index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal); index++)
        {
            var line = lines[index].Trim();
            if (!line.StartsWith("order:", StringComparison.OrdinalIgnoreCase)) continue;

            var value = line["order:".Length..].Trim();
            if (value.StartsWith('[') && value.EndsWith(']')) value = value[1..^1];

            return [.. value.Split(',')
                .Select(item => item.Trim().Trim('"', '\'').Trim())
                .Where(item => item.Length > 0)];
        }

        return [];
    }

    private static IReadOnlyList<DesignKnowledgeBlock> ParseBlocks(string[] lines)
    {
        var blocks = new List<DesignKnowledgeBlock>();
        var paragraph = new List<string>();
        var index = 0;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new DesignKnowledgeParagraph(MarkdownPreview.ParseInlines(string.Join(" ", paragraph))));
            paragraph.Clear();
        }

        while (index < lines.Length)
        {
            var line = lines[index];
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                index++;
                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                var language = trimmed[3..].Trim();
                var code = new List<string>();
                index++;

                while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    code.Add(lines[index]);
                    index++;
                }

                if (index < lines.Length) index++;
                if (string.Equals(language, "meta", StringComparison.OrdinalIgnoreCase)) continue;

                var source = string.Join('\n', code).TrimEnd();
                blocks.Add(IsDiagramLanguage(language)
                    ? new DesignKnowledgeDiagram(language, source)
                    : new DesignKnowledgeCode(language, source));
                continue;
            }

            if (IsTableStart(lines, index))
            {
                FlushParagraph();
                var tableLines = new List<string>();
                while (index < lines.Length && lines[index].TrimStart().StartsWith('|'))
                {
                    tableLines.Add(lines[index]);
                    index++;
                }

                blocks.Add(ParseTable(tableLines));
                continue;
            }

            if (TryParseHeading(line, out var heading) && heading.Level >= 3)
            {
                FlushParagraph();
                blocks.Add(new DesignKnowledgeSubheading(heading.Level, heading.Text));
                index++;
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                FlushParagraph();
                blocks.Add(new DesignKnowledgeQuote(MarkdownPreview.ParseInlines(trimmed[2..])));
                index++;
                continue;
            }

            if (trimmed is "---" or "***" or "___")
            {
                FlushParagraph();
                blocks.Add(new DesignKnowledgeDivider());
                index++;
                continue;
            }

            if (TryParseList(lines, ref index, out var list))
            {
                FlushParagraph();
                blocks.Add(list);
                continue;
            }

            paragraph.Add(trimmed);
            index++;
        }

        FlushParagraph();
        return blocks;
    }

    private static bool TryParseList(string[] lines, ref int index, out DesignKnowledgeList list)
    {
        list = new DesignKnowledgeList(false, []);
        var ordered = OrderedListRegex.Match(lines[index]).Success;
        var unordered = UnorderedListRegex.Match(lines[index]).Success;
        if (!ordered && !unordered) return false;

        var items = new List<IReadOnlyList<MdInline>>();
        while (index < lines.Length)
        {
            var match = ordered ? OrderedListRegex.Match(lines[index]) : UnorderedListRegex.Match(lines[index]);
            if (!match.Success) break;

            items.Add(MarkdownPreview.ParseInlines(match.Groups[1].Value.Trim()));
            index++;
        }

        list = new DesignKnowledgeList(ordered, items);
        return true;
    }

    private static DesignKnowledgeTable ParseTable(List<string> tableLines)
    {
        var headers = SplitTableRow(tableLines[0]);
        var rows = tableLines.Skip(2).Select(SplitTableRow).Where(row => row.Count > 0).ToList();
        var isTokenTable = headers.Any(h => string.Equals(h, "Token", StringComparison.OrdinalIgnoreCase));
        return new DesignKnowledgeTable(headers, rows, isTokenTable);
    }

    private static List<string> SplitTableRow(string line)
    {
        var text = line.Trim();
        if (text.StartsWith('|')) text = text[1..];
        if (text.EndsWith('|')) text = text[..^1];
        return [.. text.Split('|').Select(cell => cell.Trim())];
    }

    private static bool IsTableStart(string[] lines, int index) =>
        index + 1 < lines.Length
        && lines[index].TrimStart().StartsWith('|')
        && TableSeparatorRegex.IsMatch(lines[index + 1].Trim());

    private static bool IsDiagramLanguage(string language) =>
        language.Equals("mermaid", StringComparison.OrdinalIgnoreCase)
        || language.Equals("plantuml", StringComparison.OrdinalIgnoreCase)
        || language.Equals("dot", StringComparison.OrdinalIgnoreCase);

    private static int SkipBlank(string[] lines, int index)
    {
        while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index])) index++;
        return index;
    }

    /// <summary>
    /// The fenced <c>meta</c> block under the heading the cursor is sitting on,
    /// read by the shared knowledge reader rather than by a second one of this
    /// parser's own.
    /// <para>
    /// This used to keep a flat <c>key: value</c> dictionary, which is why
    /// <c>related</c> arrived at the view as raw paths and every field the schema
    /// defines beyond status and related was dropped on the floor. Collecting the
    /// fence body and handing it over means the design pane reads a block exactly
    /// as every other knowledge surface reads one — references parsed, absent
    /// fields absent.
    /// </para>
    /// </summary>
    private static MetadataRecord TryParseMeta(string[] lines, ref int index)
    {
        if (index >= lines.Length || !lines[index].Trim().Equals("```meta", StringComparison.OrdinalIgnoreCase))
        {
            return MetadataRecord.Empty;
        }

        index++;
        var body = new List<string>();
        while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            body.Add(lines[index]);
            index++;
        }

        if (index < lines.Length) index++;
        return MetadataReader.Parse(string.Join('\n', body));
    }

    private static bool TryParseHeading(string line, out ParsedHeading heading)
    {
        var match = HeadingRegex.Match(line.TrimStart());
        if (!match.Success)
        {
            heading = default;
            return false;
        }

        heading = new ParsedHeading(match.Groups[1].Value.Length, match.Groups[2].Value.Trim());
        return true;
    }

    private static string AnchorFor(string fileName, string heading)
    {
        var slug = Regex.Replace(heading.ToLowerInvariant(), @"[^a-z0-9\s-]", string.Empty);
        slug = Regex.Replace(slug, @"\s+", "-").Trim('-');
        return $"design-{Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant()}-{slug}";
    }

    private readonly record struct ParsedHeading(int Level, string Text);
}

public sealed record DesignKnowledgeModel(
    bool IsAvailable,
    string? RepositoryName,
    string? FolderPath,
    IReadOnlyList<DesignKnowledgeFile> Files,
    string Message)
{
    public static DesignKnowledgeModel Available(string repositoryName, string folderPath, IReadOnlyList<DesignKnowledgeFile> files) =>
        new(true, repositoryName, folderPath, files, string.Empty);

    public static DesignKnowledgeModel Unavailable(string message) =>
        new(false, null, null, [], message);
}

public sealed record DesignKnowledgeFile(
    string FileName,
    string Title,
    string Summary,
    MetadataRecord Meta,
    IReadOnlyList<DesignKnowledgeSection> Sections,
    IReadOnlyList<string> ReadingOrder)
{
    public IEnumerable<DesignKnowledgeTable> TokenTables =>
        Sections.SelectMany(section => section.Blocks.OfType<DesignKnowledgeTable>()).Where(table => table.IsTokenTable);
}

public sealed record DesignKnowledgeSection(
    string Heading,
    string Anchor,
    MetadataRecord Meta,
    IReadOnlyList<DesignKnowledgeBlock> Blocks);

// DesignKnowledgeMeta was here: a second reader for the `meta` fence, keeping a
// flat dictionary of strings and answering "unknown" for a status no file had
// stated. Both were visible to the reader — `related` reached the pane as raw
// paths because nothing had parsed them into references, and a chapter that said
// nothing was labelled with a word the folder does not define. The shared
// MetadataRecord is the one record now.

public abstract record DesignKnowledgeBlock;

public sealed record DesignKnowledgeSubheading(int Level, string Text) : DesignKnowledgeBlock;

public sealed record DesignKnowledgeParagraph(IReadOnlyList<MdInline> Content) : DesignKnowledgeBlock;

public sealed record DesignKnowledgeList(bool Ordered, IReadOnlyList<IReadOnlyList<MdInline>> Items) : DesignKnowledgeBlock;

public sealed record DesignKnowledgeTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, bool IsTokenTable) : DesignKnowledgeBlock;

public sealed record DesignKnowledgeQuote(IReadOnlyList<MdInline> Content) : DesignKnowledgeBlock;

public sealed record DesignKnowledgeCode(string Language, string Text) : DesignKnowledgeBlock;

public sealed record DesignKnowledgeDiagram(string Language, string Source) : DesignKnowledgeBlock;

public sealed record DesignKnowledgeDivider : DesignKnowledgeBlock;

