using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Modules.Roadmap.Services;

/// <summary>
/// The reader's paces: the one they typed, kept by the host, and three measured from
/// the estimated work they finished over the last two, four and eight weeks.
/// <para>
/// A measured pace is finished effort over <em>calendar</em> days, weekends
/// included, because that is what the roadmap draws in: a window is placed in
/// calendar days and ignores the working week (ADR 0013). Dividing by working days
/// instead would draw every bar about a third shorter than the stretch it was
/// measured over actually took.
/// </para>
/// <para>
/// Read on every call and kept nowhere, so a task ticked off is in the next reading.
/// Nothing already placed moves when a pace changes — a window is stored, not
/// recomputed (ADR 0013, ruling 5).
/// </para>
/// </summary>
internal sealed class PlanningPace(
    IPlanningVelocitySettings settings,
    IRoadmapCompletedWork completed,
    TimeProvider clock) : IPlanningPace, IPlanningVelocity
{
    /// <summary>The longest stretch measured, so one read covers all three.</summary>
    private const int LongestWeeks = 8;

    public event Action? Changed
    {
        add => settings.Changed += value;
        remove => settings.Changed -= value;
    }

    public async Task<PlanningPacesDto> ReadAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var finished = await completed.CompletedSinceAsync(StartOf(today, LongestWeeks), cancellationToken);

        return Paces(settings.Manual, settings.Source, finished, today);
    }

    public async Task<decimal> GetStoryPointsPerDayAsync(CancellationToken cancellationToken = default) =>
        (await ReadAsync(cancellationToken)).InUse;

    public string? SetManual(string? typed) => settings.SetManual(typed);

    public string? Choose(PaceSource source) => settings.Choose(source);

    internal static PlanningPacesDto Paces(
        decimal manual,
        PaceSource source,
        IReadOnlyList<CompletedEffortDto> finished,
        DateOnly today) =>
        new(
            manual,
            Measured(finished, today, 2),
            Measured(finished, today, 4),
            Measured(finished, today, LongestWeeks),
            source);

    /// <summary>
    /// Effort finished in the <paramref name="weeks"/> weeks ending today, today
    /// included, per calendar day — four decimals, the finest pace the typed one can
    /// hold. <c>null</c> when nothing estimated was finished: that stretch measured
    /// no pace, which is not the same as a pace of zero.
    /// </summary>
    internal static decimal? Measured(IReadOnlyList<CompletedEffortDto> finished, DateOnly today, int weeks)
    {
        var from = StartOf(today, weeks);
        var effort = finished
            .Where(entry => entry.CompletedOn >= from && entry.CompletedOn <= today && entry.Effort > 0)
            .Sum(entry => (decimal)entry.Effort);

        if (effort <= 0) return null;

        return Math.Round(effort / (weeks * 7), 4, MidpointRounding.AwayFromZero);
    }

    private static DateOnly StartOf(DateOnly today, int weeks) => today.AddDays(-(weeks * 7 - 1));
}
