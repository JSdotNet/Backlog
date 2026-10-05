using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.Abstractions.Services;

/// <summary>
/// The working stretches behind a begun head's actual hours, so the person can check
/// the figure (local ADR 0019, §6). The head's hours open it.
/// <para>
/// A port beside <see cref="IRoadmapActualHours"/>, answered by the same adapter over
/// the same stretches, so a day's <see cref="RoadmapWorkedDayDto.Worked"/> is always
/// the figure its head shows.
/// </para>
/// </summary>
public interface IRoadmapHoursReport
{
    /// <summary>
    /// Every working day from <paramref name="from"/> through
    /// <paramref name="through"/>, both included, that has begun, with its stretches.
    /// A day nobody worked is present with none. A day after today is never answered.
    /// <para>
    /// <see langword="null"/> when this host cannot state the hours, for
    /// <see cref="IRoadmapActualHours.ReadAsync"/>' reasons. A read that fails throws.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<RoadmapWorkedDayDto>?> ReadDaysAsync(
        DateOnly from,
        DateOnly through,
        CancellationToken cancellationToken = default);
}
