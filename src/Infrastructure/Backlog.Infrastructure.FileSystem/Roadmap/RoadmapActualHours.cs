using Backlog.Infrastructure.FileSystem.Activity;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Roadmap Planning's <see cref="IRoadmapActualHours"/> out of the Sessions
/// context's agent activity: the person's working stretches, built per session from
/// their own turns, joined so time two sessions shared counts once, cut where a working
/// day starts at 04:00 local and summed per date (ADR 0019 §6).
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
/// zone the dashboard's grid cuts its hours in. A date is a working day, from 04:00 local
/// to 04:00 the next morning (<see cref="WorkingStretches.DayStartsAt"/>), so the one the
/// clocks go back in counts twenty-five hours and the one they go forward in twenty-three.
/// </para>
/// </summary>
public sealed class RoadmapActualHours : IRoadmapActualHours, IRoadmapHoursReport
{
    /// <summary>
    /// How far before the first date the activity is read. A stretch that began before
    /// that date and runs into it is only whole when its earlier turns are read too: a
    /// turn at 04:10 belongs to the stretch a turn at 03:50 began. A day is longer than
    /// any run of turns less than half an hour apart that anybody keeps up.
    /// </summary>
    internal static readonly TimeSpan Lookback = TimeSpan.FromDays(1);

    private readonly IAgentActivitySource? _activity;
    private readonly TimeProvider _time;
    private readonly IAppFeatureSettings? _features;
    private readonly IAgentSessionSource? _sessions;

    /// <param name="activity">Where each session's human turns and runs are read from,
    /// or null in a host that composed no Sessions activity — the hours are then not
    /// stated.</param>
    /// <param name="time">The clock that says what today is, how far into it now is, and
    /// which zone a date is local to.</param>
    /// <param name="features">The feature switches, read per call so switching the
    /// Sessions area off takes effect at once, or null in a host that has none.</param>
    /// <param name="sessions">Where the hours report reads each session's title and
    /// repository, or null in a host without one: the report then names a session by
    /// its id. Never read for the figures.</param>
    public RoadmapActualHours(
        IAgentActivitySource? activity,
        TimeProvider? time = null,
        IAppFeatureSettings? features = null,
        IAgentSessionSource? sessions = null)
    {
        _activity = activity;
        _time = time ?? TimeProvider.System;
        _features = features;
        _sessions = sessions;
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

    public async Task<IReadOnlyList<RoadmapWorkedDayDto>?> ReadDaysAsync(
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
        if (through < from) return [];

        var start = WorkingStretches.StartOf(from, zone);
        var end = WorkingStretches.StartOf(through.AddDays(1), zone);

        var log = await _activity.GetActivityAsync(start - Lookback, cancellationToken).ConfigureAwait(false);
        var names = await NamesAsync(start - Lookback, cancellationToken).ConfigureAwait(false);

        var pieces = new Dictionary<DateOnly, List<RoadmapWorkedStretchDto>>();
        foreach (var stretch in WorkingStretches.Traced(log, start, end, now))
        {
            foreach (var (date, opened, closed) in WorkingStretches.ByLocalDate([(stretch.Start, stretch.End)], zone))
            {
                if (!pieces.TryGetValue(date, out var list)) pieces[date] = list = [];

                list.Add(new RoadmapWorkedStretchDto(
                    TimeZoneInfo.ConvertTime(opened, zone),
                    TimeZoneInfo.ConvertTime(closed, zone),
                    stretch.Turns.Count(turn => turn >= opened && turn < closed),
                    [.. WorkedIn(stretch, opened, closed).Select(session => Name(session, names))]));
            }
        }

        var days = new List<RoadmapWorkedDayDto>(through.DayNumber - from.DayNumber + 1);
        for (var date = from; date <= through; date = date.AddDays(1))
        {
            var stretches = pieces.TryGetValue(date, out var list) ? list : [];
            days.Add(new RoadmapWorkedDayDto(
                date,
                stretches.Aggregate(TimeSpan.Zero, (sum, stretch) => sum + stretch.Length),
                stretches));
        }

        return days;
    }

    /// <summary>
    /// The sessions of a stretch that were at work in one part of it: a turn of the
    /// person's or a run of the agent's inside that part. A part with neither, such as
    /// the gap the person spent moving between two sessions, names every session of the
    /// stretch.
    /// </summary>
    private static IReadOnlyList<AgentSessionActivity> WorkedIn(
        WorkingStretches.TracedStretch stretch,
        DateTimeOffset opened,
        DateTimeOffset closed)
    {
        var inside = stretch.Sessions
            .Where(session =>
                session.HumanTurns.Any(turn => turn >= opened && turn < closed)
                || session.Runs.Any(run => run.StartedAt < closed && run.EndedAt > opened))
            .ToList();

        return inside.Count > 0 ? inside : stretch.Sessions;
    }

    /// <summary>
    /// Each session's title and repository by its kind and id, or none where this host
    /// has no session source or it could not be read. A name is a convenience for the
    /// reader, and a report that names sessions by id is still a whole report.
    /// </summary>
    private async Task<IReadOnlyDictionary<(AgentSessionKind Kind, string Id), AgentSession>> NamesAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        if (_sessions is null) return new Dictionary<(AgentSessionKind, string), AgentSession>();

        try
        {
            var catalog = await _sessions.GetSessionsAsync(AgentSessionQuery.Since(since), cancellationToken).ConfigureAwait(false);
            return catalog.Sessions
                .GroupBy(session => (session.Kind, session.Id))
                .ToDictionary(group => group.Key, group => group.First());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new Dictionary<(AgentSessionKind, string), AgentSession>();
        }
    }

    private static RoadmapStretchSessionDto Name(
        AgentSessionActivity session,
        IReadOnlyDictionary<(AgentSessionKind Kind, string Id), AgentSession> names) =>
        names.TryGetValue((session.Kind, session.Id), out var named)
            ? new RoadmapStretchSessionDto(
                string.IsNullOrWhiteSpace(named.Title) ? session.Id : named.Title,
                named.ResolvedRepository ?? named.Repository,
                session.Environment)
            : new RoadmapStretchSessionDto(session.Id, null, session.Environment);
}
