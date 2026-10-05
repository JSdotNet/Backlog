using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.FileSystem.Activity;

/// <summary>
/// The person's working stretches, drawn from the Sessions context's activity: each
/// session's human turns and the agent's runs (ADR 0019 §6). Pure: no I/O, and the
/// clock arrives as a parameter.
/// <para>
/// A stretch starts at a human turn. It ends when the agent finishes answering its last
/// turn: the end of the run that turn started, being the run that contains it or begins
/// at it. When no such run ends after the turn, the stretch ends at the turn itself. A
/// turn less than <see cref="JoinWithin"/> after the stretch so far ended belongs to it,
/// and the stretch covers the gap. Only the session's own runs count; a subagent's work
/// starts and extends nothing, so overnight agents with no human turn behind them add
/// no time.
/// </para>
/// <para>
/// Stretches of every session are then joined the same way: one that starts less than
/// <see cref="JoinWithin"/> after another ended continues it, because the person moving
/// from one session to the next was working between them.
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
    /// <summary>A stretch with what it was made of: the sessions worked in it and the
    /// person's turns inside it, ascending.</summary>
    internal sealed record TracedStretch(
        DateTimeOffset Start,
        DateTimeOffset End,
        IReadOnlyList<AgentSessionActivity> Sessions,
        IReadOnlyList<DateTimeOffset> Turns);

    /// <summary>
    /// Thirty minutes. A turn closer than this to the end of a stretch, in its own
    /// session or another, continues it; one this far or further starts a new one, and
    /// the gap between them belongs to neither. The owner's choice of 2026-10-03: the
    /// reading and thinking between two prompts is work, and a longer pause is not. Since
    /// 2026-10-05 the pause is measured from the end of the agent's answer rather than
    /// from the prompt before it, and across sessions: reading the answer is the work.
    /// </summary>
    internal static readonly TimeSpan JoinWithin = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Four in the morning, local. A working day runs from this time on its date to this
    /// time on the next, so an evening that goes on past midnight counts on the day it
    /// began. The owner's choice of 2026-10-05, after evenings worked until two read as
    /// two short days. It also keeps the cut clear of the hour a European clock change
    /// skips or repeats.
    /// </summary>
    internal static readonly TimeSpan DayStartsAt = TimeSpan.FromHours(4);

    /// <summary>
    /// Every session's stretches in a log, joined where one starts less than
    /// <see cref="JoinWithin"/> after another ended, so time two sessions shared counts
    /// once, clipped to the window and to now, in order.
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
        DateTimeOffset now) =>
        [.. Traced(log, from, to, now).Select(stretch => (stretch.Start, stretch.End))];

    /// <summary>
    /// The same stretches as <see cref="Of(AgentActivityLog, DateTimeOffset, DateTimeOffset, DateTimeOffset)"/>,
    /// each with the sessions it was worked in and the human turns inside it, for the
    /// hours report that lets the person check a head's figure. One code path, so the
    /// report and the figure never disagree.
    /// </summary>
    internal static IReadOnlyList<TracedStretch> Traced(
        AgentActivityLog log,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (to > now) to = now;

        var idleAfter = log.IdleAfter > TimeSpan.Zero ? log.IdleAfter : DefaultIdleAfter;

        var clipped = log.Sessions
            .SelectMany(session => Of(session, now, idleAfter).Select(stretch => (Session: session, stretch.Start, stretch.End)))
            .Select(stretch => (
                stretch.Session,
                Start: stretch.Start > from ? stretch.Start : from,
                End: stretch.End < to ? stretch.End : to))
            .Where(stretch => stretch.End >= stretch.Start)
            .OrderBy(stretch => stretch.Start)
            .ToList();

        // An empty stretch, a lone turn nobody answered, still bridges the gap it falls
        // in, and is dropped only once it has had the chance to.
        var merged = new List<(DateTimeOffset Start, DateTimeOffset End, List<AgentSessionActivity> Sessions)>();
        foreach (var (session, start, end) in clipped)
        {
            if (merged.Count > 0 && start - merged[^1].End < JoinWithin)
            {
                var last = merged[^1];
                if (!last.Sessions.Contains(session)) last.Sessions.Add(session);
                if (end > last.End) merged[^1] = (last.Start, end, last.Sessions);
                continue;
            }

            merged.Add((start, end, [session]));
        }

        return
        [
            .. merged
                .Where(stretch => stretch.End > stretch.Start)
                .Select(stretch => new TracedStretch(
                    stretch.Start,
                    stretch.End,
                    stretch.Sessions,
                    [.. stretch.Sessions
                        .SelectMany(session => session.HumanTurns)
                        .Where(turn => turn >= stretch.Start && turn <= stretch.End)
                        .Distinct()
                        .Order()]))
        ];
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
        var end = EndOf(turns[0], runs, now, idleAfter);

        foreach (var turn in turns.Skip(1))
        {
            if (turn - end < JoinWithin)
            {
                var answered = EndOf(turn, runs, now, idleAfter);
                if (answered > end) end = answered;
                continue;
            }

            stretches.Add((start, end));
            start = turn;
            end = EndOf(turn, runs, now, idleAfter);
        }

        stretches.Add((start, end));

        return stretches;
    }

    /// <summary>
    /// Each stretch cut at every start of a working day it crosses, <see cref="DayStartsAt"/>
    /// local, each part on its own date. A working day runs from one such start to the
    /// next, so the one the clocks go back in holds twenty-five hours and the one they go
    /// forward in twenty-three.
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

                // A zone whose day start is skipped can name a next start at or before
                // the cursor; the rest of the stretch then stays on this date rather than
                // looping.
                if (next <= cursor || next > end) next = end;

                yield return (date, cursor, next);
                cursor = next;
            }
        }
    }

    /// <summary>The working day an instant falls in: its local date, or the date before
    /// when it is earlier than <see cref="DayStartsAt"/>.</summary>
    internal static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime - DayStartsAt);

    /// <summary>The instant a working day begins at, <see cref="DayStartsAt"/> on its
    /// date, the way the dashboard's grid turns a local cell back into an instant.</summary>
    internal static DateTimeOffset StartOf(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.FromTimeSpan(DayStartsAt));
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
