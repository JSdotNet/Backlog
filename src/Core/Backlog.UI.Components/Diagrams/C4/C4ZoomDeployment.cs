namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>
/// One deployment environment of a scope, as the zoom tab's environment tab list and
/// its doors offer it.
/// </summary>
/// <param name="Name">The <c>deploymentEnvironment</c> name — "Local machine",
/// "Azure".</param>
/// <param name="ViewKey">The deployment view that draws it.</param>
/// <param name="Current">Whether that view is the one open now.</param>
/// <param name="Runs">The names of the containers of the scope deployed there, each
/// once and in declaration order: what a door says it leads to, counted the way the
/// inspector counts what a node runs.</param>
public sealed record C4DeploymentEnvironment(
    string Name,
    string ViewKey,
    bool Current,
    IReadOnlyList<string> Runs);

/// <summary>
/// One container instance a deployment node runs: the container it is an instance of,
/// and where the zoom tab shows that container.
/// </summary>
/// <param name="ContainerId">The container, by its model identifier. Left selected on
/// arriving at <paramref name="ContainersViewKey"/>.</param>
/// <param name="ContainersViewKey">The container view of the system the container
/// belongs to, or null when the workspace declares none, in which case the instance is
/// listed and links nowhere.</param>
public sealed record C4DeploymentInstance(
    string ContainerId,
    string Name,
    string? Technology,
    string? ContainersViewKey);

/// <summary>What the inspector says about one selected deployment node.</summary>
public sealed record C4DeploymentNodeDetails(
    string ElementId,
    string Name,
    string Kind,
    string? Description,
    string? Technology,
    string? Environment,
    IReadOnlyList<C4DeploymentInstance> Runs);

/// <summary>
/// The deployment views of the C4 zoom tab: which environments a deployment view can
/// switch between, and what a selected deployment node runs.
/// <para>
/// Pure and derived from the workspace each time, by the rule
/// <see cref="C4ZoomNavigation"/> follows: the tab holds the open view and the
/// selection, and everything here is read off those two.
/// </para>
/// </summary>
public static class C4ZoomDeployment
{
    /// <summary>
    /// The environments a deployment view switches between: one per environment among
    /// the deployment views of the same scope, in the order the workspace declares
    /// them — the order its author put the environments in, the canonical one first as
    /// a rule — the open one marked current. Empty for any other kind of view.
    /// <para>
    /// An environment drawn by more than one view offers the open one when it is open,
    /// else the first: a tab is an environment, not a view.
    /// </para>
    /// </summary>
    public static IReadOnlyList<C4DeploymentEnvironment> Environments(C4Workspace workspace, C4View view)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        if (view.Kind != C4ViewKind.Deployment) return [];

        var environments = new List<C4DeploymentEnvironment>();

        var siblings = workspace.Views
            .Where(candidate => candidate.Kind == C4ViewKind.Deployment
                && string.Equals(candidate.ScopeId, view.ScopeId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(candidate => candidate.Environment ?? candidate.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in siblings)
        {
            var open = group.FirstOrDefault(candidate => string.Equals(candidate.Key, view.Key, StringComparison.OrdinalIgnoreCase));
            var pick = open ?? group.First();

            environments.Add(new C4DeploymentEnvironment(
                NameOf(pick),
                pick.Key,
                open is not null,
                RunsIn(workspace, pick.Environment, view.ScopeId)));
        }

        return environments;
    }

    /// <summary>
    /// What the inspector shows for a deployment node or an infrastructure node, or
    /// null for any other element.
    /// <para>
    /// The instances are every container instance anywhere beneath the node — the
    /// Windows PC runs what its MSIX package runs — because a reader asking what a
    /// machine runs means all of it.
    /// </para>
    /// </summary>
    public static C4DeploymentNodeDetails? Details(C4Workspace workspace, string? elementId)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (workspace.Element(elementId) is not { Kind: C4ElementKind.DeploymentNode or C4ElementKind.InfrastructureNode } node) return null;

        var runs = new List<C4DeploymentInstance>();

        foreach (var instance in workspace.Subtree(node.Id).Where(element => element.Kind == C4ElementKind.ContainerInstance))
        {
            if (workspace.Element(instance.InstanceOfId) is not { } container) continue;
            if (runs.Any(existing => string.Equals(existing.ContainerId, container.Id, StringComparison.OrdinalIgnoreCase))) continue;

            var system = workspace.Ancestry(container.Id).FirstOrDefault(element => element.Kind == C4ElementKind.SoftwareSystem);

            runs.Add(new C4DeploymentInstance(
                container.Id,
                container.Name,
                container.Technology,
                system is null ? null : C4Exploration.DrillViewKey(workspace, system.Id)));
        }

        return new C4DeploymentNodeDetails(
            node.Id,
            node.Name,
            C4LayoutEngine.KindLabel(node.Kind),
            Blank(node.Description),
            Blank(node.Technology),
            node.Environment,
            runs);
    }

    /// <summary>
    /// The aliases of the deployment nodes the open view draws as frames, which the
    /// zoom tab asks <see cref="C4SvgWriter"/> to make selectable. A deployment node
    /// that runs nothing is already a card and is selectable as one.
    /// </summary>
    public static IReadOnlySet<string> SelectableFrames(C4Diagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);

        return diagram.Boundaries
            .Where(boundary => string.Equals(boundary.Kind, C4LayoutEngine.KindLabel(C4ElementKind.DeploymentNode), StringComparison.Ordinal))
            .Select(boundary => C4MermaidWriter.AliasOf(boundary.Id))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The environment a deployment view draws, named the way a reader
    /// would: the view's own environment, else its title, else its key. Only the name:
    /// views are grouped by the environment itself, so a title that happens to match
    /// another environment's name never merges the two.</summary>
    private static string NameOf(C4View view) =>
        !string.IsNullOrWhiteSpace(view.Environment) ? view.Environment
        : !string.IsNullOrWhiteSpace(view.Title) ? view.Title
        : view.Key;

    private static IReadOnlyList<string> RunsIn(C4Workspace workspace, string? environment, string? scopeId)
    {
        if (string.IsNullOrWhiteSpace(environment)) return [];

        return
        [
            .. workspace.Elements
                .Where(element => element.Kind == C4ElementKind.ContainerInstance
                    && string.Equals(element.Environment, environment, StringComparison.OrdinalIgnoreCase))
                .Select(element => workspace.Element(element.InstanceOfId))
                .OfType<C4Element>()
                .Where(container => scopeId is null
                    || workspace.Ancestry(container.Id).Any(ancestor => string.Equals(ancestor.Id, scopeId, StringComparison.OrdinalIgnoreCase)))
                .Select(container => container.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
