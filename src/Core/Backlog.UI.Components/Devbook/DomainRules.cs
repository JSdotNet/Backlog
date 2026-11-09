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
public sealed record DomainRule(string Title, string Claim, string? EnforcedAt, DomainRuleProof Proof)
{
    /// <summary>The rule's own chapter: the invariants file as the host spells it
    /// and the rule's anchor. Empty for a rule read outside a file.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>Where the rule sits on Always true: its unit, with
    /// <see cref="DomainAlwaysTrueLink.Rule"/> naming the rule, which a list of
    /// rules shown elsewhere links to. <see langword="null"/> for a rule read
    /// outside a unit.</summary>
    public DomainAlwaysTrueLink? Place { get; init; }

    /// <summary>The concept its <c>Enforced at:</c> line names, by its reference,
    /// or <see langword="null"/> when the line names none of the domain's.</summary>
    public string? EnforcedOn { get; init; }

    /// <summary>Whether the rule's mark is known: false for a rule a page names
    /// that no invariants file read holds, which is drawn by its heading alone
    /// rather than with a mark that would claim what stands behind it.</summary>
    public bool Marked { get; init; } = true;
}

/// <summary>The rules about one concept. Either a group the invariants chapter
/// writes itself — a <c>type: invariants</c> heading inside the aggregate's
/// chapter, its rules beneath it — or, for a rule outside every such group, the
/// type its <c>Enforced at:</c> line names, else the unit itself.</summary>
/// <param name="Title">The group's heading as written, or the concept's.</param>
/// <param name="Type">The concept's <c>type</c>, which its marker draws, or
/// <see langword="null"/> for a written group, which is no model element.</param>
/// <param name="Reference">The path and anchor of the chapter the group stands
/// for: the concept's, which its heading opens, or the written group's own.</param>
public sealed record DomainRuleGroup(string Title, string? Type, string Reference, IReadOnlyList<DomainRule> Rules)
{
    /// <summary>What a written group is about, from its <c>related</c>, each
    /// shown beside its heading. Empty for a concept's group, whose heading is
    /// the concept.</summary>
    public IReadOnlyList<DomainRuleLink> Links { get; init; } = [];
}

/// <summary>One thing a written group of rules is about.</summary>
/// <param name="Text">The chapter's heading where the files read name it, else
/// its anchor or file name read back as words.</param>
/// <param name="Reference">The reference the group's <c>related</c> names, made
/// repository-relative.</param>
public sealed record DomainRuleLink(string Text, string Reference);

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

        IEnumerable<(DomainChapter Rule, DomainChapter? Written, Concept Fallback)> found;
        if (unit.Kind == DomainUnitKind.Shared)
        {
            found = SharedChapters(unit.Chapter, page, rules)
                .SelectMany(chapter => InvariantsUnder(chapter)
                    .Select(rule => (rule.Rule, rule.Group, SharedConcept(unit, chapter, page, rules))));
        }
        else
        {
            var target = Normalize(unit.Reference);
            var chapters = rules.Chapters
                .Where(chapter => chapter.Related.Any(related => Resolve(related, rules.Path) == target)
                    || string.Equals(chapter.Title, unit.Chapter.Title, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // A split page's invariants file is the one aggregate's, whatever its
            // chapters are called, and a rule written straight under its title is
            // one of them too.
            var invariants = chapters.Count > 0
                ? chapters.SelectMany(InvariantsUnder)
                : IsSplit(page)
                    ? rules.Chapters.SelectMany(chapter => IsInvariant(chapter) ? [(chapter, null)] : InvariantsUnder(chapter))
                    : [];
            found = invariants.Select(rule => (rule.Rule, rule.Group, unitConcept));
        }

        var titles = Titles([page, .. domainFiles, rules]);
        var groups = new List<(Concept Concept, IReadOnlyList<DomainRuleLink> Links, List<DomainRule> Rules)>();
        foreach (var (chapter, written, fallback) in found)
        {
            var named = NamedConcept(Rule(chapter).EnforcedAt, concepts);
            var rule = Rule(chapter) with
            {
                Reference = rules.ReferenceTo(chapter),
                Place = new DomainAlwaysTrueLink(unit.Chapter.Title, page.Path, unit.Key) { Rule = rules.ReferenceTo(chapter) },
                EnforcedOn = named?.Reference
            };
            var concept = written is null
                ? named ?? fallback
                : new Concept(written.Title, null, rules.ReferenceTo(written));
            var group = groups.FirstOrDefault(g => g.Concept.Reference == concept.Reference);
            if (group.Rules is null)
            {
                group = (concept, written is null ? [] : LinksOf(written, rules.Path, titles, Normalize(unit.Reference)), []);
                groups.Add(group);
            }
            group.Rules.Add(rule);
        }

        // Written groups read in the chapter's order. Where the chapter writes
        // none, the unit's own group leads.
        var anyWritten = groups.Any(group => group.Concept.Type is null);
        var ordered = groups
            .OrderBy(group => !anyWritten && group.Concept.Reference == unitConcept.Reference ? 0 : 1)
            .Select(group => new DomainRuleGroup(group.Concept.Title, group.Concept.Type, group.Concept.Reference, group.Rules) { Links = group.Links });
        return [.. ordered];
    }

    private sealed record Concept(string Title, string? Type, string Reference);

    /// <summary>The rules under <paramref name="chapter"/> in file order, each
    /// with the written group it sits in: the nearest <c>type: invariants</c>
    /// heading between the chapter and the rule, or none.</summary>
    private static IEnumerable<(DomainChapter Rule, DomainChapter? Group)> InvariantsUnder(DomainChapter chapter)
    {
        return Walk(chapter, null);

        static IEnumerable<(DomainChapter, DomainChapter?)> Walk(DomainChapter parent, DomainChapter? group)
        {
            foreach (var child in parent.Children)
            {
                if (IsInvariant(child))
                {
                    yield return (child, group);
                    continue;
                }
                foreach (var found in Walk(child, child.Type == "invariants" ? child : group)) yield return found;
            }
        }
    }

    /// <summary>Every chapter's heading in <paramref name="files"/>, by the
    /// normalized reference that names it; the first file wins.</summary>
    private static Dictionary<string, string> Titles(IEnumerable<DomainChapterFile> files)
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (var chapter in file.AllChapters().Where(chapter => chapter.Anchor.Length > 0))
            {
                titles.TryAdd(Normalize(file.ReferenceTo(chapter)), chapter.Title);
            }
        }
        return titles;
    }

    /// <summary>A written group's <c>related</c>, as links: each reference made
    /// repository-relative against the invariants file, and named by the
    /// heading it opens where <paramref name="titles"/> holds it. The
    /// <paramref name="unit"/> it also names, which pairs the group with its
    /// aggregate, is left out.</summary>
    private static IReadOnlyList<DomainRuleLink> LinksOf(DomainChapter group, string fromDocument, Dictionary<string, string> titles, string unit)
    {
        var links = new List<DomainRuleLink>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { unit };
        foreach (var related in group.Related)
        {
            var reference = RepositoryRelative(related, fromDocument);
            // The unit is what every group on its tab is about, so it is no link,
            // and a concept named twice is linked once.
            if (reference.Length == 0 || !seen.Add(Normalize(reference))) continue;
            var text = titles.TryGetValue(Normalize(reference), out var title) ? title : ReadBack(reference);
            links.Add(new DomainRuleLink(text, reference));
        }
        return links;
    }

    /// <summary>A <c>related</c> entry as a repository-relative reference: a
    /// rooted one as written, an anchor against the document, and a relative one
    /// against the document's folder, as a Markdown viewer reads it.</summary>
    internal static string RepositoryRelative(string related, string fromDocument)
    {
        var text = related.Trim().Trim('"', '\'');
        if (text.Length == 0) return string.Empty;
        if (text.StartsWith('#')) return PathOf(fromDocument) + text;
        if (IsRooted(text)) return text.TrimStart('/');

        var folder = PathOf(fromDocument).Replace('\\', '/');
        var slash = folder.LastIndexOf('/');
        var parts = (slash < 0 ? [] : folder[..slash].Split('/')).ToList();
        foreach (var segment in text.Split('/'))
        {
            if (segment is "." or "") continue;
            if (segment == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(segment);
        }
        return string.Join('/', parts);
    }

    /// <summary>A reference read back as words: its anchor, else its file's name
    /// without the extension, the first letter raised.</summary>
    private static string ReadBack(string reference)
    {
        var hash = reference.IndexOf('#');
        var name = hash >= 0 ? reference[(hash + 1)..] : reference[(reference.LastIndexOf('/') + 1)..];
        if (hash < 0 && name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) name = name[..^3];
        name = name.Replace('-', ' ').Trim();
        return name.Length == 0 ? reference : char.ToUpperInvariant(name[0]) + name[1..];
    }

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

/// <summary>How a rule reads wherever it is drawn — Always true, and every
/// list of rules shown in place: its mark, the words behind the mark, and the
/// short claim a row leads with.</summary>
public static class DomainRuleText
{
    /// <summary>The mark: ✓ proved, ○ no test yet, ? not enforced yet.</summary>
    public static string Glyph(DomainRuleProof proof) => proof switch
    {
        DomainRuleProof.Proved => "✓",
        DomainRuleProof.Open => "?",
        _ => "○"
    };

    /// <summary>What the mark says, for a listener.</summary>
    public static string ProofText(DomainRuleProof proof) => proof switch
    {
        DomainRuleProof.Proved => "Proved by a test",
        DomainRuleProof.Open => "Not enforced yet",
        _ => "No test yet"
    };

    /// <summary>The mark's name, as a class and a data attribute spell it.</summary>
    public static string ProofName(DomainRuleProof proof) => proof switch
    {
        DomainRuleProof.Proved => "proved",
        DomainRuleProof.Open => "open",
        _ => "unproved"
    };

    /// <summary>What a row reads as: the rule's heading, the short claim, as a
    /// sentence. Its first sentence, which names the fields and calls, waits
    /// with where it is enforced.</summary>
    public static string Headline(DomainRule rule)
    {
        var title = rule.Title.Trim();
        return title.Length == 0 || ".!?".Contains(title[^1], StringComparison.Ordinal) ? title : title + ".";
    }

    /// <summary>A count of rules in words: "1 rule", "3 rules".</summary>
    public static string Count(int count) => count == 1 ? "1 rule" : $"{count} rules";
}
