using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.RelengthenPlan;

/// <summary>
/// Re-lengthens every item whose window is still sized by its effort, at the reader's
/// pace as it stands now — what a person changing that pace asks for (ADR 0013,
/// ruling 5 as amended).
/// <para>
/// The rule "Update from tasks" applies to one item, applied to all of them: the
/// start is kept and the end recomputed. A window placed by its due date keeps the end
/// the person wrote, and one a person moved is theirs.
/// </para>
/// </summary>
/// <param name="GatheredEffort">Per item id, what the item gathers now — handed in by
/// the caller because the effort is Tasks' and the caller has just read it. An item
/// absent from it is left where it is: nobody said what it gathers.</param>
public sealed record RelengthenPlanCommand(IReadOnlyDictionary<Guid, int> GatheredEffort);

/// <summary>
/// One load, one save, and no save at all when no window moved — a save that changes
/// nothing is still a write the other devices would sync. Nothing that waits on a
/// re-lengthened item moves; the plan reports what now overlaps, as it always does.
/// </summary>
public sealed class RelengthenPlanCommandHandler(IRoadmapPlanRepository plans, IPlanningVelocity velocity)
    : ICommandHandler<RelengthenPlanCommand, Result<IReadOnlyList<RoadmapItemDto>>>
{
    public async Task<Result<IReadOnlyList<RoadmapItemDto>>> Handle(
        RelengthenPlanCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.GatheredEffort);

        var plan = await plans.LoadAsync(cancellationToken);
        var storyPointsPerWeek = await velocity.GetStoryPointsPerWeekAsync(cancellationToken);
        var relengthened = new List<RoadmapItemDto>();

        foreach (var item in plan.Items.Where(item => item.PlacedByImport is ImportPlacement.Effort).ToList())
        {
            if (!command.GatheredEffort.TryGetValue(item.Id, out var gathered)) continue;

            var previous = item.Window;
            var (window, placement) = ImportedPlanPlacement.Place(
                previous.Start,
                due: null,
                Math.Max(0, gathered),
                storyPointsPerWeek);

            if (window == previous) continue;

            var placed = plan.PlaceByImport(item.Id, window, placement);
            if (placed.IsFailure) return Result.Failure<IReadOnlyList<RoadmapItemDto>>(placed.Error);

            relengthened.Add(placed.Value.ToDto());
        }

        if (relengthened.Count > 0) await plans.SaveAsync(plan, cancellationToken);

        return Result.Success<IReadOnlyList<RoadmapItemDto>>(relengthened);
    }
}
