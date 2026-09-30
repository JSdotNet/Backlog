using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.PinItemEnd;

/// <summary>
/// Fixes where an item ends, so the forecast no longer decides it.
/// <para>
/// This is what a drag on the end of a started item's bar becomes: the start is work
/// that began and cannot move, but the end is a prediction, and the person may know
/// better. The start is deliberately not part of the command.
/// </para>
/// </summary>
public sealed record PinItemEndCommand(Guid ItemId, DateOnly End);

public sealed class PinItemEndCommandHandler(IRoadmapPlanRepository plans)
    : ICommandHandler<PinItemEndCommand, Result<RoadmapItemDto>>
{
    public async Task<Result<RoadmapItemDto>> Handle(
        PinItemEndCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var plan = await plans.LoadAsync(cancellationToken);
        var pinned = plan.PinEnd(command.ItemId, command.End);
        if (pinned.IsFailure) return Result.Failure<RoadmapItemDto>(pinned.Error);

        await plans.SaveAsync(plan, cancellationToken);
        return Result.Success(pinned.Value.ToDto());
    }
}
