using System.Text.RegularExpressions;

namespace Backlog.UI.Components.Diagrams;

/// <summary>What a class in a bounded context's <c>classDiagram</c> is, read off its
/// stereotype. The devbook's model files write the DDD building block as the
/// stereotype — <c>&lt;&lt;aggregate root&gt;&gt;</c>, <c>&lt;&lt;value object&gt;&gt;</c> — so
/// that is the whole of what decides it.</summary>
public enum DomainModelKind
{
    AggregateRoot,
    Entity,
    ValueObject,
    Enumeration,
    Service,

    /// <summary>No stereotype, or one the convention does not name — a class owned
    /// by another context written as <c>&lt;&lt;Tasks&gt;&gt;</c>, say.</summary>
    Other
}

/// <summary>One attribute line of a class body: <c>+List~Id~ depends_on</c> is the
/// name <c>depends_on</c> of type <c>List~Id~</c>. An enumeration's value has a
/// name and no type.</summary>
public sealed record DomainModelAttribute(string Name, string? Type)
{
    /// <summary>Whether the attribute holds another aggregate's identity rather than
    /// the aggregate's own state: a <c>List~Id~</c>, or a name ending <c>_id</c> or
    /// <c>_ids</c>. The class's own <c>id</c> is its identity, not a reference.</summary>
    public bool IsIdReference =>
        !string.Equals(Name, "id", StringComparison.OrdinalIgnoreCase)
        && (Name.EndsWith("_id", StringComparison.OrdinalIgnoreCase)
            || Name.EndsWith("_ids", StringComparison.OrdinalIgnoreCase)
            || IsIdList(Type));

    private static bool IsIdList(string? type) =>
        type is not null && Regex.IsMatch(type, @"^(List|Set|Collection|IReadOnlyList)~\s*(?:\w*[a-z])?Id\s*~$");
}

/// <summary>One class of the diagram: its name, its stereotype as written, the kind
/// the stereotype says, and its attributes. Operations (<c>+bool isLive(...)</c>) are
/// kept apart, because a domain service is mostly operations.</summary>
public sealed record DomainModelClass(
    string Name,
    string? Stereotype,
    DomainModelKind Kind,
    IReadOnlyList<DomainModelAttribute> Attributes,
    IReadOnlyList<string> Operations);

/// <summary>The line between two classes, as mermaid draws it.</summary>
public enum DomainModelRelationKind
{
    /// <summary><c>*--</c>: the whole owns the part.</summary>
    Composition,

    /// <summary><c>o--</c>.</summary>
    Aggregation,

    /// <summary><c>--&gt;</c>.</summary>
    Association,

    /// <summary><c>..&gt;</c>: a dashed line, which the devbook's models spend on a
    /// reference by identity.</summary>
    Dependency,

    /// <summary><c>&lt;|--</c>.</summary>
    Inheritance,

    /// <summary><c>..|&gt;</c>.</summary>
    Realization,

    /// <summary><c>--</c>.</summary>
    Link,

    /// <summary><c>..</c>.</summary>
    DashedLink
}

/// <summary>A relation, turned so <see cref="From"/> is the end the arrow leaves —
/// the whole of a composition, the referrer of a dependency — whichever way round the
/// author wrote it. <c>Part --* Whole</c> and <c>Whole *-- Part</c> read the same.</summary>
public sealed record DomainModelRelation(
    string From,
    string To,
    DomainModelRelationKind Kind,
    string? FromMultiplicity,
    string? ToMultiplicity,
    string? Label);

/// <summary>A <c>classDiagram</c> fence read into its classes and relations.</summary>
public sealed record DomainClassDiagram(IReadOnlyList<DomainModelClass> Classes, IReadOnlyList<DomainModelRelation> Relations)
{
    public DomainModelClass? Find(string name) =>
        Classes.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
}

/// <summary>
/// Reads a mermaid <c>classDiagram</c> fence — the body of a devbook <c>model.md</c>
/// diagram — into a <see cref="DomainClassDiagram"/>.
/// <para>
/// Strict on purpose. The Model page falls back to drawing the fence with mermaid
/// whenever this returns nothing, so a line it does not understand is a reason to
/// hand the fence back rather than to draw a picture with a class quietly missing.
/// The directives that only style or annotate a diagram — <c>direction</c>,
/// <c>note</c>, <c>classDef</c>, <c>style</c>, <c>cssClass</c>, <c>click</c>,
/// <c>link</c>, <c>callback</c> — are stepped over.
/// </para>
/// </summary>
public static partial class DomainClassDiagramReader
{
    /// <summary>The fence read, or null when it is not a class diagram this reader
    /// understands in full.</summary>
    public static DomainClassDiagram? TryRead(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;

        var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(StripComment)
            .Select(line => line.Trim())
            .ToList();

        var start = lines.FindIndex(line => line.Length > 0);
        if (start < 0 || !Regex.IsMatch(lines[start], @"^classDiagram(-v2)?\s*$")) return null;

        var builders = new Dictionary<string, ClassBuilder>(StringComparer.Ordinal);
        var order = new List<string>();
        var relations = new List<DomainModelRelation>();
        ClassBuilder? open = null;
        var namespaceDepth = 0;

        ClassBuilder Class(string name)
        {
            if (!builders.TryGetValue(name, out var builder))
            {
                builder = new ClassBuilder(name);
                builders[name] = builder;
                order.Add(name);
            }

            return builder;
        }

        for (var i = start + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;

            if (open is not null)
            {
                if (line == "}") { open = null; continue; }
                if (StereotypeLine().Match(line) is { Success: true } inner) { open.Stereotype = inner.Groups[1].Value.Trim(); continue; }
                open.AddMember(line);
                continue;
            }

            if (line == "}")
            {
                if (namespaceDepth == 0) return null;
                namespaceDepth--;
                continue;
            }

            if (IgnoredDirective().IsMatch(line)) continue;

            if (NamespaceLine().IsMatch(line)) { namespaceDepth++; continue; }

            if (ClassLine().Match(line) is { Success: true } declared)
            {
                var builder = Class(declared.Groups["name"].Value);
                if (declared.Groups["stereo"].Success) builder.Stereotype = declared.Groups["stereo"].Value.Trim();
                if (declared.Groups["open"].Success) open = builder;
                continue;
            }

            if (StereotypeOfLine().Match(line) is { Success: true } annotated)
            {
                Class(annotated.Groups["name"].Value).Stereotype = annotated.Groups["stereo"].Value.Trim();
                continue;
            }

            if (RelationLine().Match(line) is { Success: true } relation)
            {
                var read = Relation(relation);
                if (read is null) return null;
                Class(read.From);
                Class(read.To);
                relations.Add(read);
                continue;
            }

            if (MemberLine().Match(line) is { Success: true } member)
            {
                Class(member.Groups["name"].Value).AddMember(member.Groups["member"].Value.Trim());
                continue;
            }

            // A line this reader does not know: the picture would be missing it.
            return null;
        }

        if (open is not null || namespaceDepth != 0) return null;
        if (order.Count == 0) return null;

        return new DomainClassDiagram([.. order.Select(name => builders[name].Build())], relations);
    }

    /// <summary>What the stereotype as written says the class is.</summary>
    public static DomainModelKind KindOf(string? stereotype)
    {
        var words = Regex.Replace((stereotype ?? string.Empty).Trim().ToLowerInvariant(), @"[\s_-]+", " ");
        return words switch
        {
            "aggregate root" or "aggregateroot" or "aggregate" => DomainModelKind.AggregateRoot,
            "entity" => DomainModelKind.Entity,
            "value object" or "valueobject" or "value" => DomainModelKind.ValueObject,
            "enumeration" or "enum" => DomainModelKind.Enumeration,
            "service" or "domain service" or "domainservice" => DomainModelKind.Service,
            _ => DomainModelKind.Other
        };
    }

    private static DomainModelRelation? Relation(Match match)
    {
        var left = match.Groups["a"].Value;
        var right = match.Groups["b"].Value;
        var leftMany = Blank(match.Groups["ma"].Value);
        var rightMany = Blank(match.Groups["mb"].Value);
        var label = match.Groups["label"].Success ? Blank(match.Groups["label"].Value) : null;

        // Written with the decoration on the left, the left class is the end the
        // arrow leaves; written on the right, the right class is. Turned here so a
        // reader of the result never has to ask which way round it was typed.
        (DomainModelRelationKind Kind, bool Reversed)? arrow = match.Groups["arrow"].Value switch
        {
            "*--" => (DomainModelRelationKind.Composition, false),
            "--*" => (DomainModelRelationKind.Composition, true),
            "o--" => (DomainModelRelationKind.Aggregation, false),
            "--o" => (DomainModelRelationKind.Aggregation, true),
            "-->" => (DomainModelRelationKind.Association, false),
            "<--" => (DomainModelRelationKind.Association, true),
            "..>" => (DomainModelRelationKind.Dependency, false),
            "<.." => (DomainModelRelationKind.Dependency, true),
            "--|>" => (DomainModelRelationKind.Inheritance, false),
            "<|--" => (DomainModelRelationKind.Inheritance, true),
            "..|>" => (DomainModelRelationKind.Realization, false),
            "<|.." => (DomainModelRelationKind.Realization, true),
            "--" => (DomainModelRelationKind.Link, false),
            ".." => (DomainModelRelationKind.DashedLink, false),
            _ => null
        };

        if (arrow is not { } read) return null;

        return read.Reversed
            ? new DomainModelRelation(right, left, read.Kind, rightMany, leftMany, label)
            : new DomainModelRelation(left, right, read.Kind, leftMany, rightMany, label);
    }

    private static string StripComment(string line)
    {
        var index = line.IndexOf("%%", StringComparison.Ordinal);
        return index < 0 ? line : line[..index];
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^<<(.+)>>$")]
    private static partial Regex StereotypeLine();

    [GeneratedRegex(@"^(direction\s+\w+|note\b.*|classDef\b.*|style\b.*|cssClass\b.*|click\b.*|link\b.*|callback\b.*|title\b.*|accTitle\b.*|accDescr\b.*)$")]
    private static partial Regex IgnoredDirective();

    [GeneratedRegex(@"^namespace\s+[\w.]+\s*\{$")]
    private static partial Regex NamespaceLine();

    [GeneratedRegex(@"^class\s+(?<name>[A-Za-z_][\w]*)(?:~[^~]*~)?(?:\[""[^""]*""\])?(?:\s*<<(?<stereo>[^>]+)>>)?(?::::\w+)?\s*(?<open>\{)?\s*$")]
    private static partial Regex ClassLine();

    [GeneratedRegex(@"^<<(?<stereo>[^>]+)>>\s+(?<name>[A-Za-z_]\w*)$")]
    private static partial Regex StereotypeOfLine();

    [GeneratedRegex(@"^(?<a>[A-Za-z_]\w*)\s*(?:""(?<ma>[^""]*)"")?\s*(?<arrow><\|--|--\|>|<\|\.\.|\.\.\|>|\*--|--\*|o--|--o|-->|<--|\.\.>|<\.\.|--|\.\.)(?=[\s""])\s*(?:""(?<mb>[^""]*)"")?\s*(?<b>[A-Za-z_]\w*)\s*(?::\s*(?<label>.*))?$")]
    private static partial Regex RelationLine();

    [GeneratedRegex(@"^(?<name>[A-Za-z_]\w*)\s*:\s*(?<member>.+)$")]
    private static partial Regex MemberLine();

    private sealed class ClassBuilder(string name)
    {
        private readonly List<DomainModelAttribute> _attributes = [];
        private readonly List<string> _operations = [];

        public string? Stereotype { get; set; }

        public void AddMember(string line)
        {
            var member = line.TrimStart('+', '-', '#', '~').Trim().TrimEnd('$', '*').Trim();
            if (member.Length == 0) return;

            if (member.Contains('('))
            {
                _operations.Add(member);
                return;
            }

            // "name : Type" is mermaid's other spelling of "Type name".
            var colon = member.IndexOf(':');
            if (colon > 0)
            {
                _attributes.Add(new DomainModelAttribute(member[..colon].Trim(), Blank(member[(colon + 1)..])));
                return;
            }

            var space = member.LastIndexOf(' ');
            _attributes.Add(space < 0
                ? new DomainModelAttribute(member, null)
                : new DomainModelAttribute(member[(space + 1)..].Trim(), Blank(member[..space])));
        }

        public DomainModelClass Build() => new(name, Stereotype, KindOf(Stereotype), [.. _attributes], [.. _operations]);
    }
}
