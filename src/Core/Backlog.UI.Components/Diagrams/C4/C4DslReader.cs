namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>
/// Reads the subset of Structurizr DSL that c4hero writes.
/// <para>
/// This is a second implementation of a dialect whose first implementation is
/// c4hero's TypeScript parser, in another repository, where no test here can see
/// it. <c>.arc42/adr/0004</c> names that shape of duplication as a drift hazard and
/// it applies in full, so two things are true of this reader by construction.
/// </para>
/// <para>
/// It is specified against c4hero's own conformance fixture rather than against
/// the language reference — the fixture is what c4hero demonstrably round-trips,
/// and the reference describes a superset nobody here writes.
/// </para>
/// <para>
/// And it never guesses. Every construct outside the supported subset becomes a
/// <see cref="C4Problem"/> naming the keyword and its line, and parsing continues.
/// Refusing the file would be the safer-looking choice and the worse one: the
/// failure that matters is not an unreadable workspace, it is a readable one that
/// draws a picture quietly missing whatever could not be parsed.
/// </para>
/// </summary>
public static class C4DslReader
{
    /// <summary>The model keywords that declare a static element, and what each
    /// one's positional arguments mean.</summary>
    private static readonly Dictionary<string, C4ElementKind> StaticKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["person"] = C4ElementKind.Person,
        ["softwareSystem"] = C4ElementKind.SoftwareSystem,
        ["container"] = C4ElementKind.Container,
        ["component"] = C4ElementKind.Component
    };

    private static readonly Dictionary<string, C4ViewKind> ViewKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["systemLandscape"] = C4ViewKind.SystemLandscape,
        ["systemContext"] = C4ViewKind.SystemContext,
        ["container"] = C4ViewKind.Container,
        ["component"] = C4ViewKind.Component,
        ["dynamic"] = C4ViewKind.Dynamic,
        ["deployment"] = C4ViewKind.Deployment
    };

    /// <summary>Constructs this reader understands the syntax of and deliberately
    /// does not act on, because none of them changes a picture: server-side
    /// configuration, layout animation steps, arbitrary key/value properties. Held
    /// as a list so they are skipped knowingly rather than falling through to the
    /// problem report and burying the constructs that matter.</summary>
    private static readonly HashSet<string> SilentlySkipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "configuration", "properties", "animation", "branding", "terminology", "default", "url"
    };

    /// <summary>Directives that pull in, generate, or decorate content this reader
    /// has no way to honour. Each one reaches the reader as a named problem rather
    /// than as silence, because a workspace assembled by <c>!include</c> is a
    /// workspace this reader has only partly seen.</summary>
    private static readonly HashSet<string> UnsupportedDirectives = new(StringComparer.OrdinalIgnoreCase)
    {
        "!include", "!docs", "!adrs", "!script", "!plugin", "!constant", "!impliedRelationships", "!ref", "!extend"
    };

    public static C4Workspace Read(string? source) => new Parser(C4DslLexer.Tokenize(source)).ParseWorkspace();

    private sealed class Parser(IReadOnlyList<C4Token> tokens)
    {
        private readonly List<C4Element> _elements = [];
        private readonly List<PendingRelationship> _relationships = [];
        private readonly List<C4View> _views = [];
        private readonly List<C4Problem> _problems = [];
        private readonly List<C4ElementStyle> _styles = [];
        private readonly HashSet<string> _ids = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _viewKeys = new(StringComparer.OrdinalIgnoreCase);

        private int _index;
        private bool _hierarchical;
        private string? _name;
        private string? _description;
        private int _anonymous;

        private sealed record PendingRelationship(
            string Source,
            string Destination,
            string? Description,
            string? Technology,
            IReadOnlyList<string> Tags,
            string? Scope,
            int Line);

        private C4Token? Current => _index < tokens.Count ? tokens[_index] : null;

        private bool AtEnd => _index >= tokens.Count;

        private void Advance() => _index++;

        public C4Workspace ParseWorkspace()
        {
            SkipStatementEnds();

            if (Current is not { } first)
            {
                return C4Workspace.Empty with { Problems = [new C4Problem(1, "workspace", "The file is empty.")] };
            }

            if (!first.IsWord("workspace"))
            {
                Problem(first, "A workspace file has to open with `workspace`. Nothing was read.");
                return Build();
            }

            var (parts, hasBlock) = ReadStatement();
            var arguments = Values(parts, 1);
            _name = arguments.ElementAtOrDefault(0);
            _description = arguments.ElementAtOrDefault(1);

            if (hasBlock) ParseWorkspaceBody();
            return Build();
        }

        private void ParseWorkspaceBody()
        {
            foreach (var (parts, hasBlock) in Block())
            {
                var head = parts[0];

                if (head.IsWord("model"))
                {
                    if (hasBlock) ParseModel(null, null);
                    continue;
                }

                if (head.IsWord("views"))
                {
                    if (hasBlock) ParseViews();
                    continue;
                }

                if (head.IsWord("!identifiers"))
                {
                    ReadIdentifierMode(parts);
                    continue;
                }

                if (head.IsWord("name")) { _name = Values(parts, 1).FirstOrDefault() ?? _name; continue; }
                if (head.IsWord("description")) { _description = Values(parts, 1).FirstOrDefault() ?? _description; continue; }

                Unhandled(head, hasBlock, "workspace");
            }
        }

        // ---- model -------------------------------------------------------------

        /// <param name="parentId">The element whose block this is, or null at the top
        /// of the model. Also the element a <c>properties</c> block in this scope
        /// belongs to: at the top of the model there is no such element, and the block
        /// is skipped as before.</param>
        private void ParseModel(string? parentId, string? group)
        {
            foreach (var (parts, hasBlock) in Block())
            {
                var head = parts[0];

                if (head.IsWord("!identifiers")) { ReadIdentifierMode(parts); continue; }

                // Read rather than skipped, and only inside an element. This is where
                // c4hero keeps what Structurizr has no field for — the owning team, a
                // lifecycle status — and two of the Highlighter's four facets are those.
                if (head.IsWord("properties") && parentId is not null)
                {
                    if (hasBlock) ReadProperties(parentId);
                    continue;
                }

                if (head.IsWord("group"))
                {
                    var name = Values(parts, 1).FirstOrDefault();
                    if (hasBlock) ParseModel(parentId, name ?? group);
                    continue;
                }

                if (head.IsWord("deploymentEnvironment"))
                {
                    var environment = Values(parts, 1).FirstOrDefault() ?? "Default";
                    if (hasBlock) ParseDeployment(environment, null, group);
                    continue;
                }

                if (TryRelationship(parts, parentId, parentId)) { if (hasBlock) SkipBlock(); continue; }

                if (TryDeclaration(parts, hasBlock, parentId, group, StaticKeywords)) continue;

                Unhandled(head, hasBlock, "model");
            }
        }

        private void ParseDeployment(string environment, string? parentId, string? group)
        {
            foreach (var (parts, hasBlock) in Block())
            {
                var head = parts[0];

                // Deployment declarations carry an identifier as often as static ones
                // do — `host = deploymentNode "…"` — so the keyword is not always the
                // first token, and reading it as though it were reports every named
                // node as an unknown construct.
                var keyword = parts[KeywordIndex(parts)];

                if (keyword.IsWord("deploymentNode") || keyword.IsWord("infrastructureNode"))
                {
                    var kind = keyword.IsWord("deploymentNode") ? C4ElementKind.DeploymentNode : C4ElementKind.InfrastructureNode;
                    var id = Declare(parts, kind, parentId, group, environment);
                    if (hasBlock)
                    {
                        if (id is null) SkipBlock();
                        else ParseDeployment(environment, id, group);
                    }

                    continue;
                }

                if (keyword.IsWord("containerInstance") || keyword.IsWord("softwareSystemInstance"))
                {
                    DeclareInstance(parts, keyword.IsWord("containerInstance") ? C4ElementKind.ContainerInstance : C4ElementKind.SoftwareSystemInstance, parentId, group, environment);
                    if (hasBlock) SkipBlock();
                    continue;
                }

                if (head.IsWord("group"))
                {
                    var name = Values(parts, 1).FirstOrDefault();
                    if (hasBlock) ParseDeployment(environment, parentId, name ?? group);
                    continue;
                }

                if (TryRelationship(parts, parentId, parentId)) { if (hasBlock) SkipBlock(); continue; }

                Unhandled(keyword, hasBlock, "deploymentEnvironment");
            }
        }

        /// <summary>
        /// Reads <c>id = keyword "Name" …</c> and its anonymous form.
        /// </summary>
        private bool TryDeclaration(
            IReadOnlyList<C4Token> parts,
            bool hasBlock,
            string? parentId,
            string? group,
            Dictionary<string, C4ElementKind> keywords)
        {
            var offset = KeywordIndex(parts);
            if (parts.Count <= offset) return false;

            var keyword = parts[offset];
            if (keyword.Kind != C4TokenKind.Word || !keywords.TryGetValue(keyword.Text, out var kind)) return false;

            var id = Declare(parts, kind, parentId, group, null);
            if (hasBlock)
            {
                if (id is null) SkipBlock();
                else ParseModel(id, group);
            }

            return true;
        }

        /// <summary>
        /// Adds one element and returns the identifier everything else will name it
        /// by.
        /// <para>
        /// Under <c>!identifiers hierarchical</c> that identifier is the parent's
        /// with the declared name appended, which is what lets the same local name
        /// be declared in several systems — c4hero's fixture does exactly that, and
        /// under flat identifiers it would be a collision.
        /// </para>
        /// </summary>
        private string? Declare(IReadOnlyList<C4Token> parts, C4ElementKind kind, string? parentId, string? group, string? environment)
        {
            var keywordAt = KeywordIndex(parts);
            var assigned = keywordAt == 0 ? null : parts[0].Text;
            var arguments = Values(parts, keywordAt + 1);
            var name = arguments.ElementAtOrDefault(0);

            if (string.IsNullOrWhiteSpace(name))
            {
                Problem(parts[keywordAt], $"`{parts[keywordAt].Text}` was declared with no name.");
                return null;
            }

            // person and softwareSystem take name/description/tags; everything with
            // a technology takes name/description/technology/tags. Reading the same
            // positions for both would file a system's tags as its technology.
            var carriesTechnology = kind is not (C4ElementKind.Person or C4ElementKind.SoftwareSystem);
            var description = Blank(arguments.ElementAtOrDefault(1));
            var technology = carriesTechnology ? Blank(arguments.ElementAtOrDefault(2)) : null;
            var tags = SplitTags(carriesTechnology ? arguments.ElementAtOrDefault(3) : arguments.ElementAtOrDefault(2));

            var local = assigned ?? Anonymous(name);
            var id = _hierarchical && parentId is not null ? $"{parentId}.{local}" : local;

            if (!_ids.Add(id))
            {
                Problem(parts[keywordAt], $"`{id}` is declared more than once. The first declaration is the one everything else resolves to.");
                return id;
            }

            _elements.Add(new C4Element(id, kind, name, description, technology, tags, parentId, group, null, environment));
            return id;
        }

        private void DeclareInstance(IReadOnlyList<C4Token> parts, C4ElementKind kind, string? parentId, string? group, string environment)
        {
            var keywordAt = KeywordIndex(parts);
            var assigned = keywordAt == 0 ? null : parts[0].Text;
            var arguments = Values(parts, keywordAt + 1);
            var target = arguments.ElementAtOrDefault(0);

            if (string.IsNullOrWhiteSpace(target))
            {
                Problem(parts[keywordAt], $"`{parts[keywordAt].Text}` names nothing to instantiate.");
                return;
            }

            // The instance's own identifier is synthesised unless one was assigned:
            // `containerInstance api` names the container it instantiates, not
            // itself, so reusing that identifier would shadow the container.
            var local = assigned ?? Anonymous($"{target}-instance");
            var id = _hierarchical && parentId is not null ? $"{parentId}.{local}" : local;
            if (!_ids.Add(id)) return;

            _elements.Add(new C4Element(
                id,
                kind,
                target,
                null,
                null,
                SplitTags(arguments.ElementAtOrDefault(1)),
                parentId,
                group,
                target,
                environment));
        }

        /// <summary>
        /// Reads <c>source -&gt; destination "Description" "Technology" "Tags"</c>,
        /// including the two forms that leave the source implicit inside an element
        /// block: a bare leading arrow, and <c>this</c>.
        /// </summary>
        private bool TryRelationship(IReadOnlyList<C4Token> parts, string? implicitSource, string? scope)
        {
            var arrow = -1;
            for (var index = 0; index < parts.Count; index++)
            {
                if (parts[index].Kind == C4TokenKind.Arrow) { arrow = index; break; }
            }

            if (arrow < 0) return false;

            string? source;
            if (arrow == 0)
            {
                source = implicitSource;
            }
            else
            {
                var written = parts[arrow - 1].Text;
                source = written.Equals("this", StringComparison.OrdinalIgnoreCase) ? implicitSource : written;
            }

            var destination = parts.ElementAtOrDefault(arrow + 1);

            if (source is null || destination is null || !destination.IsValue)
            {
                Problem(parts[arrow], "A relationship needs an element on both sides of `->`.");
                return true;
            }

            var arguments = Values(parts, arrow + 2);
            _relationships.Add(new PendingRelationship(
                source,
                destination.Text,
                Blank(arguments.ElementAtOrDefault(0)),
                Blank(arguments.ElementAtOrDefault(1)),
                SplitTags(arguments.ElementAtOrDefault(2)),
                scope,
                parts[arrow].Line));

            return true;
        }

        // ---- views -------------------------------------------------------------

        private void ParseViews()
        {
            foreach (var (parts, hasBlock) in Block())
            {
                var head = parts[0];

                if (head.IsWord("styles"))
                {
                    if (hasBlock) ParseStyles();
                    continue;
                }

                if (head.Kind == C4TokenKind.Word && ViewKeywords.TryGetValue(head.Text, out var kind))
                {
                    ParseView(kind, parts, hasBlock);
                    continue;
                }

                Unhandled(head, hasBlock, "views");
            }
        }

        private void ParseView(C4ViewKind kind, IReadOnlyList<C4Token> parts, bool hasBlock)
        {
            // A landscape is of the whole model and takes no scope; every other kind
            // opens with the element it is a view of. Reading them the same way
            // would take a landscape's key for its scope.
            var arguments = Values(parts, 1);
            var scoped = kind is not C4ViewKind.SystemLandscape;
            var offset = 0;

            string? scope = null;
            string? environment = null;

            if (scoped)
            {
                scope = arguments.ElementAtOrDefault(offset++);
                if (kind is C4ViewKind.Deployment) environment = arguments.ElementAtOrDefault(offset++);
            }

            var key = arguments.ElementAtOrDefault(offset++);
            var description = arguments.ElementAtOrDefault(offset);

            var includes = new List<string>();
            var excludes = new List<string>();
            var steps = new List<C4DynamicStep>();
            var includesAll = false;
            string? autoLayout = null;
            string? title = null;

            if (hasBlock)
            {
                foreach (var (body, bodyBlock) in Block())
                {
                    var word = body[0];

                    if (word.IsWord("include"))
                    {
                        foreach (var value in Values(body, 1))
                        {
                            if (value == "*") includesAll = true;
                            else if (value.Contains("==", StringComparison.Ordinal) || value.Contains("->", StringComparison.Ordinal))
                            {
                                Problem(word, $"`include {value}` is an expression. Only `*` and named elements are read, so this view may be missing elements.");
                            }
                            else includes.Add(value);
                        }

                        continue;
                    }

                    if (word.IsWord("exclude")) { excludes.AddRange(Values(body, 1)); continue; }
                    if (word.IsWord("autolayout") || word.IsWord("autoLayout")) { autoLayout = Values(body, 1).FirstOrDefault(); continue; }
                    if (word.IsWord("title")) { title = Values(body, 1).FirstOrDefault(); continue; }
                    if (word.IsWord("description")) { description = Values(body, 1).FirstOrDefault() ?? description; continue; }

                    if (kind is C4ViewKind.Dynamic && TryDynamicStep(body, steps, scope)) continue;

                    Unhandled(word, bodyBlock, $"{kind} view");
                }
            }

            _views.Add(new C4View(
                ViewKey(kind, key, scope),
                kind,
                scope,
                title ?? description,
                description,
                includes,
                excludes,
                includesAll,
                autoLayout,
                steps,
                environment));
        }

        private bool TryDynamicStep(IReadOnlyList<C4Token> parts, List<C4DynamicStep> steps, string? scope)
        {
            var arrow = -1;
            for (var index = 0; index < parts.Count; index++)
            {
                if (parts[index].Kind == C4TokenKind.Arrow) { arrow = index; break; }
            }

            if (arrow <= 0) return false;

            var destination = parts.ElementAtOrDefault(arrow + 1);
            if (destination is null || !destination.IsValue) return false;

            steps.Add(new C4DynamicStep(
                steps.Count + 1,
                Resolve(parts[arrow - 1].Text, scope) ?? parts[arrow - 1].Text,
                Resolve(destination.Text, scope) ?? destination.Text,
                Blank(Values(parts, arrow + 2).FirstOrDefault())));

            return true;
        }

        private void ParseStyles()
        {
            foreach (var (parts, hasBlock) in Block())
            {
                var head = parts[0];
                var isElement = head.IsWord("element");

                if (!isElement && !head.IsWord("relationship"))
                {
                    Unhandled(head, hasBlock, "styles");
                    continue;
                }

                var tag = Values(parts, 1).FirstOrDefault();
                string? background = null;
                string? color = null;
                string? shape = null;

                if (hasBlock)
                {
                    foreach (var (body, bodyBlock) in Block())
                    {
                        if (bodyBlock) SkipBlock();
                        var value = Values(body, 1).FirstOrDefault();
                        if (body[0].IsWord("background")) background = value;
                        else if (body[0].IsWord("color") || body[0].IsWord("colour")) color = value;
                        else if (body[0].IsWord("shape")) shape = value;
                    }
                }

                // Relationship styles are read and kept out of the report — the
                // syntax is understood — but nothing draws them: mermaid C4 has no
                // per-relationship styling to map them onto.
                if (isElement && !string.IsNullOrWhiteSpace(tag))
                {
                    _styles.Add(new C4ElementStyle(tag, background, color, shape));
                }
            }
        }

        // ---- statement plumbing ------------------------------------------------

        /// <summary>
        /// Every non-empty statement of the block that has just been opened, each
        /// with whether it opened a block of its own. Leaves the cursor after the
        /// closing brace.
        /// </summary>
        private IEnumerable<(IReadOnlyList<C4Token> Parts, bool HasBlock)> Block()
        {
            while (!AtEnd && Current!.Kind != C4TokenKind.BraceClose)
            {
                if (Current.Kind == C4TokenKind.EndOfStatement) { Advance(); continue; }

                var statement = ReadStatement();
                if (statement.Parts.Count == 0)
                {
                    if (statement.HasBlock) SkipBlock();
                    continue;
                }

                yield return statement;
            }

            if (!AtEnd && Current!.Kind == C4TokenKind.BraceClose) Advance();
        }

        private (IReadOnlyList<C4Token> Parts, bool HasBlock) ReadStatement()
        {
            var parts = new List<C4Token>();

            while (!AtEnd)
            {
                var token = Current!;

                if (token.Kind == C4TokenKind.EndOfStatement) { Advance(); return (parts, false); }
                if (token.Kind == C4TokenKind.BraceClose) return (parts, false);
                if (token.Kind == C4TokenKind.BraceOpen) { Advance(); return (parts, true); }

                parts.Add(token);
                Advance();
            }

            return (parts, false);
        }

        /// <summary>Consumes a block whose opening brace has already been read.</summary>
        private void SkipBlock()
        {
            var depth = 1;
            while (!AtEnd && depth > 0)
            {
                if (Current!.Kind == C4TokenKind.BraceOpen) depth++;
                else if (Current.Kind == C4TokenKind.BraceClose) depth--;
                Advance();
            }
        }

        private void SkipStatementEnds()
        {
            while (!AtEnd && Current!.Kind == C4TokenKind.EndOfStatement) Advance();
        }

        /// <summary>
        /// A <c>properties { "key" "value" }</c> block, attached to the element whose
        /// body it sits in.
        /// <para>
        /// Merged into whatever the element already has rather than replacing it, so a
        /// second block adds to the first instead of erasing it.
        /// </para>
        /// </summary>
        private void ReadProperties(string elementId)
        {
            var read = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (parts, hasBlock) in Block())
            {
                if (hasBlock) SkipBlock();

                var values = Values(parts, 0);
                if (values.Count >= 2) read[values[0]] = values[1];
            }

            if (read.Count == 0) return;

            var index = _elements.FindIndex(element => string.Equals(element.Id, elementId, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;

            var existing = _elements[index].Properties;
            if (existing is not null)
            {
                foreach (var pair in existing) read.TryAdd(pair.Key, pair.Value);
            }

            _elements[index] = _elements[index] with { Properties = read };
        }

        private void ReadIdentifierMode(IReadOnlyList<C4Token> parts)
        {
            var mode = Values(parts, 1).FirstOrDefault();
            _hierarchical = string.Equals(mode, "hierarchical", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// What to do with a statement nothing above claimed: skip it knowingly, or
        /// report it.
        /// </summary>
        private void Unhandled(C4Token head, bool hasBlock, string where)
        {
            if (hasBlock) SkipBlock();

            if (SilentlySkipped.Contains(head.Text)) return;

            var message = UnsupportedDirectives.Contains(head.Text)
                ? $"`{head.Text}` is not read. What it would have contributed is missing from every view."
                : $"`{head.Text}` is not a construct this reader knows inside `{where}`, and was skipped.";

            Problem(head, message);
        }

        /// <summary>Where the keyword sits in a declaration statement: after the
        /// <c>id =</c> prefix when there is one, and first when there is not.</summary>
        private static int KeywordIndex(IReadOnlyList<C4Token> parts) =>
            parts.Count > 2 && parts[1].Kind == C4TokenKind.Assign ? 2 : 0;

        private static IReadOnlyList<string> Values(IReadOnlyList<C4Token> parts, int from)
        {
            var values = new List<string>();
            for (var index = from; index < parts.Count; index++)
            {
                if (parts[index].IsValue) values.Add(parts[index].Text);
            }

            return values;
        }

        private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        private static IReadOnlyList<string> SplitTags(string? tags) =>
            string.IsNullOrWhiteSpace(tags)
                ? []
                : [.. tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

        private string Anonymous(string name)
        {
            var slug = C4Slug.Of(name);
            return string.IsNullOrEmpty(slug) ? $"element-{++_anonymous}" : $"{slug}-{++_anonymous}";
        }

        /// <summary>
        /// The key a chapter will address this view by: the authored one, slugged,
        /// and made unique.
        /// <para>
        /// A view the DSL left unkeyed still gets one. Structurizr allows that and
        /// c4hero writes it, but an unkeyed view here would be a view no chapter can
        /// reference — and the reference is the whole point of the arrangement.
        /// </para>
        /// </summary>
        private string ViewKey(C4ViewKind kind, string? key, string? scope)
        {
            var candidate = Blank(key)
                ?? (Blank(scope) is { } named ? $"{kind}-{named}" : kind.ToString());

            var slug = C4Slug.Of(candidate);
            if (string.IsNullOrEmpty(slug)) slug = C4Slug.Of(kind.ToString());

            var unique = slug;
            var suffix = 1;
            while (!_viewKeys.Add(unique)) unique = $"{slug}-{++suffix}";

            return unique;
        }

        /// <summary>
        /// The identifier a relationship named, resolved to one that exists.
        /// <para>
        /// Under hierarchical identifiers a relationship inside a system may name a
        /// sibling by its local name alone, so the enclosing scope is tried first
        /// and then each scope above it, before the name is taken as absolute. That
        /// order is Structurizr's, and getting it backwards would bind a local name
        /// to a same-named element in another system.
        /// </para>
        /// </summary>
        private string? Resolve(string? name, string? scope)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (_ids.Contains(name)) return name;

            var current = scope;
            while (!string.IsNullOrEmpty(current))
            {
                var candidate = $"{current}.{name}";
                if (_ids.Contains(candidate)) return candidate;

                var cut = current.LastIndexOf('.');
                current = cut < 0 ? null : current[..cut];
            }

            // Last resort: a single element whose identifier ends with this name.
            // Ambiguity is not resolved by picking one — two matches means the
            // reference is genuinely unclear and is reported as unresolved.
            var tail = "." + name;
            var matches = _ids.Where(id => id.EndsWith(tail, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private void Problem(C4Token token, string message) =>
            _problems.Add(new C4Problem(token.Line, token.Text, message));

        private C4Workspace Build()
        {
            var relationships = new List<C4Relationship>();

            foreach (var pending in _relationships)
            {
                var source = Resolve(pending.Source, pending.Scope);
                var destination = Resolve(pending.Destination, pending.Scope);

                if (source is null || destination is null)
                {
                    var missing = source is null ? pending.Source : pending.Destination;
                    _problems.Add(new C4Problem(
                        pending.Line,
                        missing,
                        $"`{missing}` is not an element in this workspace, so this relationship is not drawn."));
                    continue;
                }

                relationships.Add(new C4Relationship(source, destination, pending.Description, pending.Technology, pending.Tags));
            }

            return new C4Workspace(_name, _description, _elements, relationships, _views, _problems, _styles);
        }
    }
}

/// <summary>One <c>element "Tag" { … }</c> style. Only the three properties
/// mermaid C4 can act on are kept.</summary>
public sealed record C4ElementStyle(string Tag, string? Background, string? Color, string? Shape);

/// <summary>
/// Slugs, for the one place a C4 name has to survive being written into a
/// reference: a view key. A chapter addresses a view as
/// <c>.arc42/_c4/backlog.dsl#container-backlog</c>, and a Structurizr key is
/// allowed to be a quoted string with spaces in it.
/// </summary>
public static class C4Slug
{
    public static string Of(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }

        return builder.ToString().Trim('-');
    }
}
