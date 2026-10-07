using System.ComponentModel;

using Backlog.Modules.Roadmap.Abstractions;
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
/// <para>
/// <b>Windows as the roadmap lays them out.</b> An item the import sized by its effort is
/// answered with the window the keep-up projection gives it today
/// (<see cref="RoadmapProjection"/>; ADR 0013, ruling 5): part by part, from its gathered
/// effort at each repository's pace in use, after what it waits on — not the window last
/// stored, which only an opening of the roadmap or a task change brings up to date, and
/// a pace change never does (local ADR 0018). The rollup and the pace are optional for the
/// same reason the class is its own: a head that composes the plan without the backlog
/// still answers — with the stored windows.
/// </para>
/// </summary>
/// <param name="clock">Where "today" comes from for that projection. Optional, as on
/// <see cref="TrackerTools"/>: the container the tools are built from need not register a
/// <see cref="TimeProvider"/>, and <see cref="TimeProvider.System"/> stands in.</param>
[McpServerToolType]
public sealed class RoadmapTools(
    IRoadmapPlanning planning,
    IRepositoryDirectory repositories,
    IRoadmapItemRollup? rollups = null,
    IPlanningVelocity? velocity = null,
    TimeProvider? clock = null)
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
        if (rollups is not null && velocity is not null)
        {
            // Only this repository's slice and what it waits on are gathered: gathering
            // walks the backlog, and a predecessor filed elsewhere still holds it back.
            var today = DateOnly.FromDateTime((clock ?? TimeProvider.System).GetLocalNow().DateTime);
            plan = await plan.WithDerivedWindowsAsync(
                rollups,
                velocity,
                today,
                item => Names(item.RepositoryAliases, scope.Alias),
                cancellationToken).ConfigureAwait(false);
        }

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
