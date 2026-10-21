using System.Text;
using System.Text.RegularExpressions;

namespace Backlog.Infrastructure.Devbook.Building;

/// <summary>
/// The arc42 folder's Structurizr DSL workspace, read the way devbook's
/// <c>c4.mjs</c> reads it, for the C4 part of the reference graph.
///
/// <para>A port of the reader's lexer, its <c>!include</c> expansion and the walk
/// of its statements — everything that decides which elements, relationships and
/// views exist, what each is called, and on which file and line it stands. Its
/// problems are not ported: the database keeps none of them, and reporting them is
/// the checker's job (local ADR 0015). So a construct the reader would report and
/// skip is skipped here in silence, exactly where it skips it.</para>
///
/// <para>Not the component library's <c>C4DslReader</c>: an infrastructure project
/// may not reference a UI one, and the graph's node ids are the generator's
/// — its hierarchical identifiers, the <c>~slug</c> it gives an unnamed element —
/// which that reader, written for drawing, has no reason to match.</para>
/// </summary>
internal static partial class DevbookC4Workspace
{
    /// <summary>Reads the workspace at <paramref name="workspacePath"/>, a
    /// repository-relative path. Every file name in the result is repository-relative
    /// with <c>/</c>, as the generator's <c>readWorkspace</c> labels them.</summary>
    public static C4Reading Read(string repositoryRoot, string workspacePath)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var absolute = Path.GetFullPath(Path.Combine(root, workspacePath.Replace('/', Path.DirectorySeparatorChar)));
        var allowed = Path.Combine(Path.GetDirectoryName(absolute)!, "c4");

        string Label(string full) => Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/');

        (string File, string Text)? Include(string target, string fromFile)
        {
            if (SchemePrefix().IsMatch(target) || Path.IsPathRooted(target) || target.StartsWith('/') || target.StartsWith('\\')) return null;

            var from = Path.GetFullPath(Path.Combine(root, fromFile.Replace('/', Path.DirectorySeparatorChar)));
            var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(from)!, target.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(allowed, resolved) || !File.Exists(resolved)) return null;

            // A link out of c4/ is refused by the generator after it resolves the
            // real paths; the link's own target is what is checked here.
            var real = new FileInfo(resolved).LinkTarget is { } link
                ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(resolved)!, link))
                : resolved;
            if (!Inside(allowed, real)) return null;

            return (Label(resolved), File.ReadAllText(resolved));
        }

        var file = Label(absolute);
        var tokens = ExpandIncludes(Tokenize(File.ReadAllText(absolute), file), Include, [file]);
        var parser = new Parser(tokens);
        parser.Workspace();
        parser.Finish();
        return new C4Reading(parser.Elements, parser.Relationships, parser.Views);
    }

    private static bool Inside(string folder, string path)
    {
        var relative = Path.GetRelativePath(folder, path);
        return relative == "." || (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative));
    }

    // ── lexer ──────────────────────────────────────────────────────────────
    //
    // Line-oriented: a statement ends where its line ends unless it opens a
    // brace, so a newline is a token. `#` and `//` open a comment only at the
    // start of a line, and `/* */` may sit anywhere outside a string.

    private enum Kind
    {
        Word,
        Text,
        Arrow,
        Assign,
        Open,
        Close,
        End
    }

    private sealed record Token(Kind Kind, string Text, int Line, string File);

    private static bool IsBreak(char c) => c is ' ' or '\t' or '\n' or '"' or '{' or '}';

    private static List<Token> Tokenize(string source, string file)
    {
        var tokens = new List<Token>();
        var text = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var line = 1;
        var i = 0;
        var pending = false;
        var lineStart = true;

        void End()
        {
            if (pending) tokens.Add(new Token(Kind.End, "\n", line, file));
            pending = false;
        }

        void Add(Kind kind, string value)
        {
            tokens.Add(new Token(kind, value, line, file));
            pending = true;
            lineStart = false;
        }

        char At(int index) => index < text.Length ? text[index] : '\0';

        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\n') { End(); line++; i++; lineStart = true; continue; }
            if (c is ' ' or '\t') { i++; continue; }
            if (lineStart && (c == '#' || (c == '/' && At(i + 1) == '/')))
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }

            if (c == '/' && At(i + 1) == '*')
            {
                i += 2;
                while (i < text.Length && !(text[i] == '*' && At(i + 1) == '/'))
                {
                    if (text[i] == '\n') { End(); line++; lineStart = true; }
                    i++;
                }

                i = Math.Min(i + 2, text.Length);
                continue;
            }

            if (c == '"')
            {
                var value = new StringBuilder();
                i++;
                while (i < text.Length && text[i] != '"' && text[i] != '\n')
                {
                    if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] != '\n')
                    {
                        var escaped = text[i + 1];
                        value.Append(escaped switch { 'n' => '\n', 't' => '\t', _ => escaped });
                        i += 2;
                        continue;
                    }

                    value.Append(text[i++]);
                }

                if (At(i) == '"') i++;
                Add(Kind.Text, value.ToString());
                continue;
            }

            if (c == '{') { Add(Kind.Open, "{"); End(); i++; continue; }
            if (c == '}') { End(); Add(Kind.Close, "}"); End(); i++; continue; }
            if (c == '-' && At(i + 1) == '>') { Add(Kind.Arrow, "->"); i += 2; continue; }
            if (c == '=' && At(i + 1) != '=') { Add(Kind.Assign, "="); i++; continue; }

            var start = i;
            while (i < text.Length && !IsBreak(text[i])) i++;
            if (i == start) i++;
            Add(Kind.Word, text[start..i]);
        }

        End();
        return tokens;
    }

    // ── includes ───────────────────────────────────────────────────────────
    //
    // `!include` splices the named file's statements in where it stands, on
    // tokens, before parsing — so every token keeps its own file and line.

    private static List<Token> ExpandIncludes(
        List<Token> tokens,
        Func<string, string, (string File, string Text)?> include,
        IReadOnlyList<string> stack)
    {
        var output = new List<Token>();
        var atStart = true;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (atStart && token.Kind == Kind.Word && string.Equals(token.Text, "!include", StringComparison.OrdinalIgnoreCase))
            {
                var parts = new List<Token>();
                var j = i + 1;
                while (j < tokens.Count && tokens[j].Kind is not (Kind.End or Kind.Open or Kind.Close)) parts.Add(tokens[j++]);

                var target = parts.Count == 1 && IsValue(parts[0]) ? parts[0].Text : null;
                if (target is not null && include(target, token.File) is { } found && !stack.Contains(found.File, StringComparer.Ordinal))
                {
                    output.AddRange(ExpandIncludes(Tokenize(found.Text, found.File), include, [.. stack, found.File]));
                }

                i = j < tokens.Count && tokens[j].Kind == Kind.End ? j : j - 1;
                atStart = true;
                continue;
            }

            output.Add(token);
            atStart = token.Kind is Kind.End or Kind.Open or Kind.Close;
        }

        return output;
    }

    // ── parser ─────────────────────────────────────────────────────────────

    private static bool IsValue(Token? token) => token is { Kind: Kind.Word or Kind.Text };

    private static string? Lower(Token? token) => token is { Kind: Kind.Word } ? token.Text.ToLowerInvariant() : null;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? At(IReadOnlyList<string> values, int index) => index < values.Count ? values[index] : null;

    /// <summary>The generator's <c>slug</c>: what an unnamed element's <c>~</c>
    /// identifier is made of.</summary>
    private static string Slug(string value)
    {
        var slug = NotSlugRun().Replace(value.ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "element" : slug;
    }

    private sealed record Spec(string Kind, int Arguments, IReadOnlyList<string?> Inside, bool Technology);

    private static readonly Dictionary<string, Spec> Static = new(StringComparer.Ordinal)
    {
        ["person"] = new("person", 3, [null], Technology: false),
        ["softwaresystem"] = new("softwareSystem", 3, [null], Technology: false),
        ["container"] = new("container", 4, ["softwareSystem"], Technology: true),
        ["component"] = new("component", 4, ["container"], Technology: true)
    };

    private static readonly Dictionary<string, Spec> Deployment = new(StringComparer.Ordinal)
    {
        ["deploymentnode"] = new("deploymentNode", 5, [null, "deploymentNode"], Technology: true),
        ["infrastructurenode"] = new("infrastructureNode", 4, ["deploymentNode"], Technology: true)
    };

    private static readonly Dictionary<string, (string Kind, string Of)> Instances = new(StringComparer.Ordinal)
    {
        ["containerinstance"] = ("containerInstance", "container"),
        ["softwaresysteminstance"] = ("softwareSystemInstance", "softwareSystem")
    };

    private static readonly Dictionary<string, (string Kind, bool Scoped, bool Environment)> ViewSpecs = new(StringComparer.Ordinal)
    {
        ["systemlandscape"] = ("systemLandscape", false, false),
        ["systemcontext"] = ("systemContext", true, false),
        ["container"] = ("container", true, false),
        ["component"] = ("component", true, false),
        ["dynamic"] = ("dynamic", true, false),
        ["deployment"] = ("deployment", true, true)
    };

    private sealed record Statement(List<Token> Parts, bool Block);

    private sealed record Environment(string? Id, string Name);

    private sealed class Parser(List<Token> tokens)
    {
        private int _i;
        private bool _hierarchical;
        private bool _modelSeen;
        private readonly Dictionary<string, C4Element> _byId = new(StringComparer.Ordinal);
        private readonly HashSet<string> _anonymousIds = new(StringComparer.Ordinal);
        private readonly List<(C4Element Element, string Of, string? Scope)> _pendingInstances = [];
        private readonly List<(string Source, string Destination, string? Scope)> _pendingRelationships = [];

        public List<C4Element> Elements { get; } = [];
        public List<C4Relationship> Relationships { get; } = [];
        public List<C4View> Views { get; } = [];

        private Token? Current => _i < tokens.Count ? tokens[_i] : null;

        private Statement Next()
        {
            var parts = new List<Token>();
            while (_i < tokens.Count)
            {
                var token = tokens[_i];
                if (token.Kind == Kind.End) { _i++; return new Statement(parts, false); }
                if (token.Kind == Kind.Close) return new Statement(parts, false);
                if (token.Kind == Kind.Open) { _i++; return new Statement(parts, true); }
                parts.Add(token);
                _i++;
            }

            return new Statement(parts, false);
        }

        // Every statement of the block just opened; leaves the cursor past its `}`.
        private IEnumerable<Statement> Block()
        {
            while (_i < tokens.Count && tokens[_i].Kind != Kind.Close)
            {
                if (tokens[_i].Kind == Kind.End) { _i++; continue; }
                var statement = Next();
                if (statement.Parts.Count == 0)
                {
                    if (statement.Block) Skip();
                    continue;
                }

                yield return statement;
            }

            if (_i < tokens.Count) _i++;
        }

        private void Skip()
        {
            var depth = 1;
            while (_i < tokens.Count && depth > 0)
            {
                var kind = tokens[_i].Kind;
                if (kind == Kind.Open) depth++;
                else if (kind == Kind.Close) depth--;
                _i++;
            }
        }

        private void Unknown(bool block)
        {
            if (block) Skip();
        }

        // Every value from `from` on; the generator reads past its maximum too.
        private static List<string> Values(List<Token> parts, int from) =>
            [.. parts.Skip(from).Where(part => IsValue(part)).Select(part => part.Text)];

        public void Workspace()
        {
            while (Current is { Kind: Kind.End }) _i++;
            if (Lower(Current) != "workspace") return;

            var statement = Next();
            if (statement.Parts.Count > 1 && Lower(statement.Parts[1]) == "extends") return;
            if (!statement.Block) return;

            foreach (var s in Block())
            {
                var word = Lower(s.Parts[0]);
                if (word == "!identifiers") Identifiers(s.Parts);
                else if (word == "model" && s.Block) { Model(null, null); _modelSeen = true; }
                else if (word == "views" && s.Block) ViewsBlock();
                else Unknown(s.Block);
            }
        }

        private void Identifiers(List<Token> parts)
        {
            if (_modelSeen) return;
            _hierarchical = string.Equals(At(Values(parts, 1), 0), "hierarchical", StringComparison.OrdinalIgnoreCase);
        }

        // ── model ──

        private void Model(C4Element? parent, string? group)
        {
            foreach (var s in Block())
            {
                var word = Lower(s.Parts[0]);
                if (word == "!identifiers" && parent is null) { Identifiers(s.Parts); continue; }
                if (word == "tags" && parent is not null && !s.Block) continue;
                if (word == "group" && parent?.Kind is null or "softwareSystem" or "container")
                {
                    if (s.Block) Model(parent, group ?? Blank(At(Values(s.Parts, 1), 0)));
                    continue;
                }

                if (Relationship(s, parent, environment: null)) continue;

                var keyword = KeywordOf(s.Parts);
                if (Lower(keyword) == "deploymentenvironment" && parent is null && group is null) { EnvironmentBlock(s); continue; }

                if (Lower(keyword) is { } name && Static.TryGetValue(name, out var spec))
                {
                    if (!spec.Inside.Contains(parent?.Kind))
                    {
                        if (s.Block) Skip();
                        continue;
                    }

                    var element = Declare(s.Parts, spec, parent, environment: null);
                    if (s.Block)
                    {
                        if (element is not null) Model(element, null);
                        else Skip();
                    }

                    continue;
                }

                Unknown(s.Block);
            }
        }

        private void EnvironmentBlock(Statement s)
        {
            var head = KeywordOf(s.Parts);
            var assigned = !ReferenceEquals(head, s.Parts[0]) ? s.Parts[0].Text : null;
            var name = At(Values(s.Parts, s.Parts.IndexOf(head) + 1), 0);
            if (string.IsNullOrEmpty(name))
            {
                if (s.Block) Skip();
                return;
            }

            if (s.Block) DeploymentBlock(new Environment(assigned, name), null);
        }

        private void DeploymentBlock(Environment environment, C4Element? parent)
        {
            foreach (var s in Block())
            {
                var word = Lower(s.Parts[0]);
                if (word == "tags" && parent is not null && !s.Block) continue;
                if (word == "group")
                {
                    if (s.Block) DeploymentBlock(environment, parent);
                    continue;
                }

                if (Relationship(s, parent, environment)) continue;

                var keyword = KeywordOf(s.Parts);
                var name = Lower(keyword);
                Spec? spec = name is not null && Deployment.TryGetValue(name, out var found) ? found : null;
                (string Kind, string Of)? instance = name is not null && Instances.TryGetValue(name, out var of) ? of : null;
                if (spec is null && instance is null)
                {
                    Unknown(s.Block);
                    continue;
                }

                IReadOnlyList<string?> allowed = spec is not null ? spec.Inside : ["deploymentNode"];
                if (!allowed.Contains(parent?.Kind))
                {
                    if (s.Block) Skip();
                    continue;
                }

                var element = spec is not null
                    ? Declare(s.Parts, spec, parent, environment)
                    : Instance(s.Parts, instance!.Value, parent, environment);
                if (!s.Block) continue;

                if (element is null) Skip();
                else if (spec?.Kind == "deploymentNode") DeploymentBlock(environment, element);
                else Leaf(element);
            }
        }

        // The block of an element that holds no other element: tags and relationships.
        private void Leaf(C4Element element)
        {
            foreach (var s in Block())
            {
                if (Lower(s.Parts[0]) == "tags" && !s.Block) continue;
                if (Relationship(s, element, environment: null)) continue;
                Unknown(s.Block);
            }
        }

        // The declaring keyword: after `id =` when there is one.
        private static Token KeywordOf(List<Token> parts) =>
            parts.Count > 2 && parts[1].Kind == Kind.Assign ? parts[2] : parts[0];

        private string IdFor(string local, C4Element? parent, Environment? environment)
        {
            if (local.StartsWith('~') && parent is not null) return $"{parent.Id}.{local}";
            var prefix = parent?.Id ?? environment?.Id;
            return _hierarchical && prefix is not null ? $"{prefix}.{local}" : local;
        }

        // An unnamed element gets `~<slug>`, which no reference can spell, made
        // unique among the unnamed ones by a `-<n>` suffix.
        private C4Element Register(C4Element element, bool anonymous)
        {
            if (anonymous)
            {
                var id = element.Id;
                var n = 1;
                while (_anonymousIds.Contains(id.ToLowerInvariant())) id = $"{element.Id}-{++n}";
                element.Id = id;
                _anonymousIds.Add(id.ToLowerInvariant());
                Elements.Add(element);
                return element;
            }

            _byId.TryAdd(element.Id.ToLowerInvariant(), element);
            Elements.Add(element);
            return element;
        }

        private C4Element? Declare(List<Token> parts, Spec spec, C4Element? parent, Environment? environment)
        {
            var keyword = KeywordOf(parts);
            var at = parts.IndexOf(keyword);
            var assigned = at == 2 ? parts[0].Text : null;
            var values = Values(parts, at + 1);
            var name = Blank(At(values, 0));
            if (name is null) return null;

            var element = new C4Element(IdFor(assigned ?? $"~{Slug(name)}", parent, environment), spec.Kind, name, parent?.Id, keyword.File, keyword.Line)
            {
                Anonymous = assigned is null
            };
            return Register(element, assigned is null);
        }

        private C4Element? Instance(List<Token> parts, (string Kind, string Of) spec, C4Element? parent, Environment? environment)
        {
            var keyword = KeywordOf(parts);
            var at = parts.IndexOf(keyword);
            var assigned = at == 2 ? parts[0].Text : null;
            var target = At(Values(parts, at + 1), 0);
            if (string.IsNullOrEmpty(target)) return null;

            var element = new C4Element(IdFor(assigned ?? $"~{Slug(target)}-instance", parent, environment), spec.Kind, null, parent?.Id, keyword.File, keyword.Line)
            {
                Anonymous = assigned is null,
                InstanceOf = target
            };
            _pendingInstances.Add((element, spec.Of, parent?.Id ?? environment?.Id));
            return Register(element, assigned is null);
        }

        // `[id =] source -> destination ["description"] ["technology"] ["tags"]`,
        // the source implicit inside an element block when the arrow leads or it
        // is `this`.
        private bool Relationship(Statement s, C4Element? parent, Environment? environment)
        {
            var parts = s.Parts;
            var arrow = parts.FindIndex(part => part.Kind == Kind.Arrow);
            if (arrow < 0) return false;

            var at = parts.Count > 1 && parts[1].Kind == Kind.Assign ? 2 : 0;
            var written = arrow > at ? parts[arrow - 1] : null;
            var scope = parent?.Id ?? environment?.Id;
            var source = arrow == at ? parent?.Id : Lower(written) == "this" ? parent?.Id : written!.Text;
            var destination = arrow + 1 < parts.Count ? parts[arrow + 1] : null;
            if (source is null || !IsValue(destination))
            {
                if (s.Block) Skip();
                return true;
            }

            // A relationship's block holds tags, which no row keeps; whatever else
            // it holds is skipped, a nested block with it.
            if (s.Block)
            {
                foreach (var b in Block()) Unknown(b.Block);
            }

            _pendingRelationships.Add((source, destination!.Text, scope));
            return true;
        }

        // ── views ──

        private void ViewsBlock()
        {
            foreach (var s in Block())
            {
                if (Lower(s.Parts[0]) is { } word && ViewSpecs.TryGetValue(word, out var spec)) View(spec, s);
                else Unknown(s.Block);
            }
        }

        private void View((string Kind, bool Scoped, bool Environment) spec, Statement s)
        {
            var head = s.Parts[0];
            var values = Values(s.Parts, 1);
            var k = 0;
            if (spec.Scoped) k++;
            if (spec.Environment) k++;
            var key = Blank(At(values, k));
            string? title = null;

            if (s.Block)
            {
                foreach (var b in Block())
                {
                    if (b.Block) { Skip(); continue; }
                    if (Lower(b.Parts[0]) == "title") title = Blank(At(Values(b.Parts, 1), 0));
                }
            }

            Views.Add(new C4View(key, spec.Kind, title, head.File, head.Line));
        }

        // ── resolution ──

        // An identifier as written, resolved from the innermost scope outward,
        // then as written.
        private C4Element? Resolve(string? name, string? scope)
        {
            if (string.IsNullOrEmpty(name)) return null;

            var current = _hierarchical ? scope : null;
            while (current is not null)
            {
                if (_byId.TryGetValue($"{current}.{name}".ToLowerInvariant(), out var hit)) return hit;
                var cut = current.LastIndexOf('.');
                current = cut < 0 ? null : current[..cut];
            }

            return _byId.GetValueOrDefault(name.ToLowerInvariant());
        }

        public void Finish()
        {
            foreach (var (element, of, scope) in _pendingInstances)
            {
                if (Resolve(element.InstanceOf, scope) is not { } target || target.Kind != of) continue;
                element.InstanceOf = target.Id;
                element.Name = target.Name;
            }

            foreach (var (source, destination, scope) in _pendingRelationships)
            {
                if (Resolve(source, scope) is not { } from || Resolve(destination, scope) is not { } to) continue;
                Relationships.Add(new C4Relationship(from.Id, to.Id));
            }
        }
    }

    [GeneratedRegex(@"^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase)]
    private static partial Regex SchemePrefix();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlugRun();
}

/// <summary>What the graph takes from the workspace.</summary>
internal sealed record C4Reading(
    IReadOnlyList<C4Element> Elements,
    IReadOnlyList<C4Relationship> Relationships,
    IReadOnlyList<C4View> Views);

/// <summary>One element, as <c>c4.mjs</c> names it: its hierarchical identifier,
/// its kind, its name — an instance's is its target's — and where it stands.</summary>
internal sealed class C4Element(string id, string kind, string? name, string? parent, string file, int line)
{
    public string Id { get; set; } = id;
    public string Kind { get; } = kind;
    public string? Name { get; set; } = name;
    public string? Parent { get; } = parent;
    public string File { get; } = file;
    public int Line { get; } = line;
    public bool Anonymous { get; init; }
    public string? InstanceOf { get; set; }
}

/// <summary>A relationship between two resolved elements.</summary>
internal sealed record C4Relationship(string Source, string Destination);

/// <summary>A view: its key, kind and title, and where it stands.</summary>
internal sealed record C4View(string? Key, string Kind, string? Title, string File, int Line);
