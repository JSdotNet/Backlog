using System.Text;

namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>
/// Writes one view of a workspace as mermaid C4.
/// <para>
/// Mermaid rather than a renderer of this feature's own, because the app already
/// draws mermaid — including the two <c>C4Context</c> and <c>C4Container</c> fences
/// the arc42 chapters carry today. A C4 view therefore reaches the screen through
/// exactly the path a chapter diagram does, and <see cref="DiagramView"/> needs no
/// parameter it does not already have.
/// </para>
/// <para>
/// What this writes is never saved. It is not a fence, it is not committed, and no
/// chapter gains a generated block — the <c>.dsl</c> stays the only authored
/// source, and the repository's rule that mermaid fences are canonical is left
/// alone because none of this is a fence.
/// </para>
/// </summary>
public static class C4MermaidWriter
{
    private const string Indent = "    ";

    /// <summary>
    /// The mermaid header each view kind is drawn under.
    /// <para>
    /// A system landscape draws as <c>C4Context</c>: mermaid has no landscape
    /// header, and a landscape is a context view of the whole model rather than of
    /// one system, so the picture is right even though the keyword is borrowed.
    /// </para>
    /// </summary>
    private static string Header(C4ViewKind kind) => kind switch
    {
        C4ViewKind.SystemLandscape => "C4Context",
        C4ViewKind.SystemContext => "C4Context",
        C4ViewKind.Container => "C4Container",
        C4ViewKind.Component => "C4Component",
        C4ViewKind.Dynamic => "C4Dynamic",
        C4ViewKind.Deployment => "C4Deployment",
        _ => "C4Context"
    };

    public static string Write(C4Workspace workspace, C4View view)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        var visible = Visible(workspace, view);
        var builder = new StringBuilder();

        builder.Append(Header(view.Kind)).Append('\n');
        builder.Append(Indent).Append("title ").Append(OneLine(Title(workspace, view))).Append('\n');

        if (visible.Count == 0)
        {
            // Said in the picture rather than left as an empty frame. A view whose
            // elements all resolved to nothing is a real state — an `include` naming
            // identifiers that do not exist — and an empty diagram reads as a
            // rendering failure.
            builder.Append('\n').Append(Indent).Append("System(empty, \"Nothing to draw\", \"This view selected no elements.\")").Append('\n');
            return builder.ToString();
        }

        builder.Append('\n');

        // Roots are the visible elements nothing visible contains. Everything else
        // is reached through its parent, so a system that has containers in the view
        // is drawn once, as the boundary around them.
        var roots = visible.Values
            .Where(element => element.ParentId is null || !visible.ContainsKey(element.ParentId))
            .ToList();

        foreach (var root in Ordered(roots))
        {
            WriteElement(builder, workspace, view, visible, root, 1);
        }

        builder.Append('\n');
        foreach (var line in Relationships(workspace, view, visible))
        {
            builder.Append(Indent).Append(line).Append('\n');
        }

        foreach (var line in Styles(workspace, visible))
        {
            builder.Append(Indent).Append(line).Append('\n');
        }

        return builder.ToString();
    }

    // ---- which elements a view shows ------------------------------------------

    /// <summary>
    /// The edges this view draws, with both ends resolved to something it actually
    /// contains.
    /// <para>
    /// Public for the same reason <see cref="VisibleElements"/> is: the first-party
    /// renderer draws the same view and has to agree with this one about which arrows
    /// exist. Two implementations of the roll-up would disagree about exactly the
    /// edges that needed rolling.
    /// </para>
    /// </summary>
    public static IReadOnlyList<C4VisibleRelationship> VisibleRelationships(C4Workspace workspace, C4View view)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        var visible = Visible(workspace, view);
        var edges = new List<C4VisibleRelationship>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (view.Kind is C4ViewKind.Dynamic)
        {
            foreach (var step in view.Steps.OrderBy(step => step.Order))
            {
                var from = RollUp(workspace, step.SourceId, visible);
                var to = RollUp(workspace, step.DestinationId, visible);
                if (from is null || to is null) continue;

                edges.Add(new C4VisibleRelationship(from, to, step.Description, null, step.Order));
            }

            return edges;
        }

        foreach (var relationship in workspace.Relationships)
        {
            var from = RollUp(workspace, relationship.SourceId, visible);
            var to = RollUp(workspace, relationship.DestinationId, visible);

            if (from is null || to is null) continue;
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) continue;
            if (!seen.Add($"{from}|{to}|{relationship.Description}|{relationship.Technology}")) continue;

            edges.Add(new C4VisibleRelationship(from, to, relationship.Description, relationship.Technology, null));
        }

        return edges;
    }

    /// <summary>
    /// The elements this view draws, in no particular order.
    /// <para>
    /// Public because the explorer has to index exactly what the picture contains —
    /// which node is clickable, which one a Highlighter facet dims — and computing
    /// that a second time is how the index and the diagram come to disagree about
    /// what is on screen.
    /// </para>
    /// </summary>
    public static IReadOnlyCollection<C4Element> VisibleElements(C4Workspace workspace, C4View view)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        return Visible(workspace, view).Values;
    }

    /// <summary>
    /// The elements this view draws, keyed by identifier.
    /// <para>
    /// <c>include *</c> does not mean every element in the model — it means the
    /// default set for this kind of view, which is what makes a container view of
    /// one system not a drawing of all of them. So the default set is computed
    /// first, from the view's kind and scope, and the view's own
    /// <c>include</c>/<c>exclude</c> statements are applied to it.
    /// </para>
    /// </summary>
    private static Dictionary<string, C4Element> Visible(C4Workspace workspace, C4View view)
    {
        var selected = new Dictionary<string, C4Element>(StringComparer.OrdinalIgnoreCase);

        void Take(C4Element? element)
        {
            if (element is not null) selected[element.Id] = element;
        }

        // A boundary is only drawn because something inside it is, so every element
        // taken brings its chain of parents with it.
        void TakeWithAncestors(C4Element? element)
        {
            foreach (var step in workspace.Ancestry(element?.Id)) Take(step);
        }

        var scope = workspace.Element(view.ScopeId);

        switch (view.Kind)
        {
            case C4ViewKind.SystemLandscape:
                foreach (var element in workspace.Elements)
                {
                    if (element.ParentId is null && element.Kind is C4ElementKind.Person or C4ElementKind.SoftwareSystem) Take(element);
                }

                break;

            case C4ViewKind.SystemContext:
                Take(scope);
                foreach (var neighbour in Neighbours(workspace, workspace.Subtree(scope?.Id))) TakeWithAncestors(neighbour);
                break;

            case C4ViewKind.Container:
                Take(scope);
                foreach (var child in workspace.Children(scope?.Id)) Take(child);
                foreach (var neighbour in Neighbours(workspace, workspace.Subtree(scope?.Id))) TakeWithAncestors(TopOf(workspace, neighbour, scope?.Id));
                break;

            case C4ViewKind.Component:
                TakeWithAncestors(scope);
                foreach (var child in workspace.Children(scope?.Id)) Take(child);
                foreach (var neighbour in Neighbours(workspace, workspace.Subtree(scope?.Id))) TakeWithAncestors(neighbour);
                break;

            case C4ViewKind.Dynamic:
                foreach (var step in view.Steps)
                {
                    TakeWithAncestors(workspace.Element(step.SourceId));
                    TakeWithAncestors(workspace.Element(step.DestinationId));
                }

                break;

            case C4ViewKind.Deployment:
                foreach (var element in workspace.Elements)
                {
                    if (!IsDeployment(element.Kind)) continue;
                    if (view.Environment is not null && !string.Equals(element.Environment, view.Environment, StringComparison.OrdinalIgnoreCase)) continue;
                    Take(element);
                }

                break;
        }

        foreach (var include in view.Includes) TakeWithAncestors(workspace.Element(include));

        foreach (var exclude in view.Excludes)
        {
            if (workspace.Element(exclude) is { } element) selected.Remove(element.Id);
        }

        return selected;
    }

    /// <summary>Everything related to any element of this set, from outside it.</summary>
    private static IEnumerable<C4Element> Neighbours(C4Workspace workspace, IEnumerable<C4Element> inside)
    {
        var ids = inside.Select(element => element.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var relationship in workspace.Relationships)
        {
            var sourceInside = ids.Contains(relationship.SourceId);
            var destinationInside = ids.Contains(relationship.DestinationId);
            if (sourceInside == destinationInside) continue;

            if (workspace.Element(sourceInside ? relationship.DestinationId : relationship.SourceId) is { } outside)
            {
                yield return outside;
            }
        }
    }

    /// <summary>
    /// The outermost element this one sits in, stopping short of the view's own
    /// scope. What a container view uses to draw another system as one box rather
    /// than opening it up to show the container that happened to be on the far end
    /// of a relationship.
    /// </summary>
    private static C4Element? TopOf(C4Workspace workspace, C4Element? element, string? scopeId)
    {
        C4Element? top = null;
        foreach (var step in workspace.Ancestry(element?.Id))
        {
            if (string.Equals(step.Id, scopeId, StringComparison.OrdinalIgnoreCase)) break;
            top = step;
        }

        return top ?? element;
    }

    private static bool IsDeployment(C4ElementKind kind) =>
        kind is C4ElementKind.DeploymentNode
            or C4ElementKind.InfrastructureNode
            or C4ElementKind.ContainerInstance
            or C4ElementKind.SoftwareSystemInstance;

    // ---- emission --------------------------------------------------------------

    private static void WriteElement(
        StringBuilder builder,
        C4Workspace workspace,
        C4View view,
        Dictionary<string, C4Element> visible,
        C4Element element,
        int depth)
    {
        var pad = string.Concat(Enumerable.Repeat(Indent, depth));
        var children = Ordered(visible.Values.Where(candidate =>
            string.Equals(candidate.ParentId, element.Id, StringComparison.OrdinalIgnoreCase)).ToList());

        var drawn = Drawn(workspace, element);
        var external = IsExternal(workspace, view, element);

        if (children.Count == 0)
        {
            builder.Append(pad).Append(Leaf(drawn, external, view.Kind)).Append('\n');
            return;
        }

        builder.Append(pad).Append(Boundary(drawn)).Append(" {").Append('\n');
        foreach (var child in children) WriteElement(builder, workspace, view, visible, child, depth + 1);
        builder.Append(pad).Append('}').Append('\n');
    }

    /// <summary>
    /// A deployment instance is drawn as the thing it instantiates. The DSL says
    /// <c>containerInstance api</c> and means "the API container runs here", so
    /// drawing a box labelled with the identifier would name the reference rather
    /// than the container.
    /// </summary>
    private static C4Element Drawn(C4Workspace workspace, C4Element element)
    {
        if (element.InstanceOfId is null) return element;
        if (workspace.Element(element.InstanceOfId) is not { } target) return element;

        return element with
        {
            Name = target.Name,
            Description = target.Description,
            Technology = target.Technology,
            Tags = target.Tags
        };
    }

    private static string Leaf(C4Element element, bool external, C4ViewKind kind)
    {
        var alias = Alias(element.Id);
        var suffix = external ? "_Ext" : string.Empty;

        return element.Kind switch
        {
            C4ElementKind.Person =>
                $"Person{suffix}({alias}, {Quote(element.Name)}, {Quote(element.Description)})",

            C4ElementKind.SoftwareSystem or C4ElementKind.SoftwareSystemInstance =>
                $"System{(element.HasTag("Database") ? "Db" : string.Empty)}{suffix}({alias}, {Quote(element.Name)}, {Quote(element.Description)})",

            C4ElementKind.Container or C4ElementKind.ContainerInstance =>
                $"Container{Shape(element)}{suffix}({alias}, {Quote(element.Name)}, {Quote(element.Technology)}, {Quote(element.Description)})",

            C4ElementKind.Component =>
                $"Component{Shape(element)}{suffix}({alias}, {Quote(element.Name)}, {Quote(element.Technology)}, {Quote(element.Description)})",

            // An empty deployment node still has to be a node: it is where something
            // is meant to run, and drawing it as a container would say it is one.
            C4ElementKind.DeploymentNode =>
                $"Deployment_Node({alias}, {Quote(element.Name)}, {Quote(element.Technology)}, {Quote(element.Description)})",

            // Mermaid has no infrastructure node. A container is the closest shape
            // it offers, and the technology carries what the box actually is.
            C4ElementKind.InfrastructureNode =>
                $"Container({alias}, {Quote(element.Name)}, {Quote(element.Technology)}, {Quote(element.Description)})",

            _ => $"System({alias}, {Quote(element.Name)}, {Quote(element.Description)})"
        };
    }

    private static string Boundary(C4Element element) => element.Kind switch
    {
        C4ElementKind.SoftwareSystem => $"System_Boundary({Alias(element.Id)}, {Quote(element.Name)})",
        C4ElementKind.Container => $"Container_Boundary({Alias(element.Id)}, {Quote(element.Name)})",
        C4ElementKind.DeploymentNode =>
            $"Deployment_Node({Alias(element.Id)}, {Quote(element.Name)}, {Quote(element.Technology)}, {Quote(element.Description)})",
        _ => $"Boundary({Alias(element.Id)}, {Quote(element.Name)})"
    };

    private static string Shape(C4Element element) =>
        element.HasTag("Database") ? "Db" : element.HasTag("Queue") || element.HasTag("Message Broker") ? "Queue" : string.Empty;

    /// <summary>
    /// Whether the element sits outside what the view is a view of.
    /// <para>
    /// A landscape and a deployment view have no inside and outside — a landscape is
    /// of everything, and a deployment view is of one environment — so nothing in
    /// them is external. Everywhere else, external means "not within the scope's own
    /// tree", which is what earns the <c>_Ext</c> shape.
    /// </para>
    /// </summary>
    private static bool IsExternal(C4Workspace workspace, C4View view, C4Element element)
    {
        if (view.Kind is C4ViewKind.SystemLandscape or C4ViewKind.Deployment) return false;
        if (view.ScopeId is null) return false;

        return !workspace.Ancestry(element.Id)
            .Any(step => string.Equals(step.Id, view.ScopeId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The edges the view draws.
    /// <para>
    /// An endpoint the view does not draw is rolled up to the nearest ancestor it
    /// does. Without that, a context view would show almost no edges at all: the
    /// relationships in a model are mostly between containers, and a context view
    /// draws systems. Rolling two endpoints up to the same box leaves a
    /// relationship from a thing to itself, which is dropped rather than drawn as a
    /// loop.
    /// </para>
    /// </summary>
    private static IEnumerable<string> Relationships(C4Workspace workspace, C4View view, Dictionary<string, C4Element> visible)
    {
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (view.Kind is C4ViewKind.Dynamic)
        {
            foreach (var step in view.Steps.OrderBy(step => step.Order))
            {
                var source = RollUp(workspace, step.SourceId, visible);
                var destination = RollUp(workspace, step.DestinationId, visible);
                if (source is null || destination is null) continue;

                yield return $"Rel({Alias(source)}, {Alias(destination)}, {Quote(step.Description)})";
            }

            yield break;
        }

        foreach (var relationship in workspace.Relationships)
        {
            var source = RollUp(workspace, relationship.SourceId, visible);
            var destination = RollUp(workspace, relationship.DestinationId, visible);

            if (source is null || destination is null) continue;
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) continue;

            var line = relationship.Technology is null
                ? $"Rel({Alias(source)}, {Alias(destination)}, {Quote(relationship.Description)})"
                : $"Rel({Alias(source)}, {Alias(destination)}, {Quote(relationship.Description)}, {Quote(relationship.Technology)})";

            if (written.Add(line)) yield return line;
        }
    }

    /// <summary>
    /// The nearest drawn shape at or above this element, or null.
    /// <para>
    /// A boundary is skipped rather than used. Mermaid will not accept a <c>Rel</c>
    /// whose endpoint is a boundary — it reports "references an unknown shape" and
    /// refuses the <em>whole</em> diagram, not just that one line — so an endpoint
    /// that rolls up to one has nowhere to land and the relationship is dropped.
    /// </para>
    /// <para>
    /// Dropping it is also the faithful answer. On a component view the container is
    /// the frame, and "ME uses the Desktop App" is a fact about the container rather
    /// than about anything inside it; the model does not say which component receives
    /// that, and this writer must not invent one. The relationship is on the container
    /// view, where the container is a shape.
    /// </para>
    /// </summary>
    private static string? RollUp(C4Workspace workspace, string id, Dictionary<string, C4Element> visible)
    {
        foreach (var step in workspace.Ancestry(id))
        {
            if (visible.ContainsKey(step.Id) && !IsBoundary(visible, step.Id)) return step.Id;
        }

        return null;
    }

    /// <summary>Whether this element is drawn as a boundary: something else the view
    /// draws sits inside it. The same test <see cref="WriteElement"/> makes when it
    /// chooses between a boundary and a leaf, so the two cannot disagree about which
    /// shape a box got.</summary>
    private static bool IsBoundary(Dictionary<string, C4Element> visible, string id) =>
        visible.Values.Any(candidate => string.Equals(candidate.ParentId, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The author's own colours, where mermaid can take them.
    /// <para>
    /// Only for tags that name a colour and only for elements actually drawn, so a
    /// workspace with a full Structurizr theme does not produce a wall of style
    /// calls for boxes nobody is looking at. Shapes are not translated: mermaid C4
    /// picks a shape from the macro, and a cylinder is <c>ContainerDb</c> rather
    /// than a property.
    /// </para>
    /// </summary>
    private static IEnumerable<string> Styles(C4Workspace workspace, Dictionary<string, C4Element> visible)
    {
        if (workspace.ElementStyles is not { Count: > 0 } styles) yield break;

        foreach (var element in Ordered([.. visible.Values]))
        {
            var style = styles.LastOrDefault(candidate =>
                element.HasTag(candidate.Tag) && (candidate.Background is not null || candidate.Color is not null));

            if (style is null) continue;

            // The values are quoted. Mermaid's C4 lexer rejects a bare `#7a7a7a`
            // outright — "Unrecognized text" — and it rejects the whole diagram, not
            // just the style call, so one unquoted colour costs the picture. Unit
            // tests cannot see this: they compare emitted text, and text that mermaid
            // refuses looks exactly like text it accepts.
            var parts = new List<string> { Alias(element.Id) };
            if (style.Background is not null) parts.Add($"$bgColor={Quote(style.Background)}");
            if (style.Color is not null) parts.Add($"$fontColor={Quote(style.Color)}");

            yield return $"UpdateElementStyle({string.Join(", ", parts)})";
        }
    }

    // ---- text ------------------------------------------------------------------

    /// <summary>
    /// Declaration order, with boundaries before leaves at the same level.
    /// <para>
    /// Declaration order rather than alphabetical, because the order elements are
    /// written in a mermaid C4 source is the order they are laid out in, and the
    /// author's order in the DSL is the closest thing to an intended arrangement
    /// this reader has — c4hero's own layout sidecar is not read.
    /// </para>
    /// </summary>
    private static List<C4Element> Ordered(List<C4Element> elements) =>
        [.. elements.OrderBy(element => element.Kind is C4ElementKind.Person ? 0 : 1)];

    private static string Title(C4Workspace workspace, C4View view)
    {
        if (!string.IsNullOrWhiteSpace(view.Title)) return view.Title;

        var scope = workspace.Element(view.ScopeId)?.Name ?? workspace.Name;
        var kind = view.Kind switch
        {
            C4ViewKind.SystemLandscape => "System Landscape",
            C4ViewKind.SystemContext => "System Context",
            C4ViewKind.Container => "Container",
            C4ViewKind.Component => "Component",
            C4ViewKind.Dynamic => "Dynamic",
            C4ViewKind.Deployment => "Deployment",
            _ => "C4"
        };

        return scope is null ? $"{kind} Diagram" : $"{kind} Diagram — {scope}";
    }

    /// <summary>
    /// A mermaid alias: letters, digits and underscores only.
    /// <para>
    /// Hierarchical identifiers are dotted, and a dot in a mermaid C4 alias is a
    /// parse error. The <c>e_</c> prefix keeps an identifier that begins with a
    /// digit from becoming one.
    /// </para>
    /// </summary>
    /// <summary>
    /// A mermaid alias: letters, digits and underscores only.
    /// <para>
    /// Public for the same reason <see cref="VisibleElements"/> is. Mermaid puts this
    /// alias in the rendered node's DOM id — <c>&lt;svg id&gt;-backlog_desktop</c> —
    /// and that is the only thread tying a clicked shape back to a model element. The
    /// explorer must compute it with this function rather than its own, or a click
    /// lands on nothing for exactly the identifiers that needed sanitising.
    /// </para>
    /// </summary>
    public static string AliasOf(string id) => Alias(id);

    private static string Alias(string id)
    {
        var builder = new StringBuilder(id.Length + 2);
        foreach (var character in id)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) ? character : '_');
        }

        var alias = builder.ToString();
        return alias.Length == 0 || !char.IsAsciiLetter(alias[0]) ? "e_" + alias : alias;
    }

    /// <summary>
    /// A quoted mermaid argument.
    /// <para>
    /// A double quote inside one closes the argument, and mermaid C4 has no escape
    /// for it, so an inner quote becomes a single one — the label reads the same and
    /// the diagram still parses. Commas are left alone: mermaid splits a macro's
    /// arguments on commas outside quotes only.
    /// </para>
    /// </summary>
    private static string Quote(string? value) =>
        "\"" + OneLine(value).Replace("\"", "'", StringComparison.Ordinal) + "\"";

    private static string OneLine(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}

/// <summary>One edge a view draws, after both ends have been rolled up to something
/// the view contains.</summary>
/// <param name="Order">The step number on a dynamic view, or null elsewhere.</param>
public sealed record C4VisibleRelationship(
    string FromId,
    string ToId,
    string? Description,
    string? Technology,
    int? Order);
