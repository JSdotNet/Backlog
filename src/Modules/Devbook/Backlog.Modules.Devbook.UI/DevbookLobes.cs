using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The region of a brain a Lobes atlas node is drawn in
/// (<c>.devbook/domain/devbook/features.md#lobes-atlas</c>). <see cref="Dust"/>
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

/// <summary>What the Lobes read takes from the Brain's database.</summary>
/// <param name="Nodes">The <c>node</c> rows.</param>
/// <param name="Edges">The <c>edge</c> rows, <c>repo:</c> links included.</param>
/// <param name="Para">Each node's <c>para</c> attribute: its area, or what its
/// <c>para</c> field says instead.</param>
/// <param name="Areas">The area folders the index holds, in order.</param>
/// <param name="Declared">The areas the Brain declares, so an area is known by
/// its name whatever folder holds it.</param>
/// <param name="IndexedAt">When the Brain's database was built.</param>
internal sealed record DevbookLobesBrainSource(
    IReadOnlyList<DevbookAtlasSourceNode> Nodes,
    IReadOnlyList<DevbookAtlasSourceEdge> Edges,
    IReadOnlyDictionary<string, string> Para,
    IReadOnlyList<string> Areas,
    IReadOnlyList<BrainArea>? Declared = null,
    DateTimeOffset? IndexedAt = null);

/// <summary>What the Lobes read takes from one registered repository's devbook
/// database — or, with <see cref="Available"/> false, the absence of one.</summary>
/// <param name="Alias">The alias as registered.</param>
/// <param name="RootPaths">The files the reading order roots on: each folder's
/// convention root and every directory's root document.</param>
/// <param name="IndexedAt">When its database was built.</param>
internal sealed record DevbookLobesDevbookSource(
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
/// Puts the Brain and the registered devbooks into one Lobes graph. Pure: the
/// service opens the databases and hands their rows in.
/// </summary>
internal static class DevbookLobesReader
{
    /// <summary>The edge kinds drawn inside one scope, as the atlas draws
    /// them.</summary>
    private static readonly string[] OwnEdgeKinds = ["related", "depends-on"];

    /// <summary>The node types that are notes or chapters; headings, C4 elements
    /// and views are not drawn.</summary>
    private static readonly string[] DrawnNodeTypes = ["file", "chapter"];

    public const string RepoLinkEdgeKind = DevbookAtlasReader.RepoLinkEdgeKind;

    public const string BundleEdgeKind = "bundle";

    /// <summary>The label of the Brain stem's hub for <c>CONVENTIONS.md</c>.</summary>
    public const string ConventionsHubLabel = BrainConventions.FileName;

    /// <summary>What the chip of a chapter a note names says while its devbook's
    /// index has not been built: the index is missing, not the chapter, and the
    /// scope card says the same.</summary>
    public const string NotIndexedYet = "its devbook is not indexed yet — see its scope card";

    private const char Bar = '|';

    public static DevbookLobesGraph Read(
        DevbookLobesBrainSource brain,
        IReadOnlyList<DevbookLobesDevbookSource> devbooks,
        Func<BrainRepoLink, BrainRepoLinkResolution> resolve)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(devbooks);
        ArgumentNullException.ThrowIfNull(resolve);

        var nodes = new List<DevbookLobesNode>();
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var edges = new List<DevbookLobesEdge>();
        var pairs = new HashSet<string>(StringComparer.Ordinal);

        // ----- The Brain -----
        var brainKey = DevbookLobesGraph.BrainScopeKey;
        var notes = new Dictionary<string, DevbookAtlasSourceNode>(StringComparer.OrdinalIgnoreCase);
        var para = new Dictionary<string, string>(brain.Para, StringComparer.OrdinalIgnoreCase);
        var declared = brain.Declared ?? [];

        void Add(DevbookLobesNode node)
        {
            indexes[node.Id] = nodes.Count;
            nodes.Add(node);
        }

        foreach (var note in brain.Nodes)
        {
            if (!IsDrawn(note) || !notes.TryAdd(note.Id, note)) continue;

            // A chapter of a note carries a para only when it states one, so it
            // takes its note's otherwise.
            var area = para.GetValueOrDefault(note.Id)
                       ?? para.GetValueOrDefault(note.Path)
                       ?? note.Folder;
            Add(new DevbookLobesNode(Id(brainKey, note.Id), Label(note), brainKey, BrainLobe(area, declared), DevbookLobesNodeKind.Note, note.Path));
        }

        Add(new DevbookLobesNode(Id(brainKey, "index:" + BrainConventions.FileName), ConventionsHubLabel, brainKey, DevbookLobe.Stem, DevbookLobesNodeKind.IndexHub, string.Empty));
        foreach (var area in brain.Areas.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var name = declared.FirstOrDefault(declaration => string.Equals(declaration.Folder, area, StringComparison.OrdinalIgnoreCase))?.Name
                       ?? BrainConventions.NameFor(area);
            Add(new DevbookLobesNode(Id(brainKey, "index:" + area), name, brainKey, DevbookLobe.Stem, DevbookLobesNodeKind.IndexHub, string.Empty));
        }

        AddOwnEdges(brainKey, brain.Edges, notes, edges, pairs);

        // ----- The registered devbooks -----
        var registered = new Dictionary<string, (string Key, bool Indexed, Dictionary<string, string> Chapters)>(StringComparer.OrdinalIgnoreCase);
        var scopes = new List<(string Key, string Label, DevbookLobesScopeKind Kind, string? Alias, bool Available, string? Message, DateTimeOffset? IndexedAt)>
        {
            (brainKey, BrainConventions.ScopeLabel, DevbookLobesScopeKind.Brain, null, true, null, brain.IndexedAt)
        };

        foreach (var devbook in devbooks)
        {
            if (registered.ContainsKey(devbook.Alias)) continue;

            var key = "repo:" + devbook.Alias.ToLowerInvariant();
            var chapters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var own = new Dictionary<string, DevbookAtlasSourceNode>(StringComparer.OrdinalIgnoreCase);
            registered[devbook.Alias] = (key, devbook.Available, chapters);
            scopes.Add((key, devbook.Alias, DevbookLobesScopeKind.Devbook, devbook.Alias, devbook.Available, devbook.Message, devbook.IndexedAt));

            foreach (var chapter in devbook.Nodes)
            {
                if (!IsDrawn(chapter) || !own.TryAdd(chapter.Id, chapter)) continue;

                var id = Id(key, chapter.Id);
                var isRoot = chapter.Type == "file" && devbook.RootPaths.Contains(chapter.Path);
                Add(new DevbookLobesNode(id, Label(chapter), key, isRoot ? DevbookLobe.Stem : FolderLobe(chapter.Folder), DevbookLobesNodeKind.Chapter, chapter.Path));
                chapters.TryAdd(ChapterKey(chapter.Id), id);
            }

            AddOwnEdges(key, devbook.Edges, own, edges, pairs);
        }

        // ----- The links from the Brain into them -----
        var ghosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hubs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var linking = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var linkedNodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var edge in brain.Edges)
        {
            if (!BrainRepoLink.IsRepoLink(edge.Target) || BrainRepoLink.Parse(edge.Target) is not { } link) continue;
            if (!notes.TryGetValue(edge.Source, out var note)) continue;

            var resolution = resolve(link);
            // The root layout's spelling, as the Brain atlas keys a repository
            // chapter, so every spelling of one chapter is one node.
            var path = DevbookLayout.ConventionalPath(link.Path);
            var chapterKey = link.Slug is null ? path : $"{path}#{link.Slug}";
            var raw = $"{BrainRepoLink.Scheme}{link.Alias}/{link.Reference}";

            string scopeKey;
            string target;
            bool dashed;
            var isRegistered = registered.TryGetValue(link.Alias, out var devbook);

            if (isRegistered)
            {
                // The devbook's own node whenever its index holds the chapter,
                // dashed when the resolver says it cannot be followed here (its
                // section hidden, say); a ghost chapter otherwise, in an id space
                // of its own so it never lands on a real node.
                scopeKey = devbook.Key;
                if (Find(devbook.Chapters, chapterKey) is { } found)
                {
                    target = found;
                    dashed = !resolution.Resolved;
                }
                else
                {
                    target = GhostId(scopeKey, chapterKey);
                    dashed = true;
                }
            }
            else
            {
                if (!ghosts.TryGetValue(link.Alias, out var ghostKey))
                {
                    ghostKey = DevbookLobesGraph.GhostScopePrefix + link.Alias.ToLowerInvariant();
                    ghosts[link.Alias] = ghostKey;
                    scopes.Add((ghostKey, link.Alias, DevbookLobesScopeKind.Ghost, link.Alias, false, BrainRepoLinkResolver.NotOnThisMachine, null));
                }

                scopeKey = ghostKey;
                target = GhostId(scopeKey, chapterKey);
                dashed = true;
            }

            if (linkedNodes.Add(target))
            {
                // An alias with no devbook registered is not on this machine as
                // far as the atlas goes, whatever the folder settings say. In a
                // devbook not indexed yet, no chapter can be found, so a chapter
                // the resolver did not place on another machine says that the
                // index is missing rather than that the chapter is.
                var problem = !isRegistered
                    ? BrainRepoLinkResolver.NotOnThisMachine
                    : !devbook.Indexed && (resolution.Problem is null or BrainRepoLinkResolver.NotADevbookChapter)
                        ? NotIndexedYet
                        : resolution.Problem ?? BrainRepoLinkResolver.NotADevbookChapter;
                var repositoryLink = new DevbookAtlasRepositoryLink(link.Alias, raw, !dashed, dashed ? problem : null);
                if (indexes.TryGetValue(target, out var index))
                {
                    nodes[index] = nodes[index] with { RepositoryLink = repositoryLink };
                }
                else
                {
                    Add(new DevbookLobesNode(target, resolution.Label, scopeKey, FolderLobe(FolderOf(link.Path)), DevbookLobesNodeKind.GhostChapter, link.Path, true, repositoryLink));
                }
            }

            if (!hubs.TryGetValue(scopeKey, out var hub))
            {
                hub = Id(brainKey, "hub:" + scopeKey);
                hubs[scopeKey] = hub;
                var hubLabel = scopes.First(scope => scope.Key == scopeKey).Label;
                Add(new DevbookLobesNode(hub, hubLabel, brainKey, DevbookLobe.Occipital, DevbookLobesNodeKind.RepositoryHub, string.Empty, ghosts.ContainsValue(scopeKey)));
            }

            var from = Id(brainKey, note.Id);
            if (pairs.Add(from + Bar + target)) edges.Add(new DevbookLobesEdge(from, target, RepoLinkEdgeKind, dashed, link.Alias));
            if (pairs.Add(hub + Bar + target)) edges.Add(new DevbookLobesEdge(hub, target, BundleEdgeKind, dashed));

            var notePath = note.Path.Length > 0 ? note.Path : note.Id;
            (linking.TryGetValue(scopeKey, out var set) ? set : linking[scopeKey] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(notePath);
            (linking.TryGetValue(brainKey, out var all) ? all : linking[brainKey] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(notePath);
        }

        var byScope = nodes.ToLookup(node => node.ScopeKey, StringComparer.Ordinal);
        var summaries = scopes
            .Select(scope =>
            {
                var own = byScope[scope.Key].ToList();
                var counts = Enum.GetValues<DevbookLobe>().ToDictionary(lobe => lobe, lobe => own.Count(node => node.Lobe == lobe));
                var items = own.Count(node => node.Kind is DevbookLobesNodeKind.Note or DevbookLobesNodeKind.Chapter or DevbookLobesNodeKind.GhostChapter);
                return new DevbookLobesScope(scope.Key, scope.Label, scope.Kind, scope.Alias, scope.Available, scope.Message, counts, items, linking.GetValueOrDefault(scope.Key)?.Count ?? 0, scope.IndexedAt);
            })
            .ToList();

        return new DevbookLobesGraph(true, null, summaries, nodes, edges);
    }

    /// <summary>
    /// A Brain note's region by its PARA area — the <c>para</c> attribute, which is
    /// the note's folder unless its <c>para</c> field says otherwise. The area is
    /// known by its declared name first, so a Projects area kept in a
    /// <c>work</c> folder is Frontal, then by the word itself. Any area a
    /// <c>CONVENTIONS.md</c> declares beyond PARA is Parietal.
    /// </summary>
    public static DevbookLobe BrainLobe(string? para, IReadOnlyList<BrainArea>? declared = null)
    {
        var name = declared?.FirstOrDefault(area => string.Equals(area.Folder, para?.Trim(), StringComparison.OrdinalIgnoreCase))?.Name;
        return ParaLobe(name) ?? ParaLobe(para) ?? DevbookLobe.Parietal;
    }

    /// <summary>The region a PARA area's name or folder places a note in, or null
    /// for a word PARA does not have.</summary>
    private static DevbookLobe? ParaLobe(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "projects" => DevbookLobe.Frontal,
        "areas" or "resources" => DevbookLobe.Parietal,
        "people" => DevbookLobe.Temporal,
        "journal" or "_inbox" or "inbox" => DevbookLobe.Cerebellum,
        "archive" => DevbookLobe.Dust,
        _ => null
    };

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

    private static bool IsDrawn(DevbookAtlasSourceNode node) =>
        node.Id.Length > 0 && DrawnNodeTypes.Contains(node.Type, StringComparer.OrdinalIgnoreCase);

    private static string Label(DevbookAtlasSourceNode node) => node.Label.Length > 0 ? node.Label : node.Id;

    private static string Id(string scopeKey, string id) => scopeKey + Bar + id;

    /// <summary>A ghost chapter's id: an id space of its own within the scope, the
    /// chapter's key folded to lower case so two cases of one path are one node.</summary>
    private static string GhostId(string scopeKey, string chapterKey) =>
        scopeKey + Bar + "ghost:" + chapterKey.ToLowerInvariant();

    /// <summary>A chapter's key in the root layout's spelling, so a link written in
    /// either layout finds it: the path folded, and its heading kept.</summary>
    private static string ChapterKey(string id)
    {
        var hash = id.IndexOf('#', StringComparison.Ordinal);
        return hash < 0
            ? DevbookLayout.ConventionalPath(id)
            : DevbookLayout.ConventionalPath(id[..hash]) + id[hash..];
    }

    /// <summary>The chapter a link names, or its file when the heading is not a
    /// chapter of the index.</summary>
    private static string? Find(Dictionary<string, string> chapters, string chapterKey)
    {
        if (chapters.TryGetValue(chapterKey, out var exact)) return exact;

        var hash = chapterKey.IndexOf('#', StringComparison.Ordinal);
        return hash >= 0 && chapters.TryGetValue(chapterKey[..hash], out var file) ? file : null;
    }

    /// <summary>The devbook folder a link's path lies in: <c>arc42</c> for
    /// <c>.arc42/…</c> and <c>.devbook/arc42/…</c>.</summary>
    private static string FolderOf(string path)
    {
        var conventional = DevbookLayout.ConventionalPath(path);
        var slash = conventional.IndexOf('/');
        return slash < 0 ? string.Empty : conventional[..slash];
    }

    private static void AddOwnEdges(
        string scopeKey,
        IReadOnlyList<DevbookAtlasSourceEdge> rawEdges,
        Dictionary<string, DevbookAtlasSourceNode> known,
        List<DevbookLobesEdge> edges,
        HashSet<string> pairs)
    {
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
}
