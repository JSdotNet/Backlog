using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.DomainModels;
using Backlog.Modules.Progress.Features.ReadSprintProgress;
using Backlog.Modules.Progress.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Features.CloseSprint;

/// <summary>Ends the open sprint and starts the next one in one step
/// (<c>.devbook/domain/progress/features.md#closing-a-sprint</c>).</summary>
public sealed record CloseSprintCommand(Guid SeriesId, IReadOnlyDictionary<Guid, CloseOutcome> Outcomes, string NextGoal);

/// <summary>
/// Works out which members are unfinished by the same reading the sprint view draws,
/// then lets the series close the sprint. No outcome changes a task.
/// </summary>
public sealed class CloseSprintCommandHandler(
    ISprintSeriesRepository repository,
    ProgressGate gate,
    ISprintTaskSource tasks,
    ISprintPullRequestReader pullRequests,
    TimeProvider time)
    : ICommandHandler<CloseSprintCommand, Result<SprintSeriesDto>>
{
    public async Task<Result<SprintSeriesDto>> Handle(CloseSprintCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var held = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        var series = await repository.GetAsync(command.SeriesId, cancellationToken).ConfigureAwait(false);
        if (series is null) return ProgressErrors.SeriesNotFound(command.SeriesId);

        var open = series.OpenSprint;
        if (open is null) return ProgressErrors.SprintNotFound(SprintSequence.NextNumber(series.Sprints));

        var reading = await ReadSprintProgressQueryHandler
            .ReadAsync(series, open, tasks, pullRequests, ReadSprintProgressQueryHandler.Today(time), cancellationToken)
            .ConfigureAwait(false);

        var members = reading.Members.Select(member => member.TaskId).ToList();
        var unfinished = reading.Members
            .Where(member => member.State != SprintWorkState.Done)
            .Select(member => member.TaskId)
            .ToList();

        var closed = series.CloseSprint(members, unfinished, command.Outcomes ?? new Dictionary<Guid, CloseOutcome>(), command.NextGoal);
        if (closed.IsFailure) return closed.Error;

        await repository.SaveAsync(series, cancellationToken).ConfigureAwait(false);
        return series.ToDto();
    }
}
