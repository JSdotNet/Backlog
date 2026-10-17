using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;
using Backlog.Modules.Progress.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Features.AttachWork;

/// <summary>Attaches a plan, a task or a pull request to a goal
/// (<c>.devbook/domain/progress/features.md#attaching-work-to-a-goal</c>). One another goal
/// holds is refused unless <paramref name="MoveFromOtherGoal"/> is set.</summary>
public sealed record AttachToGoalCommand(Guid GoalId, GoalItemDto Item, bool MoveFromOtherGoal = false);

/// <summary>Detaches an item from a goal; nothing on the item changes.</summary>
public sealed record DetachFromGoalCommand(Guid GoalId, GoalItemDto Item);

public sealed class AttachWorkCommandHandler(GoalChanges changes, TimeProvider clock)
    : ICommandHandler<AttachToGoalCommand, Result<GoalDto>>,
      ICommandHandler<DetachFromGoalCommand, Result<GoalDto>>
{
    public Task<Result<GoalDto>> Handle(AttachToGoalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = AttachmentOf(command.Item, DateOnly.FromDateTime(clock.GetLocalNow().DateTime));
        if (item.IsFailure) return Task.FromResult(Result.Failure<GoalDto>(item.Error));

        return changes.ChangeAsync(
            command.GoalId,
            (all, goal) =>
            {
                var assigned = GoalAssignment.Assign(all, goal, item.Value, command.MoveFromOtherGoal);
                if (assigned.IsFailure) return Result.Failure<IReadOnlyList<Goal>>(assigned.Error);

                return Result.Success<IReadOnlyList<Goal>>(assigned.Value is { } movedFrom ? [movedFrom] : []);
            },
            cancellationToken);
    }

    public Task<Result<GoalDto>> Handle(DetachFromGoalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = AttachmentOf(command.Item, DateOnly.MinValue);
        if (item.IsFailure) return Task.FromResult(Result.Failure<GoalDto>(item.Error));

        return changes.ChangeAsync(
            command.GoalId,
            (_, goal) =>
            {
                goal.Detach(item.Value);
                return Result.Success<IReadOnlyList<Goal>>([]);
            },
            cancellationToken);
    }

    /// <summary>The attachment a caller's item names.</summary>
    internal static Result<GoalAttachment> AttachmentOf(GoalItemDto? item, DateOnly attachedOn)
    {
        ArgumentNullException.ThrowIfNull(item);
        return GoalAttachment.Of(item.Kind, item.PlanTag, item.TaskId, item.Repository, item.Number, attachedOn);
    }
}
