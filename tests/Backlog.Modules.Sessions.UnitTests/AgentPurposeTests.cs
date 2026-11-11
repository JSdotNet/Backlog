using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// The pure parts behind the Sessions pane's Agents (local ADR 0051 §3, §6): a loop
/// session's title reads as the Agent purpose, the Agents section leads every grouping,
/// the schedules that need the person are counted by the states that alert, and a loop
/// reads on screen in the design's words.
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

    [Theory]
    [InlineData(ScheduledRunState.NeedsYou, true)]
    [InlineData(ScheduledRunState.Held, true)]
    [InlineData(ScheduledRunState.Failed, true)]
    [InlineData(ScheduledRunState.Missed, true)]
    [InlineData(ScheduledRunState.ReviewReady, false)]
    [InlineData(ScheduledRunState.Handled, false)]
    [InlineData(ScheduledRunState.NoSummaryYet, false)]
    [InlineData(null, false)]
    public void A_schedule_needs_the_person_in_the_states_that_alert(ScheduledRunState? state, bool needs) =>
        Assert.Equal(needs, ScheduleReading.NeedsPerson(state));

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
