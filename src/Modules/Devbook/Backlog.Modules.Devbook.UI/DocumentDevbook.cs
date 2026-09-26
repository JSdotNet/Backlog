using System.Text.RegularExpressions;

using Backlog.UI.Components.Markdown;

using Backlog.Modules.Devbook.Abstractions;

// Imported rather than aliased, which it was until the shared pair was renamed.
// Both of its old names were taken in this namespace — the arc42 reader grew a
// `DevbookMeta` of its own and the technology reader a `DevbookMetadata`,
// each predating the shared pair — so an unqualified name here would silently
// have resolved to the neighbour rather than to the library. `MetadataRecord`
// and `MetadataReader` collide with nothing, so the aliases the collision needed
// are gone. This is still the first of the three readers to read a block
// through the library; moving the other two is their own change.
using Backlog.UI.Components.Metadata;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Loads one document-shaped devbook folder — see <see cref="DocumentDevbookFolder"/>
/// — for the desktop wide-screen Devbook pane. The Markdown files remain
/// canonical; this service only builds a read model for display.
/// <para>
/// Abstract, with one sealed subclass per folder, rather than one class
/// registered twice: the view for a folder asks the container for that folder's
/// provider by type, which is how a <c>@inject</c> names it, and a keyed
/// registration would have moved the folder's name out of the type and into a
/// string beside every injection. The subclasses carry nothing but the folder.
/// </para>
/// </summary>
public abstract class DocumentDevbookProvider : IDisposable
{
    private readonly IDevbookFolderSource _source;

    /// <summary>Every file parsed so far, kept while it stays as it was on disk —
    /// see <see cref="DevbookFileCache{T}"/>. The view is disposed with its tab
    /// and asks for the folder again on the way back.</summary>
    private readonly DevbookFileCache<DocumentDevbookFile> _files = new();

    protected DocumentDevbookProvider(IDevbookFolderSource source, DocumentDevbookFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        _source = source;
        Folder = folder;
        _source.Changed += _files.Clear;
    }

    /// <summary>Which folder this provider reads. The view takes everything it
    /// says about the folder — its name, its vocabulary, its root — from here.</summary>
    public DocumentDevbookFolder Folder { get; }

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

    public async Task<DocumentDevbookModel> LoadAsync(string? repositoryAlias = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Prepared rather than resolved: the model parses every file in the
        // folder, so a branch's folder is fetched here, whole, once.
        var location = await _source.PrepareContentAsync(Folder.Key, repositoryAlias, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!location.Available || location.FullPath is null)
        {
            return DocumentDevbookModel.Unavailable(location.Message ?? $"{Folder.DisplayName} is unavailable.");
        }

        var folderPath = location.FullPath;

        // Off the dispatcher, which in the desktop host is the UI thread: the
        // parse is the whole folder, and it should not sit between a click on the
        // area's tab and the pane painting its loading line.
        var files = await Task.Run(
            () => Directory.EnumerateFiles(folderPath, "*.md", SearchOption.TopDirectoryOnly)
                .Select(path => _files.GetOrAdd(path, () => DocumentDevbookParser.ParseFile(folderPath, path, Folder.AreaKey)))
                .ToList(),
            cancellationToken).ConfigureAwait(false);

        if (files.Count == 0)
        {
            return DocumentDevbookModel.Unavailable(
                $"No Markdown files were found in the {Folder.DisplayName} knowledge folder at {folderPath}.");
        }

        files = OrderFiles(files);
        return DocumentDevbookModel.Available(location.ScopeLabel ?? "storage", folderPath, files, location.CanEdit);
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

        var location = _source.Resolve(Folder.Key, repositoryAlias);
        var folderPath = location.WritablePath(Folder.DisplayName);

        DevbookMarkdownStatusWriter.UpdateStatus(folderPath, itemPath, Folder.PathPrefix, status);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Remove the item's <c>status</c> field, leaving its <c>meta</c> fence and
    /// every other field intact. <c>.design</c> states a status only while a
    /// guideline is unsettled or superseded; a guideline that is simply current
    /// says so by saying nothing. Offered for every folder this provider reads
    /// and only ever reached in one whose vocabulary allows an absent status —
    /// <c>.ai</c> requires one, and its record view offers no way to clear it.
    ///
    /// <para>A separate method rather than <see cref="UpdateStatusAsync"/> taking a
    /// null — see the same method on the domain store for why.</para>
    /// </summary>
    public Task ClearStatusAsync(string? repositoryAlias, string itemPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(itemPath)) throw new ArgumentException("Devbook item path is required.", nameof(itemPath));

        var location = _source.Resolve(Folder.Key, repositoryAlias);
        var folderPath = location.WritablePath(Folder.DisplayName);

        DevbookMarkdownStatusWriter.RemoveStatus(folderPath, itemPath, Folder.PathPrefix);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The folder in reading order, as the folder convention derives it from the
    /// file names (<see cref="DevbookReadingConvention"/>): the root document
    /// first, then — <c>.ai</c>, whose stage files are numbered — the numbered
    /// flow, or — <c>.design</c> — the prescribed guidelines in the convention's
    /// sequence, and the rest by name.
    ///
    /// <para>The order used to be read off the README's own <c>meta</c> fence and
    /// then off a committed <c>_reading-order.json</c>; local ADR 0016 retired the
    /// file, and one left in the folder is ignored. Names only, never the files'
    /// own <c>meta</c> blocks: the database's outline honours a document's
    /// <c>index</c> and <c>number</c> fields, and this pane is the fallback that
    /// does not open a Markdown file to find an order. A folder the convention
    /// has no entry for keeps its root first and the alphabet after it.</para>
    /// </summary>
    private List<DocumentDevbookFile> OrderFiles(List<DocumentDevbookFile> files)
    {
        var byName = files.ToDictionary(f => f.FileName, StringComparer.OrdinalIgnoreCase);

        if (DevbookReadingConvention.For(Folder.AreaKey, 0) is null)
        {
            return
            [
                .. files.Where(file => string.Equals(file.FileName, Folder.RootDocument, StringComparison.OrdinalIgnoreCase)),
                .. files
                    .Where(file => !string.Equals(file.FileName, Folder.RootDocument, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            ];
        }

        return [.. DevbookReadingConvention.Order(Folder.AreaKey, 0, byName.Keys).Select(name => byName[name])];
    }
}

/// <summary>
/// The design folder's provider. Nothing but the folder; see the base class.
/// </summary>
public sealed class DesignDevbookProvider(IDevbookFolderSource source)
    : DocumentDevbookProvider(source, DocumentDevbookFolder.Design);

/// <summary>
/// The AI adoption record's provider. Nothing but the folder; see the base class.
/// </summary>
public sealed class AiDevbookProvider(IDevbookFolderSource source)
    : DocumentDevbookProvider(source, DocumentDevbookFolder.Ai);

public static class DocumentDevbookParser
{
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})[ \t]+(.+)$", RegexOptions.Compiled);
    private static readonly Regex OrderedListRegex = new(@"^[ \t]*\d+[.)][ \t]+(.+)$", RegexOptions.Compiled);
    private static readonly Regex UnorderedListRegex = new(@"^[ \t]*[-*][ \t]+(.+)$", RegexOptions.Compiled);
    private static readonly Regex TableSeparatorRegex = new(@"^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?$", RegexOptions.Compiled);

    /// <summary>
    /// One file, parsed. <paramref name="areaKey"/> prefixes the anchors the
    /// sections get, so two folders' overviews on one page never share an id.
    /// </summary>
    public static DocumentDevbookFile ParseFile(string folderPath, string path, string areaKey = "design")
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

        var sections = new List<DocumentDevbookSection>();
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
            sections.Add(new DocumentDevbookSection(
                sectionHeading.Text,
                AnchorFor(areaKey, fileName, sectionHeading.Text),
                sectionMeta,
                blocks));
        }

        return new DocumentDevbookFile(
            fileName,
            title,
            string.Join(' ', summaryLines).Trim(),
            meta,
            sections);
    }

    private static IReadOnlyList<DocumentDevbookBlock> ParseBlocks(string[] lines)
    {
        var blocks = new List<DocumentDevbookBlock>();
        var paragraph = new List<string>();
        var index = 0;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new DocumentDevbookParagraph(MarkdownPreview.ParseInlines(string.Join(" ", paragraph))));
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

            // CommonMark's fence rather than "starts with three backticks", so a
            // devbook `annotation` note holding a code sample closes where its
            // own four-backtick run does. The note stays a code record, which
            // DevbookBlocks hands to the library to draw as a review note.
            if (MarkdownFence.Open(trimmed) is { } fence)
            {
                FlushParagraph();
                var language = fence.Language;
                var code = new List<string>();
                index++;

                while (index < lines.Length && !fence.IsClosedBy(lines[index]))
                {
                    code.Add(lines[index]);
                    index++;
                }

                if (index < lines.Length) index++;
                if (string.Equals(language, "meta", StringComparison.OrdinalIgnoreCase)) continue;

                var source = string.Join('\n', code).TrimEnd();
                blocks.Add(IsDiagramLanguage(language)
                    ? new DocumentDevbookDiagram(language, source)
                    : new DocumentDevbookCode(language, source));
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
                blocks.Add(new DocumentDevbookSubheading(heading.Level, heading.Text));
                index++;
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                FlushParagraph();
                blocks.Add(new DocumentDevbookQuote(MarkdownPreview.ParseInlines(trimmed[2..])));
                index++;
                continue;
            }

            if (trimmed is "---" or "***" or "___")
            {
                FlushParagraph();
                blocks.Add(new DocumentDevbookDivider());
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

    private static bool TryParseList(string[] lines, ref int index, out DocumentDevbookList list)
    {
        list = new DocumentDevbookList(false, []);
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

        list = new DocumentDevbookList(ordered, items);
        return true;
    }

    private static DocumentDevbookTable ParseTable(List<string> tableLines)
    {
        var headers = SplitTableRow(tableLines[0]);
        var rows = tableLines.Skip(2).Select(SplitTableRow).Where(row => row.Count > 0).ToList();
        var isTokenTable = headers.Any(h => string.Equals(h, "Token", StringComparison.OrdinalIgnoreCase));
        return new DocumentDevbookTable(headers, rows, isTokenTable);
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

    private static string AnchorFor(string areaKey, string fileName, string heading)
    {
        var slug = Regex.Replace(heading.ToLowerInvariant(), @"[^a-z0-9\s-]", string.Empty);
        slug = Regex.Replace(slug, @"\s+", "-").Trim('-');
        return $"{areaKey}-{Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant()}-{slug}";
    }

    private readonly record struct ParsedHeading(int Level, string Text);
}

public sealed record DocumentDevbookModel(
    bool IsAvailable,
    string? RepositoryName,
    string? FolderPath,
    IReadOnlyList<DocumentDevbookFile> Files,
    string Message,
    bool CanEdit = true)
{
    public static DocumentDevbookModel Available(
        string repositoryName,
        string folderPath,
        IReadOnlyList<DocumentDevbookFile> files,
        bool canEdit = true) =>
        new(true, repositoryName, folderPath, files, string.Empty, canEdit);

    public static DocumentDevbookModel Unavailable(string message) =>
        new(false, null, null, [], message);
}

public sealed record DocumentDevbookFile(
    string FileName,
    string Title,
    string Summary,
    MetadataRecord Meta,
    IReadOnlyList<DocumentDevbookSection> Sections)
{
    // ReadingOrder was here, read off this file's own `meta` fence. It existed for
    // one round: the shared record had dropped `order` and the fences still carried
    // it, so a folder that wanted its reading order had to parse it itself. `main`
    // then moved the declaration into the committed `_meta/index.json`, which is a
    // better home for a directory listing than a chapter's metadata, so the parse
    // has nothing left to read. The order is now the folder convention's, derived
    // from the file names — `DevbookReadingConvention` answers instead.

    public IEnumerable<DocumentDevbookTable> TokenTables =>
        Sections.SelectMany(section => section.Blocks.OfType<DocumentDevbookTable>()).Where(table => table.IsTokenTable);
}

public sealed record DocumentDevbookSection(
    string Heading,
    string Anchor,
    MetadataRecord Meta,
    IReadOnlyList<DocumentDevbookBlock> Blocks);

// DocumentDevbookMeta was here: a second reader for the `meta` fence, keeping a
// flat dictionary of strings and answering "unknown" for a status no file had
// stated. Both were visible to the reader — `related` reached the pane as raw
// paths because nothing had parsed them into references, and a chapter that said
// nothing was labelled with a word the folder does not define. The shared
// MetadataRecord is the one record now.

public abstract record DocumentDevbookBlock;

public sealed record DocumentDevbookSubheading(int Level, string Text) : DocumentDevbookBlock;

public sealed record DocumentDevbookParagraph(IReadOnlyList<MdInline> Content) : DocumentDevbookBlock;

public sealed record DocumentDevbookList(bool Ordered, IReadOnlyList<IReadOnlyList<MdInline>> Items) : DocumentDevbookBlock;

public sealed record DocumentDevbookTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, bool IsTokenTable) : DocumentDevbookBlock;

public sealed record DocumentDevbookQuote(IReadOnlyList<MdInline> Content) : DocumentDevbookBlock;

public sealed record DocumentDevbookCode(string Language, string Text) : DocumentDevbookBlock;

public sealed record DocumentDevbookDiagram(string Language, string Source) : DocumentDevbookBlock;

public sealed record DocumentDevbookDivider : DocumentDevbookBlock;

