namespace Backlog.UI.Components.Devbook;

/// <summary>One unit's rules as Always true reads them, and the domain file it
/// is a unit of.</summary>
public sealed record DomainUnitRules(DomainChapterFile Page, DomainUnit Unit, IReadOnlyList<DomainRuleGroup> Groups)
{
    /// <summary>Every rule of the unit, in Always true's order.</summary>
    public IReadOnlyList<DomainRule> Rules { get; } = [.. Groups.SelectMany(group => group.Rules)];
}

/// <summary>
/// Every rule a bounded context's invariants files write, by the unit whose
/// Always true tab lists it (local ADR 0037) — what a page asks when it shows
/// the rules about the thing it is drawing, in place, rather than sending the
/// reader to Always true. Pure, like <see cref="DomainUnits"/>.
/// </summary>
public sealed class DomainRuleCatalog
{
    /// <summary>A context with no rules written.</summary>
    public static readonly DomainRuleCatalog Empty = new([]);

    private DomainRuleCatalog(IReadOnlyList<DomainUnitRules> units)
    {
        Units = units;
        var place = 0;
        foreach (var rule in units.SelectMany(unit => unit.Rules)) _order.TryAdd(rule, place++);
    }

    // Each rule's place in Always true's order, unit by unit.
    private readonly Dictionary<DomainRule, int> _order = [];

    // Each answer, by the normalized reference asked about: a page asks on every
    // render, a Glossary once per row.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<DomainRule>> _about = new(StringComparer.Ordinal);

    /// <summary>Each aggregate and shared grouping with rules, in file order.</summary>
    public IReadOnlyList<DomainUnitRules> Units { get; }

    /// <summary>The rules of a context's files: its domain files, and the
    /// invariants file beside each, among <paramref name="files"/>.</summary>
    public static DomainRuleCatalog Of(IEnumerable<DomainChapterFile> files)
    {
        var all = files.ToList();
        var domainFiles = all.Where(file => IsDomainFile(file.Path)).ToList();
        return Of(domainFiles, all);
    }

    /// <summary>The rules <paramref name="invariantFiles"/> write about the units
    /// of <paramref name="domainFiles"/>.</summary>
    public static DomainRuleCatalog Of(IReadOnlyList<DomainChapterFile> domainFiles, IReadOnlyList<DomainChapterFile> invariantFiles)
    {
        if (domainFiles.Count == 0 || invariantFiles.Count == 0) return Empty;

        var units = new List<DomainUnitRules>();
        foreach (var file in domainFiles)
        {
            foreach (var unit in DomainUnits.Of(file, invariantFiles).Where(unit => unit.Kind is DomainUnitKind.Aggregate or DomainUnitKind.Shared))
            {
                var groups = DomainUnits.RulesOf(unit, file, invariantFiles, domainFiles);
                if (groups.Count > 0) units.Add(new DomainUnitRules(file, unit, groups));
            }
        }
        return units.Count == 0 ? Empty : new DomainRuleCatalog(units);
    }

    /// <summary>
    /// The rules about the chapter <paramref name="reference"/> names, each once,
    /// in Always true's order: every rule of a unit it names; the rules of a group
    /// that is the concept it names, or whose <c>related</c> names it; each rule
    /// whose <c>Enforced at:</c> line names its type; and the rule, or the written
    /// group of rules, it names in an invariants file. A reference without an
    /// anchor names every chapter of its file — a flow file, a split — except an
    /// unsplit <c>domain.md</c>, which would name every rule of the context.
    /// </summary>
    public IReadOnlyList<DomainRule> About(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || Units.Count == 0) return [];
        var target = DomainUnits.Normalize(reference);
        if (_about.TryGetValue(target, out var known)) return known;
        var whole = !target.Contains('#') && !target.EndsWith("/domain.md", StringComparison.Ordinal) && target != "domain.md";

        var found = new List<DomainRule>();
        foreach (var entry in Units)
        {
            if (DomainUnits.Normalize(entry.Unit.Reference) == target)
            {
                found.AddRange(entry.Rules);
                continue;
            }

            foreach (var group in entry.Groups)
            {
                var named = Names(group.Reference) || group.Links.Any(link => Names(link.Reference));
                found.AddRange(group.Rules.Where(rule => named
                    || (rule.EnforcedOn is { } type && Names(type))
                    || (rule.Reference.Length > 0 && Names(rule.Reference))));
            }
        }
        IReadOnlyList<DomainRule> result = [.. found.Distinct()];
        _about.TryAdd(target, result);
        return result;

        bool Names(string candidate)
        {
            var normalized = DomainUnits.Normalize(candidate);
            return normalized == target || (whole && DomainUnits.PathOf(normalized) == target);
        }
    }

    /// <summary>The rules about any of <paramref name="references"/>, each once,
    /// in Always true's order.</summary>
    public IReadOnlyList<DomainRule> About(IEnumerable<string> references) =>
        InOrder(references.SelectMany(About));

    /// <summary><paramref name="rules"/> each once, in Always true's order: the
    /// order a reader meets them in on the unit's own tab. A rule the catalog
    /// does not hold keeps its place after the rest.</summary>
    public IReadOnlyList<DomainRule> InOrder(IEnumerable<DomainRule> rules) =>
        [.. rules.Distinct().OrderBy(rule => _order.TryGetValue(rule, out var place) ? place : int.MaxValue)];

    private static bool IsDomainFile(string path) =>
        DomainFeatures.KindOf(path) == "domain" && !path.EndsWith(".invariants.md", StringComparison.OrdinalIgnoreCase);
}
