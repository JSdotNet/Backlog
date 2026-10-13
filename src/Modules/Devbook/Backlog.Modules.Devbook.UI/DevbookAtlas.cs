using System.Text.Json;

using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel.Devbook;
using Backlog.UI.Components.Devbook;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The knowledge folders as a map, read from the graph the metadata generator
/// already derives.
///
/// <para>Nothing here parses Markdown. The generated devbook database
/// holds the whole repository's graph in one table — nodes with a label, a
/// folder, a status and a path, edges with a kind — and a scope is a filter over
/// it rather than a file of its own, which is what local ADR 0004 changed. A
/// repository that still has the committed <c>_meta/graph.json</c> and no database
/// is read from that instead; the map is the same either way. Re-deriving the same
/// graph by reading the documents would be a second answer to a question that
/// already has one, and it would disagree the first time the generator changed.</para>
///
/// <para>That also decides what the atlas can honestly show: it is as fresh as
/// the last generator run. A folder whose index has not been regenerated draws
/// the graph as it was, which is why an absent index says so rather than falling
/// back to something half-parsed. It is the one knowledge consumer that does not
/// degrade to Markdown, and that is deliberate — a map of the references between
/// chapters cannot be drawn from the handful of files on screen.</para>
/// </summary>
public sealed class DevbookAtlasService(IDevbookFolderSource source)
{
    public event Action? Changed
    {
        add => source.Changed += value;
        remove => source.Changed -= value;
    }

    public async Task<DevbookAtlasGraph> ReadAsync(DevbookAtlasScope scope, string? repositoryAlias = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();

        await PrepareGraphsAsync(scope, repositoryAlias, cancellationToken).ConfigureAwait(false);

        try
        {
            return Read(scope, repositoryAlias);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return DevbookAtlasGraph.Unavailable(scope, $"The {scope.Label} atlas could not be read: {ex.Message}");
        }
    }

    /// <summary>
    /// The Brain's atlas (local ADR 0027 §6): its notes and the links between
    /// them, read from the Brain's own database in the app's storage, with every
    /// <c>repo:</c> link drawn as an edge to a node for the repository chapter it
    /// names — labelled with the repository alias, and dashed where this machine
    /// cannot follow it (<see cref="BrainRepoLinkResolver"/>, asked of the
    /// repositories configured here through the same folder source the
    /// repository atlas reads).
    /// <para>
    /// No Brain set, no database built yet, or one that will not open all say so
    /// rather than drawing an empty map, for the reason the repository atlas does:
    /// a map of links cannot be drawn from the note on screen. Nothing is read
    /// from or written to a repository; the repository nodes are the links' own
    /// words.
    /// </para>
    /// </summary>
    public Task<DevbookAtlasGraph> ReadBrainAsync(IBrainFolderSource brain, CancellationToken cancellationToken = default) =>
        ReadBrainAsync(brain, BrainDatabaseFor, cancellationToken);

    /// <summary>Where a Brain folder's database is: the app's storage, unless a
    /// test points it at a scratch folder.</summary>
    internal Func<string, string?> BrainDatabaseFor { get; init; } = DevbookDatabaseLocation.ForBrainFolder;

    /// <param name="brain">The Brain, asked for its folder on every read so a
    /// moved setting is followed at once.</param>
    /// <param name="databaseFor">Where that folder's database is.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    internal Task<DevbookAtlasGraph> ReadBrainAsync(IBrainFolderSource brain, Func<string, string?> databaseFor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(databaseFor);

        var scope = DevbookAtlasScope.Brain;
        if (brain.BrainRoot is not { Length: > 0 } root)
        {
            return Task.FromResult(DevbookAtlasGraph.Unavailable(scope, NoBrainFolder));
        }

        return Task.Run(() => ReadBrain(scope, root, databaseFor), cancellationToken);
    }

    private DevbookAtlasGraph ReadBrain(DevbookAtlasScope scope, string root, Func<string, string?> databaseFor)
    {
        try
        {
            using var database = DevbookDatabase.TryOpen(databaseFor(root));
            if (database is null) return DevbookAtlasGraph.Unavailable(scope, BrainIndexMissing);

            var nodes = database.Nodes()
                .Select(node => new DevbookAtlasSourceNode(
                    node.Id,
                    node.Label ?? string.Empty,
                    node.Folder ?? string.Empty,
                    node.Type,
                    node.Status ?? string.Empty,
                    node.Path ?? string.Empty,
                    node.OutOfScope))
                .ToList();

            var edges = database.Edges()
                .Select(edge => new DevbookAtlasSourceEdge(edge.Type, edge.Source, edge.Target))
                .ToList();

            var resolver = new BrainRepoLinkResolver(source);
            return DevbookAtlasReader.ReadBrain(scope, nodes, edges, resolver.Resolve);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            // Busy, or replaced by a rebuild under the read. The next change asks
            // again.
            return DevbookAtlasGraph.Unavailable(scope, $"The Brain atlas could not be read: {ex.Message}");
        }
    }

    /// <summary>What the Brain atlas says while no Brain folder is set.</summary>
    internal const string NoBrainFolder = "No Brain folder is set, so there is no Brain to map.";

    /// <summary>What the Brain atlas says before the Brain's index exists.</summary>
    internal const string BrainIndexMissing =
        "The Brain atlas needs the Brain's index, which has not been built yet. Backlog builds it in the background when the Brain is opened — try again in a moment.";

    /// <summary>
    /// The graph files a branch may carry, fetched before the read looks for
    /// them. Only the graphs: the atlas is a reading of the generated index,
    /// not of the chapters, so no chapter is fetched to draw it. A repository
    /// that does not commit its <c>_meta</c> folder has nothing here to fetch,
    /// and the read then says the index has not been written, as it always did.
    /// </summary>
    private async Task PrepareGraphsAsync(DevbookAtlasScope scope, string? repositoryAlias, CancellationToken cancellationToken)
    {
        if (scope.FolderKey is { Length: > 0 } folderKey)
        {
            await source.PrepareContentAsync(folderKey, repositoryAlias, ["_meta/graph.json"], cancellationToken).ConfigureAwait(false);
            return;
        }

        // Repository-wide, so one folder is enough to ask through; the first
        // enabled one is as good as any.
        foreach (var setting in source.Folders(repositoryAlias))
        {
            if (!setting.Enabled) continue;

            var location = await source
                .PrepareContentAsync(setting.Key, repositoryAlias, ["**/_meta/graph.json"], cancellationToken)
                .ConfigureAwait(false);
            if (location.Available) return;
        }
    }

    private DevbookAtlasGraph Read(DevbookAtlasScope scope, string? repositoryAlias)
    {
        if (ReadFromDatabase(scope, repositoryAlias) is { } fromDatabase) return fromDatabase;

        var graphPath = ResolveGraphPath(scope, repositoryAlias);

        if (graphPath is null)
        {
            return DevbookAtlasGraph.Unavailable(scope, $"{scope.Label} is not available for this repository.");
        }

        if (!File.Exists(graphPath))
        {
            // The same words search uses, from the same place, because this is the
            // same absence. It said "run the knowledge metadata tool" until ADR 0004
            // moved the layer into the database: that tool still writes
            // `_meta/*.json`, but nothing reads them any more, so following the
            // advice would have left the atlas exactly as empty as it found it.
            return DevbookAtlasGraph.Unavailable(scope, DevbookRetrieval.UnavailableMessage("This atlas"));
        }

        return DevbookAtlasReader.Read(scope, File.ReadAllText(graphPath));
    }

    /// <summary>
    /// The atlas out of the generated database, or <see langword="null"/> when
    /// this repository has none — in which case the scoped <c>graph.json</c> beside
    /// the folders is tried next, and after that the reader says the index has not
    /// been written.
    ///
    /// <para>The graph is stored once and unprojected, so the scope is applied
    /// here: <see cref="DevbookScopeProjection"/> is the reading-side restatement
    /// of the same rule that produced the scoped JSON files, boundary nodes
    /// included. A scope with no rows — <c>instructions</c>, which is not a
    /// knowledge folder and never had a graph — answers null rather than an empty
    /// map, so the message a reader sees is still about the index rather than about
    /// the scope.</para>
    /// </summary>
    private DevbookAtlasGraph? ReadFromDatabase(DevbookAtlasScope scope, string? repositoryAlias)
    {
        using var database = OpenDatabase(repositoryAlias);
        if (database is null) return null;

        var projection = DevbookScopeProjection.Project(
            scope.FolderKey ?? DevbookDatabaseSchema.RepositoryScope,
            database.Nodes(),
            database.Edges());

        if (projection.Nodes.Count == 0) return null;

        var nodes = projection.Nodes
            .Select(node => new DevbookAtlasSourceNode(
                node.Id,
                node.Label ?? string.Empty,
                node.Folder ?? string.Empty,
                node.Type,
                node.Status ?? string.Empty,
                node.Path ?? string.Empty,
                node.OutOfScope))
            .ToList();

        var edges = projection.Edges
            .Select(edge => new DevbookAtlasSourceEdge(edge.Type, edge.Source, edge.Target))
            .ToList();

        return DevbookAtlasReader.Read(scope, nodes, edges);
    }

    /// <summary>
    /// The database for this repository, found through the same port every other
    /// knowledge read goes through: a knowledge folder resolves to a path, and the
    /// database sits at the repository root above it.
    ///
    /// <para>Every enabled folder is tried rather than one being nominated,
    /// because <c>instructions</c> resolves into <c>.github</c> — a directory
    /// deeper than the folders at the root — and going up from it lands somewhere
    /// with no database in it. That is the same reason
    /// <see cref="ResolveGraphPath"/> tries them all. The walk itself moved to
    /// <see cref="DevbookDatabaseSource"/> when retrieval became a second
    /// consumer of it: two copies of "which folder do I go up from" is exactly the
    /// drift <see cref="DevbookDatabaseLocation"/> exists to prevent one level
    /// down.</para>
    /// </summary>
    private DevbookDatabase? OpenDatabase(string? repositoryAlias) =>
        DevbookDatabaseSource.TryOpen(source, repositoryAlias);

    /// <summary>
    /// Where the scope's graph file is.
    ///
    /// <para>A folder scope resolves through the port like any other knowledge
    /// area. The whole-repository scope has no folder of its own, so it is found
    /// beside the folders: the knowledge folders sit at the repository root and
    /// the root index sits with them. Resolving through a folder rather than
    /// asking for a repository path keeps this on the port that already exists —
    /// the one that knows a folder can be turned off, or moved.</para>
    /// </summary>
    private string? ResolveGraphPath(DevbookAtlasScope scope, string? repositoryAlias)
    {
        if (scope.FolderKey is { Length: > 0 } folderKey)
        {
            var location = source.Resolve(folderKey, repositoryAlias);
            return location is { Available: true, FullPath: { Length: > 0 } path }
                ? Path.Combine(path, "_meta", "graph.json")
                : null;
        }

        // The whole-repository index sits beside the knowledge folders, so it is
        // found by going up from one of them. Which one matters: `instructions`
        // resolves into `.github`, which is a directory deeper than the folders
        // that live at the root, and going up from it lands somewhere with no
        // index in it. Rather than encode which keys are root-level, every
        // candidate is tried and the first that actually has the file wins.
        string? fallback = null;

        foreach (var setting in source.Folders(repositoryAlias))
        {
            if (!setting.Enabled) continue;

            var location = source.Resolve(setting.Key, repositoryAlias);
            if (location is not { Available: true, FullPath: { Length: > 0 } path }) continue;

            var root = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(root)) continue;

            var candidate = Path.Combine(root, "_meta", "graph.json");
            if (File.Exists(candidate)) return candidate;

            // A folder in the devbook layout sits one level deeper, under
            // .devbook/, so the repository root is the level above that.
            if (string.Equals(Path.GetFileName(root), DevbookFolderSetting.DevbookRoot, StringComparison.OrdinalIgnoreCase)
                && Path.GetDirectoryName(root) is { Length: > 0 } repositoryRoot)
            {
                var beside = Path.Combine(repositoryRoot, "_meta", "graph.json");
                if (File.Exists(beside)) return beside;
            }

            fallback ??= candidate;
        }

        // Nothing had one. Handing back a candidate rather than null is what makes
        // the reader say the index has not been generated, which is the true
        // reason, instead of saying the scope is unavailable, which is not.
        if (fallback is not null) return fallback;

        return null;
    }
}

/// <summary>
/// One reading of the devbook: a single folder, or all of it.
/// </summary>
/// <param name="Key">Stable identity, and what a host stores when it remembers a choice.</param>
/// <param name="Label">The scope's name, as a reader sees it.</param>
/// <param name="FolderKey">The knowledge folder key, or <see langword="null"/> for the whole repository.</param>
public sealed record DevbookAtlasScope(string Key, string Label, string? FolderKey)
{
    /// <summary>Every knowledge folder at once, read from the repository's own
    /// root index.</summary>
    public static DevbookAtlasScope All { get; } = new("all", "Whole devbook", null);

    /// <summary>The whole Brain, read from the Brain's own database rather than a
    /// repository's.</summary>
    public static DevbookAtlasScope Brain { get; } = new("brain", "Brain", null) { IsBrain = true };

    /// <summary>Whether this reads the Brain rather than a repository
    /// devbook.</summary>
    public bool IsBrain { get; init; }

    /// <summary>The scope for one knowledge area, named as that area is named in
    /// the section strip so the two cannot drift apart.</summary>
    public static DevbookAtlasScope ForArea(DevbookArea area) =>
        new(area.Key, area.Label, DevbookAtlasFolders.KeyFor(area.Key));
}

/// <summary>The folder key behind a section key. The section strip says
/// <c>arc42</c>; the folder settings say <c>.arc42</c>; the two have to be
/// mapped somewhere and this is the only place that needs it.</summary>
internal static class DevbookAtlasFolders
{
    public static string? KeyFor(string areaKey) => areaKey switch
    {
        "instructions" => "instructions",
        "domain" => ".domain",
        "arc42" => ".arc42",
        "tech" => ".tech",
        "design" => ".design",
        "ai" => ".ai",
        _ => null
    };
}

public sealed record DevbookAtlasNode(
    string Id,
    string Label,
    string Folder,
    string Kind,
    string Status,
    string ToneSlug,
    string Path,
    string Group,
    int GroupIndex,
    int Ordinal,
    int InDegree,
    int OutDegree,
    bool OutOfScope,
    DevbookAtlasRepositoryLink? RepositoryLink = null);

/// <summary>What a node for a repository chapter carries in the Brain atlas: the
/// link it stands for, written the way a note writes it, and whether this machine
/// can follow it.</summary>
/// <param name="Alias">The repository alias, as the first link to it wrote it.</param>
/// <param name="Raw">The link, scheme included — <c>repo:backlog/.devbook/…</c>.</param>
/// <param name="Resolved">Whether the alias is configured here and its devbook
/// shows the chapter's section.</param>
/// <param name="Problem">Why it cannot be followed, in words; null when it
/// can.</param>
public sealed record DevbookAtlasRepositoryLink(string Alias, string Raw, bool Resolved, string? Problem);

/// <param name="Source">The node the link is written on.</param>
/// <param name="Target">The node it points at.</param>
/// <param name="Kind">The edge type it was stored as.</param>
/// <param name="Label">Words drawn on the edge itself — the repository alias, on
/// a <c>repo:</c> link — or null for none.</param>
/// <param name="Dashed">Whether the edge is drawn dashed: a <c>repo:</c> link
/// this machine cannot follow.</param>
public sealed record DevbookAtlasEdge(string Source, string Target, string Kind, string? Label = null, bool Dashed = false);

public sealed record DevbookAtlasGraph(
    DevbookAtlasScope Scope,
    bool Available,
    string? Message,
    IReadOnlyList<DevbookAtlasNode> Nodes,
    IReadOnlyList<DevbookAtlasEdge> Edges)
{
    public static DevbookAtlasGraph Unavailable(DevbookAtlasScope scope, string message) =>
        new(scope, false, message, [], []);

    /// <summary>How many groups the nodes fall into — the clusters a reader sees.</summary>
    public int GroupCount => Nodes.Select(node => node.Group).Distinct(StringComparer.Ordinal).Count();
}

/// <summary>One node as either source hands it over: a row of <c>node</c>, or a
/// <c>data</c> object out of a scoped <c>graph.json</c>. Deliberately the smaller
/// of the two vocabularies — what the map draws with, not everything the graph
/// carries.</summary>
internal sealed record DevbookAtlasSourceNode(
    string Id,
    string Label,
    string Folder,
    string Type,
    string Status,
    string Path,
    bool OutOfScope);

/// <summary>One edge as either source hands it over, before the atlas decides
/// whether it is drawn.</summary>
internal sealed record DevbookAtlasSourceEdge(string Type, string Source, string Target, string? Label = null, bool Dashed = false);

internal static class DevbookAtlasReader
{
    /// <summary>
    /// The edge kinds the atlas draws.
    ///
    /// <para><c>contains</c> is deliberately not among them. It is the hierarchy —
    /// a file containing its chapters — and drawing it would put an edge between
    /// every chapter and its own file, which is most of the graph and says nothing
    /// a reader cannot already see from the clustering. It is used to work out
    /// which file a chapter belongs to and then dropped.</para>
    ///
    /// <para><c>implements</c> is not among them either, any more. It was
    /// <c>.backlog</c>'s way of saying what an item delivers, and <c>.backlog</c> is
    /// retiring: devbook contract 16 no longer draws it as an edge, and a map that
    /// did would show relationships the convention itself has stopped claiming. The
    /// field still parses — an older checkout still says it — it is only no longer
    /// drawn.</para>
    /// </summary>
    private static readonly string[] DrawnEdgeKinds = ["related", "depends-on", RepoLinkEdgeKind];

    /// <summary>The kind every <c>repo:</c> edge is drawn as in the Brain atlas,
    /// whether the note wrote the link in <c>related</c> or in prose.</summary>
    public const string RepoLinkEdgeKind = "repo-link";

    /// <summary>The kind a repository chapter's node is shown as.</summary>
    public const string RepositoryChapterKind = "repository chapter";

    /// <summary>What a repository chapter's folder is written as before it is
    /// shown, so the group it falls in is the alias as written rather than the
    /// alias titleised.</summary>
    private const string RepositoryFolderPrefix = "repo:";

    /// <summary>
    /// The Brain's atlas from its graph rows: the notes and the links between
    /// them, and one node per repository chapter a <c>repo:</c> link names, the
    /// link drawn to it as an edge labelled with the repository alias and dashed
    /// where <paramref name="resolve"/> says this machine cannot follow it.
    /// <para>
    /// The builder stores a <c>repo:</c> link as an edge with no node behind its
    /// target — <c>related</c> from a note's or chapter's <c>meta</c>,
    /// <c>repo-link</c> from its prose (local ADR 0027 §6). Here each target gets
    /// its node: one per alias and chapter, whichever layout's spelling a note
    /// used and whatever case it wrote the alias in, grouped by alias so each
    /// repository reads as one region of the map. A link that does not parse names
    /// nothing to draw it to and is dropped.
    /// </para>
    /// </summary>
    public static DevbookAtlasGraph ReadBrain(
        DevbookAtlasScope scope,
        IReadOnlyList<DevbookAtlasSourceNode> sourceNodes,
        IReadOnlyList<DevbookAtlasSourceEdge> rawEdges,
        Func<BrainRepoLink, BrainRepoLinkResolution> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);

        var nodes = new List<DevbookAtlasSourceNode>(sourceNodes);
        var edges = new List<DevbookAtlasSourceEdge>(rawEdges.Count);
        var repositoryIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // One group per repository, named as the first link to it wrote the
        // alias, so backlog/… and Backlog/… do not split it in two.
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var repositoryNodes = new Dictionary<string, DevbookAtlasRepositoryLink>(StringComparer.Ordinal);

        foreach (var edge in rawEdges)
        {
            if (!BrainRepoLink.IsRepoLink(edge.Target))
            {
                edges.Add(edge);
                continue;
            }

            if (BrainRepoLink.Parse(edge.Target) is not { } link) continue;

            // The first spelling seen names the node, and every later one is
            // pointed at that id exactly: the renderer joins edges to nodes by
            // ordinal id, so a target differing only in case would draw nothing.
            var key = RepositoryNodeId(link);
            if (!repositoryIds.TryGetValue(key, out var id))
            {
                id = key;
                repositoryIds[key] = id;

                var resolution = resolve(link);
                repositoryNodes[id] = new DevbookAtlasRepositoryLink(link.Alias, $"{BrainRepoLink.Scheme}{link.Alias}/{link.Reference}", resolution.Resolved, resolution.Problem);
                var alias = aliases.TryGetValue(link.Alias, out var seen) ? seen : aliases[link.Alias] = link.Alias;
                nodes.Add(new DevbookAtlasSourceNode(id, resolution.Label, RepositoryFolderPrefix + alias, RepositoryChapterKind, string.Empty, link.Path, true));
            }

            var repository = repositoryNodes[id];

            edges.Add(new DevbookAtlasSourceEdge(RepoLinkEdgeKind, edge.Source, id, link.Alias, !repository.Resolved));
        }

        var graph = Read(scope, nodes, edges);
        if (!graph.Available || repositoryNodes.Count == 0) return graph;

        return graph with
        {
            Nodes =
            [
                .. graph.Nodes.Select(node => repositoryNodes.TryGetValue(node.Id, out var repository)
                    // Filled where it can be followed and a hollow ring where it
                    // cannot: the same two marks a status draws, so the dashed
                    // edge is not the only thing saying it.
                    ? node with { ToneSlug = repository.Resolved ? "active" : string.Empty, RepositoryLink = repository }
                    : node)
            ]
        };
    }

    /// <summary>One id per repository chapter: the alias, the path in the root
    /// layout's spelling, and the heading.</summary>
    private static string RepositoryNodeId(BrainRepoLink link)
    {
        var path = DevbookLayout.ConventionalPath(link.Path);
        return link.Slug is null
            ? $"{BrainRepoLink.Scheme}{link.Alias}/{path}"
            : $"{BrainRepoLink.Scheme}{link.Alias}/{path}#{link.Slug}";
    }

    /// <summary>
    /// The atlas from a scoped <c>_meta/graph.json</c> — the fallback rung, for a
    /// repository that still has the committed artifacts and no database.
    /// </summary>
    public static DevbookAtlasGraph Read(DevbookAtlasScope scope, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("elements", out var elements))
        {
            return DevbookAtlasGraph.Unavailable(scope, "The devbook database has no graph in it.");
        }

        return Read(scope, ReadEntries(elements), ReadRawEdges(elements));
    }

    /// <summary>
    /// The atlas from graph rows, which is what the generated database hands back
    /// once <c>DevbookScopeProjection</c> has applied the scope.
    ///
    /// <para>The two sources meet here rather than each drawing their own map. What
    /// the map is — which edges are drawn, how nodes cluster, what a degree counts
    /// — is one set of decisions, and they were only ever expressed against the
    /// JSON because the JSON was the only source. <paramref name="rawEdges"/> arrives
    /// unfiltered, including <c>contains</c>, because the hierarchy is what decides
    /// a chapter's cluster before it is dropped.</para>
    /// </summary>
    public static DevbookAtlasGraph Read(
        DevbookAtlasScope scope,
        IReadOnlyList<DevbookAtlasSourceNode> sourceNodes,
        IReadOnlyList<DevbookAtlasSourceEdge> rawEdges)
    {
        var entries = sourceNodes
            .Select(node => new Entry(
                node.Id,
                node.Label is { Length: > 0 } label ? label : node.Id,
                node.Folder,
                node.Type is { Length: > 0 } type ? type : "chapter",
                node.Status,
                node.Path,
                node.OutOfScope))
            .ToList();

        if (entries.Count == 0)
        {
            return DevbookAtlasGraph.Unavailable(scope, "The devbook database names no documents yet.");
        }

        var edges = DrawnEdges(rawEdges, entries);
        var groups = AssignGroups(entries, scope, rawEdges);

        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var outDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in edges)
        {
            outDegree[edge.Source] = outDegree.GetValueOrDefault(edge.Source) + 1;
            inDegree[edge.Target] = inDegree.GetValueOrDefault(edge.Target) + 1;
        }

        var groupIndexes = groups.Values
            .Distinct(StringComparer.Ordinal)
            .OrderBy(group => group, StringComparer.OrdinalIgnoreCase)
            .Select((group, index) => (group, index))
            .ToDictionary(pair => pair.group, pair => pair.index, StringComparer.Ordinal);

        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        var nodes = new List<DevbookAtlasNode>(entries.Count);

        foreach (var entry in entries)
        {
            var group = groups[entry.Id];
            var ordinal = ordinals.GetValueOrDefault(group);
            ordinals[group] = ordinal + 1;

            var folder = DevbookFolders.FromPath(entry.Path.Length > 0 ? entry.Path : entry.Id);

            nodes.Add(new DevbookAtlasNode(
                entry.Id,
                entry.Label,
                FolderTitle(entry.Folder),
                entry.Type,
                entry.Status,
                // Each folder answers to its own status vocabulary, which matters
                // most in the whole-repository scope: `active` is a real status in
                // .arc42 and no status at all in .tech, and one shared list would
                // have to be wrong about one of them.
                entry.Status.Length == 0 ? string.Empty : DevbookStatus.Vocabulary(folder).SlugFor(entry.Status),
                entry.Path,
                group,
                groupIndexes[group],
                ordinal,
                inDegree.GetValueOrDefault(entry.Id),
                outDegree.GetValueOrDefault(entry.Id),
                entry.OutOfScope));
        }

        return new DevbookAtlasGraph(scope, true, null, nodes, edges);
    }

    private static List<DevbookAtlasSourceNode> ReadEntries(JsonElement elements)
    {
        var entries = new List<DevbookAtlasSourceNode>();

        if (!elements.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return entries;
        }

        foreach (var element in nodes.EnumerateArray())
        {
            if (!element.TryGetProperty("data", out var data)) continue;
            if (Text(data, "id") is not { Length: > 0 } id) continue;

            entries.Add(new DevbookAtlasSourceNode(
                id,
                Text(data, "label"),
                Text(data, "folder"),
                Text(data, "type"),
                Text(data, "status"),
                Text(data, "path"),
                data.TryGetProperty("outOfScope", out var outOfScope) && outOfScope.ValueKind == JsonValueKind.True));
        }

        return entries;
    }

    private static List<DevbookAtlasSourceEdge> ReadRawEdges(JsonElement elements)
    {
        var edges = new List<DevbookAtlasSourceEdge>();

        if (!elements.TryGetProperty("edges", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return edges;
        }

        foreach (var element in items.EnumerateArray())
        {
            if (!element.TryGetProperty("data", out var data)) continue;

            edges.Add(new DevbookAtlasSourceEdge(Text(data, "type"), Text(data, "source"), Text(data, "target")));
        }

        return edges;
    }

    private static List<DevbookAtlasEdge> DrawnEdges(IReadOnlyList<DevbookAtlasSourceEdge> items, List<Entry> entries)
    {
        // Matched ignoring case, and drawn with the node's own spelling: the
        // renderer joins an edge to its nodes by exact id, so a hand-written
        // reference differing only in case would otherwise be counted and never
        // drawn.
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) known.TryAdd(entry.Id, entry.Id);
        var edges = new List<DevbookAtlasEdge>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var kind = item.Type;
            if (!DrawnEdgeKinds.Contains(kind, StringComparer.OrdinalIgnoreCase)) continue;

            var from = item.Source;
            var to = item.Target;
            if (from.Length == 0 || to.Length == 0) continue;

            // An edge to something the index does not describe cannot be drawn:
            // there is no second end to draw it to.
            if (!known.TryGetValue(from, out var source) || !known.TryGetValue(to, out var target)) continue;

            from = source;
            to = target;

            // One line per pair, whichever way round it was written and whatever
            // kind it is. `related` is written on both chapters, so a reciprocal
            // pair arrives twice; and two documents that both relate to and depend
            // on each other would otherwise be drawn twice, on top of themselves.
            // The map says "these two are connected" — which kind of connection it
            // is, is a fact about the pair rather than a second line.
            var pair = string.CompareOrdinal(from, to) <= 0 ? from + "|" + to : to + "|" + from;
            if (!seen.Add(pair)) continue;

            edges.Add(new DevbookAtlasEdge(from, to, kind, item.Label, item.Dashed));
        }

        return edges;
    }

    /// <summary>
    /// Which cluster each node sits in.
    ///
    /// <para>Whole-repository: the folder, so the five knowledge areas read as
    /// five regions of one map.</para>
    ///
    /// <para>One folder: the directory under it when the file is in one — a
    /// bounded context in <c>.domain</c>, the ADR shelf in <c>.arc42</c> — and the
    /// document itself when it is not. That is uniform across folders shaped very
    /// differently: <c>.domain</c> is thirteen contexts of six files each, and
    /// <c>.design</c> is nine documents in a row, and both come out as a
    /// comparable handful of clusters without a rule that has to know which is
    /// which.</para>
    /// </summary>
    private static Dictionary<string, string> AssignGroups(
        List<Entry> entries,
        DevbookAtlasScope scope,
        IReadOnlyList<DevbookAtlasSourceEdge> rawEdges)
    {
        var groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (scope.FolderKey is null)
        {
            foreach (var entry in entries)
            {
                groups[entry.Id] = FolderTitle(entry.Folder);
            }

            return groups;
        }

        // A chapter's file is what its `contains` edge names. Falling back to the
        // path covers a node the generator did not connect.
        var fileOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in rawEdges)
        {
            if (!string.Equals(edge.Type, "contains", StringComparison.OrdinalIgnoreCase)) continue;
            if (edge.Source.Length > 0 && edge.Target.Length > 0) fileOf[edge.Target] = edge.Source;
        }

        var labels = entries.ToDictionary(entry => entry.Id, entry => entry.Label, StringComparer.OrdinalIgnoreCase);
        var paths = entries.ToDictionary(entry => entry.Id, entry => entry.Path, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var fileId = fileOf.GetValueOrDefault(entry.Id, entry.Id);
            // Folded to the root layout's spelling first, so the segment under
            // the folder is the group in .devbook/arc42/adr/… as in .arc42/adr/….
            var path = DevbookLayout.ConventionalPath(paths.GetValueOrDefault(fileId, entry.Path));
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            groups[entry.Id] = segments.Length > 2
                ? Titleise(segments[1])
                : labels.GetValueOrDefault(fileId, entry.Label);
        }

        return groups;
    }

    private static string Text(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>The index writes a folder as the bare word; a reader sees the
    /// section's own name.</summary>
    private static string FolderTitle(string? folder) => folder?.ToLowerInvariant() switch
    {
        // A repository chapter in the Brain atlas: the alias as the note wrote it.
        { } key when key.StartsWith(RepositoryFolderPrefix, StringComparison.Ordinal) => folder![RepositoryFolderPrefix.Length..],
        "arc42" => "Architecture",
        "domain" => "Domain",
        "design" => "Design",
        "backlog" => "Backlog",
        "tech" => "Technology",
        // Spelt out because the fallback below would print it as "Ai".
        "ai" => "AI",
        null or "" => "Elsewhere",
        _ => Titleise(folder)
    };

    private static string Titleise(string value) =>
        string.Join(' ', value
            .Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    private sealed record Entry(
        string Id,
        string Label,
        string Folder,
        string Type,
        string Status,
        string Path,
        bool OutOfScope);
}
