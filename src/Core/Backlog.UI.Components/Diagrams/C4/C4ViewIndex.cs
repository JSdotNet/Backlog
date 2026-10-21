namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>
/// One line of the "C4 views" list on the architecture surface's left navigation: a
/// view of the workspace, or a component's Code level.
/// </summary>
/// <param name="Label">What the line says: "System landscape", "Containers", the
/// container a components view is of, the environment a deployment view draws.</param>
/// <param name="Detail">What it says on the right: the number of containers or
/// components the view opens, or the kind of a view that sits beside the chain —
/// "code", "dynamic", "deploy". Null when there is nothing to add.</param>
/// <param name="Depth">How far the line is indented: 0 for the views beside the chain
/// and its top, one more per level down.</param>
/// <param name="ViewKey">The view the line opens; for a Code line, the components view
/// the component is drawn on.</param>
/// <param name="CodeComponentId">The component whose Code level the line opens, or null
/// for a view.</param>
public sealed record C4ViewIndexEntry(
    string Label,
    string? Detail,
    int Depth,
    string ViewKey,
    C4ViewKind Kind,
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
/// The architecture surface's "C4 views" list (plan c4-zoom-navigation, the design's
/// left navigation): every view of the workspace once, the zoom chain first — landscape,
/// context, containers, then a components view per container and the Code level of each
/// component that has one — and after it the views beside the chain, dynamic and
/// deployment.
/// <para>
/// Pure, so the order and the counts are tested without a renderer. The counts are the
/// elements a level opens onto — a system's containers, a container's components — not
/// what the view happens to draw, which also counts the people and systems around them.
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

        foreach (var view in views.Where(view => view.Kind == C4ViewKind.SystemLandscape && !Listed(entries, view.Key)))
        {
            entries.Add(new C4ViewIndexEntry(Title(workspace, view, "System landscape"), null, 0, view.Key, view.Kind));
        }

        foreach (var context in views.Where(view => view.Kind == C4ViewKind.SystemContext))
        {
            if (Listed(entries, context.Key)) continue;

            entries.Add(new C4ViewIndexEntry(Title(workspace, context, "System context"), null, 0, context.Key, context.Kind));

            foreach (var containers in views.Where(view => view.Kind == C4ViewKind.Container && SameScope(view, context)))
            {
                AddContainers(workspace, views, containers, hasCode, entries);
            }
        }

        // A container view whose system has no context view still belongs in the chain.
        foreach (var containers in views.Where(view => view.Kind == C4ViewKind.Container
                     && !entries.Any(entry => string.Equals(entry.ViewKey, view.Key, StringComparison.OrdinalIgnoreCase))))
        {
            AddContainers(workspace, views, containers, hasCode, entries);
        }

        // And a components view of a container no container view draws.
        foreach (var components in views.Where(view => view.Kind == C4ViewKind.Component
                     && !entries.Any(entry => string.Equals(entry.ViewKey, view.Key, StringComparison.OrdinalIgnoreCase))))
        {
            AddComponents(workspace, components, 2, hasCode, entries);
        }

        foreach (var dynamic in views.Where(view => view.Kind == C4ViewKind.Dynamic && !Listed(entries, view.Key)))
        {
            entries.Add(new C4ViewIndexEntry(C4Exploration.Label(workspace, dynamic), "dynamic", 0, dynamic.Key, dynamic.Kind));
        }

        foreach (var deployment in views.Where(view => view.Kind == C4ViewKind.Deployment && !Listed(entries, view.Key)))
        {
            var label = string.IsNullOrWhiteSpace(deployment.Environment)
                ? C4Exploration.Label(workspace, deployment)
                : deployment.Environment;
            entries.Add(new C4ViewIndexEntry(label, "deploy", 0, deployment.Key, deployment.Kind));
        }

        return entries;
    }

    private static void AddContainers(
        C4Workspace workspace,
        IReadOnlyList<C4View> views,
        C4View containers,
        Func<string, bool>? hasCode,
        List<C4ViewIndexEntry> entries)
    {
        // A view is listed once, under the first system context that leads to it: two
        // context views of one system both lead to its containers.
        if (Listed(entries, containers.Key)) return;

        var count = Children(workspace, containers.ScopeId, C4ElementKind.Container).Count;
        var label = views.Count(view => view.Kind == C4ViewKind.Container) > 1 ? C4Exploration.Label(workspace, containers) : "Containers";
        entries.Add(new C4ViewIndexEntry(label, Count(count), 1, containers.Key, containers.Kind));

        foreach (var container in Children(workspace, containers.ScopeId, C4ElementKind.Container))
        {
            foreach (var components in views.Where(view => view.Kind == C4ViewKind.Component
                         && string.Equals(view.ScopeId, container.Id, StringComparison.OrdinalIgnoreCase)))
            {
                AddComponents(workspace, components, 2, hasCode, entries);
            }
        }
    }

    private static void AddComponents(
        C4Workspace workspace,
        C4View components,
        int depth,
        Func<string, bool>? hasCode,
        List<C4ViewIndexEntry> entries)
    {
        if (Listed(entries, components.Key)) return;

        var children = Children(workspace, components.ScopeId, C4ElementKind.Component);
        var sameScope = workspace.Views.Count(view => view.Kind == C4ViewKind.Component
            && string.Equals(view.ScopeId, components.ScopeId, StringComparison.OrdinalIgnoreCase));
        var label = sameScope == 1 && workspace.Element(components.ScopeId)?.Name is { } scope
            ? scope
            : C4Exploration.Label(workspace, components);
        entries.Add(new C4ViewIndexEntry(label, Count(children.Count), depth, components.Key, components.Kind));

        if (hasCode is null) return;

        foreach (var component in children.Where(component => hasCode(component.Id)))
        {
            entries.Add(new C4ViewIndexEntry(component.Name, "code", depth + 1, components.Key, components.Kind, component.Id));
        }
    }

    /// <summary>Whether a view already has its line: every view is listed once.</summary>
    private static bool Listed(List<C4ViewIndexEntry> entries, string viewKey) =>
        entries.Any(entry => entry.CodeComponentId is null && string.Equals(entry.ViewKey, viewKey, StringComparison.OrdinalIgnoreCase));

    private static List<C4Element> Children(C4Workspace workspace, string? parentId, C4ElementKind kind) =>
        parentId is null
            ? []
            : [.. workspace.Elements.Where(element => element.Kind == kind
                && string.Equals(element.ParentId, parentId, StringComparison.OrdinalIgnoreCase))];

    private static bool SameScope(C4View view, C4View other) =>
        string.Equals(view.ScopeId, other.ScopeId, StringComparison.OrdinalIgnoreCase);

    private static string? Count(int count) => count > 0 ? count.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

    /// <summary>A landscape or context view is named by its level, as the design lists
    /// it; a second one of the same level by its own title, so two read apart.</summary>
    private static string Title(C4Workspace workspace, C4View view, string level) =>
        workspace.Views.Count(other => other.Kind == view.Kind) > 1 ? C4Exploration.Label(workspace, view) : level;
}
