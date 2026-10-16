using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.CompleteItem;

/// <summary>
/// Records the day an item's work was done, for work no task carries.
/// <para>
/// An item gathered tasks says it is finished when they are; one that gathered none has
/// nothing to say it, so a person does. The day is the caller's — today, as the person
/// sees it — rather than read from a clock here, so the command stays a plain record of
/// what was said. The clock only refuses a day that has not come yet: work cannot be done
/// in the future, and a finished bar dated ahead would release what waits on it early.
/// </para>
/// </summary>
public sealed record CompleteItemCommand(Guid ItemId, DateOnly On);

public sealed class CompleteItemCommandHandler(IRoadmapPlanRepository plans, RoadmapPlanGate gate, TimeProvider clock)
    : ICommandHandler<CompleteItemCommand, Result<RoadmapItemDto>>
{
    public async Task<Result<RoadmapItemDto>> Handle(
        CompleteItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        if (command.On > today) return Result.Failure<RoadmapItemDto>(RoadmapErrors.CompletedInFuture(command.On));

        using var held = await gate.EnterAsync(cancellationToken);
        var plan = await plans.LoadAsync(cancellationToken);
        var completed = plan.CompleteItem(command.ItemId, command.On);
        if (completed.IsFailure) return Result.Failure<RoadmapItemDto>(completed.Error);

        await plans.SaveAsync(plan, cancellationToken);
        return Result.Success(completed.Value.ToDto());
    }
}
