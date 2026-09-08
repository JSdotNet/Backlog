using Backlog.Modules.Sessions.UI.Adapters;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The gap fold: which stretches of a transcript count as an agent working, and which
/// silences count as somebody being waited on.
/// <para>
/// No filesystem and no clock here, because there is nothing in the fold that needs
/// either. It is handed instants and a threshold and answers with intervals, which is
/// what makes the one arguable number in this whole feature — five minutes — something
/// a test can stand either side of rather than something a fixture has to be built
/// around.
/// </para>
/// </summary>
public sealed class AgentActivityRunsTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(5);

    [Fact]
    public void Events_closer_together_than_the_threshold_are_one_run()
    {
        var (runs, waits) = AgentActivityRuns.Fold(
            [
                Event(TimeSpan.Zero),
                Event(TimeSpan.FromSeconds(4)),
                Event(TimeSpan.FromSeconds(40)),
                Event(TimeSpan.FromMinutes(3))
            ],
            Threshold);

        var run = Assert.Single(runs);

        Assert.Equal(Start, run.StartedAt);
        Assert.Equal(Start.AddMinutes(3), run.EndedAt);
        Assert.Empty(waits);
    }

    [Fact]
    public void A_gap_longer_than_the_threshold_closes_the_run()
    {
        var (runs, _) = AgentActivityRuns.Fold(
            [
                Event(TimeSpan.Zero),
                Event(TimeSpan.FromMinutes(2)),
                Event(TimeSpan.FromMinutes(40)),
                Event(TimeSpan.FromMinutes(41))
            ],
            Threshold);

        Assert.Equal(2, runs.Count);
        Assert.Equal(Start, runs[0].StartedAt);
        Assert.Equal(Start.AddMinutes(2), runs[0].EndedAt);
        Assert.Equal(Start.AddMinutes(40), runs[1].StartedAt);
        Assert.Equal(Start.AddMinutes(41), runs[1].EndedAt);

        // The thirty-eight minutes between them are in neither run. That is the whole
        // point: the figure this replaced counted them, and counting them is what put
        // 2,527 hours inside a 168-hour week.
        Assert.Equal(TimeSpan.FromMinutes(3), Total(runs));
    }

    [Fact]
    public void A_gap_a_prompt_ended_is_time_somebody_was_waited_on()
    {
        var (_, waits) = AgentActivityRuns.Fold(
            [
                Event(TimeSpan.Zero),
                Event(TimeSpan.FromMinutes(1)),
                Event(TimeSpan.FromMinutes(31), isPrompt: true)
            ],
            Threshold);

        var wait = Assert.Single(waits);

        // From the last thing the agent did to the moment the person answered — not
        // from the end of the threshold, which would silently shorten every wait by
        // five minutes for no reason anybody could point at on screen.
        Assert.Equal(Start.AddMinutes(1), wait.StartedAt);
        Assert.Equal(Start.AddMinutes(31), wait.EndedAt);
    }

    [Fact]
    public void A_gap_nothing_in_particular_ended_is_idle_rather_than_waiting()
    {
        var (runs, waits) = AgentActivityRuns.Fold(
            [
                Event(TimeSpan.Zero),
                Event(TimeSpan.FromMinutes(1)),
                Event(TimeSpan.FromMinutes(31)),
                Event(TimeSpan.FromMinutes(32))
            ],
            Threshold);

        // The run still closed — the agent had stopped either way.
        Assert.Equal(2, runs.Count);

        // But nobody was being waited on. A session that resumed with a tool result or
        // a skill injection was idle or abandoned, and calling that "waiting on you"
        // would put a fortnight of silence on somebody's conscience.
        Assert.Empty(waits);
    }

    [Fact]
    public void A_session_that_left_one_event_produces_no_run_rather_than_a_zero_length_one()
    {
        var (runs, waits) = AgentActivityRuns.Fold([Event(TimeSpan.Zero)], Threshold);

        Assert.Empty(runs);
        Assert.Empty(waits);
    }

    /// <summary>
    /// A transcript is appended to by one process and is very nearly ordered. "Very
    /// nearly" is what produces a run that ends before it starts, which reads as a
    /// negative duration and quietly subtracts from a tile.
    /// </summary>
    [Fact]
    public void Events_that_arrive_out_of_order_are_sorted_before_they_are_folded()
    {
        var (runs, _) = AgentActivityRuns.Fold(
            [
                Event(TimeSpan.FromMinutes(2)),
                Event(TimeSpan.Zero),
                Event(TimeSpan.FromMinutes(1))
            ],
            Threshold);

        var run = Assert.Single(runs);

        Assert.Equal(Start, run.StartedAt);
        Assert.Equal(Start.AddMinutes(2), run.EndedAt);
    }

    /// <summary>
    /// The boundary is inclusive: a gap of exactly the threshold is still one run.
    /// Which side it falls on matters less than it being asserted somewhere — an
    /// off-by-one here moves the headline figure and nothing on screen would say why.
    /// </summary>
    [Fact]
    public void A_gap_exactly_the_threshold_long_does_not_close_the_run()
    {
        var (runs, _) = AgentActivityRuns.Fold(
            [
                Event(TimeSpan.Zero),
                Event(Threshold),
                Event(Threshold + Threshold + TimeSpan.FromTicks(1)),
                Event(Threshold + Threshold + TimeSpan.FromTicks(1) + TimeSpan.FromMinutes(1))
            ],
            Threshold);

        Assert.Equal(2, runs.Count);
        Assert.Equal(Start.Add(Threshold), runs[0].EndedAt);
    }

    [Fact]
    public void The_first_event_of_a_session_opens_no_wait_because_nothing_preceded_it()
    {
        var (runs, waits) = AgentActivityRuns.Fold(
            [
                Event(TimeSpan.Zero, isPrompt: true),
                Event(TimeSpan.FromMinutes(1))
            ],
            Threshold);

        Assert.Single(runs);

        // The person typed the first thing in the file. There is no stretch before it
        // for them to have been waited on through, and dating one from the file would
        // be inventing the wait.
        Assert.Empty(waits);
    }

    /// <summary>
    /// The test that stops the threshold being changed silently.
    /// <para>
    /// Two, five and fifteen minutes gave 84.1 h, 99.5 h and 142.7 h over the same
    /// measured week — a seventy percent swing on a constant. This asserts the shape
    /// of that on a fixture: a longer threshold absorbs more gaps and therefore reports
    /// strictly more time, and the three answers are genuinely different rather than
    /// three names for one number.
    /// </para>
    /// </summary>
    [Fact]
    public void Five_minutes_and_two_minutes_and_fifteen_disagree_by_the_measured_amount()
    {
        ActivityEvent[] stream =
        [
            Event(TimeSpan.Zero),
            Event(TimeSpan.FromMinutes(1)),
            Event(TimeSpan.FromMinutes(4)),
            Event(TimeSpan.FromMinutes(5)),
            Event(TimeSpan.FromMinutes(13)),
            Event(TimeSpan.FromMinutes(14)),
            Event(TimeSpan.FromMinutes(34)),
            Event(TimeSpan.FromMinutes(35))
        ];

        Assert.Equal(TimeSpan.FromMinutes(4), Active(stream, TimeSpan.FromMinutes(2)));
        Assert.Equal(TimeSpan.FromMinutes(7), Active(stream, TimeSpan.FromMinutes(5)));
        Assert.Equal(TimeSpan.FromMinutes(15), Active(stream, TimeSpan.FromMinutes(15)));
    }

    private static TimeSpan Active(IEnumerable<ActivityEvent> events, TimeSpan idleAfter) =>
        Total(AgentActivityRuns.Fold(events, idleAfter).Runs);

    private static TimeSpan Total(IEnumerable<AgentActivityRun> runs) =>
        runs.Aggregate(TimeSpan.Zero, (total, run) => total + (run.EndedAt - run.StartedAt));

    private static ActivityEvent Event(TimeSpan after, bool isPrompt = false) =>
        new(Start.Add(after), isPrompt);
}
