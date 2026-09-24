using System.Text.RegularExpressions;

using Backlog.Modules.Devbook.Abstractions;
using Backlog.UI.Components.Devbook;
using Backlog.UI.Components.Markdown;

namespace Backlog.Desktop.UI.Devbook;

public sealed class DomainDevbookStore : IDisposable
{
    private readonly IDevbookFolderSource source;

    /// <summary>Every document parsed so far, kept while its file stays as it
    /// was — see <see cref="DevbookFileCache{T}"/>. The index already spares this
    /// store the corpus on load; this spares it the same context's six files on
    /// every return to the Domain tab, and the context map on every load.</summary>
    private readonly DevbookFileCache<DomainDevbookDocument> _documents = new();

    public DomainDevbookStore(IDevbookFolderSource source)
    {
        this.source = source;
        this.source.Changed += _documents.Clear;
    }

    /// <summary>Lets go of the folder source. The store is a singleton and so is
    /// the source, so nothing leaks in the app — but a host that tears its
    /// container down, as the tests do, must find no handler left behind.</summary>
    public void Dispose() => source.Changed -= _documents.Clear;

    private static readonly Regex Heading = new("^(#{1,6})[ \\t]+(.+?)\\s*$", RegexOptions.Compiled);
    private static readonly Regex DevbookLink = new("\\.(?:domain|arc42|backlog|tech|design)/[^\\s)`>,]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    /// <summary>
    /// The order a bounded context's files read in, by kind — the order
    /// <c>devbook-domain.md</c> gives: "reading order comes from this convention,
    /// not from a metadata field and not from filenames".
    ///
    /// <para><c>context.md</c> is first because it is the context's root document
    /// (it declares <c>index: root</c>); then <c>domain.md</c>, <c>actors.md</c>,
    /// <c>features.md</c> or <c>skills.md</c> — a context takes one of the two, so
    /// their relative order never shows — <c>requirements.md</c>,
    /// <c>invariants.md</c>, <c>model.md</c>, <c>flow.md</c> and
    /// <c>dependencies.md</c>, and after all of them the additional pages the
    /// convention does not name. A legacy <c>index.md</c>, which some contexts
    /// used as their root before contract 11, reads straight after the model
    /// narrative, where it always has.</para>
    ///
    /// <para>A split file reads directly after the file it is named after, and in
    /// that file's place when the file itself is gone — which ranking by kind gives
    /// for free, because a split file is the same kind as its base.</para>
    /// </summary>
    private static readonly DomainDevbookDocumentKind[] ContextReadingOrder =
    [
        DomainDevbookDocumentKind.Context,
        DomainDevbookDocumentKind.Domain,
        DomainDevbookDocumentKind.Other,
        DomainDevbookDocumentKind.Actors,
        DomainDevbookDocumentKind.Features,
        DomainDevbookDocumentKind.Skills,
        DomainDevbookDocumentKind.Requirements,
        DomainDevbookDocumentKind.Invariants,
        DomainDevbookDocumentKind.Model,
        DomainDevbookDocumentKind.Flow,
        DomainDevbookDocumentKind.Dependencies,
        DomainDevbookDocumentKind.Page
    ];

    /// <summary>The files a split file may be named after —
    /// <c>&lt;file&gt;.&lt;name&gt;.md</c>. <c>context.md</c> never splits (it is the
    /// root, small by construction), and <c>actors.md</c> and
    /// <c>dependencies.md</c> are themselves what <c>context.md</c> splits
    /// into.</summary>
    private static readonly Dictionary<string, DomainDevbookDocumentKind> SplittableFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["domain"] = DomainDevbookDocumentKind.Domain,
        ["features"] = DomainDevbookDocumentKind.Features,
        ["skills"] = DomainDevbookDocumentKind.Skills,
        ["requirements"] = DomainDevbookDocumentKind.Requirements,
        ["invariants"] = DomainDevbookDocumentKind.Invariants,
        ["model"] = DomainDevbookDocumentKind.Model,
        ["flow"] = DomainDevbookDocumentKind.Flow
    };

    /// <summary>Re-published from the folder source so an open panel can reload
    /// when the configured folder moves.</summary>
    public event Action? Changed
    {
        add => source.Changed += value;
        remove => source.Changed -= value;
    }

    public async Task<DomainDevbookView> LoadAsync(string? repositoryAlias = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Prepared rather than resolved: the view walks every context folder,
        // so a branch's domain folder is fetched here, whole, once.
        var location = await source.PrepareContentAsync(".domain", repositoryAlias, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!location.Available || location.FullPath is null)
        {
            return DomainDevbookView.Unavailable(location.Message ?? "Domain is unavailable.");
        }

        var root = location.FullPath;
        var contextMapPath = Path.Combine(root, "context-map.md");
        if (!File.Exists(contextMapPath)) return DomainDevbookView.Unavailable($"Domain knowledge folder at {root} has no context-map.md.");

        // On the pool: the caller is a component whose continuation is the
        // dispatcher, and in the desktop host that is the UI thread.
        return await Task.Run(() =>
        {
            // The context map is what the panel opens on, so it is the one document
            // worth reading up front. Everything else waits until a context is
            // selected — see ReadContextsFromIndex.
            var contextMap = ReadDocument(contextMapPath, root, DomainDevbookDocumentKind.ContextMap);
            // Two readers of the same index, deliberately: DevbookReadingOrder
            // answers "in what order?" for a scan that still opens every file, and
            // is what .tech and .design also ask. This asks the fuller question —
            // what is in the folder, and what does the index already know about it —
            // so the files behind the answer never have to be opened at all. The
            // scan is the fallback for a folder with no readable index.
            var index = DevbookIndexDocument.TryRead(root);
            var contexts = index is null
                ? ReadContexts(root, DevbookReadingOrder.ForFolder(root))
                : ReadContextsFromIndex(index, root);
            contexts = [.. contexts.Select(context => context with { MapChapter = MapChapterFor(contextMap, context.Slug) })];
            return new DomainDevbookView(location.ScopeLabel ?? "storage", location.RootPath ?? root, root, null, contextMap, contexts, location.CanEdit);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task UpdateStatusAsync(string? repositoryAlias, string itemPath, string status, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(itemPath)) throw new ArgumentException("Devbook item path is required.", nameof(itemPath));
        if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Status is required.", nameof(status));

        var location = source.Resolve(".domain", repositoryAlias);
        var folderPath = location.WritablePath("Domain");

        DevbookMarkdownStatusWriter.UpdateStatus(folderPath, itemPath, ".domain/", status);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Remove the item's <c>status</c> field, leaving its <c>meta</c> fence and
    /// every other field intact.
    ///
    /// <para>A separate method rather than <see cref="UpdateStatusAsync"/> taking a
    /// null: the guard above is right to refuse a blank, because a blank is not one
    /// of <c>.domain</c>'s words, and a folder that must keep its status is then
    /// exempt by having no such method at all rather than by a guard someone can
    /// copy wrongly.</para>
    /// </summary>
    public Task ClearStatusAsync(string? repositoryAlias, string itemPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(itemPath)) throw new ArgumentException("Devbook item path is required.", nameof(itemPath));

        var location = source.Resolve(".domain", repositoryAlias);
        var folderPath = location.WritablePath("Domain");

        DevbookMarkdownStatusWriter.RemoveStatus(folderPath, itemPath, ".domain/");
        return Task.CompletedTask;
    }
    /// <summary>
    /// Builds the bounded contexts from the generated <c>_meta/index.json</c>:
    /// the slug, the display name, and the status of each come from the index,
    /// and the documents inside one are not read until that context is opened.
    /// <para>
    /// This is the whole point of the index. <c>.domain</c> is over seventy
    /// Markdown files, and the panel draws a row of context tabs and the context
    /// map before the reader has chosen any of them. The directory scan below
    /// parsed all seventy to answer a question the index already answers.
    /// </para>
    /// <para>
    /// A context whose root document has been edited since the index was written
    /// is read for its own name and status, so an edit made between refreshes is
    /// never shown stale. That is one file per context at worst, and only for the
    /// ones actually touched.
    /// </para>
    /// </summary>
    private IReadOnlyList<DomainDevbookContext> ReadContextsFromIndex(DevbookIndexDocument index, string root)
    {
        var contexts = new List<DomainDevbookContext>();

        foreach (var directory in index.Directories)
        {
            var slug = directory.Name;
            if (string.IsNullOrWhiteSpace(slug)) continue;

            var files = InReadingOrder(directory.Children?.Where(child => child.IsFile) ?? [], child => child.Name).ToList();

            // context.md when the context has one, whatever the outline marked:
            // it is the root document under contract 16, and an outline written
            // before contract 11 marks domain.md instead.
            var rootEntry = files.FirstOrDefault(child => KindFromFile(child.Name) == DomainDevbookDocumentKind.Context)
                ?? directory.RootDocument;
            var fresh = rootEntry is not null && index.IsStale(rootEntry)
                ? ReadDocument(index.FullPath(rootEntry), root, KindFromFile(rootEntry.Name))
                : null;

            var title = fresh?.Title ?? FirstNonEmpty(rootEntry?.Title, directory.Title);
            var displayName = string.IsNullOrWhiteSpace(title)
                ? Humanize(slug)
                : title.Replace("Domain: ", string.Empty, StringComparison.OrdinalIgnoreCase);
            var status = fresh?.Status ?? rootEntry?.StatusOrNone ?? "none";

            contexts.Add(new DomainDevbookContext(slug, displayName, status,
                new LazyDevbookList<DomainDevbookDocument>(() => ReadIndexedDocuments(index, root, files))));
        }

        return contexts;
    }

    private IReadOnlyList<DomainDevbookDocument> ReadIndexedDocuments(DevbookIndexDocument index, string root, IReadOnlyList<DevbookIndexEntry> files) =>
        [.. files.Where(index.Exists).Select(file => ReadDocument(index.FullPath(file), root, KindFromFile(file.Name)))];

    private static string FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate)) ?? string.Empty;

    private IReadOnlyList<DomainDevbookContext> ReadContexts(string root, IReadOnlyList<string> orderedSlugs)
    {
        var dirs = Directory.EnumerateDirectories(root)
            .Where(p => !Path.GetFileName(p).StartsWith('_')).Select(p => new { Slug = Path.GetFileName(p), Path = p })
            .Where(item => !string.IsNullOrWhiteSpace(item.Slug))
            .ToDictionary(item => item.Slug!, item => item.Path, StringComparer.OrdinalIgnoreCase);
        return [.. orderedSlugs.Concat(dirs.Keys.Order(StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Where(dirs.ContainsKey).Select(slug => ReadContext(slug, dirs[slug], root))];
    }

    private DomainDevbookContext ReadContext(string slug, string path, string root)
    {
        var docs = EnumerateContextDocuments(path).Select(p => ReadDocument(p, root, KindFromFile(Path.GetFileName(p)))).ToList();
        var rootDocument = ContextRoot(docs);
        var name = rootDocument?.Title.Replace("Domain: ", string.Empty, StringComparison.OrdinalIgnoreCase) ?? Humanize(slug);
        return new DomainDevbookContext(slug, name, rootDocument?.Status ?? "none", docs);
    }

    /// <summary>
    /// The document a context takes its name and status from: <c>context.md</c>,
    /// the root document since contract 11, and <c>domain.md</c> for a context
    /// written before it — then whatever reads first.
    /// </summary>
    internal static DomainDevbookDocument? ContextRoot(IReadOnlyList<DomainDevbookDocument> documents) =>
        documents.FirstOrDefault(document => document.Kind == DomainDevbookDocumentKind.Context)
        ?? documents.FirstOrDefault(document => document.Kind == DomainDevbookDocumentKind.Domain)
        ?? documents.FirstOrDefault();

    /// <summary>
    /// The <c>bounded-context</c> chapter in <c>context-map.md</c> that stands for
    /// this context, or nothing.
    ///
    /// <para>First the chapter whose <c>related</c> names this context's
    /// <c>context.md</c> — the pairing the rule itself checks ("where the chapter's
    /// <c>related</c> names that <c>context.md</c>, the two must agree"). Failing
    /// that, the chapter whose heading slugs to the context folder's name: the
    /// convention names the folder after the context and heads the chapter with the
    /// context's name, so <c>## Order Management</c> is <c>order-management/</c>
    /// even in a map that forgot the reference. Only <c>bounded-context</c>
    /// chapters are candidates either way; the map's structural sections carry no
    /// block and name no single context.</para>
    /// </summary>
    internal static DomainDevbookSection? MapChapterFor(DomainDevbookDocument contextMap, string slug)
    {
        var chapters = contextMap.Sections
            .Where(section => section.Metadata.TryGetValue("type", out var type)
                && string.Equals(type.Trim(), "bounded-context", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var contextFile = $"/{slug}/context.md";
        return chapters.FirstOrDefault(section => section.Metadata.TryGetValue("related", out var related)
                && ListValues(related).Any(value => ReferencePath(value).EndsWith(contextFile, StringComparison.OrdinalIgnoreCase)))
            ?? chapters.FirstOrDefault(section => string.Equals(Slug(section.Title), slug, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A reference's path, without its anchor and with <c>/</c>
    /// separators, so a <c>.devbook/domain/…</c> and a <c>.domain/…</c> spelling
    /// both end in the same <c>/&lt;slug&gt;/context.md</c>.</summary>
    private static string ReferencePath(string value)
    {
        var path = value.Replace('\\', '/');
        var hash = path.IndexOf('#');
        return hash < 0 ? path : path[..hash];
    }

    /// <summary>A block value as its entries: <c>[a, b]</c>, <c>a, b</c> — the
    /// flattened shape <see cref="ReadMeta"/> leaves a list in — or one plain
    /// value.</summary>
    private static IEnumerable<string> ListValues(string value) =>
        value.Trim().Trim('[', ']')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.Trim('"', '\''))
            .Where(entry => entry.Length > 0);

    private static IReadOnlyList<string> EnumerateContextDocuments(string path) =>
        [.. InReadingOrder(
            Directory.EnumerateFiles(path, "*.md", SearchOption.TopDirectoryOnly)
                .Where(file => !Path.GetFileName(file).StartsWith('_')),
            file => Path.GetFileName(file))];

    /// <summary>
    /// A context's files in the convention's reading order — see
    /// <see cref="ContextReadingOrder"/>. Kind first; then the base file ahead of
    /// its split files; then filename, which orders the split files among their
    /// siblings and the additional pages among theirs.
    /// <para>Applied to the index's outline as well as to the scan. The outline is
    /// written by a generator that can lag the convention, and the convention is
    /// what says where a file reads, so the two routes into a context agree with
    /// each other and with the rule rather than with whichever generator last
    /// ran.</para>
    /// </summary>
    internal static IEnumerable<T> InReadingOrder<T>(IEnumerable<T> files, Func<T, string> fileName) =>
        files
            .OrderBy(file => ReadingRank(KindFromFile(fileName(file))))
            .ThenBy(file => IsSplitFile(fileName(file)) ? 1 : 0)
            .ThenBy(fileName, StringComparer.OrdinalIgnoreCase);

    private static int ReadingRank(DomainDevbookDocumentKind kind)
    {
        var rank = Array.IndexOf(ContextReadingOrder, kind);
        return rank < 0 ? ContextReadingOrder.Length : rank;
    }

    /// <summary>Whether a file is <c>&lt;file&gt;.&lt;name&gt;.md</c> for a file that
    /// splits — <c>domain.order.md</c>, <c>requirements.checkout.md</c>.</summary>
    internal static bool IsSplitFile(string file) => SplitBase(file) is not null;

    private static DomainDevbookDocumentKind? SplitBase(string file)
    {
        var name = Path.GetFileName(file);
        if (!name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return null;

        var stem = name[..^3];
        var dot = stem.IndexOf('.');
        if (dot <= 0 || dot == stem.Length - 1) return null;

        return SplittableFiles.TryGetValue(stem[..dot], out var kind) ? kind : null;
    }

    private DomainDevbookDocument ReadDocument(string path, string root, DomainDevbookDocumentKind kind) =>
        _documents.GetOrAdd(path, () => ParseDocument(path, root, kind));

    private static DomainDevbookDocument ParseDocument(string path, string root, DomainDevbookDocumentKind kind)
    {
        var relative = ".domain/" + Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
        var lines = File.ReadAllText(path).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var title = Path.GetFileName(path);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sections = new List<DomainDevbookSection>();
        var intro = new List<string>();
        var i = 0;
        while (i < lines.Length)
        {
            var match = Heading.Match(lines[i]);
            if (match.Success && match.Groups[1].Value.Length == 1)
            {
                title = match.Groups[2].Value.Trim();
                i++;
                metadata = ReadMeta(lines, ref i);
                break;
            }
            i++;
        }

        var sectionStart = -1;
        while (i < lines.Length)
        {
            var match = Heading.Match(lines[i]);
            if (match.Success && match.Groups[1].Value.Length == 2)
            {
                if (sectionStart >= 0) sections.Add(ReadSection(lines, sectionStart, i, relative));
                sectionStart = i;
            }
            else if (sectionStart < 0)
            {
                intro.Add(lines[i]);
            }
            i++;
        }
        if (sectionStart >= 0) sections.Add(ReadSection(lines, sectionStart, lines.Length, relative));

        // A review note under the title is about the document, not in it: its
        // quote, its links and any sample it holds stay out of the summary.
        intro = [.. WithoutAnnotationFences(intro)];
        var diagrams = new List<DomainDevbookDiagram>();
        CollectDiagrams(intro, title, diagrams);
        diagrams.AddRange(sections.SelectMany(s => s.Diagrams));
        var links = new SortedSet<string>(sections.SelectMany(s => s.Links), StringComparer.OrdinalIgnoreCase);
        foreach (var link in metadata.Values.SelectMany(FindLinks)) links.Add(link);
        foreach (var link in FindLinks(string.Join('\n', intro))) links.Add(link);

        return new DomainDevbookDocument(relative, title, kind, Status(metadata), metadata, Quote(intro), diagrams, sections, [.. links]);
    }
    private static DomainDevbookSection ReadSection(string[] lines, int start, int end, string documentPath)
    {
        var heading = Heading.Match(lines[start]);
        var title = heading.Success ? heading.Groups[2].Value.Trim() : "Section";
        var level = heading.Success ? heading.Groups[1].Value.Length : 2;
        var index = start + 1;
        var metadata = ReadMeta(lines, ref index);
        var body = lines[index..end].ToList();
        var diagrams = new List<DomainDevbookDiagram>();
        CollectDiagrams(body, title, diagrams);
        var readable = WithoutFences(body);
        var links = new SortedSet<string>(metadata.Values.SelectMany(FindLinks), StringComparer.OrdinalIgnoreCase);
        foreach (var link in FindLinks(string.Join('\n', readable))) links.Add(link);
        return new DomainDevbookSection(title, level, Status(metadata), metadata, Excerpt(readable), diagrams, [.. links], $"{documentPath}#{Slug(title)}");
    }


    private static Dictionary<string, string> ReadMeta(string[] lines, ref int index)
    {
        while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index])) index++;
        if (index >= lines.Length || !string.Equals(lines[index].Trim(), "```meta", StringComparison.OrdinalIgnoreCase)) return [];
        index++;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;
        while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            var rawLine = lines[index];
            var line = rawLine.Trim();
            var sep = line.IndexOf(':');
            if (sep > 0 && !line.StartsWith("-", StringComparison.Ordinal))
            {
                currentKey = line[..sep].Trim();
                result[currentKey] = line[(sep + 1)..].Trim();
            }
            else if (currentKey is not null && line.StartsWith("- ", StringComparison.Ordinal))
            {
                result[currentKey] = AppendMetadataValue(result[currentKey], line[2..].Trim());
            }
            else if (currentKey is not null && char.IsWhiteSpace(rawLine.FirstOrDefault()) && line.Length > 0)
            {
                result[currentKey] = AppendMetadataValue(result[currentKey], line);
            }
            index++;
        }
        if (index < lines.Length) index++;
        while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index])) index++;
        return result;
    }

    private static string AppendMetadataValue(string existing, string next) =>
        string.IsNullOrWhiteSpace(existing) ? next : $"{existing}, {next}";

    /// <summary>
    /// The <c>mermaid</c> fences in a run of lines, as diagrams.
    /// <para>
    /// Fences are read by CommonMark's rule — <see cref="MarkdownFence"/> — and a
    /// whole fence is stepped over at once, so a <c>mermaid</c> sample quoted
    /// inside a devbook <c>annotation</c> note is part of the note and never a
    /// diagram of the context. The old reader matched <c>```lang</c> exactly and
    /// closed at any line starting with three backticks, so a note opened with
    /// four was not a fence to it at all and the sample inside it was.
    /// </para>
    /// </summary>
    private static void CollectDiagrams(IReadOnlyList<string> lines, string title, ICollection<DomainDevbookDiagram> diagrams)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (MarkdownFence.Open(lines[i]) is not { } fence) continue;
            var code = new List<string>();
            i++;
            while (i < lines.Count && !fence.IsClosedBy(lines[i])) code.Add(lines[i++]);
            if (string.Equals(fence.Language, "mermaid", StringComparison.OrdinalIgnoreCase)) diagrams.Add(new DomainDevbookDiagram(title, MermaidKind(code), string.Join('\n', code), "mermaid"));
        }
    }

    /// <summary>A run of lines with every fence taken out — what an excerpt and
    /// a section's links are read from. That includes every devbook
    /// <c>annotation</c> note: a note is review chatter about the chapter, not the
    /// chapter, and an excerpt quoting a reviewer's question would put it on the
    /// context map as though it were what the context says
    /// (<c>devbook-annotations.md</c>, "An annotation is not chapter
    /// content").</summary>
    private static IReadOnlyList<string> WithoutFences(IReadOnlyList<string> lines) => Without(lines, _ => true);

    /// <summary>A run of lines with only the <c>annotation</c> fences taken out,
    /// for the part of a document read as content with its other fences left in —
    /// the introduction above the first section, whose diagrams, quote and links
    /// are all read from it.</summary>
    private static IReadOnlyList<string> WithoutAnnotationFences(IReadOnlyList<string> lines) =>
        Without(lines, fence => DevbookAnnotationFence.IsAnnotationBlock(fence.Language));

    private static IReadOnlyList<string> Without(IReadOnlyList<string> lines, Func<MarkdownFence, bool> cut)
    {
        var output = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (MarkdownFence.Open(lines[i]) is not { } fence)
            {
                output.Add(lines[i]);
                continue;
            }

            var remove = cut(fence);
            if (!remove) output.Add(lines[i]);
            i++;
            while (i < lines.Count && !fence.IsClosedBy(lines[i]))
            {
                if (!remove) output.Add(lines[i]);
                i++;
            }

            if (i < lines.Count && !remove) output.Add(lines[i]);
        }

        return output;
    }

    private static IReadOnlyList<string> FindLinks(string text) => [.. DevbookLink.Matches(text).Select(m => m.Value.TrimEnd('.', ',', ';', ']')).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static string Quote(IEnumerable<string> lines) => string.Join(" ", lines.Select(l => l.Trim()).Where(l => l.StartsWith('>')).Select(l => l.TrimStart('>').Trim()).Where(l => l.Length > 0).Take(2));

    private static string Excerpt(IEnumerable<string> lines) => string.Join(" ", lines.Select(l => l.Trim()).Where(l => l.Length > 0).Where(l => !l.StartsWith('|')).Where(l => !l.StartsWith('#')).Select(l => l.StartsWith('>') ? l.TrimStart('>').Trim() : l.TrimStart('-', '*').Trim()).Where(l => l.Length > 0).Take(3));

    private static string Status(IReadOnlyDictionary<string, string> metadata) => metadata.TryGetValue("status", out var status) && !string.IsNullOrWhiteSpace(status) ? status.Trim().ToLowerInvariant() : "none";

    /// <summary>
    /// What a <c>.domain</c> file is, read off its name. Internal rather than
    /// private because the Devbook menu asks the same question of the same
    /// filenames — a tree row is a file, and the mark it carries has to be the one
    /// the panel would draw for that file. Two copies of this list would be two
    /// answers the moment either gained a filename.
    /// <para><c>context-map.md</c> is here even though the loader passes its kind
    /// in directly: the map is a file like any other to a caller holding only a
    /// name, and leaving it out made this mapping right for all but one.</para>
    /// <para>Contract 16's files, each by its name; a split file
    /// (<c>domain.order.md</c>) as the file it is named after, because it
    /// "carries the type of the file it came from"; and every other file as an
    /// additional page, whose type is its own filename. <c>naming.md</c> used to
    /// be a kind of its own — this repository's glossary page — and is now one of
    /// those pages like any other. <c>index.md</c> stays
    /// <see cref="DomainDevbookDocumentKind.Other"/>: a pre-contract-11 root, read
    /// but not claimed to be a page the context chose to add.</para>
    /// </summary>
    internal static DomainDevbookDocumentKind KindFromFile(string file) => Path.GetFileName(file).ToLowerInvariant() switch
    {
        "context-map.md" => DomainDevbookDocumentKind.ContextMap,
        "context.md" => DomainDevbookDocumentKind.Context,
        "domain.md" => DomainDevbookDocumentKind.Domain,
        "actors.md" => DomainDevbookDocumentKind.Actors,
        "features.md" => DomainDevbookDocumentKind.Features,
        "skills.md" => DomainDevbookDocumentKind.Skills,
        "requirements.md" => DomainDevbookDocumentKind.Requirements,
        "invariants.md" => DomainDevbookDocumentKind.Invariants,
        "model.md" => DomainDevbookDocumentKind.Model,
        "flow.md" => DomainDevbookDocumentKind.Flow,
        "dependencies.md" => DomainDevbookDocumentKind.Dependencies,
        "index.md" => DomainDevbookDocumentKind.Other,
        var name when name.EndsWith(".md", StringComparison.Ordinal) => SplitBase(name) ?? DomainDevbookDocumentKind.Page,
        _ => DomainDevbookDocumentKind.Other
    };

    private static string MermaidKind(IReadOnlyList<string> lines)
    {
        var first = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "mermaid";
        if (first.StartsWith("classDiagram", StringComparison.OrdinalIgnoreCase)) return "domain model";
        if (first.StartsWith("flowchart", StringComparison.OrdinalIgnoreCase) || first.StartsWith("graph", StringComparison.OrdinalIgnoreCase)) return "context map";
        if (first.StartsWith("stateDiagram", StringComparison.OrdinalIgnoreCase)) return "state flow";
        if (first.StartsWith("sequenceDiagram", StringComparison.OrdinalIgnoreCase)) return "sequence flow";
        return "mermaid";
    }

    private static string Humanize(string slug) => string.Join(' ', slug.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    private static string Slug(string heading) => Regex.Replace(new string(heading.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()), "-+", "-").Trim('-');
}

/// <param name="CanEdit">Whether this view's documents may be written to. False
/// when the folder resolved to a branch snapshot, and the panel then leaves out
/// the status selectors, the editor and the launchers rather than offering
/// changes the next fetch would discard.</param>
public sealed record DomainDevbookView(string RepositoryLabel, string RepositoryRoot, string RootPath, string? Error, DomainDevbookDocument ContextMap, IReadOnlyList<DomainDevbookContext> Contexts, bool CanEdit = true)
{
    public bool IsReady => Error is null;
    public static DomainDevbookView Unavailable(string error) => new(string.Empty, string.Empty, string.Empty, error, DomainDevbookDocument.Empty, []);
}

/// <param name="MapChapter">The <c>bounded-context</c> chapter in
/// <c>context-map.md</c> that stands for this context, when the map has one —
/// see <see cref="DomainDevbookStore.MapChapterFor"/>. Read with the map, which is
/// read up front, so it costs nothing.</param>
public sealed record DomainDevbookContext(string Slug, string DisplayName, string Status, IReadOnlyList<DomainDevbookDocument> Documents, DomainDevbookSection? MapChapter = null)
{
    /// <summary>The context's root document — <c>context.md</c>, or for a context
    /// written before contract 11, <c>domain.md</c>. Reading it reads the context's
    /// documents, which the index route otherwise defers until they are
    /// shown.</summary>
    public DomainDevbookDocument? RootDocument => DomainDevbookStore.ContextRoot(Documents);

    /// <summary>
    /// How the context ships, as its two statements of it say. The <c>context.md</c>
    /// side is read off the context's documents, so like
    /// <see cref="RootDocument"/> it is only worth asking of a context that is on
    /// screen.
    /// </summary>
    public DomainDevbookDeployment Deployment => new(
        MapChapter is { } chapter && chapter.Metadata.TryGetValue("deployment", out var mapValue) ? Blank(mapValue) : null,
        Documents.FirstOrDefault(document => document.Kind == DomainDevbookDocumentKind.Context) is { } context
            && context.Metadata.TryGetValue("deployment", out var contextValue) ? Blank(contextValue) : null);

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// A bounded context's <c>deployment</c>, as the two places that state it state it:
/// the <c>bounded-context</c> chapter in <c>context-map.md</c>, and the file-level
/// block of the context's own <c>context.md</c>.
/// <para>
/// They have to agree, and a value on one side only is a disagreement too — the
/// rule's own wording, which <see cref="DevbookSchema.DeploymentDisagrees"/> keeps.
/// Neither side stating one is not: the choice is simply not made yet.
/// </para>
/// </summary>
public sealed record DomainDevbookDeployment(string? MapValue, string? ContextValue)
{
    /// <summary>Whether either side states one.</summary>
    public bool IsStated => MapValue is not null || ContextValue is not null;

    /// <summary>Whether the two statements disagree, including one side stating
    /// nothing.</summary>
    public bool Disagrees => DevbookSchema.DeploymentDisagrees(MapValue, ContextValue);

    /// <summary>The value to show: the context's own, which a reader of the context
    /// sees without opening the map, and the map's when the context states none.
    /// When the two disagree <see cref="Problem"/> names both, so showing one here
    /// hides nothing.</summary>
    public string? Value => ContextValue ?? MapValue;

    /// <summary>The disagreement as a sentence naming both values, or nothing
    /// when they agree.</summary>
    public string? Problem => Disagrees
        ? $"Deployment disagrees: the bounded-context chapter in context-map.md {Says(MapValue)}, and context.md {Says(ContextValue)}."
        : null;

    private static string Says(string? value) => value is null ? "states none" : $"says \"{value}\"";
}

public sealed record DomainDevbookDocument(string Path, string Title, DomainDevbookDocumentKind Kind, string Status, IReadOnlyDictionary<string, string> Metadata, string Summary, IReadOnlyList<DomainDevbookDiagram> Diagrams, IReadOnlyList<DomainDevbookSection> Sections, IReadOnlyList<string> Links)
{
    public static DomainDevbookDocument Empty { get; } = new(string.Empty, string.Empty, DomainDevbookDocumentKind.Other, "none", new Dictionary<string, string>(), string.Empty, [], [], []);
    public string KindLabel => Kind switch
    {
        DomainDevbookDocumentKind.ContextMap => "Strategic context map",
        DomainDevbookDocumentKind.Context => "Bounded context",
        DomainDevbookDocumentKind.Domain => "Domain model narrative",
        DomainDevbookDocumentKind.Actors => "Actors",
        DomainDevbookDocumentKind.Features => "Features",
        DomainDevbookDocumentKind.Skills => "Skills",
        DomainDevbookDocumentKind.Requirements => "Requirements",
        DomainDevbookDocumentKind.Invariants => "Invariants",
        DomainDevbookDocumentKind.Model => "Structural model",
        DomainDevbookDocumentKind.Flow => "Flow",
        DomainDevbookDocumentKind.Dependencies => "Dependencies",
        DomainDevbookDocumentKind.Page => "Additional page",
        _ => "Domain document"
    };
}

public sealed record DomainDevbookSection(string Title, int Level, string Status, IReadOnlyDictionary<string, string> Metadata, string Excerpt, IReadOnlyList<DomainDevbookDiagram> Diagrams, IReadOnlyList<string> Links, string Anchor);
public sealed record DomainDevbookDiagram(string Title, string Kind, string Source, string Language);

/// <summary>What a <c>.domain</c> file is — contract 16's file types, plus the
/// two this module needs of its own: an additional page, whose type is its own
/// filename, and <see cref="Other"/>, which claims nothing.</summary>
public enum DomainDevbookDocumentKind
{
    ContextMap,
    Context,
    Domain,
    Actors,
    Features,
    Skills,
    Requirements,
    Invariants,
    Model,
    Flow,
    Dependencies,

    /// <summary>A file the convention does not name, added by the context for
    /// something no listed file holds. Its type is its filename.</summary>
    Page,

    /// <summary>A legacy <c>index.md</c>, or a document nobody has classified —
    /// the map's empty stand-in.</summary>
    Other
}

/// <summary>
/// The bridge between this module's document kind and the shared library's
/// <c>type</c> vocabulary — the file-type marks
/// <see cref="Backlog.UI.Components.Devbook.DevbookTypeMarkers.FileTypes"/>
/// draws.
/// <para>
/// The two lists say the same thing in two vocabularies, and this is the single
/// place they meet. The alternative — a second filename-to-slug switch wherever a
/// mark is wanted — is how the tree and the panel would come to disagree about
/// what <c>flow.md</c> is.
/// </para>
/// <para>
/// An additional page has no fixed value: its type is its own filename, so
/// <see cref="Of(string)"/> answers it from the name and <see cref="Of(DomainDevbookDocumentKind)"/>,
/// holding only the kind, cannot. A caller drawing it passes the file name to the
/// marker beside the value, which is how the marker knows the value is a page's.
/// <see cref="DomainDevbookDocumentKind.Other"/> has no mark, deliberately: an
/// unknown value draws nothing rather than guessing.
/// </para>
/// </summary>
public static class DomainDevbookFileTypes
{
    /// <summary>The <c>type</c> a file carries, from its name alone — for an
    /// additional page, its own filename.</summary>
    public static string? Of(string fileName) =>
        string.IsNullOrWhiteSpace(fileName) ? null
        : DomainDevbookStore.KindFromFile(fileName) is DomainDevbookDocumentKind.Page ? DevbookSchema.OwnFileType(fileName)
        : Of(DomainDevbookStore.KindFromFile(fileName));

    /// <summary>The same for a document already read.</summary>
    public static string? Of(DomainDevbookDocument document) =>
        document.Kind is DomainDevbookDocumentKind.Page ? DevbookSchema.OwnFileType(document.Path) : Of(document.Kind);

    /// <summary>The <c>type</c> for a document whose kind is already known, when
    /// the kind alone says it.</summary>
    public static string? Of(DomainDevbookDocumentKind kind) => kind switch
    {
        DomainDevbookDocumentKind.ContextMap => "context-map",
        DomainDevbookDocumentKind.Context => "context",
        DomainDevbookDocumentKind.Domain => "domain",
        DomainDevbookDocumentKind.Actors => "actors",
        DomainDevbookDocumentKind.Features => "features",
        DomainDevbookDocumentKind.Skills => "skills",
        DomainDevbookDocumentKind.Requirements => "requirements",
        DomainDevbookDocumentKind.Invariants => "invariants",
        DomainDevbookDocumentKind.Model => "model",
        DomainDevbookDocumentKind.Flow => "flow",
        DomainDevbookDocumentKind.Dependencies => "dependencies",
        _ => null
    };
}
