using Backlog.Infrastructure.FileSystem.Activity;
using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.Dashboard;

/// <summary>
/// Answers the Dashboard's <see cref="IHoursWorkedSource"/> out of the Sessions
/// context's agent activity: the person's working stretches on each local date, split
/// into the time inside that date's office hours and the time outside them, beside the
/// date's planned hours (local ADR 0019, §7).
/// <para>
/// The stretches are <see cref="WorkingStretches"/>' — the ones
/// <see cref="RoadmapActualHours"/> sums — read from the same merged
/// <see cref="IAgentActivitySource"/>, over the same lookback, and cut into working days
/// starting at 04:00 local the same way. So a date's inside plus outside is always the figure on that date's
/// roadmap head. A cross-context join, and here for
/// <see cref="AgentActivityAssistantActivitySource"/>'s reason.
/// </para>
/// <para>
/// Office hours on a date run from its weekday's stored start to its stored end, on the
/// calendar date, when the date is worked — the override first, then the pattern — and
/// do not exist when it is not. The part of a working day after midnight therefore lies
/// outside them. A blocked date therefore counts all its work outside, and an unblocked
/// Saturday counts the Saturday times the pattern keeps. The split is to the minute;
/// the hour grids' rule of outlining an hour that office hours only touch does not
/// apply. The week is the kernel's value read per call through
/// <see cref="IWorkingHoursSettings.Current"/>, overrides included, so a day blocked on
/// the roadmap counts at once.
/// </para>
/// <para>
/// A failed read is not caught, on <see cref="RoadmapActualHours"/>' reason: a zero for
/// a day that was worked is the one wrong answer worse than none.
/// </para>
/// </summary>
public sealed class AgentActivityHoursWorkedSource : IHoursWorkedSource
{
    private readonly IAgentActivitySource? _activity;
    private readonly IWorkingHoursSettings? _week;
    private readonly TimeProvider _time;
    private readonly IAppFeatureSettings? _features;

    /// <param name="activity">Where each session's human turns and runs are read from,
    /// or null in a host that composed no Sessions activity — the hours are then not
    /// stated.</param>
    /// <param name="week">The person's working week with its day overrides, or null in a
    /// host that keeps none — the default week then applies.</param>
    /// <param name="time">The clock that says what today is, how far into it now is, and
    /// which zone a date is local to.</param>
    /// <param name="features">The feature switches, read per call so switching the
    /// Sessions area off takes effect at once, or null in a host that has none.</param>
    public AgentActivityHoursWorkedSource(
        IAgentActivitySource? activity,
        IWorkingHoursSettings? week,
        TimeProvider? time = null,
        IAppFeatureSettings? features = null)
    {
        _activity = activity;
        _week = week;
        _time = time ?? TimeProvider.System;
        _features = features;
    }

    public async Task<IReadOnlyList<HoursWorkedDay>?> ReadAsync(
        DateOnly from,
        DateOnly through,
        CancellationToken cancellationToken = default)
    {
        if (_activity is null) return null;
        if (_features is not null && !_features.IsEnabled(SessionFeatures.Sessions)) return null;
        if (through < from) return [];

        var zone = _time.LocalTimeZone;
        var now = _time.GetUtcNow();
        var week = _week?.Current ?? WorkingHours.Default;

        var start = WorkingStretches.StartOf(from, zone);
        var end = WorkingStretches.StartOf(through.AddDays(1), zone);

        // Read even when the whole range is still to come: the stretches are clipped to
        // now, so nothing is counted there, and one code path is one answer.
        var log = await _activity.GetActivityAsync(start - RoadmapActualHours.Lookback, cancellationToken).ConfigureAwait(false);

        var split = new Dictionary<DateOnly, (TimeSpan Inside, TimeSpan Outside)>();
        foreach (var (date, opened, closed) in WorkingStretches.ByLocalDate(WorkingStretches.Of(log, start, end, now), zone))
        {
            var inside = Overlap(opened, closed, OfficeHours(week, date, zone));
            var sum = split.TryGetValue(date, out var sofar) ? sofar : default;
            split[date] = (sum.Inside + inside, sum.Outside + (closed - opened - inside));
        }

        var days = new List<HoursWorkedDay>(through.DayNumber - from.DayNumber + 1);
        for (var date = from; date <= through; date = date.AddDays(1))
        {
            var (inside, outside) = split.TryGetValue(date, out var worked) ? worked : default;
            days.Add(new HoursWorkedDay(date, inside, outside, week.WorkedOn(date)));
        }

        return days;
    }

    /// <summary>
    /// The date's office hours as instants, half-open, or null when it has none: a date
    /// not worked, or a weekday whose end is not after its start. Each end is turned into
    /// an instant on its own, so on the day the clocks change the span is as long as the
    /// clock on the wall says.
    /// </summary>
    private static (DateTimeOffset Start, DateTimeOffset End)? OfficeHours(WorkingHours week, DateOnly date, TimeZoneInfo zone)
    {
        if (!week.IsWorked(date)) return null;

        var day = week.On(date.DayOfWeek);
        return (Instant(date, day.Start, zone), Instant(date, day.End, zone));
    }

    private static DateTimeOffset Instant(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>How much of one piece of a stretch falls inside the office hours.</summary>
    private static TimeSpan Overlap(DateTimeOffset opened, DateTimeOffset closed, (DateTimeOffset Start, DateTimeOffset End)? office)
    {
        if (office is not { } span) return TimeSpan.Zero;

        var from = opened > span.Start ? opened : span.Start;
        var to = closed < span.End ? closed : span.End;

        return to > from ? to - from : TimeSpan.Zero;
    }
}
