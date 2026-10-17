using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Abstractions.Services;

/// <summary>
/// The stand-up (<c>.devbook/domain/progress/domain.md#stand-up-reading</c>,
/// <c>.devbook/domain/progress/features.md#the-stand-up</c>): the goals, what was finished
/// in the last window, what is planned for the next, and the open impediments, read afresh
/// from the work they name. It stores nothing. Every call fails with
/// <see cref="ProgressErrors.GoalsOff"/> while the <see cref="ProgressFeatures.Goals"/>
/// switch is off.
/// </summary>
public interface IStandUp
{
    /// <summary>The stand-up for <paramref name="today"/> by <paramref name="length"/>,
    /// two-week windows counted from <see cref="IIterationSettings.TwoWeekStart"/>.</summary>
    Task<Result<StandUpDto>> ReadAsync(DateOnly today, IterationLength length, CancellationToken cancellationToken = default);

    /// <summary>The stand-up for the host's today, in local time, by the person's own
    /// <see cref="IIterationSettings.Length"/>.</summary>
    Task<Result<StandUpDto>> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The person's pace for the stand-up's points tag (<c>.devbook/domain/progress/domain.md#pace</c>):
/// the story points they get done in a working week, as Roadmap Planning measures it or the
/// person typed it. A port the module asks outward through; the module's own answer is
/// null, which reads as no capacity figure.
/// </summary>
public interface IStandUpPace
{
    /// <summary>Points a working week, or null when no pace is known.</summary>
    Task<decimal?> PointsPerWeekAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The iteration a person reads their goals and stand-up in
/// (<c>.devbook/domain/progress/context.md#iteration-length</c>,
/// <c>#two-week-start</c>): its length, Week until chosen, and the date two-week windows
/// count from, none until two weeks is first chosen.
/// </summary>
public interface IIterationSettings
{
    /// <summary>Raised after either choice changed. It may arrive on any thread.</summary>
    event Action? Changed;

    IterationLength Length { get; }

    DateOnly? TwoWeekStart { get; }

    /// <summary>Chooses the length. Choosing <see cref="IterationLength.TwoWeeks"/> with no
    /// start yet sets the start to the Monday of <paramref name="today"/>'s week.</summary>
    void SetLength(IterationLength length, DateOnly today);

    /// <summary>Moves every two-week window to count from <paramref name="start"/>.</summary>
    void SetTwoWeekStart(DateOnly start);
}
