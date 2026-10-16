using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.CompleteItem;

/// <summary>Opens an item a person completed by hand again ("Reopen").</summary>
public sealed record ReopenItemCommand(Guid ItemId);

public sealed class ReopenItemCommandHandler(IRoadmapPlanRepository plans, RoadmapPlanGate gate)
    : ICommandHandler<ReopenItemCommand, Result<RoadmapItemDto>>
{
    public async Task<Result<RoadmapItemDto>> Handle(
        ReopenItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var held = await gate.EnterAsync(cancellationToken);
        var plan = await plans.LoadAsync(cancellationToken);
        var reopened = plan.ReopenItem(command.ItemId);
        if (reopened.IsFailure) return Result.Failure<RoadmapItemDto>(reopened.Error);

        await plans.SaveAsync(plan, cancellationToken);
        return Result.Success(reopened.Value.ToDto());
    }
}
