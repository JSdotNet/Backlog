using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Roadmap Planning's <see cref="IRoadmapActualHours"/> out of the Sessions
/// context's agent activity: the stretches in which each agent was producing, merged
/// so time two agents shared counts once, cut at local midnight and summed per date.
/// <para>
/// The join lives here because only an adapter may see both, the reason
/// <see cref="RoadmapItemRollupService"/> lives here. It asks the same
/// <see cref="IAgentActivitySource"/> the dashboard's activity grid asks — the merged
/// one, which folds this machine's transcripts through the activity cache and adds the
/// runs replicated from paired devices — so the two surfaces read one record. It
/// counts every session's runs and every spawned agent's runs, and never a wait: a
/// parent session falls silent while the agent it spawned works, and that time was
/// worked.
/// </para>
/// <para>
/// A failed read is not caught. The port says a throw means the hours could not be
/// read, and an empty map would say they were read and came to nothing — a head would
/// then print a zero for a day that was worked. The axis catches and shows the planned
/// hours alone.
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
    private readonly IAgentActivitySource? _activity;
    private readonly TimeProvider _time;
    private readonly IAppFeatureSettings? _features;

    /// <param name="activity">Where the agents' active stretches are read from, or null
    /// in a host that composed no Sessions activity — the hours are then not
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
        var today = LocalDate(now, zone);

        if (through > today) through = today;

        // Nothing in the range has begun, so there is nothing to read.
        if (through < from) return new Dictionary<DateOnly, TimeSpan>();

        var start = StartOf(from, zone);
        var end = StartOf(through.AddDays(1), zone);
        if (end > now) end = now;

        var log = await _activity.GetActivityAsync(start, cancellationToken).ConfigureAwait(false);

        var runs = log.Sessions.SelectMany(session => session.Runs)
            .Concat(log.Subagents.SelectMany(agent => agent.Runs));

        var hours = new Dictionary<DateOnly, TimeSpan>();
        foreach (var (opened, closed) in Merged(runs, start, end))
        {
            AddByDate(hours, opened, closed, zone);
        }

        return hours;
    }

    /// <summary>
    /// The runs clipped to the window and merged where they overlap or touch, in order.
    /// This is where two agents at once become one stretch of worked time.
    /// </summary>
    private static List<(DateTimeOffset From, DateTimeOffset To)> Merged(
        IEnumerable<AgentActivityRun> runs,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        var clipped = runs
            .Select(run => (From: run.StartedAt > start ? run.StartedAt : start, To: run.EndedAt < end ? run.EndedAt : end))
            .Where(run => run.To > run.From)
            .OrderBy(run => run.From)
            .ToList();

        var merged = new List<(DateTimeOffset From, DateTimeOffset To)>();
        foreach (var run in clipped)
        {
            if (merged.Count > 0 && run.From <= merged[^1].To)
            {
                if (run.To > merged[^1].To) merged[^1] = (merged[^1].From, run.To);
                continue;
            }

            merged.Add(run);
        }

        return merged;
    }

    /// <summary>One merged stretch, cut at each local midnight it crosses, each part
    /// added to its own date.</summary>
    private static void AddByDate(
        Dictionary<DateOnly, TimeSpan> hours,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeZoneInfo zone)
    {
        var cursor = from;
        while (cursor < to)
        {
            var date = LocalDate(cursor, zone);
            var next = StartOf(date.AddDays(1), zone);

            // A zone whose midnight is skipped can name a next midnight at or before the
            // cursor; the rest of the stretch then stays on this date rather than looping.
            if (next <= cursor || next > to) next = to;

            hours[date] = (hours.TryGetValue(date, out var sum) ? sum : TimeSpan.Zero) + (next - cursor);
            cursor = next;
        }
    }

    private static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>The instant a local date begins at, the way the dashboard's grid turns a
    /// local cell back into an instant.</summary>
    private static DateTimeOffset StartOf(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
