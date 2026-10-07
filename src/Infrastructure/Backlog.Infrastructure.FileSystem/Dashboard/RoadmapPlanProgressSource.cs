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
/// the section quotes, and every repository's pace in use for the per-part projection,
/// which is how the roadmap places each bar — every part of an item at its own
/// repository's pace, after the parts it waits on (ADR 0013, ruling 4 as amended on
/// 2026-10-07). Each item crosses with its parts (<see cref="PlanPartProgress"/>), because
/// the dashboard may not name the roadmap's own part (guideline ADR 0005).
/// </para>
/// <para>
/// An item the import sized by its effort is reported with the window the keep-up
/// projection gives it today (<see cref="RoadmapProjection"/>; ADR 0013, ruling 5): part
/// by part from its gathered effort at each repository's pace in use, after what it waits
/// on — not the window last stored, which only an opening of the roadmap or a task change
/// brings up to date, and a pace change never does (local ADR 0018). Its stored window
/// therefore says nothing about where it reaches, so every such item is gathered, and the
/// whole plan is projected so each is read after its predecessors.
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
    IAppFeatureSettings features,
    TimeProvider? clock = null) : IPlanProgressSource
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
        var candidates = MayReach(plan.Items, from, to);
        var gathered = await rollups.GatherPlanAsync(plan with { Items = candidates }, cancellationToken).ConfigureAwait(false);

        // One repository in scope quotes its own pace; several or none quote the global
        // one, because there is no single repository pace that speaks for a set.
        var scoped = await pace.ReadAsync(repositoryAliases.Count == 1 ? repositoryAliases[0] : null, cancellationToken).ConfigureAwait(false);
        var inUse = await velocity.ReadPacesInUseAsync(cancellationToken).ConfigureAwait(false);

        // Narrowed again once each window reads as the roadmap lays it out.
        var today = DateOnly.FromDateTime((clock ?? TimeProvider.System).GetLocalNow().DateTime);
        return Map(Read(plan, gathered, inUse, today, from, to), gathered, scoped, inUse, today);
    }

    /// <summary>The plan's items as the keep-up projection reads them on
    /// <paramref name="today"/>, narrowed to those that then overlap the window.</summary>
    internal static IReadOnlyList<RoadmapItemDto> Read(
        RoadmapPlanDto plan,
        IReadOnlyDictionary<Guid, RoadmapItemRollupDto> gathered,
        PacesInUseDto inUse,
        DateOnly today,
        DateOnly from,
        DateOnly to) =>
        InWindow(EffortWindow.Derive(plan.Items, gathered, inUse, today, plan.Milestones), from, to);

    /// <summary>The items whose own start–end overlaps the window, both ends inclusive.</summary>
    internal static IReadOnlyList<RoadmapItemDto> InWindow(IReadOnlyList<RoadmapItemDto> items, DateOnly from, DateOnly to) =>
        [.. items.Where(item => item.Start <= to && item.End >= from)];

    /// <summary>
    /// The items that could overlap the window once read: those whose stored window
    /// does, and every one sized by its effort — the projection lays it out again from
    /// today, after what it waits on, so neither stored date says where it reaches.
    /// </summary>
    internal static IReadOnlyList<RoadmapItemDto> MayReach(IReadOnlyList<RoadmapItemDto> items, DateOnly from, DateOnly to) =>
        [.. items.Where(item => (item.Start <= to && item.End >= from) || EffortWindow.IsDerived(item))];

    /// <summary>The reading for <paramref name="items"/>, each with the window it was
    /// already read with (<see cref="Read"/>) — never projected a second time, which would
    /// read it without the predecessors that hold it back — and its parts as of
    /// <paramref name="today"/> (<see cref="PartsOf"/>).</summary>
    internal static PlanReading Map(
        IReadOnlyList<RoadmapItemDto> items,
        IReadOnlyDictionary<Guid, RoadmapItemRollupDto> rollups,
        PlanningPacesDto scoped,
        PacesInUseDto inUse,
        DateOnly today) =>
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
                        PartsOf(item, rollups.TryGetValue(item.Id, out var gathered) ? gathered : null, inUse, today),
                        item.PlacedByImport == ImportPlacement.Effort);
                })
            ])
        {
            // The week the bars are counted in, so the outlook projects through it too.
            Week = inUse.Week
        };

    /// <summary>
    /// <paramref name="item"/>'s parts as the outlook projects them: each part's open
    /// estimated work, its repository's pace in use and the parts it waits on — formed by
    /// <see cref="RoadmapItemParts"/>, the one place parts are formed (ADR 0013, ruling 4 as
    /// amended on 2026-10-07).
    /// </summary>
    /// <remarks>
    /// The parts are formed as the work would be laid out by its effort, whatever placed the
    /// item. A due date or a person's hand draws the item as one window, which collapses its
    /// hand-overs into one part per repository with nothing to wait on; but the outlook asks
    /// where the work itself lands against that window, part after part, so it needs the
    /// parts the work makes. Only their work, pace and waits are read, never their dates:
    /// the outlook lays the work out again from today, counting an unestimated entry as
    /// nothing (<c>TaskInsights</c>), where a part counts it a point.
    /// </remarks>
    internal static IReadOnlyList<PlanPartProgress> PartsOf(
        RoadmapItemDto item,
        RoadmapItemRollupDto? rollup,
        PacesInUseDto inUse,
        DateOnly today)
    {
        var layout = RoadmapItemParts.Of(item with { PlacedByImport = ImportPlacement.Effort }, rollup, inUse, today);

        return
        [
            .. layout.Parts.Select(part => new PlanPartProgress(
                part.Band,
                part.Links.Where(link => !link.IsDone).Sum(link => Math.Max(0, link.Effort ?? 0)),
                part.Pace,
                part.WaitsOn))
        ];
    }

    private static PlanPaceBasis BasisOf(PlanningPacesDto paces) => paces.InEffect switch
    {
        PaceSource.LastTwoWeeks => PlanPaceBasis.LastTwoWeeks,
        PaceSource.LastFourWeeks => PlanPaceBasis.LastFourWeeks,
        PaceSource.LastEightWeeks => PlanPaceBasis.LastEightWeeks,
        _ => PlanPaceBasis.Manual
    };
}
