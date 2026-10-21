using System.Text.RegularExpressions;

namespace Backlog.UI.Components.Diagrams;

/// <summary>One <c>classDiagram</c> fence of a model file and the heading it sits
/// under, which names the diagram.</summary>
public sealed record DomainModelFence(string Title, string Source);

/// <summary>A class composed into an aggregate: drawn inside the root's band, under
/// the class that owns it, with the owner's relation label and the part's
/// multiplicity — <c>Task "1" *-- "0..*" SubItem : contains</c> is the part
/// <c>SubItem</c>, owned by <c>Task</c>, <c>contains</c>, <c>0..*</c>.</summary>
public sealed record DomainModelPart(DomainModelClass Class, string Owner, string? Multiplicity, string? Label, int Depth);

/// <summary>Where a reference outside the band was read from.</summary>
public enum DomainModelReferenceSource
{
    /// <summary>A dashed <c>..&gt;</c> line, with any id attribute it draws.</summary>
    Relation,

    /// <summary>A <c>List~Id~</c> or <c>*_id</c> attribute of the root, and no line.</summary>
    Attribute
}

/// <summary>A chapter something can be named by id as: an aggregate or an entity
/// of any context, with its heading, its <c>aliases</c> and the reference that
/// names it. What lets <c>repo_ids</c> read as <em>Repository</em>.</summary>
public sealed record DomainModelIdentity(string Title, string Kind, IReadOnlyList<string> Aliases, string Reference);

/// <summary>A type the aggregate holds only by identity, so it sits outside the
/// band: one per type, however many attributes point at it.</summary>
/// <param name="Owner">The band member the line or attribute is on: the root,
/// unless a part draws a dashed line of its own.</param>
/// <param name="Target">The type pointed at: the class a dashed line reaches, the
/// chapter an id attribute names, or failing both the attribute's stem in words.</param>
/// <param name="Multiplicity">How many the aggregate holds: the line's, else
/// <c>0..*</c> for a list of ids and <c>0..1</c> for one.</param>
/// <param name="Label">The dashed line's label, when a line drew it.</param>
/// <param name="Via">The attributes that hold the ids, as written.</param>
/// <param name="Chapter">The chapter the target is, when one names it.</param>
public sealed record DomainModelReference(
    string Owner,
    string Target,
    DomainModelReferenceSource Source,
    string? Multiplicity,
    string? Label,
    IReadOnlyList<string> Via,
    string? Chapter = null);

/// <summary>One aggregate: its root, the parts composed into it, and what it refers
/// to by identity.</summary>
public sealed record DomainModelBoundary(DomainModelClass Root, IReadOnlyList<DomainModelPart> Parts, IReadOnlyList<DomainModelReference> References)
{
    /// <summary>The root's own fields its card names: vocabularies, then dates,
    /// then other values, with no id, no reference and no part.</summary>
    public IReadOnlyList<string> KeyFields { get; init; } = [];
}

/// <summary>
/// A class diagram arranged the way the Model page draws it: each aggregate root in
/// a consistency-boundary band with its composed parts around it, the references by
/// identity outside the band, and the context's domain services below.
/// </summary>
/// <param name="Unplaced">The classes the arrangement has no place for — the
/// enumerations and the types a band member only uses. Named so the page can say
/// they are on Fields rather than let them go missing without a word.</param>
public sealed partial record DomainModelArrangement(
    IReadOnlyList<DomainModelBoundary> Boundaries,
    IReadOnlyList<DomainModelClass> Services,
    IReadOnlyList<DomainModelClass> Unplaced)
{
    /// <summary>The fence arranged, or null when it does not parse, holds no
    /// aggregate root, or holds a class other than an enumeration that no band,
    /// reference or service section would show — the page then draws the fence with
    /// mermaid instead.</summary>
    /// <param name="identities">The chapters an id attribute can point at, the
    /// ones to prefer first.</param>
    public static DomainModelArrangement? TryArrange(string? source, IReadOnlyList<DomainModelIdentity>? identities = null) =>
        DomainClassDiagramReader.TryRead(source) is { } diagram ? Arrange(diagram, identities) : null;

    public static DomainModelArrangement? Arrange(DomainClassDiagram diagram, IReadOnlyList<DomainModelIdentity>? identities = null)
    {
        var roots = diagram.Classes.Where(c => c.Kind == DomainModelKind.AggregateRoot).ToList();
        if (roots.Count == 0) return null;

        var fields = DomainModelFields.Read(diagram).ToDictionary(c => c.Name, StringComparer.Ordinal);
        var placed = new HashSet<string>(roots.Select(r => r.Name), StringComparer.Ordinal);
        var boundaries = new List<DomainModelBoundary>();

        foreach (var root in roots)
        {
            var parts = new List<DomainModelPart>();
            var members = new List<DomainModelClass> { root };

            // Breadth first, so a part of a part sits after every part of the root.
            var frontier = new Queue<(DomainModelClass Owner, int Depth)>();
            frontier.Enqueue((root, 1));
            while (frontier.Count > 0)
            {
                var (owner, depth) = frontier.Dequeue();
                foreach (var relation in diagram.Relations.Where(r => r.Kind == DomainModelRelationKind.Composition && r.From == owner.Name))
                {
                    if (diagram.Find(relation.To) is not { } part) continue;
                    if (part.Kind == DomainModelKind.AggregateRoot || !placed.Add(part.Name)) continue;
                    parts.Add(new DomainModelPart(part, owner.Name, relation.ToMultiplicity, relation.Label, depth));
                    members.Add(part);
                    frontier.Enqueue((part, depth + 1));
                }
            }

            var memberNames = members.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
            var references = References(diagram, root, memberNames, identities ?? []);
            boundaries.Add(new DomainModelBoundary(root, parts, references) { KeyFields = KeyFields(fields.GetValueOrDefault(root.Name)) });
        }

        var services = diagram.Classes.Where(c => c.Kind == DomainModelKind.Service).ToList();
        foreach (var service in services) placed.Add(service.Name);

        // A class a dashed line reaches is drawn as that reference's target.
        foreach (var boundary in boundaries)
        {
            foreach (var reference in boundary.References.Where(r => r.Source == DomainModelReferenceSource.Relation)) placed.Add(reference.Target);
        }

        var unplaced = diagram.Classes.Where(c => !placed.Contains(c.Name)).ToList();

        // An enumeration left over is a type a member uses, listed on Fields. Any
        // other class left over carries structure of its own the bands would not
        // show — a value object with parts that no root composes — so the fence is
        // drawn with mermaid rather than as a picture with that structure missing.
        if (unplaced.Any(c => c.Kind != DomainModelKind.Enumeration)) return null;

        return new DomainModelArrangement(boundaries, services, unplaced);
    }

    /// <summary>
    /// What the aggregate holds by identity, one entry per type. A dashed line from
    /// a band member is one, unless it reaches an enumeration: the member is
    /// derived as or uses that, it does not name it by id. An id attribute of the
    /// root is one, folded into the line that draws it (<c>depends_on</c> into
    /// <c>depends on</c>) or into the entry for the same type. A part's own ids are
    /// the part's detail, which Fields lists: a <c>ProjectionRef</c>'s
    /// <c>repo_id</c> is not the aggregate naming a repository a second time.
    /// </summary>
    private static List<DomainModelReference> References(DomainClassDiagram diagram, DomainModelClass root, HashSet<string> members, IReadOnlyList<DomainModelIdentity> identities)
    {
        var ids = root.Attributes.Where(DomainModelFields.IsIdentity).ToList();
        var references = new List<DomainModelReference>();

        foreach (var relation in diagram.Relations.Where(r => r.Kind == DomainModelRelationKind.Dependency && members.Contains(r.From)))
        {
            if (diagram.Find(relation.To) is { Kind: DomainModelKind.Enumeration }) continue;

            List<DomainModelAttribute> drawn = relation.From == root.Name && relation.Label is { } label
                ? [.. ids.Where(a => SameWords(a.Name, label))]
                : [];
            foreach (var attribute in drawn) ids.Remove(attribute);

            var existing = references.FindIndex(r => r.Owner == relation.From && r.Target == relation.To);
            if (existing >= 0)
            {
                references[existing] = references[existing] with { Via = [.. references[existing].Via, .. drawn.Select(a => a.Name)] };
                continue;
            }

            var multiplicity = relation.ToMultiplicity ?? (drawn.Count > 0 ? Multiplicity(drawn[0]) : null);
            references.Add(new DomainModelReference(relation.From, relation.To, DomainModelReferenceSource.Relation, multiplicity, relation.Label, [.. drawn.Select(a => a.Name)]));
        }

        foreach (var attribute in ids)
        {
            var (title, chapter) = IdentityOf(attribute.Name, attribute.Type, identities);
            var multiplicity = Multiplicity(attribute);
            // One card per type, however the type is spelled: a line's class name
            // (InboxItem) and a chapter's heading (Inbox Item) are the same type.
            var existing = references.FindIndex(r => r.Owner == root.Name && SameType(r.Target, title));
            if (existing >= 0)
            {
                var found = references[existing];
                references[existing] = found with
                {
                    Via = [.. found.Via, attribute.Name],
                    Multiplicity = found.Source == DomainModelReferenceSource.Attribute && multiplicity == Many ? Many : found.Multiplicity
                };
                continue;
            }

            references.Add(new DomainModelReference(root.Name, title, DomainModelReferenceSource.Attribute, multiplicity, null, [attribute.Name], chapter));
        }

        return references;
    }

    private const string Many = "0..*";

    /// <summary>
    /// The type an id attribute points at. An <c>aliases</c> entry spelled as the
    /// attribute wins: <c>repo_ids</c> is <em>Repository</em>. Else the attribute's
    /// stem — its name without <c>_id</c> or <c>_ids</c>, or a <c>UserId</c> type
    /// without <c>Id</c> — matched from its last words against the heading of an
    /// aggregate or entity, the most words first, in the order
    /// <paramref name="identities"/> comes in: <c>source_inbox_id</c> is
    /// <em>Inbox Item</em>. Else the stem in words: <em>Recurrence source</em>.
    /// </summary>
    public static (string Title, string? Chapter) IdentityOf(string attribute, string? type, IReadOnlyList<DomainModelIdentity> identities)
    {
        var alias = identities.FirstOrDefault(i => i.Aliases.Any(a => string.Equals(a, attribute, StringComparison.OrdinalIgnoreCase)));
        if (alias is not null) return (alias.Title, alias.Reference);

        var words = Stem(attribute, type);
        if (words.Count == 0) return (attribute, null);

        // A heading spelled exactly as the words wins over one that only starts
        // with them, wherever it sits in the order: parent_task_id is Task, even
        // after a Task Template.
        var named = identities.Where(i => i.Kind is "aggregate" or "entity").ToList();
        for (var take = words.Count; take > 0; take--)
        {
            var tail = string.Join(' ', words.Skip(words.Count - take));
            var match = named.FirstOrDefault(i => string.Equals(i.Title, tail, StringComparison.OrdinalIgnoreCase))
                ?? named.FirstOrDefault(i => i.Title.StartsWith(tail + " ", StringComparison.OrdinalIgnoreCase));
            if (match is not null) return (match.Title, match.Reference);
        }

        var phrase = string.Join(' ', words).ToLowerInvariant();
        return (char.ToUpperInvariant(phrase[0]) + phrase[1..], null);
    }

    private static List<string> Stem(string attribute, string? type)
    {
        if (attribute.EndsWith("_ids", StringComparison.OrdinalIgnoreCase)) return Words(attribute[..^4]);
        if (attribute.EndsWith("_id", StringComparison.OrdinalIgnoreCase)) return Words(attribute[..^3]);
        if (DomainModelFields.BaseType(type) is { Length: > 2 } typed && typed.EndsWith("Id", StringComparison.Ordinal))
        {
            return [.. CamelWord().Matches(typed[..^2]).Select(m => m.Value)];
        }
        return [];

        static List<string> Words(string stem) => [.. stem.Split('_', StringSplitOptions.RemoveEmptyEntries)];
    }

    private static string Multiplicity(DomainModelAttribute attribute) =>
        attribute.Type is { } type && Collection().IsMatch(type) ? Many : "0..1";

    /// <summary><c>InboxItem</c> and <c>Inbox Item</c> name one type.</summary>
    private static bool SameType(string a, string b) =>
        string.Equals(Letters(a), Letters(b), StringComparison.OrdinalIgnoreCase);

    private static string Letters(string text) => string.Concat(text.Where(char.IsLetterOrDigit));

    /// <summary><c>depends_on</c> and <c>depends on</c> are the same words.</summary>
    private static bool SameWords(string attribute, string label) =>
        string.Equals(
            attribute.Replace("_", string.Empty, StringComparison.Ordinal),
            label.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> KeyFields(DomainFieldsClass? root)
    {
        if (root is null) return [];
        DomainFieldKind[] order = [DomainFieldKind.Vocabulary, DomainFieldKind.DateTime, DomainFieldKind.Value];
        return
        [
            .. order.SelectMany(kind => root.Groups.Where(g => g.Kind == kind).SelectMany(g => g.Fields))
                .Where(f => !string.Equals(f.Name, "id", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Name)
        ];
    }

    [GeneratedRegex("[A-Z]?[a-z0-9]+")]
    private static partial Regex CamelWord();

    [GeneratedRegex(@"^(List|Set|Collection|IReadOnlyList|IEnumerable)~")]
    private static partial Regex Collection();

    /// <summary>A mermaid generic as a reader writes it: <c>List~Id~</c> is
    /// <c>List&lt;Id&gt;</c>.</summary>
    public static string TypeLabel(string type) =>
        Regex.Replace(type, "~([^~]*)~", "<$1>");

    /// <summary>The kind as the diagram says it in words, beside its glyph.</summary>
    public static string KindLabel(DomainModelKind kind) => kind switch
    {
        DomainModelKind.AggregateRoot => "aggregate root",
        DomainModelKind.Entity => "entity",
        DomainModelKind.ValueObject => "value object",
        DomainModelKind.Enumeration => "enumeration",
        DomainModelKind.Service => "domain service",
        _ => "type"
    };

    /// <summary>The kind as markup and the stylesheet key on it.</summary>
    public static string KindKey(DomainModelKind kind) => kind switch
    {
        DomainModelKind.AggregateRoot => "root",
        DomainModelKind.Entity => "entity",
        DomainModelKind.ValueObject => "value-object",
        DomainModelKind.Enumeration => "enumeration",
        DomainModelKind.Service => "service",
        _ => "other"
    };

    /// <summary>The glyph before the kind's word. Never the sole carrier of the kind.</summary>
    public static string KindGlyph(DomainModelKind kind) => kind switch
    {
        DomainModelKind.AggregateRoot => "◆",
        DomainModelKind.Entity => "■",
        DomainModelKind.ValueObject => "●",
        DomainModelKind.Enumeration => "≡",
        DomainModelKind.Service => "⚙",
        _ => "○"
    };
}
