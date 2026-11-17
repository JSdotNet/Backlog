using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// The pure parts behind the Sessions pane's Agents (local ADR 0051 §3, §6): a loop
/// session's title reads as the Agent purpose, the Agents section leads every grouping,
/// and a loop reads on screen in the design's words.
/// </summary>
public sealed class AgentPurposeTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("📡 SESSION DISPATCHER", LoopKind.Dispatcher)]
    [InlineData("🧠 BRAIN TENDER", LoopKind.BrainTender)]
    [InlineData("  📡 SESSION DISPATCHER  ", LoopKind.Dispatcher)]
    [InlineData("SESSION DISPATCHER", LoopKind.Dispatcher)]
    [InlineData("BRAIN TENDER", LoopKind.BrainTender)]
    public void A_loop_title_reads_as_the_Agent_purpose(string title, LoopKind kind)
    {
        var reading = SessionTitles.Read(title);

        Assert.Equal(SessionPurpose.Agent, reading.Purpose);
        Assert.Equal(title.Trim(), reading.Shown);
        Assert.Null(reading.PlanTag);
        Assert.Equal(kind, LoopSessions.KindOfTitle(title));
    }

    [Theory]
    [InlineData("Session dispatcher")]
    [InlineData("📡 SESSION DISPATCHER and more")]
    [InlineData("PLAN: +loops — SESSION DISPATCHER")]
    [InlineData("")]
    public void Any_other_title_is_not_a_loop(string title)
    {
        Assert.NotEqual(SessionPurpose.Agent, SessionTitles.Read(title).Purpose);
        Assert.Null(LoopSessions.KindOfTitle(title));
    }

    [Fact]
    public void Agent_is_the_first_purpose_and_is_not_standing()
    {
        Assert.Equal(SessionPurpose.Agent, Enum.GetValues<SessionPurpose>()[0]);
        Assert.Equal("Agent", SessionTitles.Label(SessionPurpose.Agent));
        Assert.False(SessionTitles.IsStanding(SessionPurpose.Agent));
    }

    [Fact]
    public void AgentsFirst_moves_the_loop_rows_into_a_leading_section_and_keeps_the_rest()
    {
        var task = Row("task", "Fix the importer");
        var dispatcher = Row("dispatcher", "📡 SESSION DISPATCHER");
        var tender = Row("tender", "🧠 BRAIN TENDER");
        var other = Row("other", "Write the notes");
        var untouched = new AgentSessionGroup("Yesterday", [other], "yesterday");
        IReadOnlyList<AgentSessionGroup> sections =
        [
            new AgentSessionGroup("Live now", [task, dispatcher], "live"),
            new AgentSessionGroup("Earlier today", [tender], "today"),
            untouched
        ];

        var moved = AgentSessionGroups.AgentsFirst(sections);

        Assert.Equal([AgentSessionGroups.AgentsKey, "live", "yesterday"], moved.Select(section => section.Key));
        Assert.Equal([dispatcher, tender], moved[0].Rows);
        Assert.Equal([task], moved[1].Rows);
        Assert.Same(untouched, moved[2]);
    }

    [Fact]
    public void AgentsFirst_hands_back_the_same_sections_with_no_loop_session()
    {
        IReadOnlyList<AgentSessionGroup> sections = [new AgentSessionGroup("Live now", [Row("task", "Fix the importer")], "live")];

        Assert.Same(sections, AgentSessionGroups.AgentsFirst(sections));
    }

    [Fact]
    public void The_held_line_says_what_a_dispatcher_holds_against_its_limit()
    {
        var loop = Loop(LoopKind.Dispatcher, new LoopHeldWork(Requests: 0, Deliveries: 1, Sessions: 2, Notes: 0));

        Assert.Equal("3 requests waiting · 2 of 3 sessions working · 1 answer to deliver", LoopSessionView.HeldLine(loop, 3, CapacityLimits.Default));
        Assert.Equal("2 of 3 sessions working · 1 answer to deliver", LoopSessionView.HeldLine(loop, null, CapacityLimits.Default));

        // What the app measured wins over what the loop said, so the row and the tile agree.
        Assert.Equal("0 of 3 sessions working · 1 answer to deliver", LoopSessionView.HeldLine(loop, null, CapacityLimits.Default, measuredWorking: 0));
        Assert.Equal(
            "Paused · 1 request waiting · 2 of 3 sessions working · 1 answer to deliver",
            LoopSessionView.HeldLine(LoopSessions.Command(loop, LoopCommand.Pause, Noon), 1, CapacityLimits.Default));
    }

    [Fact]
    public void The_held_line_counts_the_brain_tenders_notes()
    {
        Assert.Equal("No notes in hand", LoopSessionView.HeldLine(Loop(LoopKind.BrainTender, LoopHeldWork.None), 4, CapacityLimits.Default));
        Assert.Equal("1 note in hand", LoopSessionView.HeldLine(Loop(LoopKind.BrainTender, new LoopHeldWork(0, 0, 0, 1)), 4, CapacityLimits.Default));
    }

    [Theory]
    [InlineData(LoopState.Listening, "Listening", "live")]
    [InlineData(LoopState.Holding, "Holding", "quiet")]
    [InlineData(LoopState.Stalled, "Stalled", "alert")]
    [InlineData(LoopState.NotRunning, "Not running", "fault")]
    public void Each_loop_state_has_its_word_and_tone(LoopState state, string label, string tone)
    {
        Assert.Equal(label, LoopSessionView.Label(state));
        Assert.Equal(tone, LoopSessionView.Tone(state));
    }

    [Theory]
    [InlineData(30, "30-minute")]
    [InlineData(60, "1-hour")]
    [InlineData(1440, "1-day")]
    [InlineData(90, "90-minute")]
    public void A_heartbeat_reads_as_an_adjective(int minutes, string expected) =>
        Assert.Equal(expected, LoopSessionView.Heartbeat(minutes));

    [Fact]
    public void The_tiles_mark_every_limit_reached()
    {
        var tiles = LoopSessionView.Tiles(new CapacityReading(3, 50, 90, 2.4, CapacityLimits.Default));

        Assert.Equal(["Sessions", "Plan usage", "CPU", "Free memory"], tiles.Select(tile => tile.Label));
        Assert.Equal(["3 / 3", "50%", "90%", "2 GB"], tiles.Select(tile => tile.Value));
        Assert.Equal([true, false, true, true], tiles.Select(tile => tile.Held));
        Assert.Equal("limit reached", tiles[0].Footnote);
        Assert.Equal("limit 80%", tiles[1].Footnote);
        Assert.Equal("limit 80% · reached", tiles[2].Footnote);
        Assert.Equal("needs 4 GB · reached", tiles[3].Footnote);
    }

    /// <summary>The app measures once against the default limits; each loop's card holds the
    /// same figures against the limits that loop sent (local ADR 0051 §4).</summary>
    [Fact]
    public void A_measured_reading_is_held_against_the_loops_own_limits()
    {
        var checkIn = Loop(LoopKind.Dispatcher, LoopHeldWork.None).CheckIn with { Limits = new CapacityLimits(2, 70, 90, 8) };

        var tiles = LoopSessionView.Tiles(LoopSessionView.AgainstLimits(new CapacityReading(1, 75, 34, 6.5, CapacityLimits.Default), checkIn)!);

        Assert.Equal(["1 / 2", "75%", "34%", "6 GB"], tiles.Select(tile => tile.Value));
        Assert.Equal(["working now", "limit 70% · reached", "limit 90%", "needs 8 GB · reached"], tiles.Select(tile => tile.Footnote));
        Assert.Null(LoopSessionView.AgainstLimits(null, checkIn));
    }

    [Fact]
    public void The_held_line_counts_the_sessions_against_the_loops_own_limit()
    {
        var loop = Loop(LoopKind.Dispatcher, new LoopHeldWork(0, 0, 1, 0));
        loop = loop with { CheckIn = loop.CheckIn with { Limits = new CapacityLimits(2, 80, 80, 4) } };

        Assert.Equal("1 of 2 sessions working · 0 answers to deliver", LoopSessionView.HeldLine(loop, null));
    }

    /// <summary>What holds a Holding dispatcher is what the app measured at its check-in;
    /// where that reading reaches no limit, the latest measurement against its limits.</summary>
    [Fact]
    public void What_holds_a_loop_is_its_check_in_reading_first()
    {
        var limits = new CapacityLimits(3, 80, 80, 4);
        var stored = new CapacityReading(1, 84, 30, 12, limits);
        var checkIn = Loop(LoopKind.Dispatcher, LoopHeldWork.None).CheckIn with { Limits = limits, Reading = stored, WorkWaiting = 2 };
        var later = new CapacityReading(3, 10, 10, 20, CapacityLimits.Default);

        Assert.Equal("Plan usage is 84%, over the 80% limit.", LoopSessionView.HoldingLine(LoopSessionView.WhatHolds(checkIn, later)));

        var roomAtCheckIn = checkIn with { Reading = new CapacityReading(1, null, 30, 12, limits) };
        Assert.Equal("3 of 3 sessions are working, the limit.", LoopSessionView.HoldingLine(LoopSessionView.WhatHolds(roomAtCheckIn, later)));
        Assert.Equal("It holds the waiting requests until a limit leaves room.", LoopSessionView.HoldingLine(LoopSessionView.WhatHolds(checkIn with { Reading = null }, null)));
    }

    /// <summary>A Holding dispatcher's row counts the sessions of the reading that holds it,
    /// so its held line and its Holding line agree; any other row, the latest measurement.</summary>
    [Theory]
    [InlineData(LoopState.Holding, 3)]
    [InlineData(LoopState.Listening, 2)]
    public void A_rows_working_count_is_the_holding_readings_while_it_holds(LoopState state, int expected)
    {
        var loop = Loop(LoopKind.Dispatcher, LoopHeldWork.None);
        loop = loop with { CheckIn = loop.CheckIn with { Reading = new CapacityReading(3, 40, 30, 12, CapacityLimits.Default), WorkWaiting = 2 } };
        var measured = new CapacityReading(2, 40, 30, 12, CapacityLimits.Default);

        Assert.Equal(expected, LoopSessionView.WorkingShown(new LoopSessionReading(loop, state, LoopProcess.Alive), measured));
    }

    [Fact]
    public void A_holding_reading_that_did_not_count_the_sessions_leaves_the_latest_measurement()
    {
        var loop = Loop(LoopKind.Dispatcher, LoopHeldWork.None);
        loop = loop with { CheckIn = loop.CheckIn with { Reading = new CapacityReading(null, 84, null, null, CapacityLimits.Default), WorkWaiting = 2 } };

        Assert.Equal(2, LoopSessionView.WorkingShown(new LoopSessionReading(loop, LoopState.Holding, LoopProcess.Alive), new CapacityReading(2, 84, 30, 12, CapacityLimits.Default)));
        Assert.Null(LoopSessionView.WorkingShown(new LoopSessionReading(loop, LoopState.Listening, LoopProcess.Alive), null));
    }

    private static LoopSession Loop(LoopKind kind, LoopHeldWork held) =>
        LoopSessions.Record(null, LoopSessions.CheckIn("loop-1", kind, LoopActivity.Listening, 30, held, Noon));

    private static SessionRow Row(string id, string title) =>
        new(
            new AgentSession(
                Id: id,
                Kind: AgentSessionKind.Claude,
                EnvironmentId: "tower",
                Environment: "DEV-TOWER",
                Title: title,
                WorkingFolder: "D:/Repos/Backlog",
                Repository: "JSdotNet/Backlog",
                Branch: "main",
                StartedAt: Noon.AddHours(-1),
                LastActivityAt: Noon,
                State: AgentSessionState.Running,
                TurnCount: 1,
                Origin: AgentSessionOrigin.Local),
            []);
}
