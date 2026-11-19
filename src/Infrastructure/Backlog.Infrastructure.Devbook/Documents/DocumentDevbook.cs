using System.Text.RegularExpressions;

using Backlog.Infrastructure.Devbook.Chapters;
using Backlog.SharedKernel.Markdown;

using Backlog.Modules.Devbook.Abstractions;

// Imported rather than aliased, which it was until the shared pair was renamed.
// Both of its old names were taken in the Devbook UI namespace this reader lived
// in before issue #738 moved it here — the arc42 reader grew a
// `DevbookMeta` of its own and the technology reader a `DevbookMetadata`,
// each predating the shared pair — so an unqualified name here would silently
// have resolved to the neighbour rather than to the library. `MetadataRecord`
// and `MetadataReader` collide with nothing, so the aliases the collision needed
// are gone. This is still the first of the three readers to read a block
// through the library; moving the other two is their own change.
using Backlog.SharedKernel.Metadata;

namespace Backlog.Infrastructure.Devbook.Documents;

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
                $"No Markdown files were found in the {Folder.DisplayName} devbook folder at {folderPath}.");
        }

        files = OrderFiles(files);
        return DocumentDevbookModel.Available(location.ScopeLabel ?? "storage", folderPath, files, location.CanEdit, location.RepositoryArchived);
    }

    /// <summary>
    /// Writes a rating into the <c>meta</c> fence under the addressed heading —
    /// the file's own when the path names a file, a chapter's when it carries an
    /// anchor — in <c>ai/</c>, where the field is a rating and stays in the
    /// Markdown.
    /// <para>
    /// <c>design/</c>'s status was workflow state, which is the tool's now and not
    /// the file's, so a write there is refused by the rating writer before the
    /// file is opened; the design view offers no control that would ask. A rating
    /// is never cleared — <c>ai/</c> requires one — so there is no clearing verb.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">The folder keeps no rating.</exception>
    public Task UpdateStatusAsync(string? repositoryAlias, string itemPath, string status, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(itemPath)) throw new ArgumentException("Devbook item path is required.", nameof(itemPath));
        if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

        var location = _source.Resolve(Folder.Key, repositoryAlias);
        var folderPath = location.WritablePath(Folder.DisplayName);

        DevbookMarkdownRatingWriter.UpdateRating(folderPath, itemPath, Folder.PathPrefix, status);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The <c>sync</c> state of every block in one document: where each sits, read
    /// from the Markdown with the blocks above it, and the direction in force
    /// there, read from <paramref name="stated"/> — the repository's direction map.
    /// Read from whatever the folder resolves to, branch snapshot included. There is
    /// no write beside it: a direction is the map's, set through
    /// <c>IDevbookSyncDirectionStore</c>, and nothing writes <c>sync</c> into a
    /// file.
    /// </summary>
    public Task<DevbookSyncReading> ReadSyncAsync(
        string? repositoryAlias,
        string documentPath,
        Func<string, string?> stated,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stated);

        var location = _source.Resolve(Folder.Key, repositoryAlias);
        if (!location.Available || location.FullPath is null) return Task.FromResult(DevbookSyncReading.None);

        var folderPath = location.FullPath;
        return Task.Run(() => DevbookSyncReading.Read(folderPath, Folder.Folder, documentPath, stated), cancellationToken);
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

/// <summary>
/// Reads one file of a document-shaped devbook folder into the pane's model: its
/// title, the file's <c>meta</c> block and summary quote, and a section per
/// <c>##</c> heading with that heading's block and the blocks under it.
/// <para>
/// The Markdown is read by <see cref="MarkdownPreview.ParseDocument"/>, the shared
/// block parser, and the blocks mapped onto this model; the <c>meta</c> fences by
/// <see cref="MetadataReader"/>. A <c>##</c> inside a fence is the fence's, so it
/// starts no section.
/// </para>
/// </summary>
public static partial class DocumentDevbookParser
{
    /// <summary>
    /// One file, parsed. <paramref name="areaKey"/> prefixes the anchors the
    /// sections get, so two folders' overviews on one page never share an id.
    /// </summary>
    public static DocumentDevbookFile ParseFile(string folderPath, string path, string areaKey = "design")
    {
        var fileName = Path.GetFileName(path);
        var markdown = DevbookMarkdownBlocks.Normalize(File.ReadAllText(path));
        var lines = markdown.Split('\n');
        var blocks = MarkdownPreview.ParseDocument(markdown);
        var index = 0;

        // The title is the file's first line, when that line is a `#` heading.
        var title = Path.GetFileNameWithoutExtension(path);
        if (index < blocks.Count && blocks[index] is MdHeading { Level: 1, Source.StartLine: 0 } heading)
        {
            title = DevbookMarkdownBlocks.HeadingText(lines, heading);
            index++;
        }

        var meta = DevbookMarkdownBlocks.TakeMeta(blocks, ref index) ?? MetadataRecord.Empty;

        var summary = string.Empty;
        if (index < blocks.Count && blocks[index] is MdQuote quote)
        {
            summary = string.Join(' ', DevbookMarkdownBlocks.SourceLines(lines, quote.Source)
                .Select(line => line.TrimStart().TrimStart('>').TrimStart())).Trim();
            index++;
        }

        var sections = new List<DocumentDevbookSection>();
        while (index < blocks.Count)
        {
            if (blocks[index] is not MdHeading { Level: 2 } sectionHeading)
            {
                index++;
                continue;
            }

            index++;
            var text = DevbookMarkdownBlocks.HeadingText(lines, sectionHeading);
            var sectionMeta = DevbookMarkdownBlocks.TakeMeta(blocks, ref index) ?? MetadataRecord.Empty;

            var body = new List<DocumentDevbookBlock>();
            for (; index < blocks.Count && blocks[index] is not MdHeading { Level: 2 }; index++)
            {
                if (Map(blocks[index], lines) is { } block) body.Add(block);
            }

            sections.Add(new DocumentDevbookSection(text, AnchorFor(areaKey, fileName, text), sectionMeta, body));
        }

        return new DocumentDevbookFile(fileName, title, summary, meta, sections);
    }

    /// <summary>One shared block as this model's, or null for one it does not draw:
    /// a <c>meta</c> fence away from a heading.</summary>
    private static DocumentDevbookBlock? Map(MdBlock block, IReadOnlyList<string> lines) => block switch
    {
        MdParagraph paragraph => new DocumentDevbookParagraph(paragraph.Content),
        MdHeading heading => new DocumentDevbookSubheading(heading.Level, DevbookMarkdownBlocks.HeadingText(lines, heading)),
        MdCode code when MetadataReader.IsMetaBlock(code.Language) => null,
        MdCode code when IsDiagramLanguage(code.Language) => new DocumentDevbookDiagram(code.Language, code.Text.TrimEnd()),
        MdCode code => new DocumentDevbookCode(code.Language, code.Text.TrimEnd()),
        MdTable table => ParseTable(DevbookMarkdownBlocks.SourceLines(lines, table.Source)),
        MdQuote quote => new DocumentDevbookQuote(quote.Content),
        MdList list => new DocumentDevbookList(list.Ordered, DevbookMarkdownBlocks.FlatItems(list)),
        MdFootnotes notes => new DocumentDevbookList(true, DevbookMarkdownBlocks.NoteItems(notes)),
        MdDivider => new DocumentDevbookDivider(),
        _ => null
    };

    private static DocumentDevbookTable ParseTable(IReadOnlyList<string> tableLines)
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

    private static bool IsDiagramLanguage(string language) =>
        language.Equals("mermaid", StringComparison.OrdinalIgnoreCase)
        || language.Equals("plantuml", StringComparison.OrdinalIgnoreCase)
        || language.Equals("dot", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The section's element id: the folder, the file and the heading, so two
    /// folders' overviews on one page never share one.
    /// <para>
    /// Not <see cref="HeadingAnchor"/>, on purpose: this is an element id rather than
    /// a heading's address, ASCII-only with runs collapsed, and moving it onto the
    /// GitHub rule would change the id of every section whose heading holds
    /// punctuation between words (<c>Screen Reader / Announcements</c>).
    /// </para>
    /// </summary>
    private static string AnchorFor(string areaKey, string fileName, string heading)
    {
        var slug = NotIdCharacter().Replace(heading.ToLowerInvariant(), string.Empty);
        slug = WhitespaceRun().Replace(slug, "-").Trim('-');
        return $"{areaKey}-{Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant()}-{slug}";
    }

    [GeneratedRegex(@"[^a-z0-9\s-]")]
    private static partial Regex NotIdCharacter();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
/// <param name="RepositoryArchived">Whether the repository the folder belongs to
/// is archived (local ADR 0029 §4) — which refuses a sync direction, a write to
/// Backlog's own map that a read-only branch snapshot still takes.</param>
public sealed record DocumentDevbookModel(
    bool IsAvailable,
    string? RepositoryName,
    string? FolderPath,
    IReadOnlyList<DocumentDevbookFile> Files,
    string Message,
    bool CanEdit = true,
    bool RepositoryArchived = false)
{
    public static DocumentDevbookModel Available(
        string repositoryName,
        string folderPath,
        IReadOnlyList<DocumentDevbookFile> files,
        bool canEdit = true,
        bool repositoryArchived = false) =>
        new(true, repositoryName, folderPath, files, string.Empty, canEdit, repositoryArchived);

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

