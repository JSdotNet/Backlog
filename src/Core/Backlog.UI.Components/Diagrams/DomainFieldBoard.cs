using System.Text.RegularExpressions;
using Backlog.UI.Components.Devbook;

namespace Backlog.UI.Components.Diagrams;

/// <summary>What a field's type is, as the Fields tab marks it: the three kinds of
/// time and the plain id as the legend's badges, a type of the model in its kind's
/// tone, and everything else in words.</summary>
public enum DomainFieldTone
{
    /// <summary>A day read on a local calendar.</summary>
    Date,

    /// <summary>A wall-clock time with no zone.</summary>
    LocalTime,

    /// <summary>A moment in UTC.</summary>
    Instant,

    /// <summary>A plain id of something outside the aggregate.</summary>
    Id,

    Enumeration,
    ValueObject,
    Entity,

    /// <summary>Text, a number, a list — said in words.</summary>
    Plain
}

/// <summary>A field's type as the row shows it.</summary>
public sealed record DomainFieldType(string Label, DomainFieldTone Tone);

/// <summary>One row of a purpose group: one field, or several the chapter names in
/// one bullet.</summary>
/// <param name="Type">The type when every named field has the same one, else null.</param>
/// <param name="Meaning">The chapter's sentence, as markdown, or null when the
/// chapter writes none.</param>
/// <param name="Rules">The titles of the invariants the bullet points at.</param>
/// <param name="Derived">The field is not stored: the diagram draws it as a
/// dashed dependency of the root, not an attribute.</param>
public sealed record DomainFieldRow(IReadOnlyList<string> Names, DomainFieldType? Type, string? Meaning, IReadOnlyList<string> Rules, bool Derived = false);

/// <summary>A group of rows: a purpose group the chapter writes, or — when it
/// writes none — a kind the types say.</summary>
public sealed record DomainFieldRowGroup(string Title, string? Question, IReadOnlyList<DomainFieldRow> Rows);

/// <summary>A part the root owns, for the side column.</summary>
/// <param name="Summary">An entity's chapter sentence; a value object's fields.</param>
public sealed record DomainOwnedPart(string Name, string Title, DomainModelKind Kind, string? Multiplicity, string Summary);

/// <summary>An enumeration of the diagram with its values.</summary>
/// <param name="Derived">The root holds it by a dashed dependency: worked out, not stored.</param>
public sealed record DomainOwnedEnum(string Name, string Title, IReadOnlyList<string> Values, bool Derived);

/// <summary>What a class's domain chapter says of it, by the name the model gives
/// it: the heading, its first sentence, its aliases and its status.</summary>
public sealed record DomainModelProfile(string Title, string Summary, IReadOnlyList<string> Aliases, string? Status);

/// <summary>The Fields tab for one aggregate root (local ADR 0037): its fields in
/// groups, and what it owns.</summary>
/// <param name="Written">Whether the groups are the model file's own purpose
/// groups rather than the kinds the types say.</param>
/// <param name="OtherEnums">The diagram's enumerations the root does not take
/// directly — a part's vocabulary.</param>
public sealed record DomainFieldBoard(
    DomainModelClass Root,
    bool Written,
    IReadOnlyList<DomainFieldRowGroup> Groups,
    IReadOnlyList<DomainOwnedPart> Entities,
    IReadOnlyList<DomainOwnedPart> ValueObjects,
    IReadOnlyList<DomainOwnedEnum> Enums,
    IReadOnlyList<DomainOwnedEnum> OtherEnums);

/// <summary>
/// Builds the Fields tab of an aggregate root out of its <c>classDiagram</c>, the
/// model file's <em>Fields</em> chapter (<see cref="DomainFieldPurposes"/>), the
/// context's invariants and its domain chapters. Every word is the chapters' or
/// the diagram's; a field the chapter does not group stands under
/// <em>Other fields</em>, so nothing the diagram holds goes missing.
/// </summary>
public static partial class DomainFieldBoards
{
    /// <summary>The heading of the group of fields no purpose group names.</summary>
    public const string Ungrouped = "Other fields";

    /// <summary>One board per aggregate root of the fence, in the order the fence
    /// declares them — empty when the fence cannot be read or names no root.</summary>
    public static IReadOnlyList<DomainFieldBoard> TryReadAll(
        string? source,
        IReadOnlyList<DomainFieldChapter>? chapters = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? rules = null,
        IReadOnlyDictionary<string, DomainModelProfile>? profiles = null) =>
        DomainClassDiagramReader.TryRead(source) is { } diagram
            ? [.. diagram.Classes.Where(c => c.Kind == DomainModelKind.AggregateRoot).Select(root => Read(diagram, root, chapters, rules, profiles))]
            : [];

    /// <summary>The board of the fence's first aggregate root, or null when the
    /// fence cannot be read or names no root.</summary>
    public static DomainFieldBoard? TryRead(
        string? source,
        IReadOnlyList<DomainFieldChapter>? chapters = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? rules = null,
        IReadOnlyDictionary<string, DomainModelProfile>? profiles = null) =>
        TryReadAll(source, chapters, rules, profiles).FirstOrDefault();

    public static DomainFieldBoard Read(
        DomainClassDiagram diagram,
        DomainModelClass root,
        IReadOnlyList<DomainFieldChapter>? chapters = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? rules = null,
        IReadOnlyDictionary<string, DomainModelProfile>? profiles = null)
    {

        var chapter = chapters is null ? null : DomainFieldPurposes.For(chapters, root.Name);
        var groups = chapter is { Purposes.Count: > 0 }
            ? Written(diagram, root, chapter, rules, profiles)
            : ByKind(diagram, root, profiles);

        // What the root owns: what it composes, and any entity or value object an
        // attribute of it is typed as, with or without a line.
        var lines = diagram.Relations
            .Where(r => r.Kind is DomainModelRelationKind.Composition or DomainModelRelationKind.Aggregation
                && string.Equals(r.From, root.Name, StringComparison.Ordinal))
            .Select(r => (Class: diagram.Find(r.To), Multiplicity: r.ToMultiplicity));
        var typed = root.Attributes
            .Where(a => !DomainModelFields.IsIdentity(a))
            .Select(a => (Class: DomainModelFields.BaseType(a.Type) is { } type ? diagram.Find(type) : null, Multiplicity: (string?)null))
            .Where(p => p.Class is { Kind: DomainModelKind.Entity or DomainModelKind.ValueObject });
        var owned = lines.Concat(typed)
            .Where(p => p.Class is not null && p.Class.Kind is not (DomainModelKind.Enumeration or DomainModelKind.AggregateRoot or DomainModelKind.Service))
            .GroupBy(p => p.Class!.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        var entities = owned
            .Where(p => p.Class!.Kind == DomainModelKind.Entity)
            .Select(p => Part(p.Class!, p.Multiplicity, profiles, useChapter: true))
            .ToList();
        var valueObjects = owned
            .Where(p => p.Class!.Kind != DomainModelKind.Entity)
            .Select(p => Part(p.Class!, p.Multiplicity, profiles, useChapter: false))
            .ToList();

        var taken = root.Attributes
            .Select(a => DomainModelFields.BaseType(a.Type))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var derived = diagram.Relations
            .Where(r => r.Kind == DomainModelRelationKind.Dependency && string.Equals(r.From, root.Name, StringComparison.Ordinal))
            .Select(r => r.To)
            .ToHashSet(StringComparer.Ordinal);
        var associated = diagram.Relations
            .Where(r => r.Kind == DomainModelRelationKind.Association && string.Equals(r.From, root.Name, StringComparison.Ordinal))
            .Select(r => r.To);
        taken.UnionWith(associated);

        var enumerations = diagram.Classes.Where(c => c.Kind == DomainModelKind.Enumeration).ToList();
        var enums = enumerations
            .Where(e => taken.Contains(e.Name) || derived.Contains(e.Name))
            .Select(e => Enum(e, derived.Contains(e.Name) && !taken.Contains(e.Name), profiles))
            .ToList();
        var others = enumerations
            .Where(e => !taken.Contains(e.Name) && !derived.Contains(e.Name))
            .Select(e => Enum(e, false, profiles))
            .ToList();

        return new DomainFieldBoard(root, chapter is { Purposes.Count: > 0 }, groups, entities, valueObjects, enums, others);
    }

    /// <summary>
    /// The invariants each heading of the context's invariants files holds, by
    /// the link a Fields bullet writes to it — the file's name and the anchor,
    /// in lower case: <c>domain.invariants.md#how-a-task-moves</c>. A section names
    /// every invariant beneath it; an invariant names itself.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> RuleIndex(IEnumerable<DomainChapterFile> invariantFiles)
    {
        var index = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in invariantFiles)
        {
            var name = FileName(file.Path);
            foreach (var chapter in file.AllChapters())
            {
                if (chapter.Anchor.Length == 0) continue;

                var titles = chapter.Descendants().Prepend(chapter)
                    .Where(IsInvariant)
                    .Select(c => InvariantTitle(c.Title))
                    .ToList();
                index.TryAdd($"{name}#{chapter.Anchor}", titles);
            }
        }

        return index;
    }

    /// <summary>Each class chapter of the context's domain files by the name the
    /// model gives it — what the Fields header and the side column read their
    /// titles and sentences from.</summary>
    public static IReadOnlyDictionary<string, DomainModelProfile> Profiles(IEnumerable<DomainChapterFile> domainFiles)
    {
        var profiles = new Dictionary<string, DomainModelProfile>(StringComparer.Ordinal);
        foreach (var file in domainFiles)
        {
            foreach (var chapter in file.AllChapters().Where(c => c.Type is not null and not "term" and not "ubiquitous-language" && c.Anchor.Length > 0))
            {
                var profile = new DomainModelProfile(chapter.Title, chapter.Summary, chapter.Aliases, chapter.Status);
                profiles.TryAdd(Squash(chapter.Title), profile);
                foreach (var alias in chapter.Aliases.Where(a => Identifier().IsMatch(a))) profiles.TryAdd(alias, profile);
            }
        }

        return profiles;
    }

    /// <summary>A class's name as a reader says it: its chapter's heading, else the
    /// name with its words apart — <c>TaskStatus</c> is <em>Task Status</em>.</summary>
    public static string Title(string name, IReadOnlyDictionary<string, DomainModelProfile>? profiles) =>
        profiles is not null && profiles.TryGetValue(name, out var profile) ? profile.Title : string.Join(' ', Words().Matches(name).Select(m => m.Value));

    /// <summary>A field's type as the row shows it, or null when the diagram gives none.</summary>
    public static DomainFieldType? TypeOf(DomainClassDiagram diagram, DomainModelAttribute attribute, IReadOnlyDictionary<string, DomainModelProfile>? profiles = null)
    {
        if (DomainModelFields.IsIdentity(attribute)) return new("id", DomainFieldTone.Id);
        if (attribute.Type is null) return null;

        var baseType = DomainModelFields.BaseType(attribute.Type);
        var listed = Collection().IsMatch(attribute.Type);
        if (baseType is not null && diagram.Find(baseType) is { } named)
        {
            var tone = named.Kind switch
            {
                DomainModelKind.Enumeration => DomainFieldTone.Enumeration,
                DomainModelKind.Entity => DomainFieldTone.Entity,
                DomainModelKind.ValueObject => DomainFieldTone.ValueObject,
                _ => DomainFieldTone.Plain
            };
            var title = Title(named.Name, profiles);
            return new(listed ? $"{title} list" : title, tone);
        }

        return baseType switch
        {
            "Date" or "LocalDate" or "DateOnly" => new("date", DomainFieldTone.Date),
            "LocalDateTime" or "LocalTime" or "TimeOnly" => new("local time", DomainFieldTone.LocalTime),
            "Timestamp" or "Instant" or "DateTimeOffset" or "DateTime" or "ZonedDateTime" or "OffsetDateTime" => new("instant", DomainFieldTone.Instant),
            "String" or "string" or "Text" => new(listed ? "list of text" : "text", DomainFieldTone.Plain),
            "Integer" or "int" or "Int" or "Long" or "Decimal" or "decimal" or "Double" or "Number" => new(listed ? "list of numbers" : "number", DomainFieldTone.Plain),
            "Boolean" or "bool" or "Bool" => new("yes or no", DomainFieldTone.Plain),
            "Id" => new("id", DomainFieldTone.Plain),
            _ => new(DomainModelArrangement.TypeLabel(attribute.Type), DomainFieldTone.Plain)
        };
    }

    private static List<DomainFieldRowGroup> Written(
        DomainClassDiagram diagram,
        DomainModelClass root,
        DomainFieldChapter chapter,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? rules,
        IReadOnlyDictionary<string, DomainModelProfile>? profiles)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);
        var groups = new List<DomainFieldRowGroup>();
        foreach (var purpose in chapter.Purposes)
        {
            var rows = new List<DomainFieldRow>();
            foreach (var entry in purpose.Entries)
            {
                named.UnionWith(entry.Names);
                var attributes = entry.Names.Select(n => root.Attributes.FirstOrDefault(a => string.Equals(a.Name, n, StringComparison.Ordinal))).ToList();
                var derivedFrom = attributes.All(a => a is null) && entry.Names.Count == 1 ? DerivedClass(diagram, root, entry.Names[0]) : null;

                DomainFieldType? type;
                if (derivedFrom is not null)
                {
                    type = new(Title(derivedFrom.Name, profiles), derivedFrom.Kind == DomainModelKind.Enumeration ? DomainFieldTone.Enumeration : DomainFieldTone.Plain);
                }
                else
                {
                    var types = attributes.Select(a => a is null ? null : TypeOf(diagram, a, profiles)).Distinct().ToList();
                    type = types.Count == 1 ? types[0] : null;
                }

                var titles = entry.Rules
                    .SelectMany(link => rules is not null && rules.TryGetValue(RuleKey(link), out var found) ? found : [])
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                rows.Add(new DomainFieldRow(entry.Names, type, entry.Meaning.Length > 0 ? entry.Meaning : null, titles, derivedFrom is not null));
            }

            groups.Add(new DomainFieldRowGroup(purpose.Title, purpose.Question, rows));
        }

        var rest = root.Attributes
            .Where(a => !named.Contains(a.Name))
            .Select(a => new DomainFieldRow([a.Name], TypeOf(diagram, a, profiles), null, []))
            .ToList();
        if (rest.Count > 0) groups.Add(new DomainFieldRowGroup(Ungrouped, null, rest));

        return groups;
    }

    /// <summary>No purpose groups written: the kinds the types say, as before.</summary>
    private static List<DomainFieldRowGroup> ByKind(DomainClassDiagram diagram, DomainModelClass root, IReadOnlyDictionary<string, DomainModelProfile>? profiles)
    {
        var read = DomainModelFields.Read(diagram).FirstOrDefault(c => string.Equals(c.Name, root.Name, StringComparison.Ordinal));
        if (read is null) return [];

        return
        [
            // A part composed in with no attribute naming it is the side column's;
            // an attribute is a row whatever its kind.
            .. read.Groups
                .Select(g => new DomainFieldRowGroup(
                    DomainModelFields.Label(g.Kind),
                    null,
                    [
                        .. g.Fields
                            .Select(f => root.Attributes.FirstOrDefault(a => a.Name == f.Name))
                            .OfType<DomainModelAttribute>()
                            .Select(attribute => new DomainFieldRow([attribute.Name], TypeOf(diagram, attribute, profiles), null, []))
                    ]))
                .Where(g => g.Rows.Count > 0)
        ];
    }

    /// <summary>The class a field the root does not store stands for: a dashed
    /// dependency of the root whose name, in words joined by underscores, is the
    /// field — <c>readiness</c> for <c>Task ..&gt; Readiness</c>.</summary>
    private static DomainModelClass? DerivedClass(DomainClassDiagram diagram, DomainModelClass root, string field) =>
        diagram.Relations
            .Where(r => r.Kind == DomainModelRelationKind.Dependency && string.Equals(r.From, root.Name, StringComparison.Ordinal))
            .Select(r => diagram.Find(r.To))
            .FirstOrDefault(c => c is not null && string.Equals(Snake(c.Name), field, StringComparison.OrdinalIgnoreCase));

    private static DomainOwnedPart Part(DomainModelClass part, string? multiplicity, IReadOnlyDictionary<string, DomainModelProfile>? profiles, bool useChapter)
    {
        var summary = useChapter && profiles is not null && profiles.TryGetValue(part.Name, out var profile) && profile.Summary.Length > 0
            ? profile.Summary
            : string.Join(", ", part.Attributes.Where(a => !string.Equals(a.Name, "id", StringComparison.OrdinalIgnoreCase)).Select(a => a.Name.Replace('_', ' ')));
        return new DomainOwnedPart(part.Name, Title(part.Name, profiles), part.Kind, multiplicity, summary);
    }

    private static DomainOwnedEnum Enum(DomainModelClass enumeration, bool derived, IReadOnlyDictionary<string, DomainModelProfile>? profiles) =>
        new(enumeration.Name, Title(enumeration.Name, profiles), [.. enumeration.Attributes.Select(a => a.Name)], derived);

    private static bool IsInvariant(DomainChapter chapter) =>
        chapter.Type == "invariant" || chapter.Title.StartsWith("Invariant:", StringComparison.OrdinalIgnoreCase);

    private static string InvariantTitle(string title) =>
        title.StartsWith("Invariant:", StringComparison.OrdinalIgnoreCase) ? title["Invariant:".Length..].Trim() : title.Trim();

    /// <summary>A link target as the index keys it: the file's name and the anchor.</summary>
    internal static string RuleKey(string link)
    {
        var hash = link.IndexOf('#');
        if (hash < 0) return FileName(link).ToLowerInvariant();
        return $"{FileName(link[..hash])}#{link[(hash + 1)..]}".ToLowerInvariant();
    }

    private static string FileName(string path) => Path.GetFileName(path.Replace('\\', '/'));

    private static string Snake(string name) => string.Join('_', Words().Matches(name).Select(m => m.Value.ToLowerInvariant()));

    private static string Squash(string title) => string.Concat(Words().Matches(title).Select(m => char.ToUpperInvariant(m.Value[0]) + m.Value[1..]));

    [GeneratedRegex(@"[A-Z]+(?![a-z])|[A-Z]?[a-z0-9]+")]
    private static partial Regex Words();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"^(List|Set|Collection|IReadOnlyList|IEnumerable)~")]
    private static partial Regex Collection();
}
