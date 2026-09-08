using System.Text.Json;

using Backlog.Infrastructure.Knowledge;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// The ordered reading outline of a knowledge folder — path, title, status,
/// reading order, and the directories that group them — read without opening a
/// single Markdown file.
/// <para>
/// Only the document a reader actually looks at has to be parsed. <c>.domain</c>
/// alone is over seventy files, and the behaviour before an index existed parsed
/// every one of them before the panel could draw its first tab.
/// </para>
/// <para>
/// Local ADR 0004 moved the outline into the generated <c>_meta/knowledge.db</c>,
/// where it is one <c>WHERE scope = ?</c> rather than one file per folder, and
/// this reads it from there. The committed <c>_meta/index.json</c> stays as a
/// fallback rung, for a repository that has the old artifacts and no database yet
/// — a clone that has never run the generator, or one whose knowledge folders were
/// configured somewhere with no repository root above them. What a caller gets
/// back is the same shape either way, which is the point: the rest of Second
/// Brain does not know which rung answered.
/// </para>
/// <para>
/// Either source is derived output and is refreshed deliberately, never
/// automatically on every edit, so what it says can lag the Markdown beside it.
/// Every consumer therefore pairs this reader with <see cref="IsStale"/>: an
/// entry whose file has moved on is re-read from the Markdown and the rest are
/// trusted. One <c>stat</c> per entry instead of a full parse — and from the
/// database that check is exact rather than a timestamp guess, because the writer
/// records each file's size, modification time and hash.
/// </para>
/// </summary>
public sealed class KnowledgeIndexDocument
{
    private readonly IReadOnlyDictionary<string, KnowledgeFileState>? _fileStates;

    private KnowledgeIndexDocument(
        string folderPath,
        DateTime writtenUtc,
        IReadOnlyList<KnowledgeIndexEntry> entries,
        IReadOnlyDictionary<string, KnowledgeFileState>? fileStates = null)
    {
        FolderPath = folderPath;
        WrittenUtc = writtenUtc;
        Entries = entries;
        _fileStates = fileStates;
    }

    /// <summary>The knowledge folder this index describes, e.g. the absolute path of <c>.domain</c>.</summary>
    public string FolderPath { get; }

    /// <summary>When the index was last written. Anything newer on disk is not
    /// covered by it — the coarse question, kept for the JSON rung, which records
    /// nothing finer than the file's own timestamp.</summary>
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
    /// <para>
    /// The database first, then the committed JSON. Both are ignored rather than
    /// guessed at when they declare a version this reader does not know, which is
    /// what lets either generator move ahead of an installed app without breaking
    /// it.
    /// </para>
    /// </summary>
    public static KnowledgeIndexDocument? TryRead(string folderPath) =>
        TryReadDatabase(folderPath) ?? TryReadJson(folderPath);

    /// <summary>
    /// The outline out of <c>_meta/knowledge.db</c>, for the scope this folder is.
    ///
    /// <para>The scope is the folder's own name — <c>.domain</c> — because that is
    /// what the writer files its rows under. A folder configured somewhere that is
    /// not a knowledge folder of the repository above it therefore matches no rows,
    /// and gets the same answer as no database: this returns null and the JSON rung
    /// is tried next.</para>
    /// </summary>
    private static KnowledgeIndexDocument? TryReadDatabase(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return null;

        using var database = KnowledgeDatabase.TryOpenForFolder(folderPath);
        if (database is null) return null;

        var scope = Path.GetFileName(Path.TrimEndingDirectorySeparator(folderPath));
        if (string.IsNullOrEmpty(scope)) return null;

        var rows = database.Outline(scope);
        if (rows.Count == 0) return null;

        return new KnowledgeIndexDocument(
            folderPath,
            database.GeneratedUtc,
            BuildTree(rows),
            database.FileStates(scope));
    }

    /// <summary>
    /// The outline rows, which arrive flat and already ordered, rebuilt into the
    /// nested shape <see cref="KnowledgeIndexEntry"/> has always had.
    /// <para>
    /// Nothing sorts here. The order is the authored one, resolved by the writer
    /// out of <c>_reading-order.json</c> and recorded as an ordinal, and re-sorting
    /// it on this side would be a second opinion about a fact somebody wrote down.
    /// </para>
    /// </summary>
    private static List<KnowledgeIndexEntry> BuildTree(IReadOnlyList<KnowledgeOutlineRow> rows)
    {
        var top = new List<KnowledgeIndexEntry>();
        var byId = new Dictionary<long, KnowledgeIndexEntry>(rows.Count);

        foreach (var row in rows)
        {
            var entry = new KnowledgeIndexEntry
            {
                Type = row.Type,
                Name = row.Name,
                Path = row.Path,
                Title = row.Title ?? string.Empty,
                Status = row.Status,
                Root = row.IsRoot
            };

            byId[row.Id] = entry;

            if (row.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent))
            {
                (parent.Children ??= []).Add(entry);
            }
            else
            {
                top.Add(entry);
            }
        }

        return top;
    }

    private static KnowledgeIndexDocument? TryReadJson(string folderPath)
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
    /// Whether the Markdown behind an entry has moved on from what the index
    /// records, in which case the entry's title and status are last refresh's
    /// answer and the file has to be read for this one. A file the index lists but
    /// that is no longer on disk is not stale, it is gone — see <see cref="Exists"/>.
    ///
    /// <para>Two questions, depending on which rung answered. The database records
    /// each file's size, modification time and hash, so the check is exact: a file
    /// a branch switch touched without changing keeps its row instead of being
    /// re-parsed for nothing. The JSON records only when the index itself was
    /// written, so there the question is the coarse one — anything newer on disk is
    /// assumed to have changed, which over-reports and never under-reports.</para>
    /// </summary>
    public bool IsStale(KnowledgeIndexEntry entry)
    {
        var fullPath = FullPath(entry);

        if (_fileStates is not null)
        {
            return _fileStates.TryGetValue(entry.Path, out var state)
                ? state.HasDrifted(fullPath)

                // A file the database has no row for is not covered by it at all,
                // so serving it from the outline would be serving something the
                // writer never saw. That is drift by another name.
                : File.Exists(fullPath);
        }

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
