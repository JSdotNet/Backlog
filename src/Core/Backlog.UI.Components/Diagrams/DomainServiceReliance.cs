using Backlog.UI.Components.Devbook;

namespace Backlog.UI.Components.Diagrams;

/// <summary>Rules a domain service relies on that one group of a unit's Always
/// true tab holds.</summary>
/// <param name="Unit">The unit whose Always true lists them: "Task".</param>
/// <param name="Group">The group's heading as Always true writes it: "Unlinking".</param>
public sealed record DomainRuleReliance(string Unit, string Group, IReadOnlyList<DomainRule> Rules)
{
    /// <summary>"Task · Unlinking (3)" — the unit, the group's heading as written
    /// and how many rules — or "Task (3)" when the group is the unit's own. No
    /// word is built from either name, so none is bent into a possessive.</summary>
    public string Label => string.Equals(Group.Trim(), Unit.Trim(), StringComparison.OrdinalIgnoreCase)
        ? $"{Unit} ({Rules.Count})"
        : $"{Unit} · {Group} ({Rules.Count})";
}

/// <summary>
/// What a domain service leans on, for the "Relies on" row of Model's
/// <em>Domain services · how they run</em>: the rules about the service and the
/// chapters its <c>related</c> names, as the service's own Domain page finds them,
/// grouped by the unit and group Always true lists each under.
/// </summary>
public static class DomainServiceReliance
{
    /// <summary>The groups of rules <paramref name="service"/> relies on, in Always
    /// true's order; empty when the catalog holds none about it.</summary>
    public static IReadOnlyList<DomainRuleReliance> Of(DomainServiceInvocation service, DomainRuleCatalog? catalog)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (catalog is null || service.References.Count == 0) return [];

        var rules = catalog.About(service.References);
        if (rules.Count == 0) return [];

        var groups = new List<(DomainUnitRules Unit, DomainRuleGroup Group, List<DomainRule> Rules)>();
        foreach (var rule in rules)
        {
            var owner = catalog.Units
                .SelectMany(unit => unit.Groups.Select(group => (Unit: unit, Group: group)))
                .FirstOrDefault(candidate => candidate.Group.Rules.Contains(rule));
            if (owner.Unit is null) continue;

            var entry = groups.FirstOrDefault(candidate => ReferenceEquals(candidate.Group, owner.Group));
            if (entry.Rules is null)
            {
                entry = (owner.Unit, owner.Group, []);
                groups.Add(entry);
            }
            entry.Rules.Add(rule);
        }

        return [.. groups.Select(entry => new DomainRuleReliance(entry.Unit.Unit.Label, entry.Group.Title, entry.Rules))];
    }
}
