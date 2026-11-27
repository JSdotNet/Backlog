using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.DomainModels;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Features.RescheduleItem;

/// <summary>
/// Moves planned work in time, and to another lane when one is given.
/// <para>
/// This is what a drag on the timeline becomes. The view proposes a placement and
/// this decides whether it stands — which is why the dates arrive as a proposal
/// rather than being written by whatever drew the bar.
/// </para>
/// </summary>
/// <param name="Lane">The lane it lands in, or null to leave it filed where it
/// is. Null rather than the current value on purpose: moving something in time
/// must not silently refile it.</param>
public sealed record RescheduleItemCommand(Guid ItemId, DateOnly Start, DateOnly End, string? Lane = null);

public sealed class RescheduleItemCommandHandler(IRoadmapPlanRepository plans, RoadmapPlanGate gate)
    : ICommandHandler<RescheduleItemCommand, Result<RoadmapItemDto>>
{
    public async Task<Result<RoadmapItemDto>> Handle(
        RescheduleItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var window = PlannedWindow.Create(command.Start, command.End);
        return window switch
        {
            Ok<PlannedWindow>(var planned) => await RescheduleAsync(planned),
            Error error => error,
        };

        async Task<Result<RoadmapItemDto>> RescheduleAsync(PlannedWindow planned)
        {
            using var held = await gate.EnterAsync(cancellationToken);
            var plan = await plans.LoadAsync(cancellationToken);
            var lane = command.Lane is null ? null : PlanningLane.Of(command.Lane);
            var rescheduled = plan.Reschedule(command.ItemId, planned, lane);

            return rescheduled switch
            {
                Ok<RoadmapItem>(var item) => await SavedAsync(plan, item),
                Error error => error,
            };
        }

        async Task<Result<RoadmapItemDto>> SavedAsync(RoadmapPlan plan, RoadmapItem item)
        {
            await plans.SaveAsync(plan, cancellationToken);
            return Result.Success(item.ToDto());
        }
    }
}
