using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Roadmap.Features.GetPlan;

/// <summary>The whole plan, with its contradictions worked out.</summary>
public sealed record GetPlanQuery;

/// <param name="catchUp">Where the host replicates, the pull the first read waits for
/// (local ADR 0018, Consequences): a device that drew the plan it held before pulling
/// could save over an edit the other PC made. Optional — a host that does not
/// replicate registers none — and bounded by whoever answers it, so an offline
/// device reads what it has.</param>
public sealed class GetPlanQueryHandler(IRoadmapPlanRepository plans, IRoadmapCatchUp? catchUp = null)
    : IQueryHandler<GetPlanQuery, RoadmapPlanDto>
{
    public async Task<RoadmapPlanDto> Handle(GetPlanQuery query, CancellationToken cancellationToken = default)
    {
        if (catchUp is not null) await catchUp.CatchUpAsync(cancellationToken);

        var plan = await plans.LoadAsync(cancellationToken);
        return plan.ToDto();
    }
}
