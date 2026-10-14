using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Features.MarkSprintItem;

/// <summary>Marks a sprint item as a goal task, or takes the mark off.</summary>
public sealed record MarkGoalCommand(Guid SeriesId, int Number, Guid TaskId, bool IsGoal);

/// <summary>Writes a one-line note on a sprint item, or clears it with null.</summary>
public sealed record SetNoteCommand(Guid SeriesId, int Number, Guid TaskId, string? Note);

/// <summary>Marks a sprint item as an open question, or takes the mark off.</summary>
public sealed record SetOpenQuestionCommand(Guid SeriesId, int Number, Guid TaskId, bool OpenQuestion);

/// <summary>What a person records on one item
/// (<c>.devbook/domain/progress/features.md#keeping-a-note-on-an-item</c>). Owned by the
/// sprint, never by the task.</summary>
public sealed class MarkSprintItemCommandHandler(SeriesChanges changes)
    : ICommandHandler<MarkGoalCommand, Result<SprintSeriesDto>>,
      ICommandHandler<SetNoteCommand, Result<SprintSeriesDto>>,
      ICommandHandler<SetOpenQuestionCommand, Result<SprintSeriesDto>>
{
    public Task<Result<SprintSeriesDto>> Handle(MarkGoalCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return changes.ChangeAsync(
            command.SeriesId,
            series => series.MarkGoal(command.Number, command.TaskId, command.IsGoal),
            cancellationToken);
    }

    public Task<Result<SprintSeriesDto>> Handle(SetNoteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return changes.ChangeAsync(
            command.SeriesId,
            series => series.SetNote(command.Number, command.TaskId, command.Note),
            cancellationToken);
    }

    public Task<Result<SprintSeriesDto>> Handle(SetOpenQuestionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return changes.ChangeAsync(
            command.SeriesId,
            series => series.SetOpenQuestion(command.Number, command.TaskId, command.OpenQuestion),
            cancellationToken);
    }
}
