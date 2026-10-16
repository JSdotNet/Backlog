using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Features.StartSprint;

/// <summary>Starts a series' sprint on a day that matches its cadence. After the first
/// sprint the next one is started by closing the open one.</summary>
public sealed record StartSprintCommand(Guid SeriesId, DateOnly StartOn, string Goal);

public sealed class StartSprintCommandHandler(SeriesChanges changes)
    : ICommandHandler<StartSprintCommand, Result<SprintSeriesDto>>
{
    public Task<Result<SprintSeriesDto>> Handle(StartSprintCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return changes.ChangeAsync(
            command.SeriesId,
            series => series.StartSprint(command.StartOn, command.Goal),
            cancellationToken);
    }
}
