namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>The four levels of the C4 zoom chain, numbered the way the zoom bar
/// numbers them.</summary>
public enum C4ZoomLevel
{
    Context = 1,
    Containers = 2,
    Components = 3,
    Code = 4
}

/// <summary>
/// One step of the zoom bar.
/// </summary>
/// <param name="Scope">The element the level is about — the system for Context and
/// Containers, a container for Components, a component for Code — or null while it is
/// not known yet.</param>
/// <param name="Placeholder">What the step says in place of <paramref name="Scope"/>
/// while it is not known: "pick a container", "pick a component".</param>
/// <param name="Current">Whether the open view is this level. Ringed on the bar.</param>
/// <param name="ViewKey">The view picking the step opens, or null when there is none
/// to open — an unknown scope, a level the workspace declares no view for, or Code,
/// which is never authored.</param>
/// <param name="SelectId">The element left selected on arriving: the one the reader
/// zoomed out of, so they can see where they came from.</param>
public sealed record C4ZoomStep(
    C4ZoomLevel Level,
    string Label,
    string? Scope,
    string? Placeholder,
    bool Current,
    string? ViewKey,
    string? SelectId)
{
    /// <summary>Whether picking the step goes anywhere. A step behind the reader always
    /// does; a deeper one only once its scope is known and a view of it exists.</summary>
    public bool Enabled => !Current && ViewKey is not null;

    /// <summary>A deeper step still waiting on a selection: drawn grey.</summary>
    public bool Waiting => Scope is null;
}

/// <summary>A supplementary view offered beside the bar under <c>Also</c>: Landscape,
/// Dynamic or Deployment. Not a numbered step, because none of them is a level of the
/// chain.</summary>
public sealed record C4ZoomAlso(string Label, C4ViewKind Kind, string ViewKey, bool Current);

/// <summary>Where a zoom out lands: the view, and the element to leave selected on
/// it.</summary>
public sealed record C4ZoomTarget(string ViewKey, string? SelectId);

/// <summary>Everything the zoom bar draws for one view and selection.</summary>
public sealed record C4ZoomBarState(IReadOnlyList<C4ZoomStep> Steps, IReadOnlyList<C4ZoomAlso> Also)
{
    public static C4ZoomBarState Empty { get; } = new([], []);
}

/// <summary>
/// The navigation of the C4 zoom tab: which view it opens on, what each zoom bar step
/// is about, and where zooming in and out goes.
/// <para>
/// Pure, and derived from the workspace each time, by the same rule as
/// <see cref="C4Exploration"/>: a second copy of where the reader is would be a second
/// thing to be wrong. The tab holds only the open view and the selected element; every
/// step, scope and target is read off those two.
/// </para>
/// <para>
/// It walks one level at a time — System context, Containers, Components, Code — where
/// the C4 tab's drill goes straight from a system to its containers. Landscape sits
/// above level 1 and Dynamic and Deployment beside level 2, which is why they are
/// offered under <c>Also</c> and never as a step.
/// </para>
/// </summary>
public static class C4ZoomNavigation
{
    /// <summary>What a deeper step reads while its element is not known.</summary>
    public const string PickSystem = "pick a system";
    public const string PickContainer = "pick a container";
    public const string PickComponent = "pick a component";

    /// <summary>The view the tab opens on: the first System context view, in the
    /// level order the C4 tab lists views in, or the first view of any kind when the
    /// workspace declares no context view.</summary>
    public static C4View? StartView(C4Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var ordered = C4Exploration.OrderedViews(workspace);
        return ordered.FirstOrDefault(view => view.Kind == C4ViewKind.SystemContext) ?? ordered.FirstOrDefault();
    }

    /// <summary>The level a view is, or null for a view beside the chain.</summary>
    public static C4ZoomLevel? LevelOf(C4View view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return view.Kind switch
        {
            C4ViewKind.SystemContext => C4ZoomLevel.Context,
            C4ViewKind.Container => C4ZoomLevel.Containers,
            C4ViewKind.Component => C4ZoomLevel.Components,
            _ => null
        };
    }

    /// <summary>
    /// The zoom bar for a view and the element selected on it.
    /// </summary>
    public static C4ZoomBarState Bar(C4Workspace workspace, C4View view, string? selectedId)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        var level = LevelOf(view);
        var selected = workspace.Element(selectedId);

        var system = SystemOf(workspace, view, selected);
        var container = ContainerOf(workspace, view, selected, system);
        var component = selected is { Kind: C4ElementKind.Component }
            && (container is null || string.Equals(selected.ParentId, container.Id, StringComparison.OrdinalIgnoreCase))
                ? selected
                : null;

        var contextView = system is null ? null : ContextViewOf(workspace, system.Id);
        var containersView = system is null ? null : C4Exploration.DrillViewKey(workspace, system.Id);
        var componentsView = container is null ? null : C4Exploration.DrillViewKey(workspace, container.Id);

        C4ZoomStep[] steps =
        [
            Step(C4ZoomLevel.Context, "Context", system, PickSystem, level, contextView?.Key, system?.Id),
            Step(C4ZoomLevel.Containers, "Containers", system, PickSystem, level, containersView, container?.Id),
            Step(C4ZoomLevel.Components, "Components", container, PickContainer, level, componentsView, component?.Id),

            // Code is generated from the source, never authored, so no view of the
            // workspace opens it. Until the code view exists the step names the
            // component and stays grey.
            Step(C4ZoomLevel.Code, "Code", component, PickComponent, level, null, null)
        ];

        return new C4ZoomBarState(steps, AlsoFor(workspace, view, system));
    }

    private static C4ZoomStep Step(
        C4ZoomLevel level,
        string label,
        C4Element? scope,
        string placeholder,
        C4ZoomLevel? current,
        string? viewKey,
        string? selectId)
    {
        var isCurrent = current == level;

        return new C4ZoomStep(
            level,
            label,
            scope?.Name,
            scope is null ? placeholder : null,
            isCurrent,
            viewKey,
            selectId);
    }

    /// <summary>
    /// Landscape, Dynamic and Deployment, each once and only when the workspace has
    /// one. A kind with several views offers the one about the system in view, else the
    /// first in key order.
    /// </summary>
    private static IReadOnlyList<C4ZoomAlso> AlsoFor(C4Workspace workspace, C4View view, C4Element? system)
    {
        var also = new List<C4ZoomAlso>();

        foreach (var (kind, label) in new[]
                 {
                     (C4ViewKind.SystemLandscape, "Landscape"),
                     (C4ViewKind.Dynamic, "Dynamic"),
                     (C4ViewKind.Deployment, "Deployment")
                 })
        {
            var candidates = C4Exploration.OrderedViews(workspace).Where(candidate => candidate.Kind == kind).ToList();
            if (candidates.Count == 0) continue;

            var pick = candidates.FirstOrDefault(candidate => candidate.Kind == view.Kind
                           && string.Equals(candidate.Key, view.Key, StringComparison.OrdinalIgnoreCase))
                       ?? candidates.FirstOrDefault(candidate => system is not null
                           && string.Equals(candidate.ScopeId, system.Id, StringComparison.OrdinalIgnoreCase))
                       ?? candidates[0];

            also.Add(new C4ZoomAlso(label, kind, pick.Key, view.Kind == kind));
        }

        return also;
    }

    /// <summary>
    /// Where zooming into an element goes, or null when there is nothing deeper.
    /// <para>
    /// From a landscape a system opens its context view, which is level 1; everywhere
    /// else it is the C4 tab's own drill — a system's containers, a container's
    /// components — so the two tabs never disagree about what is below a box.
    /// </para>
    /// </summary>
    public static string? ZoomInView(C4Workspace workspace, C4View view, string elementId)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        if (view.Kind == C4ViewKind.SystemLandscape
            && workspace.Element(elementId) is { Kind: C4ElementKind.SoftwareSystem } system
            && ContextViewOf(workspace, system.Id) is { } context)
        {
            return context.Key;
        }

        return C4Exploration.DrillViewKey(workspace, elementId, view.Key);
    }

    /// <summary>
    /// Where zooming out of a view goes, and what it leaves selected, or null at the
    /// top.
    /// <para>
    /// One level up, with the scope of the view being left selected on the view it
    /// opens: out of a container's components onto the containers with that container
    /// selected, out of the containers onto the context with the system selected, out
    /// of the context onto the landscape. Dynamic and Deployment sit beside the
    /// containers, so zooming out of either opens them.
    /// </para>
    /// </summary>
    public static C4ZoomTarget? ZoomOut(C4Workspace workspace, C4View view)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        var scope = workspace.Element(view.ScopeId);

        switch (view.Kind)
        {
            case C4ViewKind.Component when scope is not null:
            {
                var system = workspace.Ancestry(scope.Id).FirstOrDefault(element => element.Kind == C4ElementKind.SoftwareSystem);
                var containers = system is null ? null : C4Exploration.DrillViewKey(workspace, system.Id);
                return containers is null ? null : new C4ZoomTarget(containers, scope.Id);
            }

            case C4ViewKind.Container when scope is not null:
                return ContextViewOf(workspace, scope.Id) is { } context ? new C4ZoomTarget(context.Key, scope.Id) : null;

            case C4ViewKind.SystemContext:
                return Landscape(workspace) is { } landscape ? new C4ZoomTarget(landscape.Key, scope?.Id) : null;

            case C4ViewKind.Dynamic or C4ViewKind.Deployment when scope is not null:
            {
                var system = workspace.Ancestry(scope.Id).FirstOrDefault(element => element.Kind == C4ElementKind.SoftwareSystem);
                var containers = system is null ? null : C4Exploration.DrillViewKey(workspace, system.Id);
                return containers is null ? null : new C4ZoomTarget(containers, null);
            }

            default:
                return null;
        }
    }

    private static C4View? Landscape(C4Workspace workspace) =>
        C4Exploration.OrderedViews(workspace).FirstOrDefault(candidate => candidate.Kind == C4ViewKind.SystemLandscape);

    private static C4View? ContextViewOf(C4Workspace workspace, string systemId) =>
        C4Exploration.OrderedViews(workspace).FirstOrDefault(candidate =>
            candidate.Kind == C4ViewKind.SystemContext
            && string.Equals(candidate.ScopeId, systemId, StringComparison.OrdinalIgnoreCase));

    /// <summary>The system the open view is about, or on a landscape the system the
    /// selected element belongs to.</summary>
    private static C4Element? SystemOf(C4Workspace workspace, C4View view, C4Element? selected)
    {
        var from = view.Kind == C4ViewKind.SystemLandscape ? selected?.Id : view.ScopeId ?? selected?.Id;

        return workspace.Ancestry(from).FirstOrDefault(element => element.Kind == C4ElementKind.SoftwareSystem);
    }

    /// <summary>The container level 3 is about: the scope of a component view, else the
    /// container the selection is or sits in, when it belongs to the system in view.</summary>
    private static C4Element? ContainerOf(C4Workspace workspace, C4View view, C4Element? selected, C4Element? system)
    {
        if (view.Kind == C4ViewKind.Component)
        {
            return workspace.Ancestry(view.ScopeId).FirstOrDefault(element => element.Kind == C4ElementKind.Container);
        }

        if (selected is null || system is null) return null;

        var container = workspace.Ancestry(selected.Id).FirstOrDefault(element => element.Kind == C4ElementKind.Container);

        return container is not null && string.Equals(container.ParentId, system.Id, StringComparison.OrdinalIgnoreCase)
            ? container
            : null;
    }
}
