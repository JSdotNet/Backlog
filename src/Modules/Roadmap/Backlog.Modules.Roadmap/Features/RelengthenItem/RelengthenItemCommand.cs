using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.RelengthenItem;

/// <summary>
/// Re-lengthens one item from what its tasks register now — "Update from tasks".
/// <para>
/// The same rule a task-level re-import applies to an effort-placed item (ADR 0013,
/// ruling 5), asked for by a person rather than by an import: the start is kept, the
/// end is recomputed from the gathered effort at the reader's pace, and the window
/// stays the importer's. It is never run on its own; the stored window is what the
/// plan draws until somebody asks.
/// </para>
/// </summary>
/// <param name="GatheredEffort">What the item gathers now — the rollup total the person
/// was reading when they asked.</param>
public sealed record RelengthenItemCommand(Guid ItemId, int GatheredEffort);

/// <summary>
/// Places the item and reports what now overlaps it.
/// <para>
/// Nothing that waits on the item moves. A dependent that now opens before the item
/// closes is a contradiction, and contradictions are reported, never corrected — so
/// the ones this change introduced are named in the result, and the ones that were
/// already there are not news.
/// </para>
/// </summary>
public sealed class RelengthenItemCommandHandler(IRoadmapPlanRepository plans, IPlanningVelocity velocity)
    : ICommandHandler<RelengthenItemCommand, Result<RoadmapRelengthResultDto>>
{
    public async Task<Result<RoadmapRelengthResultDto>> Handle(
        RelengthenItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var plan = await plans.LoadAsync(cancellationToken);
        var item = plan.Items.FirstOrDefault(candidate => candidate.Id == command.ItemId);

        if (item is null)
        {
            return Result.Failure<RoadmapRelengthResultDto>(RoadmapErrors.ItemNotFound(command.ItemId));
        }

        if (item.PlacedByImport is not ImportPlacement.Effort)
        {
            return Result.Failure<RoadmapRelengthResultDto>(
                RoadmapErrors.NotPlacedByEffort(item.Title, item.PlacedByImport));
        }

        var previous = item.Window;
        var (window, placement) = ImportedPlanPlacement.Place(
            previous.Start,
            due: null,
            Math.Max(0, command.GatheredEffort),
            await velocity.GetStoryPointsPerWeekAsync(cancellationToken));

        // Nothing to write, and a save that changes nothing is still a write the other
        // devices would sync.
        if (window == previous)
        {
            return Result.Success(new RoadmapRelengthResultDto(item.ToDto(), previous.Start, previous.End, []));
        }

        var waitingBefore = WaitingInContradiction(plan, item.Id).ToHashSet();

        var placed = plan.PlaceByImport(item.Id, window, placement);
        if (placed.IsFailure) return Result.Failure<RoadmapRelengthResultDto>(placed.Error);

        var titles = plan.Nodes().ToDictionary(node => node.Id, node => node.Title);
        var nowOverlapping = WaitingInContradiction(plan, item.Id)
            .Where(id => !waitingBefore.Contains(id))
            .Select(id => new RoadmapNodeRefDto(id, titles[id]))
            .ToList();

        await plans.SaveAsync(plan, cancellationToken);

        return Result.Success(new RoadmapRelengthResultDto(placed.Value.ToDto(), previous.Start, previous.End, nowOverlapping));
    }

    /// <summary>The nodes the plan says open before <paramref name="itemId"/> closes
    /// while waiting on it, in the order the plan reports its contradictions.</summary>
    private static List<Guid> WaitingInContradiction(RoadmapPlan plan, Guid itemId) =>
    [
        .. plan.Contradictions()
            .Where(contradiction => contradiction.DependsOnId == itemId)
            .Select(contradiction => contradiction.NodeId)
            .Distinct()
    ];
}
