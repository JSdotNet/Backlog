using System.Text.RegularExpressions;

namespace Backlog.UI.Components.Diagrams;

/// <summary>What an attribute is, read off its type — the groups the Model page's
/// Fields tab lists a class's attributes under, in the order it lists them.</summary>
public enum DomainFieldKind
{
    /// <summary>A date, a time, an instant or a duration.</summary>
    DateTime,

    /// <summary>Another aggregate's identity: a <c>List~Id~</c>, or a name ending
    /// <c>_id</c> or <c>_ids</c> (<see cref="DomainModelAttribute.IsIdReference"/>).</summary>
    Reference,

    /// <summary>A part the class owns: an entity or value object of the diagram, or a
    /// class composed into it.</summary>
    OwnedPart,

    /// <summary>A value out of a fixed vocabulary: an enumeration of the diagram.</summary>
    Vocabulary,

    /// <summary>Everything else — strings, numbers, flags, the class's own id.</summary>
    Value
}

/// <summary>One attribute as the Fields tab lists it.</summary>
/// <param name="Name">The attribute's name, or for a part composed in with no
/// attribute of its own, the relation's label.</param>
/// <param name="Type">The type as a reader writes it — <c>List&lt;Id&gt;</c> — or
/// null when the diagram gives none.</param>
/// <param name="Detail">What the type is beyond its name: an enumeration's values,
/// a composed part's multiplicity.</param>
public sealed record DomainField(string Name, string? Type, DomainFieldKind Kind, string? Detail = null);

/// <summary>One kind's attributes, in the order the class declares them.</summary>
public sealed record DomainFieldGroup(DomainFieldKind Kind, IReadOnlyList<DomainField> Fields);

/// <summary>One class of the diagram with its attributes grouped by kind.</summary>
public sealed record DomainFieldsClass(string Name, DomainModelKind Kind, string? Stereotype, IReadOnlyList<DomainFieldGroup> Groups);

/// <summary>
/// The Model page's Fields reading of a <c>classDiagram</c> (local ADR 0044): every
/// class that holds state — aggregate roots, entities, value objects and the classes
/// another context owns — with its attributes grouped by what their types say.
/// <para>
/// Enumerations are not listed as classes. They are what a
/// <see cref="DomainFieldKind.Vocabulary"/> attribute's type is, and their values
/// show there, beside the attribute that takes one. Domain services hold no state:
/// how they run is the domain chapter's to say, not the diagram's.
/// </para>
/// </summary>
public static partial class DomainModelFields
{
    /// <summary>The groups in the order the tab shows them.</summary>
    public static IReadOnlyList<DomainFieldKind> Order { get; } =
    [
        DomainFieldKind.DateTime,
        DomainFieldKind.Reference,
        DomainFieldKind.OwnedPart,
        DomainFieldKind.Vocabulary,
        DomainFieldKind.Value
    ];

    /// <summary>The fence read into classes and their grouped attributes, or null
    /// when the fence is not a class diagram the reader understands.</summary>
    public static IReadOnlyList<DomainFieldsClass>? TryRead(string? source) =>
        DomainClassDiagramReader.TryRead(source) is { } diagram ? Read(diagram) : null;

    public static IReadOnlyList<DomainFieldsClass> Read(DomainClassDiagram diagram)
    {
        var result = new List<DomainFieldsClass>();
        foreach (var element in diagram.Classes)
        {
            if (element.Kind is DomainModelKind.Enumeration or DomainModelKind.Service) continue;

            var fields = new List<DomainField>();
            var typed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var attribute in element.Attributes)
            {
                var field = Classify(diagram, element, attribute);
                fields.Add(field);
                if (BaseType(attribute.Type) is { } named) typed.Add(named);
            }

            // A part composed in with no attribute naming it is still owned: the
            // line is the only place the diagram says so.
            foreach (var relation in diagram.Relations)
            {
                if (relation.Kind is not (DomainModelRelationKind.Composition or DomainModelRelationKind.Aggregation)) continue;
                if (!string.Equals(relation.From, element.Name, StringComparison.Ordinal)) continue;
                if (typed.Contains(relation.To)) continue;

                fields.Add(new DomainField(relation.Label ?? relation.To, relation.To, DomainFieldKind.OwnedPart, relation.ToMultiplicity));
                typed.Add(relation.To);
            }

            var groups = Order
                .Select(kind => new DomainFieldGroup(kind, [.. fields.Where(f => f.Kind == kind)]))
                .Where(group => group.Fields.Count > 0)
                .ToList();

            result.Add(new DomainFieldsClass(element.Name, element.Kind, element.Stereotype, groups));
        }

        return result;
    }

    /// <summary>The group's heading on the tab.</summary>
    public static string Label(DomainFieldKind kind) => kind switch
    {
        DomainFieldKind.DateTime => "Dates and times",
        DomainFieldKind.Reference => "Ids pointing elsewhere",
        DomainFieldKind.OwnedPart => "Owned parts",
        DomainFieldKind.Vocabulary => "Vocabularies",
        _ => "Other values"
    };

    /// <summary>The group as markup and the stylesheet key on it.</summary>
    public static string Key(DomainFieldKind kind) => kind switch
    {
        DomainFieldKind.DateTime => "date-time",
        DomainFieldKind.Reference => "reference",
        DomainFieldKind.OwnedPart => "owned-part",
        DomainFieldKind.Vocabulary => "vocabulary",
        _ => "value"
    };

    private static DomainField Classify(DomainClassDiagram diagram, DomainModelClass owner, DomainModelAttribute attribute)
    {
        var type = attribute.Type is { } written ? DomainModelArrangement.TypeLabel(written) : null;

        // An identity is a reference whatever else its name suggests: a
        // `recurrence_source_id` is not a part, it is somewhere else.
        if (IsIdentity(attribute)) return new DomainField(attribute.Name, type, DomainFieldKind.Reference);

        var named = BaseType(attribute.Type) is { } baseType ? diagram.Find(baseType) : null;
        if (named is not null)
        {
            if (named.Kind == DomainModelKind.Enumeration)
            {
                var values = string.Join(", ", named.Attributes.Select(value => value.Name));
                return new DomainField(attribute.Name, type, DomainFieldKind.Vocabulary, values.Length > 0 ? values : null);
            }

            if (named.Kind is DomainModelKind.Entity or DomainModelKind.ValueObject || Composes(diagram, owner.Name, named.Name))
            {
                return new DomainField(attribute.Name, type, DomainFieldKind.OwnedPart, Multiplicity(diagram, owner.Name, named.Name));
            }
        }

        if (IsTemporal(attribute)) return new DomainField(attribute.Name, type, DomainFieldKind.DateTime);

        return new DomainField(attribute.Name, type, DomainFieldKind.Value);
    }

    /// <summary>Whether the attribute holds another aggregate's identity, by its
    /// name or by its type.</summary>
    internal static bool IsIdentity(DomainModelAttribute attribute) => attribute.IsIdReference || NamesAnotherId(attribute);

    /// <summary>An id written as its type — <c>TaskId</c>, <c>List~OwnerId~</c> — is
    /// another aggregate's identity too. The bare <c>Id</c> is not: it is how a
    /// class writes its own identity, and a reference by <c>Id</c> already says so
    /// in its name.</summary>
    private static bool NamesAnotherId(DomainModelAttribute attribute) =>
        !string.Equals(attribute.Name, "id", StringComparison.OrdinalIgnoreCase)
        && BaseType(attribute.Type) is { Length: > 2 } type
        && type.EndsWith("Id", StringComparison.Ordinal)
        && char.IsLower(type[^3]);

    private static bool Composes(DomainClassDiagram diagram, string whole, string part) =>
        diagram.Relations.Any(r => r.Kind is DomainModelRelationKind.Composition or DomainModelRelationKind.Aggregation
            && string.Equals(r.From, whole, StringComparison.Ordinal)
            && string.Equals(r.To, part, StringComparison.Ordinal));

    private static string? Multiplicity(DomainClassDiagram diagram, string whole, string part) =>
        diagram.Relations.FirstOrDefault(r => r.Kind is DomainModelRelationKind.Composition or DomainModelRelationKind.Aggregation
            && string.Equals(r.From, whole, StringComparison.Ordinal)
            && string.Equals(r.To, part, StringComparison.Ordinal))?.ToMultiplicity;

    /// <summary>The class a type names, through a collection or a nullable:
    /// <c>List~Weekday~</c> is <c>Weekday</c>, <c>Recurrence?</c> is
    /// <c>Recurrence</c>.</summary>
    internal static string? BaseType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return null;

        var inner = type.Trim();
        if (GenericType().Match(inner) is { Success: true } generic) inner = generic.Groups[1].Value.Trim();
        inner = inner.TrimEnd('?').Trim();
        if (inner.EndsWith("[]", StringComparison.Ordinal)) inner = inner[..^2].Trim();
        return inner.Length == 0 ? null : inner;
    }

    private static bool IsTemporal(DomainModelAttribute attribute)
    {
        if (BaseType(attribute.Type) is { } type && TemporalType().IsMatch(type)) return true;

        // A diagram that writes the moment as text still names it a moment.
        return (attribute.Type is null || BaseType(attribute.Type) is "String" or "string")
            && TemporalName().IsMatch(attribute.Name);
    }

    [GeneratedRegex(@"^\w+~\s*([^~]+?)\s*~$")]
    private static partial Regex GenericType();

    [GeneratedRegex(@"^(Date|DateTime|DateOnly|TimeOnly|Time|Timestamp|Instant|LocalDate|LocalDateTime|LocalTime|ZonedDateTime|OffsetDateTime|DateTimeOffset|TimeSpan|Duration|Period)$", RegexOptions.IgnoreCase)]
    private static partial Regex TemporalType();

    [GeneratedRegex(@"_(at|on|date|time)$", RegexOptions.IgnoreCase)]
    private static partial Regex TemporalName();
}
