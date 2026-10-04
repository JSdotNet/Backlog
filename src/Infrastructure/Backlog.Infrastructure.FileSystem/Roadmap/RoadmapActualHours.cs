using Backlog.Infrastructure.FileSystem.Activity;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Roadmap Planning's <see cref="IRoadmapActualHours"/> out of the Sessions
/// context's agent activity: the person's working stretches, built per session from
/// their own turns, merged so time two sessions shared counts once, cut at local
/// midnight and summed per date (ADR 0019 §6).
/// <para>
/// The join lives here because only an adapter may see both, the reason
/// <see cref="RoadmapItemRollupService"/> lives here. It asks the same
/// <see cref="IAgentActivitySource"/> the dashboard's activity grid asks — the merged
/// one, which folds this machine's transcripts through the activity cache and adds the
/// records replicated from paired devices — so the two surfaces read one record.
/// <see cref="WorkingStretches"/> draws the stretches, and the Dashboard's hours worked
/// draws the same ones. Agent and subagent time with no human turn behind it counts
/// nothing: on Tue 29 Sept 2026 agent-active time read 13.1 hours, most of them
/// overnight subagents of one orchestrating session.
/// </para>
/// <para>
/// A failed read is not caught. The port says a throw means the hours could not be
/// read, and an empty map would say they were read and came to nothing — a head would
/// then print a zero for a day that was worked.
/// </para>
/// <para>
/// Local dates are the clock's <see cref="TimeProvider.LocalTimeZone"/>, which is the
/// zone the dashboard's grid cuts its hours in. A date runs from one local midnight to
/// the next, so the day the clocks go back counts twenty-five hours and the day they go
/// forward twenty-three.
/// </para>
/// </summary>
public sealed class RoadmapActualHours : IRoadmapActualHours
{
    /// <summary>
    /// How far before the first date the activity is read. A stretch that began before
    /// that date and runs into it is only whole when its earlier turns are read too: a
    /// turn at 00:10 belongs to the stretch a turn at 23:50 began. A day is longer than
    /// any run of turns less than half an hour apart that anybody keeps up.
    /// </summary>
    internal static readonly TimeSpan Lookback = TimeSpan.FromDays(1);

    private readonly IAgentActivitySource? _activity;
    private readonly TimeProvider _time;
    private readonly IAppFeatureSettings? _features;

    /// <param name="activity">Where each session's human turns and runs are read from,
    /// or null in a host that composed no Sessions activity — the hours are then not
    /// stated.</param>
    /// <param name="time">The clock that says what today is, how far into it now is, and
    /// which zone a date is local to.</param>
    /// <param name="features">The feature switches, read per call so switching the
    /// Sessions area off takes effect at once, or null in a host that has none.</param>
    public RoadmapActualHours(
        IAgentActivitySource? activity,
        TimeProvider? time = null,
        IAppFeatureSettings? features = null)
    {
        _activity = activity;
        _time = time ?? TimeProvider.System;
        _features = features;
    }

    public async Task<IReadOnlyDictionary<DateOnly, TimeSpan>?> ReadAsync(
        DateOnly from,
        DateOnly through,
        CancellationToken cancellationToken = default)
    {
        if (_activity is null) return null;
        if (_features is not null && !_features.IsEnabled(SessionFeatures.Sessions)) return null;

        var zone = _time.LocalTimeZone;
        var now = _time.GetUtcNow();
        var today = WorkingStretches.LocalDate(now, zone);

        if (through > today) through = today;

        // Nothing in the range has begun, so there is nothing to read.
        if (through < from) return new Dictionary<DateOnly, TimeSpan>();

        var start = WorkingStretches.StartOf(from, zone);
        var end = WorkingStretches.StartOf(through.AddDays(1), zone);

        var log = await _activity.GetActivityAsync(start - Lookback, cancellationToken).ConfigureAwait(false);

        var hours = new Dictionary<DateOnly, TimeSpan>();
        foreach (var (date, opened, closed) in WorkingStretches.ByLocalDate(WorkingStretches.Of(log, start, end, now), zone))
        {
            hours[date] = (hours.TryGetValue(date, out var sum) ? sum : TimeSpan.Zero) + (closed - opened);
        }

        return hours;
    }
}
