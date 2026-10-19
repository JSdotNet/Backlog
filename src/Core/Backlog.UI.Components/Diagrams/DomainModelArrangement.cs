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
    /// <summary>A dashed <c>..&gt;</c> line.</summary>
    Relation,

    /// <summary>A <c>List~Id~</c> or <c>*_id</c> attribute.</summary>
    Attribute
}

/// <summary>Something a member of the band holds only by identity, so it sits
/// outside the band: <see cref="Target"/> is the class the dashed line reaches, or,
/// for an attribute, the attribute's name.</summary>
public sealed record DomainModelReference(
    string Owner,
    string Target,
    DomainModelReferenceSource Source,
    string? Multiplicity,
    string? Label,
    string? Type);

/// <summary>One aggregate: its root, the parts composed into it, and what it refers
/// to by identity.</summary>
public sealed record DomainModelBoundary(DomainModelClass Root, IReadOnlyList<DomainModelPart> Parts, IReadOnlyList<DomainModelReference> References);

/// <summary>
/// A class diagram arranged the way the Model page draws it: each aggregate root in
/// a consistency-boundary band with its composed parts around it, the references by
/// identity outside the band, and the context's domain services below.
/// </summary>
/// <param name="Unplaced">The classes the arrangement has no place for — the
/// enumerations and the types a band member only uses. Named so the page can say
/// they are on Fields rather than let them go missing without a word.</param>
public sealed record DomainModelArrangement(
    IReadOnlyList<DomainModelBoundary> Boundaries,
    IReadOnlyList<DomainModelClass> Services,
    IReadOnlyList<DomainModelClass> Unplaced)
{
    /// <summary>The fence arranged, or null when it does not parse, holds no
    /// aggregate root, or holds a class other than an enumeration that no band,
    /// reference or service section would show — the page then draws the fence with
    /// mermaid instead.</summary>
    public static DomainModelArrangement? TryArrange(string? source) =>
        DomainClassDiagramReader.TryRead(source) is { } diagram ? Arrange(diagram) : null;

    public static DomainModelArrangement? Arrange(DomainClassDiagram diagram)
    {
        var roots = diagram.Classes.Where(c => c.Kind == DomainModelKind.AggregateRoot).ToList();
        if (roots.Count == 0) return null;

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
            var references = new List<DomainModelReference>();

            foreach (var relation in diagram.Relations.Where(r => r.Kind == DomainModelRelationKind.Dependency && memberNames.Contains(r.From)))
            {
                references.Add(new DomainModelReference(relation.From, relation.To, DomainModelReferenceSource.Relation, relation.ToMultiplicity, relation.Label, null));
            }

            foreach (var member in members)
            {
                foreach (var attribute in member.Attributes.Where(a => a.IsIdReference))
                {
                    references.Add(new DomainModelReference(member.Name, attribute.Name, DomainModelReferenceSource.Attribute, null, null, attribute.Type));
                }
            }

            boundaries.Add(new DomainModelBoundary(root, parts, references));
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

    /// <summary>A mermaid generic as a reader writes it: <c>List~Id~</c> is
    /// <c>List&lt;Id&gt;</c>.</summary>
    public static string TypeLabel(string type) =>
        System.Text.RegularExpressions.Regex.Replace(type, "~([^~]*)~", "<$1>");

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
