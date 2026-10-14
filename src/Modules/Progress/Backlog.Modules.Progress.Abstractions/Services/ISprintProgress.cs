using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Abstractions.Services;

/// <summary>
/// The progress service: one sprint read against its members' current state
/// (<c>.devbook/domain/progress/domain.md#sprint-progress</c>).
/// <para>
/// Every member task with its status, plan, repository, done date and linked pull
/// requests; the goal set; the goal and all-work counts; done per working day;
/// and whether the goal is met. Read afresh each time, so a task finished
/// elsewhere moves the sprint at once. Tasks are read only through
/// <see cref="ISprintTaskSource"/>.
/// </para>
/// </summary>
public interface ISprintProgress
{
    /// <summary>Sprint <paramref name="number"/> of the series, or the series' open
    /// sprint when <paramref name="number"/> is null. Fails with
    /// <see cref="ProgressErrors.Off"/> while Progress is switched off.</summary>
    Task<Result<SprintProgressDto>> ReadAsync(
        Guid seriesId,
        int? number = null,
        CancellationToken cancellationToken = default);
}
