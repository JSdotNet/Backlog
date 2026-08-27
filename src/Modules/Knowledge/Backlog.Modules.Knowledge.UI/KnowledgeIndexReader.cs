using System.Text.Json;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// Reads the generated <c>_meta/index.json</c> that sits beside a knowledge
/// folder — the ordered reading outline the <c>knowledge-meta</c> generator
/// derives from the <c>meta</c> blocks in the Markdown.
/// <para>
/// The point of reading it is that a panel can list what a folder holds — path,
/// title, status, reading order, and the directories that group them — without
/// opening a single Markdown file. Only the document the reader actually looks
/// at has to be parsed. <c>.domain</c> alone is over seventy files, and the old
/// behaviour parsed every one of them before the panel could draw its first tab.
/// </para>
/// <para>
/// The index is derived output and is refreshed deliberately — by the update
/// command or the nightly build, never automatically on every edit, because a
/// regenerated index on every branch is what makes <c>_meta/*.json</c> conflict
/// on merge. That means the committed copy can lag the Markdown beside it, so
/// every consumer pairs this reader with <see cref="IsStale"/>: an entry whose
/// file has been written since the index was is re-read from the Markdown, and
/// the rest are trusted. One <c>stat</c> per entry instead of a full parse.
/// </para>
/// </summary>
public sealed class KnowledgeIndexDocument
{
    private KnowledgeIndexDocument(string folderPath, DateTime writtenUtc, IReadOnlyList<KnowledgeIndexEntry> entries)
    {
        FolderPath = folderPath;
        WrittenUtc = writtenUtc;
        Entries = entries;
    }

    /// <summary>The knowledge folder this index describes, e.g. the absolute path of <c>.domain</c>.</summary>
    public string FolderPath { get; }

    /// <summary>When <c>_meta/index.json</c> was last written. Anything newer on disk is not covered by it.</summary>
    public DateTime WrittenUtc { get; }

    /// <summary>The outline, in reading order, exactly as the generator emitted it.</summary>
    public IReadOnlyList<KnowledgeIndexEntry> Entries { get; }

    /// <summary>Every file entry in the outline, depth-first, in reading order.</summary>
    public IEnumerable<KnowledgeIndexEntry> Files => Flatten(Entries).Where(entry => entry.IsFile);

    /// <summary>The directory entries at the top of the outline — one per bounded context, in <c>.domain</c>.</summary>
    public IEnumerable<KnowledgeIndexEntry> Directories => Entries.Where(entry => entry.IsDirectory);

    /// <summary>
    /// Reads the index for a knowledge folder, or returns <c>null</c> when the
    /// folder has none — a repository that never adopted the generator, or a
    /// checkout where it has not been run yet. Callers fall back to scanning the
    /// directory, which is what they did before the index existed.
    /// </summary>
    public static KnowledgeIndexDocument? TryRead(string folderPath)
    {
        var indexPath = Path.Combine(folderPath, "_meta", "index.json");
        if (!File.Exists(indexPath)) return null;

        try
        {
            var payload = JsonSerializer.Deserialize<IndexPayload>(File.ReadAllText(indexPath), JsonOptions);
            if (payload?.Entries is not { Count: > 0 }) return null;

            // An unrecognised schemaVersion means the payload is not the shape
            // this reader knows, and guessing at it would be worse than the scan
            // it replaces. Falling back is the convention's own instruction —
            // see the consumer rules in knowledge-derived-artifacts.
            if (!SupportedSchemaVersions.Contains(payload.SchemaVersion)) return null;

            return new KnowledgeIndexDocument(folderPath, File.GetLastWriteTimeUtc(indexPath), payload.Entries);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A malformed or unreadable index is not worth failing a panel over.
            // The folder is still on disk and the scan still works, so this
            // degrades to the pre-index behaviour rather than to an error.
            return null;
        }
    }

    /// <summary>
    /// Whether the Markdown behind an entry has been written since the index
    /// was, in which case the entry's title and status are last refresh's answer
    /// and the file has to be read for this one. A file the index lists but that
    /// is no longer on disk is not stale, it is gone — see <see cref="Exists"/>.
    /// </summary>
    public bool IsStale(KnowledgeIndexEntry entry)
    {
        var fullPath = FullPath(entry);
        return File.Exists(fullPath) && File.GetLastWriteTimeUtc(fullPath) > WrittenUtc;
    }

    /// <summary>Whether the file an entry names is still on disk.</summary>
    public bool Exists(KnowledgeIndexEntry entry) => File.Exists(FullPath(entry));

    /// <summary>
    /// The absolute path of an entry. Index paths are repository-relative and
    /// always <c>/</c>-separated, and they lead with the knowledge folder's own
    /// name (<c>.domain/inbox/domain.md</c>), so the folder segment is dropped
    /// before combining with the folder path this index was read from — which is
    /// where the folder actually is, wherever the workspace put it.
    /// </summary>
    public string FullPath(KnowledgeIndexEntry entry) => Path.Combine(FolderPath, RelativeToFolder(entry.Path));

    /// <summary>The entry's path relative to the knowledge folder, in the platform's separator.</summary>
    public static string RelativeToFolder(string indexPath)
    {
        var normalized = indexPath.Replace('\\', '/');
        var separator = normalized.IndexOf('/');
        var withinFolder = separator >= 0 ? normalized[(separator + 1)..] : normalized;
        return withinFolder.Replace('/', Path.DirectorySeparatorChar);
    }

    private static IEnumerable<KnowledgeIndexEntry> Flatten(IEnumerable<KnowledgeIndexEntry> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
            if (entry.Children is { Count: > 0 })
            {
                foreach (var child in Flatten(entry.Children)) yield return child;
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The envelope versions this reader understands. A file declaring anything
    /// else is left to the directory scan.
    /// <para>
    /// Two are listed because the repository's installed generator emits
    /// <c>1</c> while the current <c>knowledge-base</c> plugin emits <c>4</c>,
    /// and adopting that generator is gated on a separate Markdown migration
    /// (the <c>order</c> field it removed). Both were checked: every field read
    /// here — <c>type</c>, <c>name</c>, <c>path</c>, <c>title</c>,
    /// <c>status</c>, <c>root</c>, <c>children</c> — is identical in the two,
    /// and <c>4</c> only <em>adds</em> <c>kind</c>, <c>summary</c> and a
    /// <c>diagrams</c> count. Listing it is therefore a verified shape, not a
    /// guess, and it stops the upgrade from silently turning this optimisation
    /// off and falling back to parsing every file.
    /// </para>
    /// <para>
    /// Those two added fields are worth something later: a per-entry summary and
    /// diagram count are exactly what <c>.arc42</c> and <c>.design</c> need in
    /// order to defer their Markdown too, which they currently cannot.
    /// </para>
    /// </summary>
    private static readonly int[] SupportedSchemaVersions = [1, 4];

    private sealed class IndexPayload
    {
        public int SchemaVersion { get; set; }

        public List<KnowledgeIndexEntry>? Entries { get; set; }
    }
}

/// <summary>
/// One line of the generated outline: a file, the directory that groups a set of
/// them, or — at the repository-wide scope — a whole knowledge area.
/// </summary>
public sealed class KnowledgeIndexEntry
{
    /// <summary><c>file</c>, <c>directory</c>, or <c>area</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The file or directory name, e.g. <c>domain.md</c> or <c>inbox</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Repository-relative, <c>/</c>-separated, e.g. <c>.domain/inbox/domain.md</c>.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The document's H1, or the directory's root document's H1.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The <c>status</c> field of the file's <c>meta</c> block. Absent on directories.</summary>
    public string? Status { get; set; }

    /// <summary>Whether this file is the directory's root document — <c>domain.md</c>, <c>README.md</c>.</summary>
    public bool Root { get; set; }

    /// <summary>The entries this directory or area groups, in reading order.</summary>
    public List<KnowledgeIndexEntry>? Children { get; set; }

    public bool IsFile => string.Equals(Type, "file", StringComparison.OrdinalIgnoreCase);

    public bool IsDirectory => string.Equals(Type, "directory", StringComparison.OrdinalIgnoreCase);

    /// <summary>The status to show when the index carries none — the same word the panels use for "not stated".</summary>
    public string StatusOrNone => string.IsNullOrWhiteSpace(Status) ? "none" : Status.Trim().ToLowerInvariant();

    /// <summary>This directory's root document, which is where its title and status come from.</summary>
    public KnowledgeIndexEntry? RootDocument =>
        Children?.FirstOrDefault(child => child.IsFile && child.Root) ?? Children?.FirstOrDefault(child => child.IsFile);
}
