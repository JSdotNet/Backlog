using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Features.FillSprint;

/// <summary>Adds one task to a sprint by its id. Nothing on the task changes.</summary>
public sealed record AddTaskCommand(Guid SeriesId, int Number, Guid TaskId);

/// <summary>Takes a directly added task out of a sprint. Nothing on the task changes.</summary>
public sealed record RemoveTaskCommand(Guid SeriesId, int Number, Guid TaskId);

/// <summary>Links a whole plan to a sprint by its plan tag.</summary>
public sealed record LinkPlanCommand(Guid SeriesId, int Number, string PlanTag);

/// <summary>Unlinks a plan; the tasks it brought in leave unless added directly.</summary>
public sealed record UnlinkPlanCommand(Guid SeriesId, int Number, string PlanTag);

/// <summary>Rewrites a sprint's goal in words.</summary>
public sealed record SetSprintGoalCommand(Guid SeriesId, int Number, string Goal);

/// <summary>The writes that say what a sprint holds
/// (<c>.devbook/domain/progress/features.md#filling-a-sprint</c>). Each is recorded in
/// the sprint alone; a closed sprint refuses them.</summary>
public sealed class FillSprintCommandHandler(SeriesChanges changes, TimeProvider time)
    : ICommandHandler<AddTaskCommand, Result<SprintSeriesDto>>,
      ICommandHandler<RemoveTaskCommand, Result<SprintSeriesDto>>,
      ICommandHandler<LinkPlanCommand, Result<SprintSeriesDto>>,
      ICommandHandler<UnlinkPlanCommand, Result<SprintSeriesDto>>,
      ICommandHandler<SetSprintGoalCommand, Result<SprintSeriesDto>>
{
    public Task<Result<SprintSeriesDto>> Handle(AddTaskCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var today = Today();
        return changes.ChangeAsync(
            command.SeriesId,
            series => series.AddTask(command.Number, command.TaskId, today),
            cancellationToken);
    }

    public Task<Result<SprintSeriesDto>> Handle(RemoveTaskCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return changes.ChangeAsync(
            command.SeriesId,
            series => series.RemoveTask(command.Number, command.TaskId),
            cancellationToken);
    }

    public Task<Result<SprintSeriesDto>> Handle(LinkPlanCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var today = Today();
        return changes.ChangeAsync(
            command.SeriesId,
            series => series.LinkPlan(command.Number, command.PlanTag, today),
            cancellationToken);
    }

    public Task<Result<SprintSeriesDto>> Handle(UnlinkPlanCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return changes.ChangeAsync(
            command.SeriesId,
            series => series.UnlinkPlan(command.Number, command.PlanTag),
            cancellationToken);
    }

    public Task<Result<SprintSeriesDto>> Handle(SetSprintGoalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return changes.ChangeAsync(
            command.SeriesId,
            series => series.SetSprintGoal(command.Number, command.Goal),
            cancellationToken);
    }

    private DateOnly Today() => DateOnly.FromDateTime(time.GetLocalNow().DateTime);
}
