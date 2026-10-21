namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>Which group of the architecture surface's views list a line sits in.</summary>
public enum C4ViewIndexGroup
{
    /// <summary>The landscape and the zoom chain: context, containers, components, code.</summary>
    Model,

    /// <summary>Dynamic views: a flow told step by step.</summary>
    Flows,

    /// <summary>Deployment views, one per environment.</summary>
    Deployment,

    /// <summary>A second view of a level the chain already has a view of — another
    /// context or containers view of the same system. Listed by its own title, apart
    /// from the chain, so the chain reads one line per level.</summary>
    Other
}

/// <summary>
/// One line of the views list on the architecture surface's left navigation: a view
/// of the workspace, or a component's Code level.
/// </summary>
/// <param name="Label">What the line says: short and by level — "System context",
/// "Containers", the container a components view is of, the component a Code level is
/// of, the environment a deployment view draws.</param>
/// <param name="Title">The view's full title, for the line's tooltip. The main area's
/// heading says it too.</param>
/// <param name="Detail">The right-hand column: how many containers or components the
/// level opens onto, or "code". Null when there is nothing to add.</param>
/// <param name="Depth">How far the line is indented within its group: one step per
/// level of the chain.</param>
/// <param name="ViewKey">The view the line opens; for a Code line, the components view
/// the component is drawn on.</param>
/// <param name="CodeComponentId">The component whose Code level the line opens, or null
/// for a view.</param>
public sealed record C4ViewIndexEntry(
    string Label,
    string Title,
    string? Detail,
    int Depth,
    string ViewKey,
    C4ViewKind Kind,
    C4ViewIndexGroup Group,
    string? CodeComponentId = null)
{
    /// <summary>Whether the line is open now: its view with no Code level open, or its
    /// component's Code level.</summary>
    public bool IsOpen(string? viewKey, string? codeComponentId) =>
        CodeComponentId is null
            ? codeComponentId is null && string.Equals(ViewKey, viewKey, StringComparison.OrdinalIgnoreCase)
            : string.Equals(CodeComponentId, codeComponentId, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The architecture surface's views list (plan c4-zoom-navigation, the design's left
/// navigation): the landscape and the zoom chain — a system's context, its containers,
/// a components view per container and the Code level of each component that has one,
/// one line per level and indented one step per level — then the flows, the
/// deployments, and last any second view of a level the chain already shows.
/// <para>
/// The chain takes, per level and scope, the view the zoom bar opens — the first in
/// level order — so the line and the bar never disagree about which view "Containers"
/// is. A second view of the same level and scope is still listed, under Other views by
/// its own title: it is a view somebody authored, and dropping it would hide it.
/// </para>
/// <para>
/// Pure, so the order, the names and the counts are tested without a renderer. The
/// counts are the elements a level opens onto — a system's containers, a container's
/// components — not what the view happens to draw.
/// </para>
/// </summary>
public static class C4ViewIndex
{
    /// <param name="hasCode">Whether a component has a Code level to open; null — a host
    /// with no source to read — lists none.</param>
    public static IReadOnlyList<C4ViewIndexEntry> For(C4Workspace workspace, Func<string, bool>? hasCode = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var views = C4Exploration.OrderedViews(workspace);
        var entries = new List<C4ViewIndexEntry>();
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(C4ViewIndexEntry entry)
        {
            entries.Add(entry);
            if (entry.CodeComponentId is null) listed.Add(entry.ViewKey);
        }

        // The chain's view of a level: the first of that kind and scope.
        C4View? Chain(C4ViewKind kind, string? scope) =>
            views.FirstOrDefault(view => view.Kind == kind && string.Equals(view.ScopeId, scope, StringComparison.OrdinalIgnoreCase));

        if (views.FirstOrDefault(view => view.Kind == C4ViewKind.SystemLandscape) is { } landscape)
        {
            Add(new C4ViewIndexEntry("System landscape", Title(workspace, landscape), null, 0, landscape.Key, landscape.Kind, C4ViewIndexGroup.Model));
        }

        // One chain per system that has a context or containers view, in the order the
        // views name them.
        var systems = views
            .Where(view => view.Kind is C4ViewKind.SystemContext or C4ViewKind.Container && view.ScopeId is not null)
            .Select(view => view.ScopeId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var system in systems)
        {
            var depth = 0;
            if (Chain(C4ViewKind.SystemContext, system) is { } context)
            {
                var label = systems.Count > 1 ? workspace.Element(system)?.Name ?? "System context" : "System context";
                Add(new C4ViewIndexEntry(label, Title(workspace, context), null, 0, context.Key, context.Kind, C4ViewIndexGroup.Model));
                depth = 1;
            }

            if (Chain(C4ViewKind.Container, system) is not { } containers) continue;

            var children = Children(workspace, system, C4ElementKind.Container);
            Add(new C4ViewIndexEntry("Containers", Title(workspace, containers), Count(children.Count), depth, containers.Key, containers.Kind, C4ViewIndexGroup.Model));

            foreach (var container in children)
            {
                if (Chain(C4ViewKind.Component, container.Id) is { } components)
                {
                    AddComponents(workspace, components, container.Name, depth + 1, hasCode, Add);
                }
            }
        }

        // A components view of a container no containers view leads to.
        foreach (var components in views.Where(view => view.Kind == C4ViewKind.Component && !listed.Contains(view.Key)))
        {
            if (!ReferenceEquals(Chain(C4ViewKind.Component, components.ScopeId), components)) continue;

            var name = workspace.Element(components.ScopeId)?.Name ?? Title(workspace, components);
            AddComponents(workspace, components, name, 0, hasCode, Add);
        }

        // Flows and deployments in the order the workspace declares them: an author
        // lists the canonical environment first.
        foreach (var dynamic in workspace.Views.Where(view => view.Kind == C4ViewKind.Dynamic))
        {
            var title = Title(workspace, dynamic);
            Add(new C4ViewIndexEntry(title, title, null, 0, dynamic.Key, dynamic.Kind, C4ViewIndexGroup.Flows));
        }

        foreach (var deployment in workspace.Views.Where(view => view.Kind == C4ViewKind.Deployment))
        {
            var title = Title(workspace, deployment);
            var label = string.IsNullOrWhiteSpace(deployment.Environment) ? title : deployment.Environment;
            Add(new C4ViewIndexEntry(label, title, null, 0, deployment.Key, deployment.Kind, C4ViewIndexGroup.Deployment));
        }

        // Every view not yet listed: a second view of a level the chain shows.
        foreach (var other in views.Where(view => !listed.Contains(view.Key)))
        {
            var title = Title(workspace, other);
            Add(new C4ViewIndexEntry(title, title, null, 0, other.Key, other.Kind, C4ViewIndexGroup.Other));
        }

        return entries;
    }

    private static void AddComponents(
        C4Workspace workspace,
        C4View components,
        string label,
        int depth,
        Func<string, bool>? hasCode,
        Action<C4ViewIndexEntry> add)
    {
        var children = Children(workspace, components.ScopeId, C4ElementKind.Component);
        add(new C4ViewIndexEntry(label, Title(workspace, components), Count(children.Count), depth, components.Key, components.Kind, C4ViewIndexGroup.Model));

        if (hasCode is null) return;

        foreach (var component in children.Where(component => hasCode(component.Id)))
        {
            add(new C4ViewIndexEntry(component.Name, $"Code — {component.Name}", "code", depth + 1, components.Key, components.Kind, C4ViewIndexGroup.Model, component.Id));
        }
    }

    private static List<C4Element> Children(C4Workspace workspace, string? parentId, C4ElementKind kind) =>
        parentId is null
            ? []
            : [.. workspace.Elements.Where(element => element.Kind == kind
                && string.Equals(element.ParentId, parentId, StringComparison.OrdinalIgnoreCase))];

    private static string? Count(int count) => count > 0 ? count.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

    private static string Title(C4Workspace workspace, C4View view) => C4Exploration.Label(workspace, view);
}
