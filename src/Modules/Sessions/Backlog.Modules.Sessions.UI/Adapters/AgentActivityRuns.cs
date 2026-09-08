using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UI.Adapters;

/// <summary>
/// One thing a transcript recorded happening, reduced to the two facts the fold
/// needs: when, and whether it was a person arriving.
/// <para>
/// A struct with two fields rather than the line it came from, because the fold is
/// the same fold for both agents and the two formats have nothing else in common.
/// Whatever else a line said is the reader's business and is deliberately lost here.
/// </para>
/// </summary>
internal readonly record struct ActivityEvent(DateTimeOffset At, bool IsPrompt);

/// <summary>
/// Turning a stream of recorded instants into the stretches an agent was working and
/// the stretches it was waiting on somebody. Pure: no I/O, no clock, no state.
/// </summary>
internal static class AgentActivityRuns
{
    /// <summary>
    /// Five minutes. The gap that ends a run.
    /// <para>
    /// Evidence rather than taste: the 99th percentile gap between two consecutive
    /// events inside genuine agent work is 15.5 seconds, so five minutes is twenty
    /// times the longest thing a working agent does and clips nothing real. It is
    /// still a judgement, and it is a judgement the answer moves under —
    /// 2 / 5 / 15 minutes give 84.1 h, 99.5 h and 142.7 h over the same week. A
    /// figure that swings by 70% on a constant has to have that constant somewhere
    /// arguable and named on screen, which is why it travels out on
    /// <see cref="AgentActivityLog.IdleAfter"/> rather than staying private to this
    /// fold, and why the footnote spells it out.
    /// </para>
    /// </summary>
    internal static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The runs and waits a stream of events implies.
    /// <para>
    /// Events are sorted before folding rather than trusted to arrive ordered: a
    /// transcript is appended to by one process and is very nearly ordered, and
    /// "very nearly" is what produces a negative-length run.
    /// </para>
    /// <para>
    /// A gap larger than <paramref name="idleAfter"/> closes the run. It opens a wait
    /// only when the event that ended it was a real prompt — otherwise the session was
    /// idle or abandoned, and calling that "waiting for you" would put a fortnight's
    /// silence on somebody's conscience.
    /// </para>
    /// <para>
    /// Zero-length runs are dropped. A session with one event happened, and the
    /// session list says so; claiming it took no time is one thing and claiming it
    /// occupied an instant of the grid is another.
    /// </para>
    /// <para>
    /// The threshold arrives as a parameter rather than being read off
    /// <see cref="IdleAfter"/>, which is the only reason the three-way comparison in
    /// the tests can exist at all: a fold that knew its own constant could not be
    /// asked what a different one would say.
    /// </para>
    /// </summary>
    internal static (IReadOnlyList<AgentActivityRun> Runs, IReadOnlyList<AgentActivityWait> Waits) Fold(
        IEnumerable<ActivityEvent> events,
        TimeSpan idleAfter)
    {
        ArgumentNullException.ThrowIfNull(events);

        var sorted = events.OrderBy(activity => activity.At).ToList();

        if (sorted.Count == 0) return ([], []);

        var runs = new List<AgentActivityRun>();
        var waits = new List<AgentActivityWait>();

        var runStart = sorted[0].At;
        var previous = sorted[0].At;

        foreach (var activity in sorted.Skip(1))
        {
            if (activity.At - previous <= idleAfter)
            {
                previous = activity.At;

                continue;
            }

            // The run ends at the last thing that happened, not at the threshold. A run
            // that ran on for five more minutes than the transcript records would be
            // five minutes this class invented.
            if (previous > runStart) runs.Add(new AgentActivityRun(runStart, previous));

            // And the wait, when there is one, is the whole gap: from the agent's last
            // event to the person's answer. Starting it after the threshold would shave
            // the same five minutes off every wait for no reason a reader could see.
            if (activity.IsPrompt) waits.Add(new AgentActivityWait(previous, activity.At));

            runStart = activity.At;
            previous = activity.At;
        }

        if (previous > runStart) runs.Add(new AgentActivityRun(runStart, previous));

        return (runs, waits);
    }
}
