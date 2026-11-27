using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The region of a brain a Lobes atlas node is drawn in
/// (<c>.devbook/domain/brain/features.md#lobes-atlas</c>). <see cref="Dust"/>
/// is no region: an archived Brain note drawn as the faint dust around the
/// outside.
/// </summary>
public enum DevbookLobe
{
    Frontal,
    Parietal,
    Temporal,
    Occipital,
    Cerebellum,
    Stem,
    Dust
}

/// <summary>What a Lobes atlas node stands for.</summary>
public enum DevbookLobesNodeKind
{
    /// <summary>A Brain note, or a chapter inside one.</summary>
    Note,

    /// <summary>A file or chapter of a registered repository's devbook.</summary>
    Chapter,

    /// <summary>The Brain's hub for one repository alias its notes link to, in
    /// Occipital.</summary>
    RepositoryHub,

    /// <summary>The Brain's <c>CONVENTIONS.md</c> or one of its area roots, in the
    /// Brain stem.</summary>
    IndexHub,

    /// <summary>A chapter a note names that no devbook on this machine holds —
    /// every chapter of a ghost devbook, and a chapter a registered devbook's
    /// index does not have — labelled with the link's own words.</summary>
    GhostChapter
}

/// <summary>Which of the three things a Lobes atlas scope is.</summary>
public enum DevbookLobesScopeKind
{
    Brain,
    Devbook,
    Ghost
}

/// <param name="Id">Unique across every scope: the scope's key, a bar, and the
/// id inside it.</param>
/// <param name="Label">What the node is called — the note's or chapter's title,
/// or for a ghost chapter the link's own words.</param>
/// <param name="ScopeKey">The <see cref="DevbookLobesScope.Key"/> it belongs to.</param>
/// <param name="Lobe">The region it is drawn in.</param>
/// <param name="Kind">What it stands for.</param>
/// <param name="Path">The file it is in, as its own index wrote it; the link's
/// path for a ghost chapter; empty for a hub.</param>
/// <param name="Dashed">Drawn dashed: a ghost chapter, or the hub of an alias not
/// registered here.</param>
/// <param name="RepositoryLink">For a chapter a note links to and a ghost
/// chapter: the first link to it as written, and whether it can be followed.</param>
public sealed record DevbookLobesNode(
    string Id,
    string Label,
    string ScopeKey,
    DevbookLobe Lobe,
    DevbookLobesNodeKind Kind,
    string Path,
    bool Dashed = false,
    DevbookAtlasRepositoryLink? RepositoryLink = null);

/// <param name="Source">The node the edge leaves.</param>
/// <param name="Target">The node it reaches.</param>
/// <param name="Kind"><c>related</c> or <c>depends-on</c> inside one scope;
/// <c>repo-link</c> from a note to the chapter it links; <c>bundle</c> from a
/// repository hub to each chapter its alias's links reach.</param>
/// <param name="Dashed">The link cannot be followed on this machine.</param>
/// <param name="Label">The repository alias on a <c>repo-link</c>; null
/// otherwise.</param>
public sealed record DevbookLobesEdge(string Source, string Target, string Kind, bool Dashed = false, string? Label = null);

/// <summary>One scope of the Lobes atlas, with the counts its panel and card
/// show.</summary>
/// <param name="Key"><c>brain</c>, <c>repo:&lt;alias&gt;</c> or
/// <c>ghost:&lt;alias&gt;</c>, lower case.</param>
/// <param name="Label">The scope's name: Brain, or the alias as registered — for a
/// ghost, as the first link to it wrote it.</param>
/// <param name="Kind">Brain, devbook or ghost devbook.</param>
/// <param name="Alias">The repository alias; null for the Brain.</param>
/// <param name="Available">False for a registered devbook whose index has not
/// been built: it is still a scope, holding only what the notes name.</param>
/// <param name="Message">Why it is not available; null when it is.</param>
/// <param name="LobeCounts">Every node of the scope, hubs included, per region —
/// every region present, at zero when empty.</param>
/// <param name="ItemCount">The notes or chapters in the scope, hubs not
/// counted.</param>
/// <param name="LinkingNotes">How many Brain notes link into this devbook; for
/// the Brain, how many link into any.</param>
/// <param name="IndexedAt">When the scope's index was last built, as its database
/// records it; null for a ghost, an unbuilt index, or one that recorded no
/// time.</param>
public sealed record DevbookLobesScope(
    string Key,
    string Label,
    DevbookLobesScopeKind Kind,
    string? Alias,
    bool Available,
    string? Message,
    IReadOnlyDictionary<DevbookLobe, int> LobeCounts,
    int ItemCount,
    int LinkingNotes,
    DateTimeOffset? IndexedAt = null);

/// <summary>
/// The Lobes atlas: the Brain and every registered devbook in one graph, each
/// node carrying its scope, its region and its kind.
/// </summary>
public sealed record DevbookLobesGraph(
    bool Available,
    string? Message,
    IReadOnlyList<DevbookLobesScope> Scopes,
    IReadOnlyList<DevbookLobesNode> Nodes,
    IReadOnlyList<DevbookLobesEdge> Edges)
{
    /// <summary>The key of the Brain's scope.</summary>
    public const string BrainScopeKey = "brain";

    /// <summary>What a ghost devbook's scope key starts with, its alias in lower
    /// case after it.</summary>
    public const string GhostScopePrefix = "ghost:";

    /// <summary>True when the graph is unavailable because the Brain's index has
    /// not been built yet — the one absence the atlas can offer to mend.</summary>
    public bool BrainIndexMissing { get; init; }

    public static DevbookLobesGraph Unavailable(string message) => new(false, message, [], [], []);

    /// <summary>No Brain index yet: unavailable, and saying which absence it is.</summary>
    public static DevbookLobesGraph MissingBrainIndex(string message) => Unavailable(message) with { BrainIndexMissing = true };

    /// <summary>The scope with this key, or null.</summary>
    public DevbookLobesScope? Scope(string key) =>
        Scopes.FirstOrDefault(scope => string.Equals(scope.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>What the Lobes read takes from one registered repository's devbook
/// database — or, with <see cref="Available"/> false, the absence of one.</summary>
/// <param name="Alias">The alias as registered.</param>
/// <param name="RootPaths">The files the reading order roots on: each folder's
/// convention root and every directory's root document.</param>
/// <param name="IndexedAt">When its database was built.</param>
public sealed record DevbookLobesDevbookSource(
    string Alias,
    bool Available,
    string? Message,
    IReadOnlyList<DevbookAtlasSourceNode> Nodes,
    IReadOnlyList<DevbookAtlasSourceEdge> Edges,
    IReadOnlySet<string> RootPaths,
    DateTimeOffset? IndexedAt = null)
{
    public static DevbookLobesDevbookSource Missing(string alias, string message) =>
        new(alias, false, message, [], [], new HashSet<string>());
}

/// <summary>
/// The devbook half of the Lobes read: the edge kinds the atlas draws, where a
/// devbook chapter sits, and the ids every scope's nodes are keyed by. The
/// personal devbook that links into the registered ones conforms to these when it
/// puts the whole graph together (local ADR 0040 §2: the context that supplies
/// the personal devbook reads it, and Devbook draws it). Pure: nothing here opens
/// a database.
/// </summary>
public static class DevbookLobesReader
{
    /// <summary>The edge kinds drawn inside one scope, as the atlas draws
    /// them.</summary>
    private static readonly string[] OwnEdgeKinds = ["related", "depends-on"];

    /// <summary>The node types that are notes or chapters; headings, C4 elements
    /// and views are not drawn.</summary>
    private static readonly string[] DrawnNodeTypes = ["file", "chapter"];

    public const string RepoLinkEdgeKind = DevbookAtlasReader.RepoLinkEdgeKind;

    public const string BundleEdgeKind = "bundle";

    /// <summary>What the chip of a chapter a note names says while its devbook's
    /// index has not been built: the index is missing, not the chapter, and the
    /// scope card says the same.</summary>
    public const string NotIndexedYet = "its devbook is not indexed yet — see its scope card";

    /// <summary>What a scope key starts with for a registered repository's devbook,
    /// its alias in lower case after it.</summary>
    public const string RepositoryScopePrefix = "repo:";

    private const char Bar = '|';

    /// <summary>A devbook chapter's region by its folder. A chapter in no folder
    /// sits with the roots, in the stem.</summary>
    public static DevbookLobe FolderLobe(string? folder) => folder?.Trim().TrimStart('.').ToLowerInvariant() switch
    {
        "arc42" => DevbookLobe.Frontal,
        "tech" => DevbookLobe.Parietal,
        "domain" => DevbookLobe.Temporal,
        "design" => DevbookLobe.Occipital,
        "ai" => DevbookLobe.Cerebellum,
        _ => DevbookLobe.Stem
    };

    /// <summary>Whether a node is drawn: a note or a chapter with an id.</summary>
    public static bool IsDrawn(DevbookAtlasSourceNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Id.Length > 0 && DrawnNodeTypes.Contains(node.Type, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>What a node is called: its label, or its id without one.</summary>
    public static string Label(DevbookAtlasSourceNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Label.Length > 0 ? node.Label : node.Id;
    }

    /// <summary>A node's id across every scope: the scope's key, a bar, and the id
    /// inside it.</summary>
    public static string Id(string scopeKey, string id) => scopeKey + Bar + id;

    /// <summary>A ghost chapter's id: an id space of its own within the scope, the
    /// chapter's key folded to lower case so two cases of one path are one node.</summary>
    public static string GhostId(string scopeKey, string chapterKey)
    {
        ArgumentNullException.ThrowIfNull(chapterKey);
        return scopeKey + Bar + "ghost:" + chapterKey.ToLowerInvariant();
    }

    /// <summary>A chapter's key in the root layout's spelling, so a link written in
    /// either layout finds it: the path folded, and its heading kept.</summary>
    public static string ChapterKey(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var hash = id.IndexOf('#', StringComparison.Ordinal);
        return hash < 0
            ? DevbookLayout.ConventionalPath(id)
            : DevbookLayout.ConventionalPath(id[..hash]) + id[hash..];
    }

    /// <summary>The chapter a link names, or its file when the heading is not a
    /// chapter of the index.</summary>
    public static string? Find(IReadOnlyDictionary<string, string> chapters, string chapterKey)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        ArgumentNullException.ThrowIfNull(chapterKey);
        if (chapters.TryGetValue(chapterKey, out var exact)) return exact;

        var hash = chapterKey.IndexOf('#', StringComparison.Ordinal);
        return hash >= 0 && chapters.TryGetValue(chapterKey[..hash], out var file) ? file : null;
    }

    /// <summary>The devbook folder a link's path lies in: <c>arc42</c> for
    /// <c>.arc42/…</c> and <c>.devbook/arc42/…</c>.</summary>
    public static string FolderOf(string path)
    {
        var conventional = DevbookLayout.ConventionalPath(path);
        var slash = conventional.IndexOf('/');
        return slash < 0 ? string.Empty : conventional[..slash];
    }

    /// <summary>The <c>related</c> and <c>depends-on</c> edges between a scope's own
    /// drawn nodes, each pair once whichever way it runs.</summary>
    public static void AddOwnEdges(
        string scopeKey,
        IReadOnlyList<DevbookAtlasSourceEdge> rawEdges,
        IReadOnlyDictionary<string, DevbookAtlasSourceNode> known,
        List<DevbookLobesEdge> edges,
        HashSet<string> pairs)
    {
        ArgumentNullException.ThrowIfNull(rawEdges);
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(pairs);

        foreach (var edge in rawEdges)
        {
            if (!OwnEdgeKinds.Contains(edge.Type, StringComparer.OrdinalIgnoreCase)) continue;
            if (!known.TryGetValue(edge.Source, out var source) || !known.TryGetValue(edge.Target, out var target)) continue;

            var from = Id(scopeKey, source.Id);
            var to = Id(scopeKey, target.Id);
            if (from == to) continue;

            var pair = string.CompareOrdinal(from, to) <= 0 ? from + Bar + to : to + Bar + from;
            if (pairs.Add(pair)) edges.Add(new DevbookLobesEdge(from, to, edge.Type));
        }
    }

    /// <summary>
    /// The registered devbooks' scopes and chapter nodes, each devbook once by
    /// alias, appended to <paramref name="nodes"/>, <paramref name="edges"/> and
    /// <paramref name="scopes"/>. Returns, per alias, the scope's key, whether its
    /// index was there, and its chapters by <see cref="ChapterKey"/> so a link into
    /// it finds the node.
    /// </summary>
    public static Dictionary<string, (string Key, bool Indexed, Dictionary<string, string> Chapters)> AddDevbooks(
        IReadOnlyList<DevbookLobesDevbookSource> devbooks,
        Action<DevbookLobesNode> add,
        List<DevbookLobesEdge> edges,
        HashSet<string> pairs,
        List<(string Key, string Label, DevbookLobesScopeKind Kind, string? Alias, bool Available, string? Message, DateTimeOffset? IndexedAt)> scopes)
    {
        ArgumentNullException.ThrowIfNull(devbooks);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(scopes);

        var registered = new Dictionary<string, (string Key, bool Indexed, Dictionary<string, string> Chapters)>(StringComparer.OrdinalIgnoreCase);

        foreach (var devbook in devbooks)
        {
            if (registered.ContainsKey(devbook.Alias)) continue;

            var key = RepositoryScopePrefix + devbook.Alias.ToLowerInvariant();
            var chapters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var own = new Dictionary<string, DevbookAtlasSourceNode>(StringComparer.OrdinalIgnoreCase);
            registered[devbook.Alias] = (key, devbook.Available, chapters);
            scopes.Add((key, devbook.Alias, DevbookLobesScopeKind.Devbook, devbook.Alias, devbook.Available, devbook.Message, devbook.IndexedAt));

            foreach (var chapter in devbook.Nodes)
            {
                if (!IsDrawn(chapter) || !own.TryAdd(chapter.Id, chapter)) continue;

                var id = Id(key, chapter.Id);
                var isRoot = chapter.Type == "file" && devbook.RootPaths.Contains(chapter.Path);
                add(new DevbookLobesNode(id, Label(chapter), key, isRoot ? DevbookLobe.Stem : FolderLobe(chapter.Folder), DevbookLobesNodeKind.Chapter, chapter.Path));
                chapters.TryAdd(ChapterKey(chapter.Id), id);
            }

            AddOwnEdges(key, devbook.Edges, own, edges, pairs);
        }

        return registered;
    }

    /// <summary>Each scope's summary: its counts per region, its items, and how many
    /// notes link into it.</summary>
    public static List<DevbookLobesScope> Summaries(
        IReadOnlyList<(string Key, string Label, DevbookLobesScopeKind Kind, string? Alias, bool Available, string? Message, DateTimeOffset? IndexedAt)> scopes,
        IReadOnlyList<DevbookLobesNode> nodes,
        IReadOnlyDictionary<string, HashSet<string>> linking)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(linking);

        var byScope = nodes.ToLookup(node => node.ScopeKey, StringComparer.Ordinal);
        return scopes
            .Select(scope =>
            {
                var own = byScope[scope.Key].ToList();
                var counts = Enum.GetValues<DevbookLobe>().ToDictionary(lobe => lobe, lobe => own.Count(node => node.Lobe == lobe));
                var items = own.Count(node => node.Kind is DevbookLobesNodeKind.Note or DevbookLobesNodeKind.Chapter or DevbookLobesNodeKind.GhostChapter);
                return new DevbookLobesScope(scope.Key, scope.Label, scope.Kind, scope.Alias, scope.Available, scope.Message, counts, items, linking.GetValueOrDefault(scope.Key)?.Count ?? 0, scope.IndexedAt);
            })
            .ToList();
    }
}
