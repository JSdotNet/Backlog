using System.ComponentModel;

using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The plan above the backlog: what is scheduled, the days it is read against,
/// and where it disagrees with itself.
/// <para>
/// <b>Its own class because it is its own group.</b> Local ADR 0012 §7 gives each
/// switchable area one feature check, and Roadmap Planning is a bounded context
/// with a key of its own (<see cref="Backlog.Modules.Roadmap.Abstractions.RoadmapFeatures.Roadmap"/>)
/// — the same key the Home shell reads to decide whether to draw the band. A
/// group is a tool class, so a second group needs a second class.
/// </para>
/// <para>
/// It would have been fewer files to leave this method on <c>WorkTools</c> and
/// give the catalog two entries pointing at one type, and it would have been
/// wrong: the SDK constructs the whole tool class per invocation, so a class
/// holding both <see cref="ITaskItems"/> and <see cref="IRoadmapPlanning"/> needs
/// both registered to answer either tool. A head that composes the backlog and
/// not the plan — or a person who has switched the roadmap off — would then be
/// one <c>ActivatorUtilities</c> call away from a failure inside
/// <c>list_entries</c>, which has nothing to do with the roadmap. One port per
/// class keeps a group's cost to the group.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class RoadmapTools(IRoadmapPlanning planning, IRepositoryDirectory repositories)
{
    internal const string GetRoadmap = "get_roadmap";

    [McpServerTool(Name = GetRoadmap, Title = "Get the roadmap for a repository", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("The planned work, milestones and date contradictions belonging to one repository. Read-only.")]
    public async Task<RoadmapPayload> GetRoadmapAsync(
        [Description("The repository in owner/name form, e.g. JSdotNet/Backlog.")]
        string repository,
        CancellationToken cancellationToken = default)
    {
        var scope = RepositoryScope.Resolve(repositories, repository).ValueOrThrow();

        var plan = await planning.GetPlanAsync(cancellationToken).ConfigureAwait(false);

        // Aliases here, ids in the backlog tools, and the difference is not an
        // inconsistency to be smoothed over: the plan files work under the short
        // name a person types, the backlog files it under the coordinate.
        // TasksRepositoryRef carries both, which is why the mapping happens once
        // at the top.
        var items = plan.Items
            .Where(item => Names(item.RepositoryAliases, scope.Alias))
            .Select(Projections.RoadmapItem)
            .ToList();

        var milestones = plan.Milestones
            .Where(milestone => milestone.IsPlanWide || Names(milestone.RepositoryAliases, scope.Alias))
            .Select(Projections.Milestone)
            .ToList();

        // A contradiction names a pair of nodes, so it travels only when both
        // ends are in the slice being answered. One half of an arrow is not a
        // contradiction a reader can act on.
        var nodes = items.Select(item => item.Id)
            .Concat(milestones.Select(milestone => milestone.Id))
            .ToHashSet();

        var contradictions = plan.Contradictions
            .Where(contradiction => nodes.Contains(contradiction.NodeId) && nodes.Contains(contradiction.DependsOnId))
            .Select(Projections.Contradiction)
            .ToList();

        return new RoadmapPayload(scope.Id, scope.Alias, items, milestones, contradictions);
    }

    /// <summary>Whether a plan node is filed against this repository. A plan-wide
    /// milestone answers for every repository and is handled by its own clause;
    /// an empty list means unfiled, not "all".</summary>
    private static bool Names(IReadOnlyList<string> aliases, string alias) =>
        aliases.Contains(alias, StringComparer.OrdinalIgnoreCase);
}
