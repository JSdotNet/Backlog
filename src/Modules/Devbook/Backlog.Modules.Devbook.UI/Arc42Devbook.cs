using System.Text.RegularExpressions;

using Backlog.UI.Components.Markdown;

using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.Devbook;

public sealed class Arc42DevbookStore : IDisposable
{
    private readonly IDevbookFolderSource _source;

    /// <summary>Every chapter parsed so far, kept while its file stays as it was.
    /// The panel that shows this catalog is disposed on every tab switch and asks
    /// for the folder again on the way back; without this that was thirty-nine
    /// files parsed to redraw one. See <see cref="DevbookFileCache{T}"/>.</summary>
    private readonly DevbookFileCache<DevbookDocument> _documents = new();

    public Arc42DevbookStore(IDevbookFolderSource source)
    {
        _source = source;
        _source.Changed += _documents.Clear;
    }

    /// <summary>Lets go of the folder source. The store is a singleton and so is
    /// the source, so nothing leaks in the app — but a host that tears its
    /// container down, as the tests do, must find no handler left behind.</summary>
    public void Dispose() => _source.Changed -= _documents.Clear;

    public event Action? Changed
    {
        add => _source.Changed += value;
        remove => _source.Changed -= value;
    }

    public async Task<Arc42DevbookCatalog> LoadAsync(string? repositoryAlias = null)
    {
        // Prepared rather than resolved: the catalog parses every chapter in
        // the folder, so this is the moment a branch's architecture chapters
        // are fetched — the whole area, once, and never again until it moves.
        var location = await _source.PrepareContentAsync(".arc42", repositoryAlias).ConfigureAwait(false);
        if (!location.Available || location.FullPath is null)
        {
            return Arc42DevbookCatalog.Missing(location.RootPath ?? location.FullPath ?? string.Empty);
        }

        // The root travels with the folder, because the index inside a folder
        // spells its entries relative to the repository, not to the folder. A
        // folder pointed at docs/arch carries entries reading docs/arch/..., and
        // resolving those against the folder's own parent would look for them
        // under docs/docs/arch and list nothing at all.
        // The reader knows nothing about where the folder came from, so the
        // editability the resolution decided is stamped on afterwards rather
        // than threaded through a loader that would only carry it.
        //
        // On the thread pool, because the caller is a component and its
        // continuation is the dispatcher: in the desktop host that is the UI
        // thread, and a folder parse there is a pane that does not paint until it
        // is over.
        var folderPath = location.FullPath;
        var rootPath = location.RootPath;
        var catalog = await Task.Run(() => Arc42DevbookReader.LoadFolder(folderPath, rootPath, _documents)).ConfigureAwait(false);

        return catalog with { CanEdit = location.CanEdit };
    }

    public Task UpdateStatusAsync(string? repositoryAlias, string itemPath, string status, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(itemPath)) throw new ArgumentException("Devbook item path is required.", nameof(itemPath));
        if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

        var location = _source.Resolve(".arc42", repositoryAlias);
        var folderPath = location.WritablePath("Architecture");

        DevbookMarkdownStatusWriter.UpdateStatus(folderPath, itemPath, ".arc42/", status);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Remove the item's <c>status</c> field, leaving its <c>meta</c> fence and
    /// every other field intact. An <c>.arc42</c> chapter describing standing
    /// structure states a status only while it is still being agreed, or once it
    /// has been superseded.
    ///
    /// <para>A separate method rather than <see cref="UpdateStatusAsync"/> taking a
    /// null — see the same method on the domain store for why.</para>
    /// </summary>
    public Task ClearStatusAsync(string? repositoryAlias, string itemPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(itemPath)) throw new ArgumentException("Devbook item path is required.", nameof(itemPath));

        var location = _source.Resolve(".arc42", repositoryAlias);
        var folderPath = location.WritablePath("Architecture");

        DevbookMarkdownStatusWriter.RemoveStatus(folderPath, itemPath, ".arc42/");
        return Task.CompletedTask;
    }
}

public static class Arc42DevbookReader
{
    public static Task<Arc42DevbookCatalog> LoadAsync(string rootDirectory) =>
        LoadFolderAsync(Path.Combine(rootDirectory, ".arc42"), rootDirectory);

    /// <summary>Reads one arc42 folder into a catalog.
    /// <para>
    /// <paramref name="repositoryRoot"/> is the repository the folder sits in, for
    /// a caller that knows it. Every path this catalog carries is spelled relative
    /// to that root — which is also the spelling the index generator writes, so
    /// the two agree for a folder wherever it has been pointed. Without it the
    /// folder's own parent stands in, which is the same answer for the
    /// conventional folder at the repository root and the only answer available
    /// for a folder configured somewhere off the clone entirely.
    /// </para></summary>
    public static Task<Arc42DevbookCatalog> LoadFolderAsync(string arc42Directory, string? repositoryRoot = null) =>
        Task.FromResult(LoadFolder(arc42Directory, repositoryRoot, cache: null));

    /// <summary>
    /// The same read, synchronous and remembering.
    /// <para>
    /// <paramref name="cache"/> is the store's: a chapter whose file has not
    /// changed since it was last parsed is handed back as the same record, which
    /// is also what lets the panel's reference-identity guard skip re-reading a
    /// chapter that a reload brought back unchanged. Null parses everything, which
    /// is what the tests and the standalone page want.
    /// </para>
    /// <para>
    /// Synchronous on purpose: the caller decides which thread pays, and the
    /// store puts it on the pool.
    /// </para>
    /// </summary>
    internal static Arc42DevbookCatalog LoadFolder(string arc42Directory, string? repositoryRoot, DevbookFileCache<DevbookDocument>? cache)
    {
        var rootDirectory = ResolveRoot(arc42Directory, repositoryRoot);
        if (!Directory.Exists(arc42Directory))
        {
            return Arc42DevbookCatalog.Missing(rootDirectory);
        }

        var documentPaths = IndexedPaths(arc42Directory, rootDirectory)
            ?? ScannedPaths(arc42Directory, rootDirectory);

        var documents = new List<DevbookDocument>();
        foreach (var relativePath in documentPaths)
        {
            var fullPath = Path.Combine(rootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath)) continue;

            var documentPath = relativePath.Replace('\\', '/');
            documents.Add(cache is null
                ? DevbookMarkdownParser.Parse(documentPath, File.ReadAllText(fullPath))
                : cache.GetOrAdd(fullPath, () => DevbookMarkdownParser.Parse(documentPath, File.ReadAllText(fullPath))));
        }

        return new Arc42DevbookCatalog(rootDirectory, true, documents);
    }

    /// <summary>
    /// The directory every path in this catalog is spelled relative to.
    /// <para>
    /// The offered root is believed only while the folder is actually inside it.
    /// A configured folder may name somewhere off the clone entirely, and
    /// spelling its documents relative to a repository that does not contain them
    /// would produce a path climbing out of the root the writer later checks
    /// against — so that case falls back to the folder's own parent, and the
    /// documents keep the last-segment spelling the resolver already reads.
    /// </para>
    /// </summary>
    private static string ResolveRoot(string arc42Directory, string? repositoryRoot)
    {
        var parent = Directory.GetParent(arc42Directory)?.FullName ?? arc42Directory;
        if (string.IsNullOrWhiteSpace(repositoryRoot)) return parent;

        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
            var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(arc42Directory));

            return folder.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? root : parent;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return parent;
        }
    }

    /// <summary>
    /// The chapters this folder holds, in reading order, or <see langword="null"/>
    /// when nothing has indexed it and the caller should scan the directory.
    ///
    /// <para>This used to parse <c>_meta/index.json</c> here, with its own pair of
    /// DTOs and — the part that mattered — no <c>schemaVersion</c> check at all, so
    /// a file written by a generator this app had never heard of was read as though
    /// it were the shape it expected. <see cref="DevbookIndexDocument"/> is the
    /// reader that already knows how to refuse that, and asking it means arc42
    /// reads the generated database like every other area rather than being the one
    /// that still opens a JSON file of its own.</para>
    ///
    /// <para>What it does not buy, and is worth saying plainly: the documents are
    /// still parsed from Markdown afterwards. A catalog carries rendered blocks, not
    /// just titles, so the saving here is the index read rather than the corpus
    /// read. Deferring the parse per chapter is what <c>LazyDevbookList</c> does
    /// for <c>.domain</c>, and arc42 has not been moved onto it.</para>
    /// </summary>
    private static List<string>? IndexedPaths(string arc42Directory, string rootDirectory)
    {
        if (DevbookIndexDocument.TryRead(arc42Directory) is not { } index) return null;

        var paths = index.Files
            .Select(entry => entry.Path)
            .Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.Exists(Path.Combine(rootDirectory, path.Replace('/', Path.DirectorySeparatorChar))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // An index that names nothing this folder actually has is no more use than
        // no index: the scan still finds the chapters.
        return paths.Count == 0 ? null : paths;
    }

    /// <summary>
    /// The chapters on disk, for a folder nothing has indexed.
    ///
    /// <para>Recursive, because the folder is. <c>adr/</c> and <c>tdr/</c> are where
    /// this repository keeps its decision records, and <c>adr/guidelines/</c> is a
    /// level below that again. The menu walks the whole folder whether or not an
    /// index exists, so a scan that stopped at the top left the menu offering
    /// chapters this catalog then had no document for — and an unmatched selection
    /// keeps the chapter already on screen, so the tree row went active while the
    /// prose beside it stayed put. That reads as a click that did nothing rather
    /// than as something that failed, which is why it went unnoticed for as long as
    /// it did.</para>
    ///
    /// <para>ADR 0004 is where the requirement comes from: an index is an
    /// optimisation, and a repository nobody has indexed still browses. It has to
    /// browse the whole folder to be the same repository.</para>
    ///
    /// <para>Underscored segments are left out on the rule the menu already leaves
    /// them out on, so the two agree on what the folder holds rather than agreeing
    /// by coincidence: <c>_meta</c> is the index itself, and <c>_c4</c> and
    /// <c>_archify</c> are read by the surfaces that own them rather than as
    /// chapters.</para>
    /// </summary>
    private static List<string> ScannedPaths(string arc42Directory, string rootDirectory) =>
        [.. Directory.EnumerateFiles(arc42Directory, "*.md", Recursive)
            .Where(path => !IsUnderscored(Path.GetRelativePath(arc42Directory, path)))
            .Select(path => Path.GetRelativePath(rootDirectory, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Walking the folder rather than its top, and skipping what it is not
    /// allowed to read. <see cref="SearchOption.AllDirectories"/> would do the first
    /// and not the second — it throws on the first unreadable subdirectory, which
    /// over a whole tree means one protected folder costs the reader every chapter
    /// in the area rather than the ones inside it. The menu already degrades that
    /// way round, and nothing above this call catches.
    ///
    /// <para>The other two are spelled out because <see cref="EnumerationOptions"/>
    /// does not default to what the <see cref="SearchOption"/> overload did, and the
    /// difference here is meant to be recursion and tolerance and nothing else. A
    /// dot-prefixed file counts as hidden on Unix, and these folders are named
    /// <c>.arc42</c>, so leaving the default in place would drop chapters on one
    /// platform and keep them on another.</para></summary>
    private static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        MatchType = MatchType.Win32,
    };

    /// <summary>Whether any segment of a folder-relative path is one the menu hides.
    /// Checked against the folder rather than the repository, so a clone that itself
    /// sits under an underscored directory does not hide every chapter in it.</summary>
    private static bool IsUnderscored(string relativePath) =>
        relativePath.Split('/', '\\').Any(segment => segment.StartsWith('_'));
}

/// <param name="CanEdit">Whether these documents may be written to. False for a
/// branch snapshot; the panel then leaves out the status selector and renders
/// the chapter read-only.</param>
public sealed record Arc42DevbookCatalog(string RootDirectory, bool Exists, IReadOnlyList<DevbookDocument> Documents, bool CanEdit = true)
{
    public static Arc42DevbookCatalog Missing(string rootDirectory) => new(rootDirectory, false, []);

    public int DiagramCount => Documents.Sum(document => document.DiagramCount);

    public int DecisionRecordCount => Documents.Count(document => IsDecisionRecord(document.Path));

    /// <summary>Whether the path is an ADR or TDR, in either layout: the
    /// catalog spells paths relative to the repository, so a folder under
    /// <c>.devbook/</c> names them <c>.devbook/arc42/adr/…</c>.</summary>
    public static bool IsDecisionRecord(string path)
    {
        var normalized = path.Replace('\\', '/');
        var devbook = DevbookFolderSetting.DevbookRoot + "/";
        var within = normalized.StartsWith(devbook, StringComparison.OrdinalIgnoreCase)
            ? "." + normalized[devbook.Length..]
            : normalized;

        return within.StartsWith(".arc42/adr/", StringComparison.OrdinalIgnoreCase) ||
               within.StartsWith(".arc42/tdr/", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record DevbookDocument(
    string Path,
    string Title,
    DevbookMeta Metadata,
    IReadOnlyList<DevbookBlock> Blocks,
    IReadOnlyList<DevbookHeadingSummary> Headings,
    int DiagramCount)
{
    public string? Status => Metadata.Status;

    /// <summary>The body without its title heading. Computed once: the panel
    /// hands this to the file view on every render, and a fresh list per render
    /// is a fresh parameter per render, which re-rendered every block below
    /// it.</summary>
    public IReadOnlyList<DevbookBlock> ContentBlocks => _contentBlocks ??=
        Blocks.FirstOrDefault() is DevbookHeadingBlock { Level: 1 } heading
        && string.Equals(heading.Text, Title, StringComparison.Ordinal)
            ? Blocks.Skip(1).ToList()
            : Blocks;

    private IReadOnlyList<DevbookBlock>? _contentBlocks;
}

public sealed record DevbookMeta(string? Status, IReadOnlyList<string> Related)
{
    public static DevbookMeta Empty { get; } = new(null, []);
}

public sealed record DevbookHeadingSummary(int Level, string Text, DevbookMeta Metadata);

public abstract record DevbookBlock;

public sealed record DevbookHeadingBlock(int Level, string Text, DevbookMeta Metadata) : DevbookBlock;

public sealed record DevbookParagraphBlock(IReadOnlyList<MdInline> Content) : DevbookBlock;

public sealed record DevbookListBlock(bool Ordered, IReadOnlyList<IReadOnlyList<MdInline>> Items) : DevbookBlock;

public sealed record DevbookQuoteBlock(IReadOnlyList<MdInline> Content) : DevbookBlock;

public sealed record DevbookCodeBlock(string Language, string Text) : DevbookBlock;

public sealed record DevbookDiagramBlock(string Language, string Text, string Title) : DevbookBlock;

public sealed record DevbookTableBlock(IReadOnlyList<IReadOnlyList<MdInline>> Rows) : DevbookBlock;

public sealed record DevbookDividerBlock : DevbookBlock;

public static class DevbookMarkdownParser
{
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})[ \t]+(.+)$", RegexOptions.Compiled);
    private static readonly Regex UnorderedRegex = new(@"^[ \t]*[-*][ \t]+(.+)$", RegexOptions.Compiled);
    private static readonly Regex OrderedRegex = new(@"^[ \t]*\d+[.)][ \t]+(.+)$", RegexOptions.Compiled);

    public static DevbookDocument Parse(string path, string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var blocks = new List<DevbookBlock>();
        var headings = new List<DevbookHeadingSummary>();
        var paragraph = new List<string>();
        var listItems = new List<IReadOnlyList<MdInline>>();
        bool? orderedList = null;
        var title = Path.GetFileNameWithoutExtension(path);
        var documentMeta = DevbookMeta.Empty;
        var diagramCount = 0;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new DevbookParagraphBlock(MarkdownPreview.ParseInlines(string.Join(" ", paragraph))));
            paragraph.Clear();
        }

        void FlushList()
        {
            if (listItems.Count == 0) return;
            blocks.Add(new DevbookListBlock(orderedList ?? false, [.. listItems]));
            listItems.Clear();
            orderedList = null;
        }

        void FlushAll()
        {
            FlushParagraph();
            FlushList();
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd();
            var trimmed = line.TrimStart();

            // CommonMark's fence, not "starts with three backticks": a devbook
            // `annotation` note quoting a code sample is opened with four so the
            // sample can sit inside it, and closing at the first inner ``` spilled
            // the rest of the note into the chapter as prose. The note itself
            // stays a code record here and DevbookMarkdownView draws it as one.
            if (MarkdownFence.Open(trimmed) is { } fence)
            {
                FlushAll();
                var language = fence.Language;
                var code = new List<string>();
                index++;
                while (index < lines.Length && !fence.IsClosedBy(lines[index]))
                {
                    code.Add(lines[index]);
                    index++;
                }

                var text = string.Join('\n', code);
                if (IsDiagramLanguage(language))
                {
                    diagramCount++;
                    blocks.Add(new DevbookDiagramBlock(language, text, DiagramTitle(text, diagramCount)));
                }
                else
                {
                    blocks.Add(new DevbookCodeBlock(language, text));
                }

                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushAll();
                continue;
            }

            var heading = HeadingRegex.Match(trimmed);
            if (heading.Success)
            {
                FlushAll();
                var level = heading.Groups[1].Value.Length;
                var text = heading.Groups[2].Value.Trim();
                var (metadata, nextIndex) = ReadMetadata(lines, index + 1);
                index = nextIndex;

                if (level == 1)
                {
                    title = text;
                    documentMeta = metadata;
                }

                blocks.Add(new DevbookHeadingBlock(level, text, metadata));
                headings.Add(new DevbookHeadingSummary(level, text, metadata));
                continue;
            }

            if (IsTableStart(lines, index))
            {
                FlushAll();
                var tableLines = new List<string>();
                while (index < lines.Length && IsTableLine(lines[index]))
                {
                    var row = lines[index].Trim();
                    if (!IsTableSeparator(row)) tableLines.Add(row);
                    index++;
                }

                index--;
                blocks.Add(new DevbookTableBlock([.. tableLines.Select(ParseTableRow)]));
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                FlushAll();
                blocks.Add(new DevbookQuoteBlock(MarkdownPreview.ParseInlines(trimmed[2..])));
                continue;
            }

            if (trimmed is "---" or "***" or "___")
            {
                FlushAll();
                blocks.Add(new DevbookDividerBlock());
                continue;
            }

            var unordered = UnorderedRegex.Match(line);
            if (unordered.Success)
            {
                FlushParagraph();
                if (orderedList is true) FlushList();
                orderedList = false;
                listItems.Add(MarkdownPreview.ParseInlines(unordered.Groups[1].Value));
                continue;
            }

            var ordered = OrderedRegex.Match(line);
            if (ordered.Success)
            {
                FlushParagraph();
                if (orderedList is false) FlushList();
                orderedList = true;
                listItems.Add(MarkdownPreview.ParseInlines(ordered.Groups[1].Value));
                continue;
            }

            FlushList();
            paragraph.Add(trimmed);
        }

        FlushAll();
        return new DevbookDocument(path, title, documentMeta, blocks, headings, diagramCount);
    }

    private static (DevbookMeta Metadata, int NextIndex) ReadMetadata(string[] lines, int startIndex)
    {
        var index = startIndex;
        while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index])) index++;

        if (index >= lines.Length || !string.Equals(lines[index].Trim(), "```meta", StringComparison.Ordinal))
        {
            return (DevbookMeta.Empty, startIndex - 1);
        }

        var metaLines = new List<string>();
        index++;
        while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            metaLines.Add(lines[index].Trim());
            index++;
        }

        return (ParseMetadata(metaLines), index);
    }

    private static DevbookMeta ParseMetadata(IEnumerable<string> lines)
    {
        string? status = null;
        var related = new List<string>();
        var readingRelated = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("status:", StringComparison.OrdinalIgnoreCase))
            {
                status = line["status:".Length..].Trim().Trim('"', '\'');
                readingRelated = false;
            }
            else if (line.StartsWith("related:", StringComparison.OrdinalIgnoreCase))
            {
                related.AddRange(ParseInlineList(line["related:".Length..].Trim()));
                readingRelated = true;
            }
            else if (readingRelated && line.StartsWith("- ", StringComparison.Ordinal))
            {
                related.Add(line[2..].Trim().Trim('"', '\''));
            }
            else
            {
                readingRelated = false;
            }
        }

        return new DevbookMeta(status, related.Distinct(StringComparer.Ordinal).ToList());
    }

    private static IEnumerable<string> ParseInlineList(string value)
    {
        if (!value.StartsWith('[') || !value.EndsWith(']'))
        {
            if (!string.IsNullOrWhiteSpace(value)) yield return value.Trim('"', '\'');
            yield break;
        }

        foreach (var item in value[1..^1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            yield return item.Trim().Trim('"', '\'');
        }
    }

    private static bool IsDiagramLanguage(string language) =>
        language.Equals("mermaid", StringComparison.OrdinalIgnoreCase) ||
        language.StartsWith("c4", StringComparison.OrdinalIgnoreCase);

    private static string DiagramTitle(string text, int number)
    {
        var title = text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith("title ", StringComparison.OrdinalIgnoreCase));
        return title is null ? $"Architecture diagram {number}" : title["title ".Length..].Trim();
    }

    private static bool IsTableStart(string[] lines, int index) =>
        index + 1 < lines.Length && IsTableLine(lines[index]) && IsTableSeparator(lines[index + 1].Trim());

    private static bool IsTableLine(string line) => line.Trim().StartsWith('|') && line.Trim().EndsWith('|');

    private static bool IsTableSeparator(string line)
    {
        var cleaned = line.Trim().Trim('|').Replace(" ", string.Empty);
        return cleaned.Length > 0 && cleaned.Split('|').All(cell => cell.Length >= 3 && cell.All(c => c is '-' or ':'));
    }

    private static IReadOnlyList<MdInline> ParseTableRow(string line) =>
        MarkdownPreview.ParseInlines(string.Join(" | ", line.Trim().Trim('|').Split('|', StringSplitOptions.TrimEntries)));
}


