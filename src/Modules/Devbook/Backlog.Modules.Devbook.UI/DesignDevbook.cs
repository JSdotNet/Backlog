using System.Text.RegularExpressions;

using Backlog.UI.Components.Markdown;

using Backlog.Modules.Devbook.Abstractions;

// Imported rather than aliased, which it was until the shared pair was renamed.
// Both of its old names were taken in this namespace — the arc42 reader grew a
// `DevbookMeta` of its own and the technology reader a `DevbookMetadata`,
// each predating the shared pair — so an unqualified name here would silently
// have resolved to the neighbour rather than to the library. `MetadataRecord`
// and `MetadataReader` collide with nothing, so the aliases the collision needed
// are gone. Design is still the first of the three readers to read a block
// through the library; moving the other two is their own change.
using Backlog.UI.Components.Metadata;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Loads the repository's `.design` knowledge folder for the desktop wide-screen
/// Devbook pane. The Markdown files remain canonical; this service only builds
/// a read model for display.
/// </summary>
public sealed class DesignDevbookProvider : IDisposable
{
    private readonly IDevbookFolderSource _source;

    /// <summary>Every file parsed so far, kept while it stays as it was on disk —
    /// see <see cref="DevbookFileCache{T}"/>. The design view is disposed with its
    /// tab and asks for the folder again on the way back.</summary>
    private readonly DevbookFileCache<DesignDevbookFile> _files = new();

    public DesignDevbookProvider(IDevbookFolderSource source)
    {
        _source = source;
        _source.Changed += _files.Clear;
    }

    /// <summary>Lets go of the folder source. The store is a singleton and so is
    /// the source, so nothing leaks in the app — but a host that tears its
    /// container down, as the tests do, must find no handler left behind.</summary>
    public void Dispose() => _source.Changed -= _files.Clear;

    /// <summary>Re-published from the folder source so an open panel can reload
    /// when the configured folder moves.</summary>
    public event Action? Changed
    {
        add => _source.Changed += value;
        remove => _source.Changed -= value;
    }

    public async Task<DesignDevbookModel> LoadAsync(string? repositoryAlias = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Prepared rather than resolved: the model parses every file in the
        // folder, so a branch's design folder is fetched here, whole, once.
        var location = await _source.PrepareContentAsync(".design", repositoryAlias, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!location.Available || location.FullPath is null)
        {
            return DesignDevbookModel.Unavailable(location.Message ?? "Design is unavailable.");
        }

        var folderPath = location.FullPath;

        // Off the dispatcher, which in the desktop host is the UI thread: the
        // parse is the whole folder, and it should not sit between a click on the
        // Design tab and the pane painting its loading line.
        var files = await Task.Run(
            () => Directory.EnumerateFiles(folderPath, "*.md", SearchOption.TopDirectoryOnly)
                .Select(path => _files.GetOrAdd(path, () => DesignDevbookParser.ParseFile(folderPath, path)))
                .ToList(),
            cancellationToken).ConfigureAwait(false);

        if (files.Count == 0)
        {
            return DesignDevbookModel.Unavailable(
                $"No Markdown files were found in the Design knowledge folder at {folderPath}.");
        }

        files = OrderFiles(files, folderPath);
        return DesignDevbookModel.Available(location.ScopeLabel ?? "storage", folderPath, files, location.CanEdit);
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
        if (string.IsNullOrWhiteSpace(itemPath)) throw new ArgumentException("Devbook item path is required.", nameof(itemPath));
        if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

        var location = _source.Resolve(".design", repositoryAlias);
        var folderPath = location.WritablePath("Design");

        DevbookMarkdownStatusWriter.UpdateStatus(folderPath, itemPath, ".design/", status);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Remove the item's <c>status</c> field, leaving its <c>meta</c> fence and
    /// every other field intact. <c>.design</c> states a status only while a
    /// guideline is unsettled or superseded; a guideline that is simply current
    /// says so by saying nothing.
    ///
    /// <para>A separate method rather than <see cref="UpdateStatusAsync"/> taking a
    /// null — see the same method on the domain store for why.</para>
    /// </summary>
    public Task ClearStatusAsync(string? repositoryAlias, string itemPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(itemPath)) throw new ArgumentException("Devbook item path is required.", nameof(itemPath));

        var location = _source.Resolve(".design", repositoryAlias);
        var folderPath = location.WritablePath("Design");

        DevbookMarkdownStatusWriter.RemoveStatus(folderPath, itemPath, ".design/");
        return Task.CompletedTask;
    }

    /// <summary>
    /// The folder in reading order: the README first, then the siblings in the
    /// order the folder's committed <c>_meta/index.json</c> records, then anything
    /// the index does not mention, alphabetically.
    ///
    /// <para>The order used to be read off the README's own <c>meta</c> fence. It
    /// is not metadata about a chapter — it is a directory listing — so it now
    /// lives in the index that describes the directory, which is also the one
    /// place the generator and the pane can agree on it.</para>
    /// </summary>
    private static List<DesignDevbookFile> OrderFiles(List<DesignDevbookFile> files, string folderPath)
    {
        var byName = files.ToDictionary(f => f.FileName, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<DesignDevbookFile>();

        if (byName.TryGetValue("README.md", out var readme))
        {
            ordered.Add(readme);
            foreach (var fileName in DevbookReadingOrder.ForFolder(folderPath))
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

public static class DesignDevbookParser
{
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})[ \t]+(.+)$", RegexOptions.Compiled);
    private static readonly Regex OrderedListRegex = new(@"^[ \t]*\d+[.)][ \t]+(.+)$", RegexOptions.Compiled);
    private static readonly Regex UnorderedListRegex = new(@"^[ \t]*[-*][ \t]+(.+)$", RegexOptions.Compiled);
    private static readonly Regex TableSeparatorRegex = new(@"^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?$", RegexOptions.Compiled);

    public static DesignDevbookFile ParseFile(string folderPath, string path)
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

        var sections = new List<DesignDevbookSection>();
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
            sections.Add(new DesignDevbookSection(
                sectionHeading.Text,
                AnchorFor(fileName, sectionHeading.Text),
                sectionMeta,
                blocks));
        }

        return new DesignDevbookFile(
            fileName,
            title,
            string.Join(' ', summaryLines).Trim(),
            meta,
            sections);
    }

    private static IReadOnlyList<DesignDevbookBlock> ParseBlocks(string[] lines)
    {
        var blocks = new List<DesignDevbookBlock>();
        var paragraph = new List<string>();
        var index = 0;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new DesignDevbookParagraph(MarkdownPreview.ParseInlines(string.Join(" ", paragraph))));
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
                    ? new DesignDevbookDiagram(language, source)
                    : new DesignDevbookCode(language, source));
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
                blocks.Add(new DesignDevbookSubheading(heading.Level, heading.Text));
                index++;
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                FlushParagraph();
                blocks.Add(new DesignDevbookQuote(MarkdownPreview.ParseInlines(trimmed[2..])));
                index++;
                continue;
            }

            if (trimmed is "---" or "***" or "___")
            {
                FlushParagraph();
                blocks.Add(new DesignDevbookDivider());
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

    private static bool TryParseList(string[] lines, ref int index, out DesignDevbookList list)
    {
        list = new DesignDevbookList(false, []);
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

        list = new DesignDevbookList(ordered, items);
        return true;
    }

    private static DesignDevbookTable ParseTable(List<string> tableLines)
    {
        var headers = SplitTableRow(tableLines[0]);
        var rows = tableLines.Skip(2).Select(SplitTableRow).Where(row => row.Count > 0).ToList();
        var isTokenTable = headers.Any(h => string.Equals(h, "Token", StringComparison.OrdinalIgnoreCase));
        return new DesignDevbookTable(headers, rows, isTokenTable);
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

public sealed record DesignDevbookModel(
    bool IsAvailable,
    string? RepositoryName,
    string? FolderPath,
    IReadOnlyList<DesignDevbookFile> Files,
    string Message,
    bool CanEdit = true)
{
    public static DesignDevbookModel Available(
        string repositoryName,
        string folderPath,
        IReadOnlyList<DesignDevbookFile> files,
        bool canEdit = true) =>
        new(true, repositoryName, folderPath, files, string.Empty, canEdit);

    public static DesignDevbookModel Unavailable(string message) =>
        new(false, null, null, [], message);
}

public sealed record DesignDevbookFile(
    string FileName,
    string Title,
    string Summary,
    MetadataRecord Meta,
    IReadOnlyList<DesignDevbookSection> Sections)
{
    // ReadingOrder was here, read off this file's own `meta` fence. It existed for
    // one round: the shared record had dropped `order` and the fences still carried
    // it, so a folder that wanted its reading order had to parse it itself. `main`
    // then moved the declaration into the committed `_meta/index.json`, which is a
    // better home for a directory listing than a chapter's metadata, so the parse
    // has nothing left to read and `DevbookReadingOrder.ForFolder` answers instead.

    public IEnumerable<DesignDevbookTable> TokenTables =>
        Sections.SelectMany(section => section.Blocks.OfType<DesignDevbookTable>()).Where(table => table.IsTokenTable);
}

public sealed record DesignDevbookSection(
    string Heading,
    string Anchor,
    MetadataRecord Meta,
    IReadOnlyList<DesignDevbookBlock> Blocks);

// DesignDevbookMeta was here: a second reader for the `meta` fence, keeping a
// flat dictionary of strings and answering "unknown" for a status no file had
// stated. Both were visible to the reader — `related` reached the pane as raw
// paths because nothing had parsed them into references, and a chapter that said
// nothing was labelled with a word the folder does not define. The shared
// MetadataRecord is the one record now.

public abstract record DesignDevbookBlock;

public sealed record DesignDevbookSubheading(int Level, string Text) : DesignDevbookBlock;

public sealed record DesignDevbookParagraph(IReadOnlyList<MdInline> Content) : DesignDevbookBlock;

public sealed record DesignDevbookList(bool Ordered, IReadOnlyList<IReadOnlyList<MdInline>> Items) : DesignDevbookBlock;

public sealed record DesignDevbookTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, bool IsTokenTable) : DesignDevbookBlock;

public sealed record DesignDevbookQuote(IReadOnlyList<MdInline> Content) : DesignDevbookBlock;

public sealed record DesignDevbookCode(string Language, string Text) : DesignDevbookBlock;

public sealed record DesignDevbookDiagram(string Language, string Source) : DesignDevbookBlock;

public sealed record DesignDevbookDivider : DesignDevbookBlock;

