using Backlog.UI.Components.Diagrams;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Turns the Lobes read model into what the atlas draws
/// (<c>.devbook/domain/devbook/features.md#lobes-atlas</c>): the
/// <see cref="LobesAtlasModel"/> the canvas renderer paints, and the
/// <see cref="GraphAtlasNode"/> list <c>GraphAtlasIndex</c> lists — the same marks
/// in both, so the picture and the keyboard control never disagree about what is
/// on screen.
/// </summary>
public static class DevbookLobesAtlas
{
    /// <summary>Each Brain region's name, as written over the region and in the
    /// index.</summary>
    private static readonly IReadOnlyDictionary<DevbookLobe, string> BrainRegionNames = new Dictionary<DevbookLobe, string>
    {
        [DevbookLobe.Frontal] = "Frontal",
        [DevbookLobe.Parietal] = "Parietal",
        [DevbookLobe.Temporal] = "Temporal",
        [DevbookLobe.Occipital] = "Occipital",
        [DevbookLobe.Cerebellum] = "Cerebellum",
        [DevbookLobe.Stem] = "Brain stem",
        [DevbookLobe.Dust] = "Archive"
    };

    /// <summary>Each devbook region's name: the folder it is drawn from.</summary>
    private static readonly IReadOnlyDictionary<DevbookLobe, string> DevbookRegionNames = new Dictionary<DevbookLobe, string>
    {
        [DevbookLobe.Frontal] = "arc42",
        [DevbookLobe.Parietal] = "tech",
        [DevbookLobe.Temporal] = "domain",
        [DevbookLobe.Occipital] = "design",
        [DevbookLobe.Cerebellum] = "ai",
        [DevbookLobe.Stem] = "Index",
        [DevbookLobe.Dust] = "Outline"
    };

    /// <summary>What a ghost chapter's index row says when its link carries no
    /// reason of its own.</summary>
    public const string NotOnThisMachine = "not on this machine";

    /// <summary>The Lobes scope that shows what the Devbook pane reads: the Brain
    /// for a blank alias, else that repository's devbook — the key
    /// <see cref="DevbookLobesReader"/> gives it.</summary>
    public static string ScopeKeyFor(string? repositoryAlias) =>
        string.IsNullOrWhiteSpace(repositoryAlias)
            ? DevbookLobesGraph.BrainScopeKey
            : "repo:" + repositoryAlias.Trim().ToLowerInvariant();

    /// <summary>The name a region of this kind of scope goes by.</summary>
    public static string RegionName(DevbookLobesScopeKind kind, DevbookLobe lobe) =>
        (kind == DevbookLobesScopeKind.Brain ? BrainRegionNames : DevbookRegionNames)[lobe];

    /// <summary>The renderer's key for a region.</summary>
    public static string LobeKey(DevbookLobe lobe) => lobe switch
    {
        DevbookLobe.Frontal => LobesAtlasMark.Frontal,
        DevbookLobe.Parietal => LobesAtlasMark.Parietal,
        DevbookLobe.Temporal => LobesAtlasMark.Temporal,
        DevbookLobe.Occipital => LobesAtlasMark.Occipital,
        DevbookLobe.Cerebellum => LobesAtlasMark.Cerebellum,
        DevbookLobe.Stem => LobesAtlasMark.Stem,
        _ => LobesAtlasMark.Dust
    };

    /// <summary>
    /// The drawing of <paramref name="graph"/> in <paramref name="layout"/> —
    /// <see cref="LobesAtlasModel.AllLayout"/> or a scope key — with the scopes and
    /// regions the reader has hidden left undrawn.
    /// </summary>
    public static LobesAtlasModel Model(
        DevbookLobesGraph graph,
        string layout,
        IReadOnlyCollection<string>? hiddenScopes = null,
        IReadOnlyCollection<LobesAtlasRegionKey>? hiddenLobes = null,
        bool showLabels = true)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var scopes = graph.Scopes.Select(Scope).ToList();
        var marks = graph.Nodes.Select(Mark).ToList();
        var links = graph.Edges
            .Where(edge => edge.Kind != DevbookLobesReader.RepoLinkEdgeKind)
            .Select(edge => new LobesAtlasLink(
                edge.Source,
                edge.Target,
                edge.Kind == DevbookLobesReader.BundleEdgeKind ? LobesAtlasLink.Bundle : LobesAtlasLink.Own,
                edge.Dashed))
            .ToList();

        return new LobesAtlasModel(
            string.IsNullOrWhiteSpace(layout) ? LobesAtlasModel.AllLayout : layout,
            scopes,
            marks,
            links,
            [.. hiddenScopes ?? []],
            [.. hiddenLobes ?? []],
            showLabels,
            graph.Message ?? "The Lobes atlas has nothing to draw.");
    }

    /// <summary>
    /// The marks <see cref="Model"/> draws, as the index lists them: every mark of
    /// every scope not hidden in <see cref="LobesAtlasModel.AllLayout"/>, or of the
    /// one scope with its hidden regions left out.
    /// </summary>
    public static IReadOnlyList<GraphAtlasNode> IndexNodes(
        DevbookLobesGraph graph,
        string layout,
        IReadOnlyCollection<string>? hiddenScopes = null,
        IReadOnlyCollection<LobesAtlasRegionKey>? hiddenLobes = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var all = string.IsNullOrWhiteSpace(layout) || layout == LobesAtlasModel.AllLayout;
        var scopes = graph.Scopes.ToDictionary(scope => scope.Key, StringComparer.Ordinal);
        var hiddenScopeSet = new HashSet<string>(hiddenScopes ?? [], StringComparer.Ordinal);
        var hiddenLobeSet = new HashSet<LobesAtlasRegionKey>(hiddenLobes ?? []);

        var drawn = graph.Nodes
            .Where(node => all
                ? !hiddenScopeSet.Contains(node.ScopeKey)
                : node.ScopeKey == layout && !hiddenLobeSet.Contains(new LobesAtlasRegionKey(node.ScopeKey, LobeKey(node.Lobe))))
            .ToList();

        var ids = drawn.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var inDegree = new Dictionary<string, int>(StringComparer.Ordinal);
        var outDegree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var edge in graph.Edges)
        {
            if (edge.Kind == DevbookLobesReader.RepoLinkEdgeKind) continue;
            if (!ids.Contains(edge.Source) || !ids.Contains(edge.Target)) continue;
            outDegree[edge.Source] = outDegree.GetValueOrDefault(edge.Source) + 1;
            inDegree[edge.Target] = inDegree.GetValueOrDefault(edge.Target) + 1;
        }

        return
        [
            .. drawn.Select(node =>
            {
                var scope = scopes.GetValueOrDefault(node.ScopeKey);
                var kind = scope?.Kind ?? DevbookLobesScopeKind.Devbook;
                var hollow = node.Dashed || node.Kind == DevbookLobesNodeKind.GhostChapter;
                return new GraphAtlasNode(
                    node.Id,
                    node.Label,
                    KindName(node.Kind),
                    hollow ? node.RepositoryLink?.Problem ?? NotOnThisMachine : string.Empty,
                    hollow ? "archived" : "active",
                    $"{scope?.Label ?? node.ScopeKey} · {RegionName(kind, node.Lobe)}",
                    inDegree.GetValueOrDefault(node.Id),
                    outDegree.GetValueOrDefault(node.Id));
            })
        ];
    }

    private static LobesAtlasScope Scope(DevbookLobesScope scope)
    {
        var kind = scope.Kind switch
        {
            DevbookLobesScopeKind.Brain => LobesAtlasScope.Brain,
            DevbookLobesScopeKind.Ghost => LobesAtlasScope.Ghost,
            _ => LobesAtlasScope.Devbook
        };

        var regions = scope.LobeCounts
            .Where(pair => pair.Key != DevbookLobe.Dust && pair.Value > 0)
            .OrderBy(pair => pair.Key)
            .Select(pair => new LobesAtlasRegion(LobeKey(pair.Key), RegionName(scope.Kind, pair.Key), pair.Value))
            .ToList();

        // A devbook's own layout floats a Brain hub beside it, carrying the
        // bundles the Brain's notes send in; the words say how many notes.
        var satellite = scope.Kind != DevbookLobesScopeKind.Brain && scope.LinkingNotes > 0
            ? $"Brain · {scope.LinkingNotes} {(scope.LinkingNotes == 1 ? "note links" : "notes link")} here"
            : null;

        return new LobesAtlasScope(
            scope.Key,
            scope.Label,
            kind,
            scope.Kind == DevbookLobesScopeKind.Ghost ? null : scope.ItemCount,
            regions,
            satellite);
    }

    private static LobesAtlasMark Mark(DevbookLobesNode node) => new(
        node.Id,
        node.Label,
        node.ScopeKey,
        LobeKey(node.Lobe),
        node.Kind switch
        {
            DevbookLobesNodeKind.RepositoryHub or DevbookLobesNodeKind.IndexHub => LobesAtlasMark.Hub,
            DevbookLobesNodeKind.GhostChapter => LobesAtlasMark.GhostItem,
            // A devbook's roots sit in its stem, and the stem is drawn as hubs.
            DevbookLobesNodeKind.Chapter when node.Lobe == DevbookLobe.Stem => LobesAtlasMark.Hub,
            _ => LobesAtlasMark.Item
        },
        node.Dashed && node.Kind != DevbookLobesNodeKind.GhostChapter);

    private static string KindName(DevbookLobesNodeKind kind) => kind switch
    {
        DevbookLobesNodeKind.Note => "Note",
        DevbookLobesNodeKind.Chapter => "Chapter",
        DevbookLobesNodeKind.RepositoryHub => "Repository hub",
        DevbookLobesNodeKind.IndexHub => "Index hub",
        _ => "Ghost chapter"
    };
}
