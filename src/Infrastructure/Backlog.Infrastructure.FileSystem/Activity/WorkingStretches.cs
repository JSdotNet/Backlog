using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.FileSystem.Activity;

/// <summary>
/// The person's working stretches, drawn from the Sessions context's activity: each
/// session's human turns and the agent's runs (ADR 0019 §6). Pure: no I/O, and the
/// clock arrives as a parameter.
/// <para>
/// A stretch starts at a human turn. Turns of one session less than
/// <see cref="JoinWithin"/> apart belong to one stretch, which covers the gap between
/// them. It ends when the agent finishes answering its last turn: the end of the run
/// that turn started, being the run that contains it or begins at it. When no such run
/// ends after the turn, the stretch ends at the turn itself. Only the session's own
/// runs count; a subagent's work starts and extends nothing, so overnight agents with
/// no human turn behind them add no time.
/// </para>
/// <para>
/// Here, beside the two adapters that read it, because only an adapter may see both
/// sides. Roadmap Planning's actual hours (<c>RoadmapActualHours</c>) and the
/// Dashboard's hours worked both count these same stretches, so a date's hours inside
/// and outside office hours always add up to the figure on the roadmap's head.
/// </para>
/// </summary>
internal static class WorkingStretches
{
    /// <summary>
    /// Thirty minutes. Turns closer than this belong to one stretch; turns this far
    /// apart or further start a new one, and the gap between them belongs to neither.
    /// The owner's choice of 2026-10-03: the reading and thinking between two prompts
    /// is work, and a longer pause is not.
    /// </summary>
    internal static readonly TimeSpan JoinWithin = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Every session's stretches in a log, merged where they overlap or touch so time
    /// two sessions shared counts once, clipped to the window and to now, in order.
    /// <para>
    /// Sessions from every source the log composed count alike — this machine's
    /// transcripts, its records and the records replicated from paired machines — and
    /// the log's subagents count not at all. An open stretch counts up to now; see
    /// <see cref="Of(AgentSessionActivity, DateTimeOffset, TimeSpan)"/>.
    /// </para>
    /// </summary>
    /// <param name="log">The activity read, with each session's human turns.</param>
    /// <param name="from">The start of the window.</param>
    /// <param name="to">The end of the window, exclusive. Clipped to
    /// <paramref name="now"/>.</param>
    /// <param name="now">What time it is.</param>
    /// <returns>Disjoint, ascending, non-empty intervals.</returns>
    internal static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Of(
        AgentActivityLog log,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (to > now) to = now;

        var idleAfter = log.IdleAfter > TimeSpan.Zero ? log.IdleAfter : DefaultIdleAfter;

        var clipped = log.Sessions
            .SelectMany(session => Of(session, now, idleAfter))
            .Select(stretch => (Start: stretch.Start > from ? stretch.Start : from, End: stretch.End < to ? stretch.End : to))
            .Where(stretch => stretch.End > stretch.Start)
            .OrderBy(stretch => stretch.Start)
            .ToList();

        var merged = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var stretch in clipped)
        {
            if (merged.Count > 0 && stretch.Start <= merged[^1].End)
            {
                if (stretch.End > merged[^1].End) merged[^1] = (merged[^1].Start, stretch.End);
                continue;
            }

            merged.Add(stretch);
        }

        return merged;
    }

    /// <summary>
    /// One session's stretches, unclipped and in order. A stretch with a lone turn and
    /// no run after it starts and ends at that turn, so it may be empty.
    /// <para>
    /// A stretch is open while the agent may still be answering its last turn: its end
    /// lies less than <paramref name="idleAfter"/> before now, the gap after which the
    /// fold would have closed the run. An open stretch counts up to now. One that ends
    /// after now, which only a skewed clock produces, ends at now.
    /// </para>
    /// </summary>
    /// <param name="session">One session's activity.</param>
    /// <param name="now">What time it is.</param>
    /// <param name="idleAfter">The gap that ends a run, as the fold used it.</param>
    internal static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Of(
        AgentSessionActivity session,
        DateTimeOffset now,
        TimeSpan idleAfter)
    {
        ArgumentNullException.ThrowIfNull(session);

        var turns = session.HumanTurns.Distinct().Order().ToList();

        if (turns.Count == 0) return [];

        var runs = session.Runs.OrderBy(run => run.StartedAt).ToList();
        var stretches = new List<(DateTimeOffset Start, DateTimeOffset End)>();

        var start = turns[0];
        var last = turns[0];

        foreach (var turn in turns.Skip(1))
        {
            if (turn - last < JoinWithin)
            {
                last = turn;
                continue;
            }

            stretches.Add((start, EndOf(last, runs, now, idleAfter)));
            start = turn;
            last = turn;
        }

        stretches.Add((start, EndOf(last, runs, now, idleAfter)));

        return stretches;
    }

    /// <summary>
    /// Each stretch cut at every local midnight it crosses, each part on its own date.
    /// A date runs from one local midnight to the next, so the day the clocks go back
    /// holds twenty-five hours and the day they go forward twenty-three.
    /// </summary>
    internal static IEnumerable<(DateOnly Date, DateTimeOffset Start, DateTimeOffset End)> ByLocalDate(
        IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> stretches,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(stretches);
        ArgumentNullException.ThrowIfNull(zone);

        foreach (var (start, end) in stretches)
        {
            var cursor = start;
            while (cursor < end)
            {
                var date = LocalDate(cursor, zone);
                var next = StartOf(date.AddDays(1), zone);

                // A zone whose midnight is skipped can name a next midnight at or before
                // the cursor; the rest of the stretch then stays on this date rather than
                // looping.
                if (next <= cursor || next > end) next = end;

                yield return (date, cursor, next);
                cursor = next;
            }
        }
    }

    /// <summary>The local date an instant falls on.</summary>
    internal static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>The instant a local date begins at, the way the dashboard's grid turns a
    /// local cell back into an instant.</summary>
    internal static DateTimeOffset StartOf(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>
    /// The fold's own gap, for a log composed only of sources that folded nothing
    /// themselves and so state none — the replicated records say
    /// <see cref="TimeSpan.Zero"/>.
    /// </summary>
    private static readonly TimeSpan DefaultIdleAfter = TimeSpan.FromMinutes(5);

    /// <summary>Where the stretch whose last turn this is ends: the end of the run that
    /// contains the turn or begins at it, else the turn, then open up to now.</summary>
    private static DateTimeOffset EndOf(
        DateTimeOffset turn,
        List<AgentActivityRun> runs,
        DateTimeOffset now,
        TimeSpan idleAfter)
    {
        var end = turn;

        foreach (var run in runs)
        {
            if (run.StartedAt > turn) break;
            if (run.EndedAt > end) end = run.EndedAt;
        }

        if (end > now || now - end < idleAfter) end = now > turn ? now : turn;

        return end;
    }
}
