using Backlog.Infrastructure.FileSystem.Activity;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The person's working stretches, built per session from their human turns and the
/// agent's runs (ADR 0019 §6). The edges the roadmap's figures stand on: where a stretch
/// ends when there is no reply to end it, the exact half hour that splits one, and what
/// "open" means.
/// </summary>
public sealed class WorkingStretchesTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(5);

    private static readonly DateTimeOffset Later = Nine.AddDays(1);

    [Fact]
    public void Turns_exactly_thirty_minutes_apart_start_a_new_stretch()
    {
        var stretches = WorkingStretches.Of(Session([Nine, Nine.AddMinutes(30)]), Later, IdleAfter);

        Assert.Equal([(Nine, Nine), (Nine.AddMinutes(30), Nine.AddMinutes(30))], stretches);
    }

    [Fact]
    public void A_turn_no_run_follows_ends_its_stretch_at_the_turn_and_the_gap_before_it_still_counts()
    {
        var stretches = WorkingStretches.Of(
            Session([Nine, Nine.AddMinutes(20)], Run(Nine, Nine.AddMinutes(5))),
            Later,
            IdleAfter);

        Assert.Equal([(Nine, Nine.AddMinutes(20))], stretches);
    }

    [Fact]
    public void A_run_that_ended_before_the_turn_does_not_end_its_stretch()
    {
        var stretches = WorkingStretches.Of(
            Session([Nine.AddMinutes(10)], Run(Nine, Nine.AddMinutes(5))),
            Later,
            IdleAfter);

        Assert.Equal([(Nine.AddMinutes(10), Nine.AddMinutes(10))], stretches);
    }

    [Fact]
    public void A_turn_inside_a_run_ends_its_stretch_with_that_run()
    {
        // The person typed while the agent was still answering, so the fold kept one run.
        var stretches = WorkingStretches.Of(
            Session([Nine, Nine.AddMinutes(3)], Run(Nine, Nine.AddMinutes(40))),
            Later,
            IdleAfter);

        Assert.Equal([(Nine, Nine.AddMinutes(40))], stretches);
    }

    [Fact]
    public void A_stretch_whose_reply_is_still_running_is_open_until_now()
    {
        var now = Nine.AddMinutes(12);

        var stretches = WorkingStretches.Of(Session([Nine], Run(Nine, Nine.AddMinutes(10))), now, IdleAfter);

        Assert.Equal([(Nine, now)], stretches);
    }

    [Fact]
    public void Turns_arrive_in_any_order_and_a_turn_twice_is_one()
    {
        var stretches = WorkingStretches.Of(
            Session([Nine.AddMinutes(20), Nine, Nine.AddMinutes(20)], Run(Nine.AddMinutes(20), Nine.AddMinutes(25))),
            Later,
            IdleAfter);

        Assert.Equal([(Nine, Nine.AddMinutes(25))], stretches);
    }

    [Fact]
    public void The_log_merges_sessions_drops_empty_stretches_and_clips_to_the_window()
    {
        var log = new AgentActivityLog(
            [
                Session([Nine], Run(Nine, Nine.AddHours(2))),
                Session([Nine.AddHours(1)], Run(Nine.AddHours(1), Nine.AddHours(3))),
                Session([Nine.AddHours(5)])
            ],
            [],
            DateTimeOffset.MinValue,
            IdleAfter);

        var stretches = WorkingStretches.Of(log, Nine.AddMinutes(30), Nine.AddHours(8), Later);

        Assert.Equal([(Nine.AddMinutes(30), Nine.AddHours(3))], stretches);
    }

    [Fact]
    public void A_stretch_is_cut_at_each_local_midnight()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var start = new DateTimeOffset(2026, 10, 1, 23, 0, 0, TimeSpan.FromHours(2));

        var parts = WorkingStretches.ByLocalDate([(start, start.AddHours(2))], zone);

        Assert.Equal(
            [
                (new DateOnly(2026, 10, 1), start, start.AddHours(1)),
                (new DateOnly(2026, 10, 2), start.AddHours(1), start.AddHours(2))
            ],
            parts);
    }

    private static AgentActivityRun Run(DateTimeOffset from, DateTimeOffset to) => new(from, to);

    private static AgentSessionActivity Session(DateTimeOffset[] turns, params AgentActivityRun[] runs) =>
        new("s", AgentSessionKind.Claude, "tower", "DEV-TOWER", runs, []) { HumanTurns = turns };
}
