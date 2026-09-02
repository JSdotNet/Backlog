namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>
/// One drawn node, as the explorer needs to know it.
/// <para>
/// <paramref name="Alias"/> is the thread back from the picture to the model.
/// Mermaid puts it in the rendered shape's DOM id, so a click on a box, or a
/// Highlighter dimming a box, both resolve through this and nothing else. It comes
/// from <see cref="C4MermaidWriter.AliasOf"/> rather than being derived again here,
/// because two implementations of that sanitising would disagree exactly where it
/// mattered.
/// </para>
/// </summary>
/// <param name="DrillViewKey">The view that opens this element, or null when there
/// is nothing deeper to see. A system with a container view drills into it; a
/// container with a component view drills into that; a component is a leaf.</param>
public sealed record C4Node(
    string Alias,
    string ElementId,
    string Name,
    C4ElementKind Kind,
    string? Description,
    string? Technology,
    IReadOnlyList<string> Tags,
    string? Owner,
    string? Status,
    string? DrillViewKey);

/// <summary>One value a Highlighter facet can be set to, and how many drawn nodes
/// carry it.</summary>
public sealed record C4FacetValue(string Value, int Count);

/// <summary>
/// The four things the Highlighter filters on, with the values actually present in
/// the workspace.
/// <para>
/// Derived from the model rather than configured, so a facet with nothing in it is
/// absent instead of being an empty dropdown. A workspace that never states an
/// owner gets no Team facet at all.
/// </para>
/// </summary>
public sealed record C4Facets(
    IReadOnlyList<C4FacetValue> Tags,
    IReadOnlyList<C4FacetValue> Technologies,
    IReadOnlyList<C4FacetValue> Owners,
    IReadOnlyList<C4FacetValue> Statuses)
{
    public static C4Facets Empty { get; } = new([], [], [], []);

    public bool Any => Tags.Count > 0 || Technologies.Count > 0 || Owners.Count > 0 || Statuses.Count > 0;
}

/// <summary>
/// One search hit: what matched, where it can be seen, and why it matched.
/// </summary>
/// <param name="ViewKey">A view that draws this element, so the hit can be jumped
/// to. Null for a hit on a view itself, where <paramref name="Alias"/> is null and
/// the view is the destination.</param>
public sealed record C4SearchHit(
    string Label,
    string Detail,
    string? Alias,
    string? ViewKey,
    C4SearchHitKind Kind);

public enum C4SearchHitKind
{
    Element,
    View
}

/// <summary>
/// What the explorer needs that is not the picture: where a node drills to, which
/// facet values exist, and what a search matches.
/// <para>
/// All of it derived from the workspace and none of it stored, for the same reason
/// the chapter references are derived: a second copy is a second thing to be wrong.
/// It is separate from <see cref="C4MermaidWriter"/> because the writer answers
/// "what does this view look like" and this answers "what can I do from here" —
/// and only the second one changes when the exploration affordances change.
/// </para>
/// </summary>
public static class C4Exploration
{
    /// <summary>The four C4 levels, in the order a reader descends them. What the
    /// Views panel groups by.</summary>
    public static IReadOnlyList<C4ViewKind> Levels { get; } =
    [
        C4ViewKind.SystemLandscape,
        C4ViewKind.SystemContext,
        C4ViewKind.Container,
        C4ViewKind.Component,
        C4ViewKind.Dynamic,
        C4ViewKind.Deployment
    ];

    /// <summary>How a reader says a view kind.</summary>
    public static string LevelLabel(C4ViewKind kind) => kind switch
    {
        C4ViewKind.SystemLandscape => "System landscape",
        C4ViewKind.SystemContext => "System context",
        C4ViewKind.Container => "Container",
        C4ViewKind.Component => "Component",
        C4ViewKind.Dynamic => "Dynamic",
        C4ViewKind.Deployment => "Deployment",
        _ => "C4"
    };

    /// <summary>The views of a workspace in level order, which is the order the
    /// Views panel lists them in: the big picture first, then down.</summary>
    public static IReadOnlyList<C4View> OrderedViews(C4Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        return
        [
            .. workspace.Views
                .OrderBy(LevelOrder)
                .ThenBy(view => view.Key, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>Where a view sits in the descent. A kind the list does not know sorts
    /// last rather than first, so an unrecognised view never displaces the
    /// landscape.</summary>
    private static int LevelOrder(C4View view)
    {
        for (var index = 0; index < Levels.Count; index++)
        {
            if (Levels[index] == view.Kind) return index;
        }

        return Levels.Count;
    }

    /// <summary>
    /// Every node the view draws, with what it drills into.
    /// <para>
    /// Read off <see cref="C4MermaidWriter.VisibleElements"/> so the index and the
    /// picture cannot disagree about what is on screen — an index built from its own
    /// idea of the view would offer a drill-in on a box nobody can see.
    /// </para>
    /// </summary>
    public static IReadOnlyList<C4Node> Nodes(C4Workspace workspace, C4View view)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        var nodes = new List<C4Node>();

        foreach (var element in C4MermaidWriter.VisibleElements(workspace, view))
        {
            // A deployment instance is drawn as the thing it instantiates, so it is
            // searched, filtered and drilled as that thing too. Otherwise the box
            // labelled "API" would answer to the instance's synthesised identifier.
            var described = element.InstanceOfId is not null && workspace.Element(element.InstanceOfId) is { } target
                ? target
                : element;

            nodes.Add(new C4Node(
                C4MermaidWriter.AliasOf(element.Id),
                described.Id,
                described.Name,
                described.Kind,
                described.Description,
                described.Technology,
                described.Tags,
                described.Owner,
                described.Status,
                DrillViewKey(workspace, described.Id, view.Key)));
        }

        return nodes;
    }

    /// <summary>
    /// The view that opens this element, or null.
    /// <para>
    /// A view of the element itself, one level down: the container view scoped to a
    /// software system, the component view scoped to a container. The view the reader
    /// is already on is never offered, because drilling into the picture you are
    /// looking at is not a move.
    /// </para>
    /// <para>
    /// Only a view that actually exists is offered. c4hero picks a level and frames
    /// it; this reader cannot invent a view the workspace does not declare, and
    /// offering a drill-in that opens nothing would be worse than offering none —
    /// which is the same rule the Archify affordance follows.
    /// </para>
    /// </summary>
    public static string? DrillViewKey(C4Workspace workspace, string elementId, string? fromViewKey = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (workspace.Element(elementId) is not { } element) return null;

        var wanted = element.Kind switch
        {
            C4ElementKind.SoftwareSystem => C4ViewKind.Container,
            C4ElementKind.Container => C4ViewKind.Component,
            _ => (C4ViewKind?)null
        };

        if (wanted is null) return null;

        foreach (var view in workspace.Views)
        {
            if (view.Kind != wanted) continue;
            if (!string.Equals(view.ScopeId, element.Id, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(view.Key, fromViewKey, StringComparison.OrdinalIgnoreCase)) continue;

            return view.Key;
        }

        return null;
    }

    /// <summary>
    /// The path from the top of the model down to this view, for the breadcrumb.
    /// <para>
    /// Built from the scope's own ancestry rather than from where the reader has
    /// been. A trail of visited views would say how they arrived; this says where
    /// they are, which is what stops a breadcrumb becoming a second, worse Back
    /// button.
    /// </para>
    /// </summary>
    public static IReadOnlyList<C4View> Trail(C4Workspace workspace, C4View view)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        var trail = new List<C4View>();

        // The landscape is the root of every trail when the workspace has one: it is
        // the only view that is of the whole model.
        if (workspace.Views.FirstOrDefault(candidate => candidate.Kind == C4ViewKind.SystemLandscape) is { } landscape
            && !string.Equals(landscape.Key, view.Key, StringComparison.OrdinalIgnoreCase))
        {
            trail.Add(landscape);
        }

        // Ancestry runs element-upward, so it is reversed into reading order, and the
        // view's own scope is left to the last entry — which is the view itself.
        var ancestors = workspace.Ancestry(view.ScopeId).Skip(1).Reverse();

        foreach (var ancestor in ancestors)
        {
            // The step for an ancestor is the view you would *drill into* from it —
            // the same rule the drill affordance uses, so the trail retraces the way
            // down rather than describing it differently.
            //
            // Which matters: a system has both a context view and a container view,
            // and only the container view draws the container the reader descended
            // through. Taking whichever came first in the file put the context view on
            // the path, which is a sibling detour and not a step on it.
            var opens = workspace.View(DrillViewKey(workspace, ancestor.Id))
                ?? workspace.Views.FirstOrDefault(candidate =>
                    string.Equals(candidate.ScopeId, ancestor.Id, StringComparison.OrdinalIgnoreCase)
                    && candidate.Kind == C4ViewKind.SystemContext);

            if (opens is not null && !trail.Any(existing => string.Equals(existing.Key, opens.Key, StringComparison.OrdinalIgnoreCase)))
            {
                trail.Add(opens);
            }
        }

        trail.Add(view);
        return trail;
    }

    /// <summary>What a view is called on screen.</summary>
    public static string Label(C4Workspace workspace, C4View view)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        if (!string.IsNullOrWhiteSpace(view.Title)) return view.Title;
        if (!string.IsNullOrWhiteSpace(view.Description)) return view.Description;

        var scope = workspace.Element(view.ScopeId)?.Name;
        return scope is null ? LevelLabel(view.Kind) : $"{LevelLabel(view.Kind)} — {scope}";
    }

    // ---- highlighter -----------------------------------------------------------

    /// <summary>
    /// The facet values present on the cards a view actually draws.
    /// <para>
    /// Per view, not per workspace. Offering every tag and technology in the model was
    /// the first version and it was wrong in the way that matters: a Highlighter is a
    /// question about the picture in front of you, and most of a workspace-wide list
    /// matches nothing on it — so almost every chip dimmed the whole diagram. A filter
    /// that is mostly dead options is not a filter, it is a catalogue.
    /// </para>
    /// <para>
    /// The cost is that the chips change as the reader drills, which is the right
    /// trade: they change because the picture changed, and every chip that remains
    /// does something.
    /// </para>
    /// </summary>
    public static C4Facets Facets(C4Workspace workspace, C4View view)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        return Facets(Nodes(workspace, view));
    }

    /// <summary>The same, over cards already indexed — what the explorer uses, since
    /// it is holding them anyway.</summary>
    public static C4Facets Facets(IReadOnlyList<C4Node> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var tags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var technologies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var owners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var statuses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        static void Count(Dictionary<string, int> into, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            into[value] = into.TryGetValue(value, out var seen) ? seen + 1 : 1;
        }

        foreach (var node in nodes)
        {
            foreach (var tag in node.Tags) Count(tags, tag);
            Count(technologies, node.Technology);
            Count(owners, node.Owner);
            Count(statuses, node.Status);
        }

        return new C4Facets(Ordered(tags), Ordered(technologies), Ordered(owners), Ordered(statuses));

        static IReadOnlyList<C4FacetValue> Ordered(Dictionary<string, int> counts) =>
        [
            .. counts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new C4FacetValue(pair.Key, pair.Value))
        ];
    }

    /// <summary>
    /// Whether a node matches the selected facet values.
    /// <para>
    /// Across facets it is AND and within a facet it is OR, which is what makes the
    /// Highlighter answer a real question: "the containers owned by either of these
    /// two teams, that are also .NET". A facet with nothing selected constrains
    /// nothing.
    /// </para>
    /// </summary>
    public static bool Matches(
        C4Node node,
        IReadOnlyCollection<string>? tags,
        IReadOnlyCollection<string>? technologies,
        IReadOnlyCollection<string>? owners,
        IReadOnlyCollection<string>? statuses)
    {
        ArgumentNullException.ThrowIfNull(node);

        return Facet(tags, node.Tags)
            && Facet(technologies, node.Technology is null ? [] : [node.Technology])
            && Facet(owners, node.Owner is null ? [] : [node.Owner])
            && Facet(statuses, node.Status is null ? [] : [node.Status]);

        static bool Facet(IReadOnlyCollection<string>? selected, IReadOnlyList<string> has) =>
            selected is null || selected.Count == 0
            || has.Any(value => selected.Any(pick => string.Equals(pick, value, StringComparison.OrdinalIgnoreCase)));
    }

    // ---- search ----------------------------------------------------------------

    /// <summary>
    /// Elements and views matching a query, across the four things c4hero searches:
    /// element names, descriptions, technologies, and view titles.
    /// </summary>
    /// <param name="limit">How many hits to hand back. A search box shows a list, not
    /// a corpus.</param>
    public static IReadOnlyList<C4SearchHit> Search(C4Workspace workspace, string? query, int limit = 12)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2) return [];

        var needle = query.Trim();
        var hits = new List<(int Rank, C4SearchHit Hit)>();

        foreach (var element in workspace.Elements)
        {
            // An instance is drawn as what it instantiates, so it is not a second hit
            // for the same box.
            if (element.InstanceOfId is not null) continue;

            var rank = Rank(element.Name, needle) is var name and > 0 ? name
                : Rank(element.Technology, needle) is var tech and > 0 ? tech + 10
                : Rank(element.Description, needle) is var descr and > 0 ? descr + 20
                : 0;

            if (rank == 0) continue;

            var view = ViewShowing(workspace, element.Id);

            hits.Add((rank, new C4SearchHit(
                element.Name,
                Detail(element),
                C4MermaidWriter.AliasOf(element.Id),
                view?.Key,
                C4SearchHitKind.Element)));
        }

        foreach (var view in workspace.Views)
        {
            var label = Label(workspace, view);
            var rank = Rank(label, needle);
            if (rank == 0) continue;

            hits.Add((rank + 5, new C4SearchHit(label, LevelLabel(view.Kind), null, view.Key, C4SearchHitKind.View)));
        }

        return
        [
            .. hits
                .OrderBy(entry => entry.Rank)
                .ThenBy(entry => entry.Hit.Label, StringComparer.OrdinalIgnoreCase)
                .Select(entry => entry.Hit)
                .Take(limit)
        ];
    }

    /// <summary>Lower is better: a prefix beats a word start, which beats a match
    /// anywhere. Zero is no match.</summary>
    private static int Rank(string? haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack)) return 0;

        var at = haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return 0;
        if (at == 0) return 1;

        return char.IsWhiteSpace(haystack[at - 1]) ? 2 : 3;
    }

    private static string Detail(C4Element element)
    {
        var parts = new List<string> { LevelLabel(KindAsLevel(element.Kind)) };
        if (!string.IsNullOrWhiteSpace(element.Technology)) parts.Add(element.Technology);

        return string.Join(" · ", parts);
    }

    private static C4ViewKind KindAsLevel(C4ElementKind kind) => kind switch
    {
        C4ElementKind.Container or C4ElementKind.ContainerInstance => C4ViewKind.Container,
        C4ElementKind.Component => C4ViewKind.Component,
        C4ElementKind.DeploymentNode or C4ElementKind.InfrastructureNode => C4ViewKind.Deployment,
        _ => C4ViewKind.SystemContext
    };

    /// <summary>
    /// A view that draws this element, preferring the shallowest one.
    /// <para>
    /// Preferring shallow because a search hit is a way in, not a destination: landing
    /// on the context view with the box highlighted leaves the reader somewhere they
    /// can drill from, where landing three levels down leaves them somewhere they have
    /// to climb out of.
    /// </para>
    /// </summary>
    public static C4View? ViewShowing(C4Workspace workspace, string elementId)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        foreach (var view in OrderedViews(workspace))
        {
            if (C4MermaidWriter.VisibleElements(workspace, view)
                .Any(element => string.Equals(element.Id, elementId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(element.InstanceOfId, elementId, StringComparison.OrdinalIgnoreCase)))
            {
                return view;
            }
        }

        return null;
    }
}
