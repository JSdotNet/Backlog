using Backlog.Modules.Work.Abstractions.Plans.DataTransferObjects;
using Backlog.Modules.Work.Plans.DomainModels;
using Backlog.Modules.Work.Plans.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Work.Plans.Features.PinItemEnd;

/// <summary>Lets the forecast decide where a started item ends again ("Use forecast").</summary>
public sealed record UnpinItemEndCommand(Guid ItemId);

public sealed class UnpinItemEndCommandHandler(IRoadmapPlanRepository plans, RoadmapPlanGate gate)
    : ICommandHandler<UnpinItemEndCommand, Result<RoadmapItemDto>>
{
    public async Task<Result<RoadmapItemDto>> Handle(
        UnpinItemEndCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var held = await gate.EnterAsync(cancellationToken);
        var plan = await plans.LoadAsync(cancellationToken);
        var unpinned = plan.UnpinEnd(command.ItemId);
        if (unpinned is not Ok<RoadmapItem>(var item)) return (Error)unpinned.Value!;

        await plans.SaveAsync(plan, cancellationToken);
        return Result.Success(item.ToDto());
    }
}
