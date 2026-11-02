using Backlog.Desktop.UI.Devbook;
using Backlog.Modules.Brain.Abstractions;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Modules.Brain.UI;

/// <summary>What the Lobes read takes from the Brain's database.</summary>
/// <param name="Nodes">The <c>node</c> rows.</param>
/// <param name="Edges">The <c>edge</c> rows, <c>repo:</c> links included.</param>
/// <param name="Para">Each node's <c>para</c> attribute: its area, or what its
/// <c>para</c> field says instead.</param>
/// <param name="Areas">The area folders the index holds, in order.</param>
/// <param name="Declared">The areas the Brain declares, so an area is known by
/// its name whatever folder holds it.</param>
/// <param name="IndexedAt">When the Brain's database was built.</param>
internal sealed record BrainLobesNotes(
    IReadOnlyList<DevbookAtlasSourceNode> Nodes,
    IReadOnlyList<DevbookAtlasSourceEdge> Edges,
    IReadOnlyDictionary<string, string> Para,
    IReadOnlyList<string> Areas,
    IReadOnlyList<BrainArea>? Declared = null,
    DateTimeOffset? IndexedAt = null);

/// <summary>
/// Puts the Brain and the registered devbooks into one Lobes graph
/// (<c>.devbook/domain/brain/features.md#lobes-atlas</c>). The Brain conforms to
/// Devbook's Lobes read model (local ADR 0040 §2): its notes take their region from
/// their PARA area and their <c>repo:</c> links reach into the devbooks, while each
/// devbook's own chapters, ids and summaries are Devbook's
/// (<see cref="DevbookLobesReader"/>). Pure: the atlas opens the databases and
/// hands their rows in.
/// </summary>
internal static class BrainLobesReader
{
    /// <summary>The label of the Brain stem's hub for <c>CONVENTIONS.md</c>.</summary>
    public const string ConventionsHubLabel = BrainConventions.FileName;

    private const char Bar = '|';

    public static DevbookLobesGraph Read(
        BrainLobesNotes brain,
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
            if (!DevbookLobesReader.IsDrawn(note) || !notes.TryAdd(note.Id, note)) continue;

            // A chapter of a note carries a para only when it states one, so it
            // takes its note's otherwise.
            var area = para.GetValueOrDefault(note.Id)
                       ?? para.GetValueOrDefault(note.Path)
                       ?? note.Folder;
            Add(new DevbookLobesNode(DevbookLobesReader.Id(brainKey, note.Id), DevbookLobesReader.Label(note), brainKey, BrainLobe(area, declared), DevbookLobesNodeKind.Note, note.Path));
        }

        Add(new DevbookLobesNode(DevbookLobesReader.Id(brainKey, "index:" + BrainConventions.FileName), ConventionsHubLabel, brainKey, DevbookLobe.Stem, DevbookLobesNodeKind.IndexHub, string.Empty));
        foreach (var area in brain.Areas.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var name = declared.FirstOrDefault(declaration => string.Equals(declaration.Folder, area, StringComparison.OrdinalIgnoreCase))?.Name
                       ?? BrainConventions.NameFor(area);
            Add(new DevbookLobesNode(DevbookLobesReader.Id(brainKey, "index:" + area), name, brainKey, DevbookLobe.Stem, DevbookLobesNodeKind.IndexHub, string.Empty));
        }

        DevbookLobesReader.AddOwnEdges(brainKey, brain.Edges, notes, edges, pairs);

        // ----- The registered devbooks -----
        var scopes = new List<(string Key, string Label, DevbookLobesScopeKind Kind, string? Alias, bool Available, string? Message, DateTimeOffset? IndexedAt)>
        {
            (brainKey, BrainConventions.ScopeLabel, DevbookLobesScopeKind.Brain, null, true, null, brain.IndexedAt)
        };
        var registered = DevbookLobesReader.AddDevbooks(devbooks, Add, edges, pairs, scopes);

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
                if (DevbookLobesReader.Find(devbook.Chapters, chapterKey) is { } found)
                {
                    target = found;
                    dashed = !resolution.Resolved;
                }
                else
                {
                    target = DevbookLobesReader.GhostId(scopeKey, chapterKey);
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
                target = DevbookLobesReader.GhostId(scopeKey, chapterKey);
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
                        ? DevbookLobesReader.NotIndexedYet
                        : resolution.Problem ?? BrainRepoLinkResolver.NotADevbookChapter;
                var repositoryLink = new DevbookAtlasRepositoryLink(link.Alias, raw, !dashed, dashed ? problem : null);
                if (indexes.TryGetValue(target, out var index))
                {
                    nodes[index] = nodes[index] with { RepositoryLink = repositoryLink };
                }
                else
                {
                    Add(new DevbookLobesNode(target, resolution.Label, scopeKey, DevbookLobesReader.FolderLobe(DevbookLobesReader.FolderOf(link.Path)), DevbookLobesNodeKind.GhostChapter, link.Path, true, repositoryLink));
                }
            }

            if (!hubs.TryGetValue(scopeKey, out var hub))
            {
                hub = DevbookLobesReader.Id(brainKey, "hub:" + scopeKey);
                hubs[scopeKey] = hub;
                var hubLabel = scopes.First(scope => scope.Key == scopeKey).Label;
                Add(new DevbookLobesNode(hub, hubLabel, brainKey, DevbookLobe.Occipital, DevbookLobesNodeKind.RepositoryHub, string.Empty, ghosts.ContainsValue(scopeKey)));
            }

            var from = DevbookLobesReader.Id(brainKey, note.Id);
            if (pairs.Add(from + Bar + target)) edges.Add(new DevbookLobesEdge(from, target, DevbookLobesReader.RepoLinkEdgeKind, dashed, link.Alias));
            if (pairs.Add(hub + Bar + target)) edges.Add(new DevbookLobesEdge(hub, target, DevbookLobesReader.BundleEdgeKind, dashed));

            var notePath = note.Path.Length > 0 ? note.Path : note.Id;
            (linking.TryGetValue(scopeKey, out var set) ? set : linking[scopeKey] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(notePath);
            (linking.TryGetValue(brainKey, out var all) ? all : linking[brainKey] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(notePath);
        }

        return new DevbookLobesGraph(true, null, DevbookLobesReader.Summaries(scopes, nodes, linking), nodes, edges);
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
}
