using System.Text.RegularExpressions;

namespace Backlog.UI.Components.Devbook;

/// <summary>What a rule's mark on the Always true tab says about it.</summary>
public enum DomainRuleProof
{
    /// <summary>✓ — the rule's chapter names a test in its <c>tests</c> field.</summary>
    Proved,

    /// <summary>○ — the rule is enforced, and no test is named for it yet.</summary>
    Unproved,

    /// <summary>? — its <c>Enforced at:</c> line is <c>open</c>: nothing enforces it yet.</summary>
    Open
}

/// <summary>One rule of a unit, as the Always true tab reads it.</summary>
/// <param name="Title">The heading as written, <c>Invariant:</c> left off.</param>
/// <param name="Claim">The rule's first sentence as plain text — its heading when
/// the chapter has no sentence of its own.</param>
/// <param name="EnforcedAt">What its <c>Enforced at:</c> line says, as written,
/// or <see langword="null"/> when it has none.</param>
public sealed record DomainRule(string Title, string Claim, string? EnforcedAt, DomainRuleProof Proof);

/// <summary>The rules about one concept: the type their <c>Enforced at:</c> line
/// names, or the unit itself.</summary>
/// <param name="Title">The concept's heading.</param>
/// <param name="Type">The concept's <c>type</c>, which its marker draws.</param>
/// <param name="Reference">The path and anchor of the concept's chapter, which
/// the group's heading opens.</param>
public sealed record DomainRuleGroup(string Title, string Type, string Reference, IReadOnlyList<DomainRule> Rules);

public static partial class DomainUnits
{
    private static readonly string[] ConceptTypes = ["aggregate", "entity", "value-object", "enum", "domain-service"];

    /// <summary>The invariants file beside a domain file: <c>domain.md</c> keeps
    /// its rules in <c>domain.invariants.md</c>, a split <c>domain.&lt;name&gt;.md</c>
    /// in <c>domain.&lt;name&gt;.invariants.md</c>.</summary>
    public static string InvariantsPathFor(string domainPath)
    {
        var path = domainPath ?? string.Empty;
        return path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? path[..^3] + ".invariants.md"
            : path + ".invariants.md";
    }

    /// <summary>The units of <paramref name="file"/>, as <see cref="Of(DomainChapterFile)"/>
    /// reads them, with each <c>shared-value-objects</c> and <c>shared-enums</c>
    /// grouping that has rules in <paramref name="invariantFiles"/> among them, in
    /// file order: the base page is where a shared value's or enum's rules show.</summary>
    public static IReadOnlyList<DomainUnit> Of(DomainChapterFile file, IEnumerable<DomainChapterFile> invariantFiles)
    {
        var units = Of(file);
        var rules = InvariantsOf(file, invariantFiles);
        if (rules is null) return units;

        var keys = units.Select(unit => unit.Key).ToHashSet(StringComparer.Ordinal);
        var shared = file.Chapters
            .Where(chapter => chapter.Type is "shared-value-objects" or "shared-enums")
            .Where(grouping => SharedChapters(grouping, file, rules).Any(chapter => chapter.Descendants().Any(IsInvariant)))
            .ToList();
        if (shared.Count == 0) return units;

        var result = new List<DomainUnit>(units);
        foreach (var grouping in shared)
        {
            var key = grouping.Anchor;
            for (var n = 1; !keys.Add(key); n++) key = $"{grouping.Anchor}-{n}";
            result.Add(new DomainUnit(DomainUnitKind.Shared, key, grouping.Title, grouping.Type!, file.ReferenceTo(grouping), grouping));
        }

        // In file order, as the bar lists everything else.
        var order = file.AllChapters().ToList();
        return [.. result.OrderBy(unit => order.FindIndex(chapter => ReferenceEquals(chapter, unit.Chapter)))];
    }

    /// <summary>
    /// The rules of <paramref name="unit"/>, a unit of <paramref name="page"/>,
    /// read from the page's invariants file among <paramref name="invariantFiles"/>
    /// and grouped by the concept each rule's <c>Enforced at:</c> line names — an
    /// aggregate, entity, value object, enum or domain service of
    /// <paramref name="domainFiles"/>, by its model name. A line that names none,
    /// or is <c>open</c>, keeps the rule with the unit: the aggregate, or for a
    /// shared grouping the value or enum its chapter is about. The unit's own group
    /// comes first, then the others in the order their first rule is written.
    /// </summary>
    public static IReadOnlyList<DomainRuleGroup> RulesOf(
        DomainUnit unit,
        DomainChapterFile page,
        IEnumerable<DomainChapterFile> invariantFiles,
        IEnumerable<DomainChapterFile> domainFiles)
    {
        var rules = InvariantsOf(page, invariantFiles);
        if (rules is null) return [];

        var concepts = Concepts([page, .. domainFiles]);
        var unitConcept = new Concept(unit.Chapter.Title, unit.Type, unit.Reference);

        IEnumerable<(DomainChapter Rule, Concept Fallback)> written;
        if (unit.Kind == DomainUnitKind.Shared)
        {
            written = SharedChapters(unit.Chapter, page, rules)
                .SelectMany(chapter => chapter.Descendants().Where(IsInvariant)
                    .Select(rule => (rule, SharedConcept(unit, chapter, page, rules))));
        }
        else
        {
            var target = Normalize(unit.Reference);
            var chapters = rules.Chapters
                .Where(chapter => chapter.Related.Any(related => Resolve(related, rules.Path) == target)
                    || string.Equals(chapter.Title, unit.Chapter.Title, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // A split page's invariants file is the one aggregate's, whatever its
            // chapters are called.
            var invariants = chapters.Count > 0
                ? chapters.SelectMany(chapter => chapter.Descendants().Where(IsInvariant))
                : IsSplit(page) ? rules.AllChapters().Where(IsInvariant) : [];
            written = invariants.Select(rule => (rule, unitConcept));
        }

        var groups = new List<(Concept Concept, List<DomainRule> Rules)>();
        foreach (var (chapter, fallback) in written)
        {
            var rule = Rule(chapter);
            var concept = NamedConcept(rule.EnforcedAt, concepts) ?? fallback;
            var group = groups.FirstOrDefault(g => g.Concept.Reference == concept.Reference);
            if (group.Rules is null)
            {
                group = (concept, []);
                groups.Add(group);
            }
            group.Rules.Add(rule);
        }

        var ordered = groups
            .OrderBy(group => group.Concept.Reference == unitConcept.Reference ? 0 : 1)
            .Select(group => new DomainRuleGroup(group.Concept.Title, group.Concept.Type, group.Concept.Reference, group.Rules));
        return [.. ordered];
    }

    private sealed record Concept(string Title, string Type, string Reference);

    /// <summary>The page's own invariants file, or <see langword="null"/>: the
    /// one <see cref="InvariantsPathFor"/> names, else the older spelling the
    /// store still reads as one — <c>invariants.md</c> beside <c>domain.md</c>,
    /// <c>invariants.&lt;name&gt;.md</c> beside <c>domain.&lt;name&gt;.md</c>.</summary>
    private static DomainChapterFile? InvariantsOf(DomainChapterFile page, IEnumerable<DomainChapterFile> invariantFiles)
    {
        var files = invariantFiles.ToList();
        var path = Normalize(InvariantsPathFor(page.Path));
        var slash = Normalize(page.Path).LastIndexOf('/');
        var name = Normalize(page.Path)[(slash + 1)..];
        var legacy = name.StartsWith("domain.", StringComparison.Ordinal)
            ? Normalize(page.Path)[..(slash + 1)] + "invariants." + name["domain.".Length..]
            : null;
        return files.FirstOrDefault(file => Normalize(file.Path) == path)
            ?? files.FirstOrDefault(file => Normalize(file.Path) == legacy);
    }

    private static bool IsSplit(DomainChapterFile page)
    {
        var name = Normalize(page.Path);
        var slash = name.LastIndexOf('/');
        return !string.Equals(name[(slash + 1)..], "domain.md", StringComparison.Ordinal);
    }

    /// <summary>The invariants chapters about a shared grouping: those whose
    /// <c>related</c> names it or a value or enum in it, or whose heading is the
    /// grouping's or one of theirs.</summary>
    private static IEnumerable<DomainChapter> SharedChapters(DomainChapter grouping, DomainChapterFile page, DomainChapterFile rules)
    {
        var members = grouping.Children.Where(child => child.Type is not null).ToList();
        var references = members.Prepend(grouping).Select(chapter => Normalize(page.ReferenceTo(chapter))).ToHashSet(StringComparer.Ordinal);
        var titles = members.Prepend(grouping).Select(chapter => chapter.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return rules.Chapters.Where(chapter =>
            chapter.Related.Any(related => references.Contains(Resolve(related, rules.Path)))
            || titles.Contains(chapter.Title));
    }

    /// <summary>The value or enum an invariants chapter of a shared grouping is
    /// about — the one its <c>related</c> or its heading names — else the
    /// grouping.</summary>
    private static Concept SharedConcept(DomainUnit unit, DomainChapter chapter, DomainChapterFile page, DomainChapterFile rules)
    {
        var named = chapter.Related.Select(related => Resolve(related, rules.Path)).ToHashSet(StringComparer.Ordinal);
        var member = unit.Chapter.Children
            .Where(child => child.Type is not null)
            .FirstOrDefault(child => named.Contains(Normalize(page.ReferenceTo(child)))
                || string.Equals(child.Title, chapter.Title, StringComparison.OrdinalIgnoreCase));
        return member is null
            ? new Concept(unit.Chapter.Title, unit.Type, unit.Reference)
            : new Concept(member.Title, member.Type!, page.ReferenceTo(member));
    }

    /// <summary>Every concept of the files by the names the model may call it:
    /// its model name, its heading run together, and each alias that is an
    /// identifier. The first file wins a name two files share.</summary>
    private static Dictionary<string, Concept> Concepts(IEnumerable<DomainChapterFile> files)
    {
        var concepts = new Dictionary<string, Concept>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (var chapter in file.AllChapters().Where(chapter => chapter.Type is not null && ConceptTypes.Contains(chapter.Type)))
            {
                var concept = new Concept(chapter.Title, chapter.Type!, file.ReferenceTo(chapter));
                var runTogether = string.Concat(Words().Matches(chapter.Title).Select(word => char.ToUpperInvariant(word.Value[0]) + word.Value[1..]));
                foreach (var name in chapter.Aliases.Where(alias => Identifier().IsMatch(alias)).Prepend(runTogether).Prepend(ModelName(chapter)))
                {
                    concepts.TryAdd(name, concept);
                }
            }
        }
        return concepts;
    }

    /// <summary>The first concept a code span of the <c>Enforced at:</c> line
    /// names — <c>`OrderLine`</c>, <c>`OrderLine.SetQuantity()`</c> — or
    /// <see langword="null"/>. A method call (<c>`Place()`</c>) and a type the
    /// domain does not name are no concept.</summary>
    private static Concept? NamedConcept(string? enforcedAt, Dictionary<string, Concept> concepts)
    {
        if (string.IsNullOrWhiteSpace(enforcedAt)) return null;
        foreach (Match span in CodeSpan().Matches(enforcedAt))
        {
            var lead = LeadingType().Match(span.Groups[1].Value);
            if (lead.Success && concepts.TryGetValue(lead.Groups[1].Value, out var concept)) return concept;
        }
        return null;
    }

    private static bool IsInvariant(DomainChapter chapter) =>
        chapter.Type == "invariant"
        || (chapter.Type is null && chapter.Title.StartsWith("Invariant:", StringComparison.OrdinalIgnoreCase));

    private static DomainRule Rule(DomainChapter chapter)
    {
        var title = chapter.Title.StartsWith("Invariant:", StringComparison.OrdinalIgnoreCase)
            ? chapter.Title["Invariant:".Length..].Trim()
            : chapter.Title;

        string? enforcedAt = null;
        var claimLines = new List<string>();
        foreach (var line in chapter.Prose.Split('\n'))
        {
            var match = EnforcedAtLine().Match(line);
            if (match.Success)
            {
                enforcedAt ??= match.Groups[1].Value.Trim();
                continue;
            }
            claimLines.Add(line);
        }

        var claim = FirstSentence(string.Join('\n', claimLines));
        var proof = enforcedAt is { } at && string.Equals(at.TrimEnd('.').Trim(), "open", StringComparison.OrdinalIgnoreCase)
            ? DomainRuleProof.Open
            : chapter.Tests.Count > 0 ? DomainRuleProof.Proved : DomainRuleProof.Unproved;
        return new DomainRule(title, claim.Length > 0 ? claim : title, enforcedAt, proof);
    }

    [GeneratedRegex(@"^\s*Enforced at:\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex EnforcedAtLine();

    [GeneratedRegex(@"^([A-Z][A-Za-z0-9]*)(?:$|\.[A-Za-z])")]
    private static partial Regex LeadingType();
}
