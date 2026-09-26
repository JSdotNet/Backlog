using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Roadmap.Features.RelengthenItem;

/// <summary>
/// The window an item's tasks would make now, asked before "Update from tasks" is
/// offered.
/// </summary>
/// <param name="GatheredEffort">What the item gathers now, handed in by the caller
/// for the reason an import hands it in: the effort is Tasks', and the caller has just
/// read it.</param>
public sealed record ProposeRelengthQuery(Guid ItemId, int GatheredEffort);

/// <summary>
/// Answers with a proposal only when there is something to offer. An item a person
/// placed, or one ending on its due date, is not the importer's to size; and an effort
/// that makes the window already drawn would offer a button that does nothing.
/// </summary>
public sealed class ProposeRelengthQueryHandler(IRoadmapPlanRepository plans, IPlanningVelocity velocity)
    : IQueryHandler<ProposeRelengthQuery, RoadmapRelengthProposalDto?>
{
    public async Task<RoadmapRelengthProposalDto?> Handle(
        ProposeRelengthQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var plan = await plans.LoadAsync(cancellationToken);
        var item = plan.Items.FirstOrDefault(candidate => candidate.Id == query.ItemId);
        if (item?.PlacedByImport is not ImportPlacement.Effort) return null;

        var current = item.Window;
        var (proposed, _) = ImportedPlanPlacement.Place(
            current.Start,
            due: null,
            Math.Max(0, query.GatheredEffort),
            await velocity.GetStoryPointsPerWeekAsync(cancellationToken));

        return proposed == current
            ? null
            : new RoadmapRelengthProposalDto(item.Id, current.Start, current.End, proposed.End, query.GatheredEffort);
    }
}
