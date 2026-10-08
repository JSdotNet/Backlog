namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>One mermaid C4 fence, and the chapter it was written in.</summary>
/// <param name="Origin">The chapter's path, spelled the way the chapter catalog
/// spells it. Carried because a derived view documents the chapter its fence came
/// from, and because a problem that does not say which chapter it is in sends the
/// reader looking through all of them.</param>
/// <param name="Text">The fence's body, without the backtick lines.</param>
public sealed record C4MermaidSource(string Origin, string Text);

/// <summary>A workspace read out of fences, with the chapter each view was drawn
/// in. The workspace alone cannot say that — a <see cref="C4View"/> has no field
/// for where it came from, and the host needs it to tie each view back to its
/// chapter.</summary>
public sealed record C4MermaidReading(C4Workspace Workspace, IReadOnlyDictionary<string, string> ViewOrigins);

/// <summary>
/// Reads the C4 fences the chapters already carry into one workspace.
/// <para>
/// The fallback for a folder nobody has written a <c>.dsl</c> for. A chapter's
/// <c>C4Context</c> fence is already a model — it names a person, a system, the
/// systems around it and how they talk — and a repository that has only that should
/// still be able to explore it rather than being offered nothing until somebody
/// authors a workspace in c4hero.
/// </para>
/// <para>
/// Each fence becomes one view, and every fence feeds one model. Merging is by alias:
/// <c>System(finance, …)</c> in a context fence and <c>System_Boundary(finance, …)</c>
/// in a container fence are the same system, which is what lets the explorer drill
/// from one picture into the next. Authors who reuse an alias across chapters get
/// that for free; authors who do not get pictures that sit side by side.
/// </para>
/// <para>
/// It never guesses, for the same reason <see cref="C4DslReader"/> does not: a
/// fence this reader half understood still draws a confident picture. Anything
/// outside the subset below becomes a <see cref="C4Problem"/> naming the chapter,
/// and the styling macros mermaid uses to colour and lay out a fence are known and
/// skipped, because reporting them would bury the constructs that matter.
/// </para>
/// </summary>
public static class C4MermaidReader
{
    private static readonly Dictionary<string, C4ViewKind> Headers = new(StringComparer.Ordinal)
    {
        ["C4Context"] = C4ViewKind.SystemContext,
        ["C4Container"] = C4ViewKind.Container,
        ["C4Component"] = C4ViewKind.Component,
        ["C4Dynamic"] = C4ViewKind.Dynamic,
        ["C4Deployment"] = C4ViewKind.Deployment
    };

    private enum Shape
    {
        Element,
        SystemBoundary,
        ContainerBoundary,
        Group,
        Node
    }

    /// <summary>What one element macro declares. <paramref name="Technology"/> is
    /// whether the third positional argument is a technology — containers,
    /// components and nodes — or the description, as it is for people and systems.
    /// One set of positions for both would file a system's description as its
    /// technology.</summary>
    private sealed record Macro(C4ElementKind Kind, Shape Shape, bool Technology, string? Tag, bool External);

    private static readonly Dictionary<string, Macro> ElementMacros = BuildElements();

    private static Dictionary<string, Macro> BuildElements()
    {
        var macros = new Dictionary<string, Macro>(StringComparer.Ordinal)
        {
            ["System_Boundary"] = new(C4ElementKind.SoftwareSystem, Shape.SystemBoundary, false, null, false),
            ["Container_Boundary"] = new(C4ElementKind.Container, Shape.ContainerBoundary, false, null, false),
            ["Enterprise_Boundary"] = new(C4ElementKind.SoftwareSystem, Shape.Group, false, null, false),
            ["Boundary"] = new(C4ElementKind.SoftwareSystem, Shape.Group, false, null, false),
            ["Deployment_Node"] = new(C4ElementKind.DeploymentNode, Shape.Node, true, null, false),
            ["Node"] = new(C4ElementKind.DeploymentNode, Shape.Node, true, null, false),
            ["Node_L"] = new(C4ElementKind.DeploymentNode, Shape.Node, true, null, false),
            ["Node_R"] = new(C4ElementKind.DeploymentNode, Shape.Node, true, null, false)
        };

        void Family(string stem, C4ElementKind kind, bool technology, bool shapes)
        {
            foreach (var external in new[] { false, true })
            {
                var suffix = external ? "_Ext" : string.Empty;
                macros[stem + suffix] = new(kind, Shape.Element, technology, null, external);
                if (!shapes) continue;

                macros[stem + "Db" + suffix] = new(kind, Shape.Element, technology, "Database", external);
                macros[stem + "Queue" + suffix] = new(kind, Shape.Element, technology, "Queue", external);
            }
        }

        Family("Person", C4ElementKind.Person, false, shapes: false);
        Family("System", C4ElementKind.SoftwareSystem, false, shapes: true);
        Family("Container", C4ElementKind.Container, true, shapes: true);
        Family("Component", C4ElementKind.Component, true, shapes: true);

        return macros;
    }

    private enum Direction
    {
        Forward,
        Back,
        Both,
        Indexed
    }

    private static readonly Dictionary<string, Direction> RelationshipMacros = new(StringComparer.Ordinal)
    {
        ["Rel"] = Direction.Forward,
        ["Rel_U"] = Direction.Forward,
        ["Rel_Up"] = Direction.Forward,
        ["Rel_D"] = Direction.Forward,
        ["Rel_Down"] = Direction.Forward,
        ["Rel_L"] = Direction.Forward,
        ["Rel_Left"] = Direction.Forward,
        ["Rel_R"] = Direction.Forward,
        ["Rel_Right"] = Direction.Forward,
        ["Rel_Back"] = Direction.Back,
        ["BiRel"] = Direction.Both,
        ["RelIndex"] = Direction.Indexed
    };

    /// <summary>Macros that colour, tag or lay out a fence. None of them says
    /// anything about the model, and the explorer draws with its own styling, so
    /// they are skipped knowingly rather than reported.</summary>
    private static readonly HashSet<string> Styling = new(StringComparer.Ordinal)
    {
        "UpdateElementStyle", "UpdateRelStyle", "UpdateLayoutConfig", "UpdateBoundaryStyle",
        "AddElementTag", "AddRelTag", "AddBoundaryTag"
    };

    /// <summary>Whether a mermaid fence's body is a C4 diagram: its first line that
    /// is not blank, a <c>%%</c> comment or front matter is one of the five C4
    /// headers. The question the host asks before handing a fence over, so that a
    /// flowchart in the same chapter is not reported as a C4 diagram it could not
    /// read.</summary>
    public static bool IsC4(string? fenceText) =>
        Header(Lines(fenceText)) is { } header && Headers.ContainsKey(header.Text);

    /// <summary>One workspace from these fences: one view per fence, one model
    /// across all of them.</summary>
    public static C4Workspace Read(string? name, IEnumerable<C4MermaidSource> sources) =>
        ReadWithOrigins(name, sources).Workspace;

    /// <summary><see cref="Read"/>, with the chapter each view was drawn in.</summary>
    public static C4MermaidReading ReadWithOrigins(string? name, IEnumerable<C4MermaidSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var builder = new Builder();
        foreach (var source in sources) builder.ReadFence(source);

        return builder.Build(name);
    }

    private static string[] Lines(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    /// <summary>The header line and its one-based number, past blank lines,
    /// <c>%%</c> comments and directives, and a <c>---</c> front matter block —
    /// mermaid's own place for a diagram's configuration.</summary>
    private static (string Text, int Line)? Header(string[] lines)
    {
        var inFrontMatter = false;
        var seenAnything = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0) continue;

            if (line == "---" && (inFrontMatter || !seenAnything))
            {
                inFrontMatter = !inFrontMatter;
                seenAnything = true;
                continue;
            }

            if (inFrontMatter || line.StartsWith("%%", StringComparison.Ordinal)) continue;

            return (line, index + 1);
        }

        return null;
    }

    private sealed record Frame(string? ParentId, string? Group);

    private sealed record PendingRelationship(
        string Origin,
        int Line,
        string Construct,
        string Source,
        string Destination,
        string? Description,
        string? Technology,
        IReadOnlyList<string> Tags,
        int? Order);

    private sealed record PendingView(
        string Origin,
        string Key,
        C4ViewKind Kind,
        string? ScopeId,
        string? Title,
        IReadOnlyList<string> Includes,
        IReadOnlyList<PendingRelationship> Steps,
        string? Environment);

    private sealed class Builder
    {
        private readonly List<C4Element> _elements = [];
        private readonly Dictionary<string, int> _byId = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<PendingRelationship> _relationships = [];
        private readonly List<PendingView> _views = [];
        private readonly List<C4Problem> _problems = [];
        private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string Id, string Alias)> _instances = [];

        public void ReadFence(C4MermaidSource source)
        {
            var origin = source.Origin;
            var lines = Lines(source.Text);

            if (Header(lines) is not { } header || !Headers.TryGetValue(header.Text, out var declared))
            {
                var at = Header(lines);
                _problems.Add(new C4Problem(
                    at?.Line ?? 1,
                    FirstWord(at?.Text) ?? "C4",
                    $"{origin}: the fence does not open with a C4 diagram type (C4Context, C4Container, C4Component, C4Dynamic, C4Deployment), so nothing in it was read."));
                return;
            }

            var title = Title(lines, header.Line);
            var key = UniqueKey(title, origin, header.Text);
            var deployment = declared is C4ViewKind.Deployment;
            var dynamic = declared is C4ViewKind.Dynamic;

            var fence = new Fence(origin, key, deployment);
            var frames = new Stack<Frame>();
            frames.Push(new Frame(null, null));
            Frame? opens = null;
            var steps = 0;

            for (var index = header.Line; index < lines.Length; index++)
            {
                var number = index + 1;
                var line = lines[index].Trim();

                if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal)) continue;
                if (IsTitle(line)) { opens = null; continue; }

                if (line == "{")
                {
                    if (opens is null)
                    {
                        Problem(origin, number, "{", "an opening brace follows nothing that can hold elements.");
                        opens = frames.Peek();
                    }

                    frames.Push(opens);
                    opens = null;
                    continue;
                }

                if (line.All(character => character == '}'))
                {
                    opens = null;
                    for (var brace = 0; brace < line.Length; brace++)
                    {
                        if (frames.Count > 1) frames.Pop();
                        else Problem(origin, number, "}", "a closing brace closes nothing.");
                    }

                    continue;
                }

                opens = null;

                if (!TryStatement(line, out var macro, out var arguments, out var block))
                {
                    Problem(origin, number, FirstWord(line) ?? line, "this line is not a mermaid C4 statement this reader knows; it is not drawn.");
                    continue;
                }

                Frame? frame = null;

                if (ElementMacros.TryGetValue(macro, out var element))
                {
                    frame = Declare(fence, element, macro, arguments, frames.Peek(), number);
                }
                else if (RelationshipMacros.TryGetValue(macro, out var direction))
                {
                    foreach (var relationship in Relate(fence, macro, direction, arguments, number))
                    {
                        if (dynamic)
                        {
                            steps++;
                            fence.Steps.Add(relationship with { Order = relationship.Order ?? steps });
                        }
                        else
                        {
                            fence.Relationships.Add(relationship with { Order = null });
                        }
                    }
                }
                else if (!Styling.Contains(macro))
                {
                    Problem(origin, number, macro, $"`{macro}` is not a mermaid C4 construct this reader knows; it is not drawn.");
                }

                if (block)
                {
                    if (frame is null)
                    {
                        Problem(origin, number, macro, $"`{macro}` cannot hold elements, so its block was read as though it were not there.");
                        frame = frames.Peek();
                    }

                    frames.Push(frame);
                }
                else
                {
                    opens = frame;
                }
            }

            if (frames.Count > 1)
            {
                Problem(origin, lines.Length, "{", "a boundary is opened and never closed.");
            }

            // Endpoints resolve against this fence's own declarations first. In a
            // deployment fence that is what keeps a relationship between two node
            // instances from landing on the static elements of the same alias.
            _relationships.AddRange(fence.Relationships.Select(fence.Resolve));

            _views.Add(new PendingView(
                origin,
                key,
                Kind(declared, fence),
                Scope(declared, fence),
                title,
                [.. fence.Declared.Distinct(StringComparer.OrdinalIgnoreCase)],
                [.. fence.Steps.Select(fence.Resolve)],
                deployment ? key : null));
        }

        /// <summary>
        /// The kind a fence's header asks for, with one reading of its own.
        /// <para>
        /// A <c>C4Context</c> is a context view only when it is plainly about one
        /// system: exactly one system that is neither external nor inside anything.
        /// With none, or several, there is no "the system" to scope it on, and a
        /// context view with no scope draws nothing — so it is read as the landscape
        /// it is.
        /// </para>
        /// </summary>
        private static C4ViewKind Kind(C4ViewKind declared, Fence fence) =>
            declared is C4ViewKind.SystemContext && fence.Subjects.Count != 1 ? C4ViewKind.SystemLandscape : declared;

        private static string? Scope(C4ViewKind declared, Fence fence) => declared switch
        {
            C4ViewKind.SystemContext => fence.Subjects.Count == 1 ? fence.Subjects[0] : null,
            C4ViewKind.Container => fence.FirstSystemBoundary,
            C4ViewKind.Component => fence.FirstContainerBoundary,
            _ => null
        };

        private Frame? Declare(Fence fence, Macro macro, string name, Arguments arguments, Frame current, int line)
        {
            var alias = arguments.Positional.ElementAtOrDefault(0);
            if (string.IsNullOrWhiteSpace(alias))
            {
                Problem(fence.Origin, line, name, $"`{name}` was declared with no alias.");
                return null;
            }

            var label = Blank(arguments.Positional.ElementAtOrDefault(1)) ?? alias;

            // Enterprise and generic boundaries frame a group of elements and are
            // nothing themselves, which is what a Structurizr `group` is too.
            if (macro.Shape is Shape.Group) return current with { Group = label };

            var id = fence.Deployment ? $"{fence.Key}.{alias}" : alias;
            var kind = macro.Kind;

            // A container drawn inside a deployment fence is the container running
            // somewhere, not a second declaration of it — the same distinction the
            // DSL's `containerInstance` makes.
            if (fence.Deployment && macro.Shape is Shape.Element)
            {
                kind = kind switch
                {
                    C4ElementKind.Container => C4ElementKind.ContainerInstance,
                    C4ElementKind.SoftwareSystem => C4ElementKind.SoftwareSystemInstance,
                    _ => kind
                };
            }

            var tags = new List<string>();
            if (macro.Tag is not null) tags.Add(macro.Tag);
            if (macro.External) tags.Add("External");
            tags.AddRange(arguments.Tags);

            // A system or container boundary takes an alias and a label and nothing
            // that describes it; a node's third argument is its type, which is what
            // a technology is on every other card.
            string? technology = null;
            string? description = null;
            if (macro.Shape is Shape.Element or Shape.Node)
            {
                technology = macro.Technology ? Blank(arguments.Positional.ElementAtOrDefault(2)) : null;
                description = Blank(arguments.Positional.ElementAtOrDefault(macro.Technology ? 3 : 2));
            }

            var declared = Add(
                new C4Element(id, kind, label, description, technology, tags, current.ParentId, current.Group, null, fence.Deployment ? fence.Key : null),
                fence.Origin,
                line,
                name);

            fence.Declared.Add(declared);
            fence.Aliases[alias] = declared;

            if (kind is C4ElementKind.ContainerInstance or C4ElementKind.SoftwareSystemInstance) _instances.Add((declared, alias));

            if (kind is C4ElementKind.SoftwareSystem && !macro.External && current.ParentId is null && !fence.Subjects.Contains(declared, StringComparer.OrdinalIgnoreCase))
            {
                fence.Subjects.Add(declared);
            }

            if (macro.Shape is Shape.SystemBoundary) fence.FirstSystemBoundary ??= declared;
            if (macro.Shape is Shape.ContainerBoundary) fence.FirstContainerBoundary ??= declared;

            return current with { ParentId = declared };
        }

        private IEnumerable<PendingRelationship> Relate(Fence fence, string name, Direction direction, Arguments arguments, int line)
        {
            var positional = arguments.Positional;
            int? order = null;

            if (direction is Direction.Indexed)
            {
                if (int.TryParse(positional.ElementAtOrDefault(0), out var index)) order = index;
                positional = [.. positional.Skip(1)];
            }

            var from = Blank(positional.ElementAtOrDefault(0));
            var to = Blank(positional.ElementAtOrDefault(1));
            if (from is null || to is null)
            {
                Problem(fence.Origin, line, name, $"`{name}` needs both ends; it is not drawn.");
                yield break;
            }

            var description = Blank(positional.ElementAtOrDefault(2));
            var technology = Blank(positional.ElementAtOrDefault(3));

            if (direction is Direction.Back) (from, to) = (to, from);

            yield return new PendingRelationship(fence.Origin, line, name, from, to, description, technology, arguments.Tags, order);
            if (direction is Direction.Both)
            {
                yield return new PendingRelationship(fence.Origin, line, name, to, from, description, technology, arguments.Tags, order);
            }
        }

        /// <summary>
        /// Adds an element, or folds it into the one already declared under the
        /// same alias.
        /// <para>
        /// The first declaration wins and later ones only fill what it left empty —
        /// a description, a technology, a parent, a group — and add their tags. That
        /// is what makes a system declared as a box in one chapter and as a boundary
        /// in another one element with its containers inside it. A later declaration
        /// that says it is a different kind of thing is a disagreement between two
        /// chapters, and it is reported rather than resolved.
        /// </para>
        /// </summary>
        private string Add(C4Element element, string origin, int line, string construct)
        {
            if (!_byId.TryGetValue(element.Id, out var at))
            {
                if (WouldSitInsideItself(element.Id, element.ParentId))
                {
                    Problem(origin, line, construct, $"`{element.Id}` would sit inside itself; it is drawn at the top of the model.");
                    element = element with { ParentId = null };
                }

                _byId[element.Id] = _elements.Count;
                _elements.Add(element);
                return element.Id;
            }

            var existing = _elements[at];
            if (existing.Kind != element.Kind)
            {
                Problem(origin, line, construct, $"`{element.Id}` is declared as a {element.Kind} here and as a {existing.Kind} elsewhere; the first declaration is the one drawn.");
                return existing.Id;
            }

            // A parent filled in from a later chapter is checked before it is taken.
            // `System_Boundary(x) { System(x) }`, or two chapters each nesting one
            // system inside the other, would otherwise make a loop of parents — and
            // every walk down the model (a subtree, a drill target) would never end.
            var parent = existing.ParentId;
            if (parent is null && element.ParentId is not null)
            {
                if (WouldSitInsideItself(existing.Id, element.ParentId))
                {
                    Problem(origin, line, construct, $"`{existing.Id}` would sit inside itself; it is drawn where it was first declared.");
                }
                else
                {
                    parent = element.ParentId;
                }
            }

            _elements[at] = existing with
            {
                Description = existing.Description ?? element.Description,
                Technology = existing.Technology ?? element.Technology,
                ParentId = parent,
                Group = existing.Group ?? element.Group,
                Tags = [.. existing.Tags.Concat(element.Tags).Distinct(StringComparer.OrdinalIgnoreCase)]
            };

            return existing.Id;
        }

        /// <summary>Whether giving <paramref name="id"/> this parent would put it
        /// beneath itself: the parent's own chain of parents reaches it. Bounded,
        /// because the chain it walks is the one this check exists to keep
        /// acyclic.</summary>
        private bool WouldSitInsideItself(string id, string? parentId)
        {
            var current = parentId;
            for (var guard = 0; current is not null && guard < 256; guard++)
            {
                if (string.Equals(current, id, StringComparison.OrdinalIgnoreCase)) return true;
                current = _byId.TryGetValue(current, out var at) ? _elements[at].ParentId : null;
            }

            return false;
        }

        /// <summary>
        /// What an unscoped view must leave out: every element that is neither one the
        /// fence drew nor a boundary around one.
        /// <para>
        /// A landscape draws every top-level person and system, and a container or
        /// component view with no boundary to scope it on draws the whole top of the
        /// model. That is right for one authored workspace and wrong here, where the
        /// model is every chapter's fences merged: a boundary-less fence would pick up
        /// the systems another chapter drew. A scoped view keeps its default set,
        /// because that is what lets a context view be drilled into a container view
        /// that another chapter's fence supplied.
        /// </para>
        /// </summary>
        private List<string> Unrelated(IReadOnlyList<string> includes)
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in includes)
            {
                var current = id;
                for (var guard = 0; current is not null && guard < 256 && keep.Add(current); guard++)
                {
                    current = _byId.TryGetValue(current, out var at) ? _elements[at].ParentId : null;
                }
            }

            return [.. _elements.Where(element => !keep.Contains(element.Id)).Select(element => element.Id)];
        }

        public C4MermaidReading Build(string? name)
        {
            // An instance points at the static element of its alias once every fence
            // has been read, because the container it runs may be declared in a
            // chapter that comes after the deployment one.
            foreach (var (id, alias) in _instances)
            {
                if (!_byId.TryGetValue(alias, out var target)) continue;
                if (_elements[target].Kind is not (C4ElementKind.Person or C4ElementKind.SoftwareSystem or C4ElementKind.Container or C4ElementKind.Component)) continue;

                var at = _byId[id];
                _elements[at] = _elements[at] with { InstanceOfId = _elements[target].Id };
            }

            var relationships = new List<C4Relationship>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var relationship in _relationships)
            {
                if (Endpoints(relationship) is not { } ends) continue;
                if (!seen.Add($"{ends.From}|{ends.To}|{relationship.Description}|{relationship.Technology}")) continue;

                relationships.Add(new C4Relationship(ends.From, ends.To, relationship.Description, relationship.Technology, relationship.Tags));
            }

            var views = new List<C4View>();
            var origins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var view in _views)
            {
                var steps = new List<C4DynamicStep>();
                foreach (var step in view.Steps)
                {
                    if (Endpoints(step) is { } ends) steps.Add(new C4DynamicStep(step.Order ?? steps.Count + 1, ends.From, ends.To, step.Description));
                }

                var unscoped = view.ScopeId is null
                    && view.Kind is C4ViewKind.SystemLandscape or C4ViewKind.Container or C4ViewKind.Component;

                views.Add(new C4View(
                    view.Key,
                    view.Kind,
                    view.ScopeId,
                    view.Title,
                    null,
                    view.Includes,
                    unscoped ? Unrelated(view.Includes) : [],
                    true,
                    null,
                    steps,
                    view.Environment));

                origins[view.Key] = view.Origin;
            }

            return new C4MermaidReading(
                new C4Workspace(name, null, [.. _elements], relationships, views, [.. _problems]),
                origins);
        }

        /// <summary>Both ends of a relationship as model identifiers, or a problem.
        /// Mermaid draws an edge to an alias nothing declared as an empty box; the
        /// writer here drops it, and dropping it silently is the one thing a reader
        /// of the picture cannot notice.</summary>
        private (string From, string To)? Endpoints(PendingRelationship relationship)
        {
            var missing = new[] { relationship.Source, relationship.Destination }
                .Where(id => !_byId.ContainsKey(id))
                .ToList();

            if (missing.Count > 0)
            {
                Problem(
                    relationship.Origin,
                    relationship.Line,
                    relationship.Construct,
                    $"`{relationship.Construct}` names {string.Join(" and ", missing.Select(id => $"`{id}`"))}, which no C4 diagram declares; it is not drawn.");
                return null;
            }

            return (_elements[_byId[relationship.Source]].Id, _elements[_byId[relationship.Destination]].Id);
        }

        /// <summary>
        /// The view's key: its title as a slug, or the chapter and the diagram type
        /// when it has none.
        /// <para>
        /// A key is what a reference's anchor addresses, so it is made from what the
        /// author wrote rather than numbered, and made unique by suffix rather than
        /// by refusing the second view — two chapters may well both title a picture
        /// "System Context".
        /// </para>
        /// </summary>
        private string UniqueKey(string? title, string origin, string header)
        {
            var stem = Path.GetFileNameWithoutExtension(origin.Replace('\\', '/').Split('/')[^1]);
            var kind = header["C4".Length..].ToLowerInvariant();

            var key = C4Slug.Of(title);
            if (key.Length == 0) key = C4Slug.Of($"{stem}-{kind}");
            if (key.Length == 0) key = kind;

            var candidate = key;
            for (var suffix = 2; !_keys.Add(candidate); suffix++) candidate = $"{key}-{suffix}";

            return candidate;
        }

        private void Problem(string origin, int line, string construct, string message) =>
            _problems.Add(new C4Problem(line, construct, $"{origin}: {message}"));
    }

    /// <summary>What one fence has said so far.</summary>
    private sealed class Fence(string origin, string key, bool deployment)
    {
        public string Origin { get; } = origin;

        public string Key { get; } = key;

        public bool Deployment { get; } = deployment;

        /// <summary>Every element this fence drew, in order. A derived view
        /// includes exactly these, so nothing the fence drew goes missing from its
        /// picture whatever the default rule for the view's kind would leave
        /// out.</summary>
        public List<string> Declared { get; } = [];

        /// <summary>The fence's own aliases, mapped to model identifiers.</summary>
        public Dictionary<string, string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Top-level systems that are not external: the candidates for
        /// what a context fence is about.</summary>
        public List<string> Subjects { get; } = [];

        public string? FirstSystemBoundary { get; set; }

        public string? FirstContainerBoundary { get; set; }

        public List<PendingRelationship> Relationships { get; } = [];

        public List<PendingRelationship> Steps { get; } = [];

        public PendingRelationship Resolve(PendingRelationship relationship) => relationship with
        {
            Source = Aliases.TryGetValue(relationship.Source, out var source) ? source : relationship.Source,
            Destination = Aliases.TryGetValue(relationship.Destination, out var destination) ? destination : relationship.Destination
        };
    }

    private sealed record Arguments(IReadOnlyList<string> Positional, IReadOnlyList<string> Tags);

    private static bool IsTitle(string line) =>
        line == "title" || line.StartsWith("title ", StringComparison.Ordinal) || line.StartsWith("title\t", StringComparison.Ordinal);

    private static string? Title(string[] lines, int headerLine)
    {
        for (var index = headerLine; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (IsTitle(line)) return Blank(line["title".Length..]);
        }

        return null;
    }

    /// <summary>
    /// <c>Macro(arguments)</c>, optionally followed by the <c>{</c> that opens its
    /// block.
    /// <para>
    /// The closing parenthesis is the last one on the line, not the first, because a
    /// label may hold one — <c>"Store (SQLite)"</c> — and only a brace may follow
    /// it.
    /// </para>
    /// </summary>
    private static bool TryStatement(string line, out string macro, out Arguments arguments, out bool block)
    {
        macro = string.Empty;
        arguments = new Arguments([], []);
        block = false;

        var open = line.IndexOf('(', StringComparison.Ordinal);
        var close = line.LastIndexOf(')');
        if (open <= 0 || close < open) return false;

        var name = line[..open].Trim();
        if (name.Length == 0 || !name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')) return false;

        var rest = line[(close + 1)..].Trim();
        if (rest.Length > 0 && rest != "{") return false;

        macro = name;
        block = rest == "{";
        arguments = Split(line[(open + 1)..close]);
        return true;
    }

    /// <summary>
    /// The arguments, split on commas outside double quotes.
    /// <para>
    /// Named arguments — <c>$tags="v1+v2"</c>, <c>$link</c>, <c>$sprite</c> — are
    /// held apart from the positional ones, because they may sit anywhere and
    /// counting one as a position would shift every argument after it. Only
    /// <c>$tags</c> says anything about the model.
    /// </para>
    /// </summary>
    private static Arguments Split(string text)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;

        foreach (var character in text)
        {
            if (character == '"') quoted = !quoted;

            if (character == ',' && !quoted)
            {
                parts.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0 || parts.Count > 0) parts.Add(current.ToString());

        var positional = new List<string>();
        var tags = new List<string>();

        foreach (var raw in parts)
        {
            var part = raw.Trim();
            if (part.StartsWith('$'))
            {
                var equals = part.IndexOf('=', StringComparison.Ordinal);
                if (equals < 0) continue;

                var key = part[1..equals].Trim();
                if (!string.Equals(key, "tags", StringComparison.OrdinalIgnoreCase)) continue;

                tags.AddRange(Unquote(part[(equals + 1)..])
                    .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                continue;
            }

            positional.Add(Unquote(part));
        }

        return new Arguments(positional, tags);
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"' ? trimmed[1..^1] : trimmed.Trim('"');
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstWord(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var trimmed = line.Trim();
        var end = trimmed.IndexOfAny([' ', '\t', '(', '{']);
        return end <= 0 ? trimmed : trimmed[..end];
    }
}
