using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;
using Backlog.Modules.Progress.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Features.SetGoal;

/// <summary>Sets a new goal (<c>.devbook/domain/progress/features.md#setting-a-goal</c>).</summary>
public sealed record CreateGoalCommand(SetGoalRequest Request);

/// <summary>Replaces a goal's title, outcome and due date.</summary>
public sealed record EditGoalCommand(Guid GoalId, SetGoalRequest Request);

/// <summary>Deletes a goal and its attachments; the work it held is unchanged
/// (<c>.devbook/domain/progress/requirements.md#requirement-deleting-a-goal-leaves-its-work-unchanged</c>).</summary>
public sealed record DeleteGoalCommand(Guid GoalId);

/// <summary>Puts a goal at <paramref name="ToIndex"/> in the person's order, counted among
/// every goal as the list gives them; nothing it holds changes.</summary>
public sealed record MoveGoalCommand(Guid GoalId, int ToIndex);

public sealed class SetGoalCommandHandler(GoalChanges changes, TimeProvider clock)
    : ICommandHandler<CreateGoalCommand, Result<GoalDto>>,
      ICommandHandler<EditGoalCommand, Result<GoalDto>>,
      ICommandHandler<DeleteGoalCommand, Result>,
      ICommandHandler<MoveGoalCommand, Result>
{
    public async Task<Result<GoalDto>> Handle(CreateGoalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);

        var created = Goal.Create(
            Guid.NewGuid(),
            command.Request.Title,
            command.Request.Outcome,
            command.Request.DueDate,
            clock.GetUtcNow());

        if (created.IsFailure) return created.Error;

        return await changes.AddAsync(created.Value, cancellationToken).ConfigureAwait(false);
    }

    public Task<Result<GoalDto>> Handle(EditGoalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);

        // Checked whole on a copy first, so a refused outcome never leaves a renamed goal.
        var check = Goal.Create(command.GoalId, command.Request.Title, command.Request.Outcome, command.Request.DueDate);
        if (check.IsFailure) return Task.FromResult(Result.Failure<GoalDto>(check.Error));

        return changes.ChangeAsync(
            command.GoalId,
            (_, goal) =>
            {
                var steps = new[]
                {
                    goal.Rename(command.Request.Title),
                    goal.ChangeOutcome(command.Request.Outcome),
                    goal.ChangeDueDate(command.Request.DueDate),
                };

                var failed = steps.FirstOrDefault(step => step.IsFailure);
                return failed is null
                    ? Result.Success<IReadOnlyList<Goal>>([])
                    : Result.Failure<IReadOnlyList<Goal>>(failed.Error);
            },
            cancellationToken);
    }

    public Task<Result> Handle(DeleteGoalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return changes.DeleteAsync(command.GoalId, cancellationToken);
    }

    public Task<Result> Handle(MoveGoalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return changes.MoveAsync(command.GoalId, command.ToIndex, cancellationToken);
    }
}
