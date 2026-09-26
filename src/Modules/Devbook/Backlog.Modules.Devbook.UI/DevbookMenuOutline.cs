using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// What the Devbook menu needs to know about a folder before it draws a row:
/// the order its directories are meant to be read in, and the real title of the
/// chapter behind each one.
///
/// <para>The order is the folder convention's (<see cref="DevbookReadingConvention"/>)
/// — the same one the domain, design and technology panes beside the rail order
/// themselves by, and the one the database's outline is derived from. Nothing is
/// authored per repository: local ADR 0016 retired <c>_reading-order.json</c>, and
/// one left in a folder is ignored. The titles are derived, in the devbook
/// database that ADR 0004 introduced, which is where <c>dev-pc-management</c> is
/// known to be "Dev PC Management" rather than the "Dev Pc Management" a filename
/// can be title-cased into.</para>
///
/// <para>The titles are read once per area, not once per directory, because a
/// tab click should cost one folder walk. Neither answer opens a Markdown file,
/// which is the whole reason the rail can consult them at all: the convention
/// asks names only, so a document's own <c>index: root</c> or <c>number</c> field
/// is the database's to honour and not the rail's, and the alternative — parse
/// every chapter for its H1 — is the corpus parse the index exists to avoid.</para>
///
/// <para>Both degrade to nothing, and they degrade together. A directory the
/// convention has no entry for — all of <c>arc42</c>, anything below a bounded
/// context — orders nothing and titles no row either, because a directory that
/// keeps the rail's own sort keeps its labels with it. A checkout with no database
/// titles no row at all. Either way the rail is exactly what it was before these
/// artifacts existed.</para>
/// </summary>
internal sealed class DevbookMenuOutline
{
    private readonly string? _folderKind;
    private readonly DevbookIndexDocument? _index;
    private readonly IReadOnlyDictionary<string, OutlineTitle> _titles;

    private DevbookMenuOutline(
        string? folderKind,
        DevbookIndexDocument? index,
        IReadOnlyDictionary<string, OutlineTitle> titles)
    {
        _folderKind = folderKind;
        _index = index;
        _titles = titles;
    }

    /// <summary>A folder with neither answer: every directory sorts itself and
    /// every row keeps its filename label. What the instruction area gets, since
    /// it is assembled out of agent folders rather than being a knowledge folder
    /// with an order of its own.</summary>
    public static DevbookMenuOutline None { get; } = new(
        null,
        null,
        new Dictionary<string, OutlineTitle>(StringComparer.OrdinalIgnoreCase));

    /// <param name="folderPath">The folder on disk, where its database is looked up.</param>
    /// <param name="folderKind">The folder's kind, <c>domain</c> or <c>tech</c> —
    /// the area key — which picks its convention.</param>
    public static DevbookMenuOutline Read(string folderPath, string folderKind)
    {
        var index = DevbookIndexDocument.TryRead(folderPath);

        return new DevbookMenuOutline(folderKind, index, index is null ? None._titles : Titles(index));
    }

    /// <summary>
    /// <paramref name="names"/> — one directory's entries — in the convention's
    /// reading order, or empty when the convention says nothing about that
    /// directory and the rail's own sort stands.
    /// </summary>
    public IReadOnlyList<string> Order(string relativeDirectory, IEnumerable<string> names) =>
        Convention(relativeDirectory) is null
            ? []
            : DevbookReadingConvention.Order(_folderKind, Depth(relativeDirectory), names);

    /// <summary>
    /// The root document of a directory the convention says nothing about —
    /// <c>arc42/adr</c>, <c>arc42/tdr</c> — which the rail pins first while the
    /// rest keeps its own sort. <see langword="null"/> where the convention
    /// orders the directory (its <see cref="Order"/> already leads with the
    /// root), for the instruction area, and where there is none.
    ///
    /// <para>The database outline answers first: the child it marked
    /// <c>is_root</c>, which is where a document's own <c>index: root</c> lands,
    /// read without opening a Markdown file. A directory the outline does not
    /// know — no database, or one built before the directory existed — falls
    /// back to a <c>README.md</c> by name. A directory the outline knows and
    /// gives no root has none here either, so the rail and the outline agree.</para>
    ///
    /// <para>Deliberately separate from <see cref="Order"/>: an empty order is
    /// the rung <see cref="TryTitle"/> stands on, and pinning a root does not
    /// change that the directory keeps its own sort and its own labels.</para>
    /// </summary>
    public string? RootDocument(string relativeDirectory, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (_folderKind is null || Convention(relativeDirectory) is not null) return null;

        if (OutlineChildren(relativeDirectory) is { } children)
        {
            return children.FirstOrDefault(child => child.IsFile && child.Root)?.Name;
        }

        return names.FirstOrDefault(name => string.Equals(name, "README.md", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The outline's entries directly inside a directory, or
    /// <see langword="null"/> when there is no outline or it does not know the
    /// directory.</summary>
    private IEnumerable<DevbookIndexEntry>? OutlineChildren(string relativeDirectory)
    {
        if (_index is null) return null;
        if (string.IsNullOrEmpty(relativeDirectory)) return _index.Entries;

        return _titles.TryGetValue(relativeDirectory, out var directory) && directory.Entry.IsDirectory
            ? directory.Entry.Children ?? []
            : null;
    }

    private DevbookDirectoryConvention? Convention(string relativeDirectory) =>
        DevbookReadingConvention.For(_folderKind, Depth(relativeDirectory));

    private static int Depth(string relativeDirectory) =>
        string.IsNullOrEmpty(relativeDirectory) ? 0 : relativeDirectory.Split('/').Length;

    /// <summary>
    /// The generated title of one entry, when adopting it would say more than the
    /// filename does.
    ///
    /// <para>Five cases where it would not, all of them ordinary rather than
    /// defensive. A file is labelled by its filename, because an H1 is written to
    /// open a chapter and not to sit in a rail. A directory the convention says
    /// nothing about is a directory the
    /// rail draws exactly as it drew it before any of this existed, labels
    /// included — see <see cref="Order"/>'s rung. A directory's root document is
    /// titled after the directory, so <c>.design/README.md</c> is "Design
    /// Devbook" and belongs to the row above it rather than to itself. Every
    /// document inside a bounded context carries the context's H1, so
    /// <c>.domain/inbox/features.md</c> is titled "Inbox" like its five siblings.
    /// And a chapter edited since the last build has a title from the build before
    /// it — the rung ADR 0004 answers by reading the Markdown, which the rail will
    /// not do, so it keeps the filename.</para>
    /// </summary>
    public bool TryTitle(string relativeDirectory, string name, out string title)
    {
        title = string.Empty;
        if (_index is null) return false;

        // Titles ride on the same rung as the order, because ADR 0004's ladder is
        // about the folder and not about one artifact of it: a directory the
        // convention has no entry for reads as it did before there was a
        // convention or a database, byte for byte. `arc42` is why it matters.
        // Its chapters are numbered and it names no root, so its rail stays on
        // the alphabetical fallback that puts the record folders at 09.5 and
        // 11.5 — and those two rows are labelled ADR and TDR, which the outline
        // would replace with "Architecture Decision Records" and "Technical Debt
        // Records". Truer titles, and not this rail's rows.
        if (Convention(relativeDirectory) is not { } convention) return false;

        // Asked of the convention as well as of the generated `is_root` flag
        // below, so a database built before the root was there cannot title it.
        if (string.Equals(name, convention.Root, StringComparison.OrdinalIgnoreCase)) return false;

        var key = relativeDirectory.Length == 0 ? name : relativeDirectory + "/" + name;
        if (!_titles.TryGetValue(key, out var candidate)) return false;

        var entry = candidate.Entry;

        // A row takes its label from the outline only when the row is a
        // directory. That is where the labels the issue is about live — a
        // bounded context is "Dev PC Management" and "Monitoring & Dashboard"
        // and a filename cannot say either — and a chapter's H1 is a sentence
        // rather than a label: were a directory of records ever ordered, adopting
        // its files' titles would draw "ADR 0008: Devbook reads from a cached
        // branch snapshot when there is no clone; only a clone is editable"
        // into a rail 240px wide, where "0008 Devbook Reads From A Branch
        // Snapshot When There Is No Clone" at least starts with its number.
        if (!entry.IsDirectory) return false;

        if (entry.Root) return false;
        if (string.IsNullOrWhiteSpace(entry.Title)) return false;
        if (string.Equals(entry.Title.Trim(), candidate.ParentTitle?.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (LooksStale(entry)) return false;

        title = entry.Title.Trim();
        return true;
    }

    /// <summary>Whether the outline's answer for an entry is last build's. A
    /// directory is asked about through its root document, which is where its
    /// title came from.
    /// <para>Asked of the reader's hash-free check, never of
    /// <c>IsStale</c>: the panels are allowed to spend a file read to serve
    /// current content, and the rail is not. A file whose modification time has
    /// moved is treated as drifted here rather than hashed to find out, which
    /// costs this row its generated title and keeps the rail one folder walk.</para></summary>
    private bool LooksStale(DevbookIndexEntry entry)
    {
        if (_index is null) return true;

        var file = entry.IsDirectory ? entry.RootDocument : entry;
        return file is null || !_index.Exists(file) || _index.LooksStale(file);
    }

    /// <summary>
    /// The outline flattened to one row per folder-relative path, each carrying
    /// the title of the directory it sits in — which is the comparison that tells
    /// a chapter titled after its context from one titled after itself.
    /// </summary>
    private static Dictionary<string, OutlineTitle> Titles(DevbookIndexDocument index)
    {
        var titles = new Dictionary<string, OutlineTitle>(StringComparer.OrdinalIgnoreCase);
        Collect(index.Entries, parentTitle: null, titles);
        return titles;
    }

    private static void Collect(
        IEnumerable<DevbookIndexEntry> entries,
        string? parentTitle,
        Dictionary<string, OutlineTitle> titles)
    {
        foreach (var entry in entries)
        {
            var key = DevbookIndexDocument.RelativeToFolder(entry.Path).Replace(Path.DirectorySeparatorChar, '/');
            titles[key] = new OutlineTitle(entry, parentTitle);

            if (entry.Children is { Count: > 0 })
            {
                Collect(entry.Children, entry.Title, titles);
            }
        }
    }

    private sealed record OutlineTitle(DevbookIndexEntry Entry, string? ParentTitle);
}
