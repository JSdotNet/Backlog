using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.Dashboard;

/// <summary>
/// ADAPTER — answers the dashboard's <see cref="IPlanProgressSource"/> from the Roadmap
/// context's plan, its rollups and its paces.
/// <para>
/// The plan is narrowed to the window before its work is gathered, because gathering
/// walks the backlog per item and the dashboard shows a quarter at most of a plan that
/// may run for years. Both paces are read once a call: the scope's pace for the figure
/// the section quotes, and every repository's pace in use for the per-item projection,
/// which is how the roadmap places each bar — an item at the lowest pace across its
/// repositories.
/// </para>
/// <para>
/// With the roadmap switched off nothing is read at all: a plan somebody turned off is
/// not one the dashboard should keep reading behind their back.
/// </para>
/// </summary>
public sealed class RoadmapPlanProgressSource(
    IRoadmapPlanning planning,
    IRoadmapItemRollup rollups,
    IPlanningPace pace,
    IPlanningVelocity velocity,
    IAppFeatureSettings features) : IPlanProgressSource
{
    public async Task<PlanReading> ReadAsync(
        DateOnly from,
        DateOnly to,
        IReadOnlyList<string> repositoryAliases,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repositoryAliases);

        if (!features.IsEnabled(RoadmapFeatures.Roadmap)) return PlanReading.Off;

        var plan = await planning.GetPlanAsync(cancellationToken).ConfigureAwait(false);
        var inWindow = InWindow(plan.Items, from, to);
        var gathered = await rollups.GatherPlanAsync(plan with { Items = inWindow }, cancellationToken).ConfigureAwait(false);

        // One repository in scope quotes its own pace; several or none quote the global
        // one, because there is no single repository pace that speaks for a set.
        var scoped = await pace.ReadAsync(repositoryAliases.Count == 1 ? repositoryAliases[0] : null, cancellationToken).ConfigureAwait(false);
        var inUse = await velocity.ReadPacesInUseAsync(cancellationToken).ConfigureAwait(false);

        return Map(inWindow, gathered, scoped, inUse);
    }

    /// <summary>The items whose own start–end overlaps the window, both ends inclusive.</summary>
    internal static IReadOnlyList<RoadmapItemDto> InWindow(IReadOnlyList<RoadmapItemDto> items, DateOnly from, DateOnly to) =>
        [.. items.Where(item => item.Start <= to && item.End >= from)];

    internal static PlanReading Map(
        IReadOnlyList<RoadmapItemDto> items,
        IReadOnlyDictionary<Guid, RoadmapItemRollupDto> rollups,
        PlanningPacesDto scoped,
        PacesInUseDto inUse) =>
        new(
            true,
            new PlanPace(scoped.InUse, BasisOf(scoped)),
            [
                .. items.Select(item =>
                {
                    var rollup = rollups.TryGetValue(item.Id, out var found) ? found : RoadmapItemRollupDto.Empty;
                    return new PlanItemProgress(
                        item.Id,
                        item.Title,
                        item.Start,
                        item.End,
                        item.RepositoryAliases,
                        rollup.GatheredCount,
                        rollup.DoneCount,
                        rollup.TotalEffort,
                        rollup.DoneEffort,
                        rollup.UnestimatedCount,
                        rollup.IsFinished,
                        rollup.LastCompletedOn,
                        inUse.For(item.RepositoryAliases),
                        item.PlacedByImport == ImportPlacement.Effort);
                })
            ]);

    private static PlanPaceBasis BasisOf(PlanningPacesDto paces) => paces.InEffect switch
    {
        PaceSource.LastTwoWeeks => PlanPaceBasis.LastTwoWeeks,
        PaceSource.LastFourWeeks => PlanPaceBasis.LastFourWeeks,
        PaceSource.LastEightWeeks => PlanPaceBasis.LastEightWeeks,
        _ => PlanPaceBasis.Manual
    };
}
