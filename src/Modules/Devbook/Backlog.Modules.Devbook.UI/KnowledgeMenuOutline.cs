namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// What the knowledge menu needs to know about a folder before it draws a row:
/// the order its directories are meant to be read in, and the real title of the
/// chapter behind each one.
///
/// <para>Both answers already exist and neither used to reach the rail. The order
/// is authored, in the folder's committed <c>_reading-order.json</c>, and it is
/// the same list the domain, design and technology panes beside the rail order
/// themselves by. The titles are derived, in the generated
/// <c>_meta/knowledge.db</c> that ADR 0004 introduced, which is where
/// <c>dev-pc-management</c> is known to be "Dev PC Management" rather than the
/// "Dev Pc Management" a filename can be title-cased into.</para>
///
/// <para>The two are read once per area, not once per directory, because a tab
/// click should cost one folder walk. Neither read opens a Markdown file, which
/// is the whole reason the rail can consult them at all: the alternative — parse
/// every chapter for its H1 — is the corpus parse the index exists to avoid.</para>
///
/// <para>Both degrade to nothing, and they degrade together. A folder with no
/// readable declaration orders no directory — and titles no row either, because a
/// directory that keeps the old sort keeps the old labels with it. A checkout with
/// no database titles no row whatever the declaration says. Either way the rail is
/// exactly what it was before these artifacts existed.</para>
/// </summary>
internal sealed class KnowledgeMenuOutline
{
    private readonly KnowledgeFolderReadingOrder _order;
    private readonly KnowledgeIndexDocument? _index;
    private readonly IReadOnlyDictionary<string, OutlineTitle> _titles;

    private KnowledgeMenuOutline(
        KnowledgeFolderReadingOrder order,
        KnowledgeIndexDocument? index,
        IReadOnlyDictionary<string, OutlineTitle> titles)
    {
        _order = order;
        _index = index;
        _titles = titles;
    }

    /// <summary>A folder with neither answer: every directory sorts itself and
    /// every row keeps its filename label. What the instruction area gets, since
    /// it is assembled out of agent folders rather than being a knowledge folder
    /// with an order of its own.</summary>
    public static KnowledgeMenuOutline None { get; } = new(
        KnowledgeFolderReadingOrder.Empty,
        null,
        new Dictionary<string, OutlineTitle>(StringComparer.OrdinalIgnoreCase));

    public static KnowledgeMenuOutline Read(string folderPath)
    {
        var order = KnowledgeReadingOrder.Read(folderPath);
        var index = KnowledgeIndexDocument.TryRead(folderPath);

        return new KnowledgeMenuOutline(order, index, index is null ? None._titles : Titles(index));
    }

    /// <summary>The declared entry names of one directory of the folder, root
    /// document first, or empty when it declares none.</summary>
    public IReadOnlyList<string> Order(string relativeDirectory) => _order.ForDirectory(relativeDirectory);

    /// <summary>
    /// The generated title of one entry, when adopting it would say more than the
    /// filename does.
    ///
    /// <para>Five cases where it would not, all of them ordinary rather than
    /// defensive. A file is labelled by its filename, because an H1 is written to
    /// open a chapter and not to sit in a rail. A directory nobody declared an
    /// order for is a directory the
    /// rail draws exactly as it drew it before any of this existed, labels
    /// included — see <see cref="Order"/>'s rung. A directory's root document is
    /// titled after the directory, so <c>.design/README.md</c> is "Design
    /// Knowledge" and belongs to the row above it rather than to itself. Every
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
        // about the folder and not about one artifact of it: a directory whose
        // declaration this reader cannot use reads as it did before there was a
        // declaration or a database, byte for byte. `.arc42` is why it matters.
        // It declares `root: null` and an empty order deliberately, so its rail
        // stays on the alphabetical fallback that puts the record folders at 09.5
        // and 11.5 — and those two rows are labelled ADR and TDR, which the
        // outline would replace with "Architecture Decision Records" and
        // "Technical Debt Records". Truer titles, and not this rail's rows.
        if (_order.ForDirectory(relativeDirectory).Count == 0) return false;

        // Asked of the authored file rather than only of the generated `is_root`
        // flag, because the folder that declares its root document is the one
        // place that fact is written by hand.
        if (string.Equals(name, _order.RootDocumentIn(relativeDirectory), StringComparison.OrdinalIgnoreCase)) return false;

        var key = relativeDirectory.Length == 0 ? name : relativeDirectory + "/" + name;
        if (!_titles.TryGetValue(key, out var candidate)) return false;

        var entry = candidate.Entry;

        // A row takes its label from the outline only when the row is a
        // directory. That is where the labels the issue is about live — a
        // bounded context is "Dev PC Management" and "Monitoring & Dashboard"
        // and a filename cannot say either — and a chapter's H1 is a sentence
        // rather than a label: `.arc42/adr` declares an order, so adopting its
        // files' titles would draw "ADR 0008: Knowledge reads from a cached
        // branch snapshot when there is no clone; only a clone is editable"
        // into a rail 240px wide, where "0008 Knowledge Reads From A Branch
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
    private bool LooksStale(KnowledgeIndexEntry entry)
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
    private static Dictionary<string, OutlineTitle> Titles(KnowledgeIndexDocument index)
    {
        var titles = new Dictionary<string, OutlineTitle>(StringComparer.OrdinalIgnoreCase);
        Collect(index.Entries, parentTitle: null, titles);
        return titles;
    }

    private static void Collect(
        IEnumerable<KnowledgeIndexEntry> entries,
        string? parentTitle,
        Dictionary<string, OutlineTitle> titles)
    {
        foreach (var entry in entries)
        {
            var key = KnowledgeIndexDocument.RelativeToFolder(entry.Path).Replace(Path.DirectorySeparatorChar, '/');
            titles[key] = new OutlineTitle(entry, parentTitle);

            if (entry.Children is { Count: > 0 })
            {
                Collect(entry.Children, entry.Title, titles);
            }
        }
    }

    private sealed record OutlineTitle(KnowledgeIndexEntry Entry, string? ParentTitle);
}
