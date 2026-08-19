using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.PrioritiseItem;

/// <summary>
/// Changes how much the plan wants a piece of work.
/// <para>
/// It touches the plan and nothing else. A linked backlog entry keeps its own
/// priority: the two are different judgements made for different reasons, and
/// reprioritising a quarter must not mean editing a dozen issues.
/// </para>
/// </summary>
public sealed record PrioritiseItemCommand(Guid ItemId, PlanningPriority Priority);

public sealed class PrioritiseItemCommandHandler(IRoadmapPlanRepository plans)
    : ICommandHandler<PrioritiseItemCommand, Result<RoadmapItemDto>>
{
    public async Task<Result<RoadmapItemDto>> Handle(
        PrioritiseItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var plan = await plans.LoadAsync(cancellationToken);
        var prioritised = plan.Prioritise(command.ItemId, command.Priority);

        if (prioritised.IsFailure) return Result.Failure<RoadmapItemDto>(prioritised.Error);

        await plans.SaveAsync(plan, cancellationToken);
        return Result.Success(prioritised.Value.ToDto());
    }
}
