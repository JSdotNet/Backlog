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
    public void The_pause_is_measured_from_when_the_agent_finished_not_from_the_last_turn()
    {
        // Prompted at 09:00, answered until 09:40; the person read it and prompted again
        // at 09:45 — forty-five minutes after the first turn, five after the answer.
        var stretches = WorkingStretches.Of(
            Session([Nine, Nine.AddMinutes(45)], Run(Nine, Nine.AddMinutes(40)), Run(Nine.AddMinutes(45), Nine.AddMinutes(50))),
            Later,
            IdleAfter);

        Assert.Equal([(Nine, Nine.AddMinutes(50))], stretches);
    }

    [Fact]
    public void A_turn_thirty_minutes_after_the_agent_finished_starts_a_new_stretch()
    {
        var stretches = WorkingStretches.Of(
            Session([Nine, Nine.AddMinutes(40)], Run(Nine, Nine.AddMinutes(10)), Run(Nine.AddMinutes(40), Nine.AddMinutes(45))),
            Later,
            IdleAfter);

        Assert.Equal([(Nine, Nine.AddMinutes(10)), (Nine.AddMinutes(40), Nine.AddMinutes(45))], stretches);
    }

    [Fact]
    public void Stretches_of_two_sessions_less_than_thirty_minutes_apart_join()
    {
        // The person moved from one session to another: the time between is work too.
        var log = Log(
            Session([Nine], Run(Nine, Nine.AddMinutes(20))),
            Session([Nine.AddMinutes(45)], Run(Nine.AddMinutes(45), Nine.AddMinutes(60))));

        var stretches = WorkingStretches.Of(log, Nine.AddHours(-1), Nine.AddHours(8), Later);

        Assert.Equal([(Nine, Nine.AddMinutes(60))], stretches);
    }

    [Fact]
    public void Stretches_of_two_sessions_thirty_minutes_apart_stay_apart()
    {
        var log = Log(
            Session([Nine], Run(Nine, Nine.AddMinutes(20))),
            Session([Nine.AddMinutes(50)], Run(Nine.AddMinutes(50), Nine.AddMinutes(60))));

        var stretches = WorkingStretches.Of(log, Nine.AddHours(-1), Nine.AddHours(8), Later);

        Assert.Equal([(Nine, Nine.AddMinutes(20)), (Nine.AddMinutes(50), Nine.AddMinutes(60))], stretches);
    }

    [Fact]
    public void A_lone_turn_in_another_session_bridges_the_gap_it_falls_in()
    {
        // A one-line prompt nobody answered is still the person at the keyboard.
        var log = Log(
            Session([Nine], Run(Nine, Nine.AddMinutes(10))),
            Session([Nine.AddMinutes(30)]),
            Session([Nine.AddMinutes(55)], Run(Nine.AddMinutes(55), Nine.AddMinutes(65))));

        var stretches = WorkingStretches.Of(log, Nine.AddHours(-1), Nine.AddHours(8), Later);

        Assert.Equal([(Nine, Nine.AddMinutes(65))], stretches);
    }

    [Fact]
    public void A_working_day_runs_from_four_in_the_morning_to_four_the_next()
    {
        // 23:00 to 05:00: the evening and the small hours count on the day they began,
        // and only the hour after four belongs to the next.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var start = new DateTimeOffset(2026, 10, 1, 23, 0, 0, TimeSpan.FromHours(2));

        var parts = WorkingStretches.ByLocalDate([(start, start.AddHours(6))], zone);

        Assert.Equal(
            [
                (new DateOnly(2026, 10, 1), start, start.AddHours(5)),
                (new DateOnly(2026, 10, 2), start.AddHours(5), start.AddHours(6))
            ],
            parts);
    }

    [Fact]
    public void Half_past_one_at_night_is_on_the_previous_date()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

        Assert.Equal(
            new DateOnly(2026, 10, 1),
            WorkingStretches.LocalDate(new DateTimeOffset(2026, 10, 2, 1, 30, 0, TimeSpan.FromHours(2)), zone));
        Assert.Equal(
            new DateTimeOffset(2026, 10, 2, 4, 0, 0, TimeSpan.FromHours(2)),
            WorkingStretches.StartOf(new DateOnly(2026, 10, 2), zone));
    }

    private static AgentActivityLog Log(params AgentSessionActivity[] sessions) =>
        new(sessions, [], DateTimeOffset.MinValue, IdleAfter);

    private static AgentActivityRun Run(DateTimeOffset from, DateTimeOffset to) => new(from, to);

    private static AgentSessionActivity Session(DateTimeOffset[] turns, params AgentActivityRun[] runs) =>
        new("s", AgentSessionKind.Claude, "tower", "DEV-TOWER", runs, []) { HumanTurns = turns };
}
