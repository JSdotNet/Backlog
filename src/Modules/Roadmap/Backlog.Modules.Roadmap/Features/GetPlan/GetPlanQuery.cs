using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Roadmap.Features.GetPlan;

/// <summary>The whole plan, with its contradictions worked out.</summary>
public sealed record GetPlanQuery;

public sealed class GetPlanQueryHandler(IRoadmapPlanRepository plans)
    : IQueryHandler<GetPlanQuery, RoadmapPlanDto>
{
    public async Task<RoadmapPlanDto> Handle(GetPlanQuery query, CancellationToken cancellationToken = default)
    {
        var plan = await plans.LoadAsync(cancellationToken);
        return plan.ToDto();
    }
}
