using System.Text;

namespace Backlog.SharedKernel.C4;

/// <summary>
/// Which reading of the DSL to make: whose identifiers the elements get, and so
/// which statements declare an element at all.
/// </summary>
public enum C4IdMode
{
    /// <summary>
    /// The reading the diagrams draw from. An unnamed element is <c>slug-n</c>, a
    /// view key is slugged and made unique, a view's title falls back to its
    /// description, and everything outside the supported subset is a
    /// <see cref="C4Problem"/>.
    /// </summary>
    Drawing,

    /// <summary>
    /// The reading devbook's <c>c4.mjs</c> makes, whose identifiers are the
    /// reference graph's node ids: an unnamed element is <c>~slug</c> (inside an
    /// element, <c>parent.~slug</c>; a repeat gets <c>-2</c>, <c>-3</c>), a
    /// deployment element under <c>!identifiers hierarchical</c> takes its
    /// environment's identifier as its prefix, and an element declared where
    /// <c>c4.mjs</c> does not allow it — a container outside a system, a group
    /// inside a person — is not declared. A view's <see cref="C4View.Key"/> is the
    /// key as written (empty when it has none) and its <see cref="C4View.Title"/>
    /// only what a <c>title</c> statement says; an instance takes its target's name,
    /// or its own identifier when the target does not resolve. The lexer reads
    /// comments the way <c>c4.mjs</c> does too (<see cref="C4DslLexer"/>).
    /// </summary>
    Generator
}

/// <summary>A file an <c>!include</c> named: its label, which every token read
/// from it carries, and its text.</summary>
public sealed record C4IncludedFile(string File, string Text);

/// <summary>
/// Resolves one <c>!include</c>: the target as written and the label of the file
/// it stands in. Null when it may not be read — outside the allowed folder,
/// missing, a URL — and the include is then not expanded.
/// </summary>
public delegate C4IncludedFile? C4IncludeResolver(string target, string? fromFile);

/// <summary>How <see cref="C4DslReader.Read(string?, C4ReadOptions)"/> reads.</summary>
public sealed record C4ReadOptions
{
    public static C4ReadOptions Default { get; } = new();

    public C4IdMode Ids { get; init; } = C4IdMode.Drawing;

    /// <summary>The label of the text being read, which its tokens, elements and
    /// views carry, and which an <c>!include</c> in it is resolved from.</summary>
    public string? File { get; init; }

    /// <summary>What resolves an <c>!include</c>. Without one no include is
    /// expanded, and under <see cref="C4IdMode.Drawing"/> each is reported.</summary>
    public C4IncludeResolver? Include { get; init; }
}

/// <summary>
/// Reads the subset of Structurizr DSL that c4hero writes, and the reading
/// devbook's <c>c4.mjs</c> makes of the same file (<see cref="C4IdMode"/>).
/// <para>
/// This is a second implementation of a dialect whose first implementation is
/// c4hero's TypeScript parser, in another repository, where no test here can see
/// it. <c>.devbook/arc42/adr/0004</c> names that shape of duplication as a drift hazard and
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
/// <para>
/// <c>!include</c> is expanded here, on tokens and before parsing, so every token
/// keeps its own file and line; the caller's <see cref="C4IncludeResolver"/>
/// decides which files may be read.
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

    private static readonly Dictionary<string, C4ElementKind> DeploymentKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["deploymentNode"] = C4ElementKind.DeploymentNode,
        ["infrastructureNode"] = C4ElementKind.InfrastructureNode,
        ["containerInstance"] = C4ElementKind.ContainerInstance,
        ["softwareSystemInstance"] = C4ElementKind.SoftwareSystemInstance
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
    /// configuration, layout animation steps, arbitrary key/value properties, and the
    /// <c>!docs</c> and <c>!adrs</c> folders — documentation attached to a scope,
    /// which c4hero 0.7 adds on save. Held as a list so they are skipped knowingly
    /// rather than falling through to the problem report and burying the constructs
    /// that matter.</summary>
    private static readonly HashSet<string> SilentlySkipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "configuration", "properties", "animation", "branding", "terminology", "default", "url", "!docs", "!adrs"
    };

    /// <summary>Directives that pull in, generate, or decorate content this reader
    /// has no way to honour. Each one reaches the reader as a named problem rather
    /// than as silence, because a workspace assembled by an <c>!include</c> no
    /// resolver could read is a workspace this reader has only partly seen.</summary>
    private static readonly HashSet<string> UnsupportedDirectives = new(StringComparer.OrdinalIgnoreCase)
    {
        "!include", "!script", "!plugin", "!constant", "!impliedRelationships", "!ref", "!extend"
    };

    public static C4Workspace Read(string? source) => Read(source, C4ReadOptions.Default);

    public static C4Workspace Read(string? source, C4ReadOptions options)
    {
        var tokens = C4DslLexer.Tokenize(source, options.Ids, options.File);
        if (options.Include is { } include)
        {
            tokens = ExpandIncludes(tokens, include, options.Ids, options.File is null ? [] : [options.File]);
        }

        return new Parser(tokens, options.Ids).ParseWorkspace();
    }

    /// <summary>
    /// Splices each resolvable <c>!include</c> — the first word of a statement,
    /// followed by exactly one value — with the named file's tokens, recursively.
    /// A file already being read is not read again. An include that does not
    /// resolve is left in place under <see cref="C4IdMode.Drawing"/>, for the parser
    /// to report, and dropped under <see cref="C4IdMode.Generator"/>, as
    /// <c>c4.mjs</c> drops it.
    /// </summary>
    private static IReadOnlyList<C4Token> ExpandIncludes(
        IReadOnlyList<C4Token> tokens,
        C4IncludeResolver include,
        C4IdMode mode,
        IReadOnlyList<string> stack)
    {
        var output = new List<C4Token>();
        var atStart = true;

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (atStart && token.IsWord("!include"))
            {
                var parts = new List<C4Token>();
                var next = index + 1;
                while (next < tokens.Count && tokens[next].Kind is not (C4TokenKind.EndOfStatement or C4TokenKind.BraceOpen or C4TokenKind.BraceClose))
                {
                    parts.Add(tokens[next++]);
                }

                var ended = next < tokens.Count && tokens[next].Kind == C4TokenKind.EndOfStatement;
                var target = parts.Count == 1 && parts[0].IsValue ? parts[0].Text : null;
                var found = target is null ? null : include(target, token.File);

                if (found is not null && !stack.Contains(found.File, StringComparer.Ordinal))
                {
                    var included = C4DslLexer.Tokenize(found.Text, mode, found.File);
                    output.AddRange(ExpandIncludes(included, include, mode, [.. stack, found.File]));
                }
                else if (mode == C4IdMode.Drawing)
                {
                    output.Add(token);
                    output.AddRange(parts);
                    if (ended) output.Add(tokens[next]);
                }

                index = ended ? next : next - 1;
                atStart = true;
                continue;
            }

            output.Add(token);
            atStart = token.Kind is C4TokenKind.EndOfStatement or C4TokenKind.BraceOpen or C4TokenKind.BraceClose;
        }

        return output;
    }

    /// <summary>Where a statement may declare an element in the generator's
    /// reading, by the kind of the element whose block it is in.</summary>
    private static bool Fits(C4ElementKind kind, C4ElementKind? parent) => kind switch
    {
        C4ElementKind.Person or C4ElementKind.SoftwareSystem => parent is null,
        C4ElementKind.Container => parent is C4ElementKind.SoftwareSystem,
        C4ElementKind.Component => parent is C4ElementKind.Container,
        C4ElementKind.DeploymentNode => parent is null or C4ElementKind.DeploymentNode,
        _ => parent is C4ElementKind.DeploymentNode
    };

    private sealed class Parser(IReadOnlyList<C4Token> tokens, C4IdMode mode)
    {
        private readonly bool _generator = mode == C4IdMode.Generator;
        private readonly List<C4Element> _elements = [];
        private readonly List<PendingRelationship> _relationships = [];
        private readonly List<C4View> _views = [];
        private readonly List<C4Problem> _problems = [];
        private readonly List<C4ElementStyle> _styles = [];
        private readonly HashSet<string> _ids = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _viewKeys = new(StringComparer.OrdinalIgnoreCase);

        // The generator's own bookkeeping: its named elements by lower-cased
        // identifier, first declaration first; the ids it made up; the instances
        // whose target is resolved once the whole model is read.
        private readonly Dictionary<string, C4Element> _named = new(StringComparer.Ordinal);
        private readonly HashSet<string> _anonymousIds = new(StringComparer.Ordinal);
        private readonly List<PendingInstance> _instances = [];
        private bool _modelSeen;

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

        private sealed record PendingInstance(int Index, C4ElementKind Of, string? Scope);

        /// <param name="Id">The identifier assigned to the environment, if any: the
        /// generator's prefix for what is declared at its top.</param>
        private sealed record Environment(string? Id, string Name);

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

            // The generator reads no workspace that extends another.
            if (_generator && parts.Count > 1 && parts[1].IsWord("extends")) return Build();

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
                    if (hasBlock)
                    {
                        ParseModel(null, null);
                        _modelSeen = true;
                    }

                    continue;
                }

                if (head.IsWord("views"))
                {
                    if (hasBlock) ParseViews();
                    continue;
                }

                if (head.IsWord("!identifiers"))
                {
                    if (!_generator || !_modelSeen) ReadIdentifierMode(parts);
                    continue;
                }

                if (head.IsWord("name")) { _name = Values(parts, 1).FirstOrDefault() ?? _name; continue; }
                if (head.IsWord("description")) { _description = Values(parts, 1).FirstOrDefault() ?? _description; continue; }

                Unhandled(head, hasBlock, "workspace");
            }
        }

        // ---- model -------------------------------------------------------------

        /// <param name="parent">The element whose block this is, or null at the top
        /// of the model. Also the element a <c>properties</c> block in this scope
        /// belongs to: at the top of the model there is no such element, and the block
        /// is skipped as before.</param>
        private void ParseModel(C4Element? parent, string? group)
        {
            foreach (var (parts, hasBlock) in Block())
            {
                var head = parts[0];

                if (head.IsWord("!identifiers") && (!_generator || parent is null))
                {
                    if (!_generator || !_modelSeen) ReadIdentifierMode(parts);
                    continue;
                }

                if (_generator && head.IsWord("tags") && parent is not null && !hasBlock) continue;

                // Read rather than skipped, and only inside an element. This is where
                // c4hero keeps what Structurizr has no field for — the owning team, a
                // lifecycle status — and the inspector shows both.
                if (head.IsWord("properties") && parent is not null)
                {
                    if (hasBlock) ReadProperties(parent.Id);
                    continue;
                }

                if (head.IsWord("group") && (!_generator || parent?.Kind is null or C4ElementKind.SoftwareSystem or C4ElementKind.Container))
                {
                    var name = Values(parts, 1).FirstOrDefault();
                    if (hasBlock) ParseModel(parent, name ?? group);
                    continue;
                }

                if (TryRelationship(parts, parent?.Id, parent?.Id)) { if (hasBlock) SkipBlock(); continue; }

                var keyword = parts[KeywordIndex(parts)];

                if (keyword.IsWord("deploymentEnvironment"))
                {
                    if (_generator && parent is null && group is null)
                    {
                        ReadEnvironment(parts, hasBlock);
                        continue;
                    }

                    if (!_generator && ReferenceEquals(keyword, head))
                    {
                        var environment = Values(parts, 1).FirstOrDefault() ?? "Default";
                        if (hasBlock) ParseDeployment(new Environment(null, environment), null, group);
                        continue;
                    }
                }

                if (keyword.Kind == C4TokenKind.Word && StaticKeywords.TryGetValue(keyword.Text, out var kind))
                {
                    if (_generator && !Fits(kind, parent?.Kind))
                    {
                        if (hasBlock) SkipBlock();
                        continue;
                    }

                    var element = Declare(parts, kind, parent, group, null);
                    if (hasBlock)
                    {
                        if (element is null) SkipBlock();
                        else ParseModel(element, group);
                    }

                    continue;
                }

                Unhandled(head, hasBlock, "model");
            }
        }

        /// <summary>The generator's <c>[id =] deploymentEnvironment "Name" { … }</c>,
        /// only at the top of the model and outside a group.</summary>
        private void ReadEnvironment(IReadOnlyList<C4Token> parts, bool hasBlock)
        {
            var keywordAt = KeywordIndex(parts);
            var assigned = keywordAt == 0 ? null : parts[0].Text;
            var name = Values(parts, keywordAt + 1).ElementAtOrDefault(0);

            if (string.IsNullOrEmpty(name))
            {
                if (hasBlock) SkipBlock();
                return;
            }

            if (hasBlock) ParseDeployment(new Environment(assigned, name), null, null);
        }

        private void ParseDeployment(Environment environment, C4Element? parent, string? group)
        {
            foreach (var (parts, hasBlock) in Block())
            {
                var head = parts[0];

                if (_generator && head.IsWord("tags") && parent is not null && !hasBlock) continue;

                if (head.IsWord("group"))
                {
                    var name = Values(parts, 1).FirstOrDefault();
                    if (hasBlock) ParseDeployment(environment, parent, name ?? group);
                    continue;
                }

                if (TryRelationship(parts, parent?.Id, parent?.Id ?? environment.Id)) { if (hasBlock) SkipBlock(); continue; }

                // Deployment declarations carry an identifier as often as static ones
                // do — `host = deploymentNode "…"` — so the keyword is not always the
                // first token, and reading it as though it were reports every named
                // node as an unknown construct.
                var keyword = parts[KeywordIndex(parts)];

                if (keyword.Kind != C4TokenKind.Word || !DeploymentKeywords.TryGetValue(keyword.Text, out var kind))
                {
                    Unhandled(keyword, hasBlock, "deploymentEnvironment");
                    continue;
                }

                if (_generator && !Fits(kind, parent?.Kind))
                {
                    if (hasBlock) SkipBlock();
                    continue;
                }

                var instance = kind is C4ElementKind.ContainerInstance or C4ElementKind.SoftwareSystemInstance;
                var element = instance
                    ? DeclareInstance(parts, kind, parent, group, environment)
                    : Declare(parts, kind, parent, group, environment);

                if (!hasBlock) continue;

                if (element is null) SkipBlock();
                else if (kind is C4ElementKind.DeploymentNode) ParseDeployment(environment, element, group);
                else if (_generator) Leaf(element);
                else if (instance) SkipBlock();
                else ParseDeployment(environment, element, group);
            }
        }

        /// <summary>The generator's block of an element that holds no other element:
        /// tags and relationships, the element their implicit source.</summary>
        private void Leaf(C4Element element)
        {
            foreach (var (parts, hasBlock) in Block())
            {
                if (parts[0].IsWord("tags") && !hasBlock) continue;
                if (TryRelationship(parts, element.Id, element.Id)) { if (hasBlock) SkipBlock(); continue; }
                Unhandled(parts[0], hasBlock, "element");
            }
        }

        /// <summary>
        /// Adds one element, read from <c>id = keyword "Name" …</c> or its anonymous
        /// form, and returns it: its identifier is what everything else names it by.
        /// <para>
        /// Under <c>!identifiers hierarchical</c> that identifier is the parent's
        /// with the declared name appended, which is what lets the same local name
        /// be declared in several systems — c4hero's fixture does exactly that, and
        /// under flat identifiers it would be a collision.
        /// </para>
        /// </summary>
        private C4Element? Declare(IReadOnlyList<C4Token> parts, C4ElementKind kind, C4Element? parent, string? group, Environment? environment)
        {
            var keywordAt = KeywordIndex(parts);
            var keyword = parts[keywordAt];
            var assigned = keywordAt == 0 ? null : parts[0].Text;
            var arguments = Values(parts, keywordAt + 1);
            var name = arguments.ElementAtOrDefault(0);

            if (string.IsNullOrWhiteSpace(name))
            {
                Problem(keyword, $"`{keyword.Text}` was declared with no name.");
                return null;
            }

            // person and softwareSystem take name/description/tags; everything with
            // a technology takes name/description/technology/tags. Reading the same
            // positions for both would file a system's tags as its technology.
            var carriesTechnology = kind is not (C4ElementKind.Person or C4ElementKind.SoftwareSystem);
            var description = Blank(arguments.ElementAtOrDefault(1));
            var technology = carriesTechnology ? Blank(arguments.ElementAtOrDefault(2)) : null;
            var tags = SplitTags(carriesTechnology ? arguments.ElementAtOrDefault(3) : arguments.ElementAtOrDefault(2));

            var id = _generator
                ? GeneratorId(assigned ?? $"~{GeneratorSlug(name)}", parent, environment)
                : DrawingId(assigned ?? Anonymous(name), parent);

            var element = new C4Element(id, kind, name, description, technology, tags, parent?.Id, group, null, environment?.Name)
            {
                File = keyword.File,
                Line = keyword.Line,
                Anonymous = assigned is null
            };

            return Register(element, keyword);
        }

        private C4Element? DeclareInstance(IReadOnlyList<C4Token> parts, C4ElementKind kind, C4Element? parent, string? group, Environment environment)
        {
            var keywordAt = KeywordIndex(parts);
            var keyword = parts[keywordAt];
            var assigned = keywordAt == 0 ? null : parts[0].Text;
            var arguments = Values(parts, keywordAt + 1);
            var target = arguments.ElementAtOrDefault(0);

            if (_generator ? string.IsNullOrEmpty(target) : string.IsNullOrWhiteSpace(target))
            {
                Problem(keyword, $"`{keyword.Text}` names nothing to instantiate.");
                return null;
            }

            // The instance's own identifier is synthesised unless one was assigned:
            // `containerInstance api` names the container it instantiates, not
            // itself, so reusing that identifier would shadow the container.
            var id = _generator
                ? GeneratorId(assigned ?? $"~{GeneratorSlug(target!)}-instance", parent, environment)
                : DrawingId(assigned ?? Anonymous($"{target}-instance"), parent);

            var element = new C4Element(
                id,
                kind,
                target!,
                null,
                null,
                SplitTags(arguments.ElementAtOrDefault(1)),
                parent?.Id,
                group,
                target,
                environment.Name)
            {
                File = keyword.File,
                Line = keyword.Line,
                Anonymous = assigned is null
            };

            if (!_generator)
            {
                if (!_ids.Add(id)) return null;
                _elements.Add(element);
                return element;
            }

            element = Register(element, keyword);
            var of = kind is C4ElementKind.ContainerInstance ? C4ElementKind.Container : C4ElementKind.SoftwareSystem;
            _instances.Add(new PendingInstance(_elements.Count - 1, of, parent?.Id ?? environment.Id));
            return element;
        }

        private string DrawingId(string local, C4Element? parent) =>
            _hierarchical && parent is not null ? $"{parent.Id}.{local}" : local;

        /// <summary>The generator's identifier: an unnamed element is always under
        /// its parent, and under hierarchical identifiers everything is under its
        /// parent, or at the top of an environment under the environment.</summary>
        private string GeneratorId(string local, C4Element? parent, Environment? environment)
        {
            if (local.StartsWith('~') && parent is not null) return $"{parent.Id}.{local}";
            var prefix = parent?.Id ?? environment?.Id;
            return _hierarchical && prefix is not null ? $"{prefix}.{local}" : local;
        }

        /// <summary>The generator's <c>slug</c>: what an unnamed element's <c>~</c>
        /// identifier is made of.</summary>
        private static string GeneratorSlug(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var character in value.ToLowerInvariant())
            {
                if (character is (>= 'a' and <= 'z') or (>= '0' and <= '9')) builder.Append(character);
                else if (builder.Length == 0 || builder[^1] != '-') builder.Append('-');
            }

            var slug = builder.ToString().Trim('-');
            return slug.Length == 0 ? "element" : slug;
        }

        /// <summary>
        /// Keeps one declared element. The drawing keeps the first of a repeated
        /// identifier and reports the rest; the generator keeps every one, resolves
        /// a name to the first, and numbers a repeated made-up identifier.
        /// </summary>
        private C4Element Register(C4Element element, C4Token keyword)
        {
            if (!_generator)
            {
                if (!_ids.Add(element.Id))
                {
                    Problem(keyword, $"`{element.Id}` is declared more than once. The first declaration is the one everything else resolves to.");
                    return element;
                }

                _elements.Add(element);
                return element;
            }

            if (element.Anonymous)
            {
                var id = element.Id;
                var n = 1;
                while (_anonymousIds.Contains(id.ToLowerInvariant())) id = $"{element.Id}-{++n}";
                _anonymousIds.Add(id.ToLowerInvariant());
                element = element with { Id = id };
            }
            else
            {
                _named.TryAdd(element.Id.ToLowerInvariant(), element);
            }

            _ids.Add(element.Id);
            _elements.Add(element);
            return element;
        }

        /// <summary>
        /// Reads <c>[id =] source -&gt; destination "Description" "Technology" "Tags"</c>,
        /// including the two forms that leave the source implicit inside an element
        /// block: a leading arrow, and <c>this</c>.
        /// </summary>
        private bool TryRelationship(IReadOnlyList<C4Token> parts, string? implicitSource, string? scope)
        {
            var arrow = -1;
            for (var index = 0; index < parts.Count; index++)
            {
                if (parts[index].Kind == C4TokenKind.Arrow) { arrow = index; break; }
            }

            if (arrow < 0) return false;

            var at = parts.Count > 1 && parts[1].Kind == C4TokenKind.Assign ? 2 : 0;
            var source = arrow <= at || parts[arrow - 1].IsWord("this") ? implicitSource : parts[arrow - 1].Text;
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
            var head = parts[0];
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

            if (_generator)
            {
                ParseGeneratorView(kind, head, key, scope, description, environment, hasBlock);
                return;
            }

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
                environment)
            {
                File = head.File,
                Line = head.Line
            });
        }

        /// <summary>The generator's view: its key as written and the title its
        /// <c>title</c> statement gives, nothing else of its body.</summary>
        private void ParseGeneratorView(C4ViewKind kind, C4Token head, string? key, string? scope, string? description, string? environment, bool hasBlock)
        {
            string? title = null;

            if (hasBlock)
            {
                foreach (var (body, bodyBlock) in Block())
                {
                    if (bodyBlock) SkipBlock();
                    else if (body[0].IsWord("title")) title = Blank(Values(body, 1).FirstOrDefault());
                }
            }

            _views.Add(new C4View(Blank(key) ?? string.Empty, kind, scope, title, description, [], [], false, null, [], environment)
            {
                File = head.File,
                Line = head.Line
            });
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
            if (_generator) return ResolveNamed(name, scope)?.Id;

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

        /// <summary>The generator's resolution: a named element, from the innermost
        /// scope outward under hierarchical identifiers, then as written.</summary>
        private C4Element? ResolveNamed(string? name, string? scope)
        {
            if (string.IsNullOrEmpty(name)) return null;

            var current = _hierarchical ? scope : null;
            while (current is not null)
            {
                if (_named.TryGetValue($"{current}.{name}".ToLowerInvariant(), out var hit)) return hit;
                var cut = current.LastIndexOf('.');
                current = cut < 0 ? null : current[..cut];
            }

            return _named.GetValueOrDefault(name.ToLowerInvariant());
        }

        private void Problem(C4Token token, string message) =>
            _problems.Add(new C4Problem(token.Line, token.Text, message));

        private C4Workspace Build()
        {
            foreach (var pending in _instances)
            {
                var element = _elements[pending.Index];
                _elements[pending.Index] = ResolveNamed(element.InstanceOfId, pending.Scope) is { } target && target.Kind == pending.Of
                    ? element with { InstanceOfId = target.Id, Name = target.Name }
                    : element with { Name = element.Id };
            }

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
/// <c>.devbook/arc42/workspace.dsl#container-backlog</c>, and a Structurizr key is
/// allowed to be a quoted string with spaces in it.
/// </summary>
public static class C4Slug
{
    public static string Of(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }

        return builder.ToString().Trim('-');
    }
}
