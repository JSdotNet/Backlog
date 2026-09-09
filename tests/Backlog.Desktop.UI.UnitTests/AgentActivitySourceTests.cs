using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using Backlog.Modules.Sessions.UI.Adapters;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the two agents write into their transcripts, and what comes back out as time
/// an agent was producing. The seam worth testing directly: a line counted that should
/// not have been, a gap closed at the wrong place, or a turn bracket believed all
/// render as a perfectly plausible number.
/// <para>
/// Fixture folders and hand-written files, in the shape the agents actually write, for
/// the reason <see cref="AgentSessionSourceTests"/> gives about its own — and here the
/// point is sharper still. All three of the Claude extraction rules are rules about
/// <em>which fields a line has</em>, so a fixture tidied down to a timestamp and a type
/// would exercise none of them. There is no filesystem abstraction and no mock; the
/// behaviour under test is the disk, and a fake would be asserting the fake.
/// </para>
/// </summary>
public sealed class AgentActivitySourceTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The horizon every read here is asked for. A week back, which is the
    /// window the measurements behind this feature were taken over.</summary>
    private static readonly DateTimeOffset Horizon = Noon.AddDays(-7);

    /// <summary>Comfortably inside the horizon, so a fixture that is about something
    /// else does not have to think about clipping.</summary>
    private static readonly DateTimeOffset Yesterday = Noon.AddDays(-1);

    private const string Machine = "DEV-TOWER";

    private const string MachineId = "6b8e6f0c-1a4f-4a2e-9f4b-6a2c0f5d3a71";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "backlog-agent-activity-tests",
        Guid.NewGuid().ToString("n"));

    private string ClaudeHome => Path.Combine(_root, ".claude");

    private string CopilotHome => Path.Combine(_root, ".copilot");

    /// <summary>
    /// Nineteen percent of the lines on this machine carry no <c>timestamp</c> at all —
    /// UI and session state rather than anything the agent did. Dating them off the
    /// file is the obvious shortcut and it would invent the activity this whole reader
    /// exists to stop inventing: the file's own mtime is hours after its last event.
    /// </summary>
    [Fact]
    public async Task A_line_with_no_timestamp_is_not_activity()
    {
        GivenClaudeRawTranscript(
            "D--Repos-Backlog",
            "state-only",
            [
                """{"type":"last-prompt","content":"look at the dashboard","sessionId":"state-only"}""",
                """{"type":"custom-title","title":"dashboard work","sessionId":"state-only"}""",
                """{"type":"permission-mode","mode":"acceptEdits","sessionId":"state-only"}"""
            ],
            lastWrite: Noon);

        GivenClaudeRawTranscript(
            "D--Repos-Backlog",
            "worked",
            [
                """{"type":"ai-title","title":"derived","sessionId":"worked"}""",
                Assistant("worked", Yesterday, "thinking"),
                """{"type":"mode","mode":"plan","sessionId":"worked"}""",
                Assistant("worked", Yesterday.AddMinutes(1), "done"),
                """{"type":"atis-latch","state":"idle","sessionId":"worked"}"""
            ],
            lastWrite: Noon);

        var log = await ReadAsync();

        // The state-only transcript left no interval to report, so it is absent rather
        // than present with an empty list: the session list already says it existed.
        var session = Assert.Single(log.Sessions);

        Assert.Equal("worked", session.Id);

        var run = Assert.Single(session.Runs);

        // Exactly the two events, and nothing dated off the file — which was written
        // eleven hours after the second of them.
        Assert.Equal(Yesterday, run.StartedAt);
        Assert.Equal(Yesterday.AddMinutes(1), run.EndedAt);
    }

    /// <summary>
    /// A prompt is <c>promptSource</c> being present. Not absent, not a value —
    /// presence. Three values have been seen so far (sdk, typed, system) and keying on
    /// the field rather than on the set is what makes the rule survive a fourth.
    /// </summary>
    [Fact]
    public async Task A_prompt_is_recognised_by_the_field_being_there_rather_than_by_its_value()
    {
        var start = Yesterday;

        GivenClaudeRawTranscript(
            "D--Repos-Backlog",
            "three-prompts",
            [
                Assistant("three-prompts", start, "one"),
                Prompt("three-prompts", start.AddMinutes(30), "sdk"),
                Assistant("three-prompts", start.AddMinutes(31), "two"),
                Prompt("three-prompts", start.AddMinutes(61), "typed"),
                Assistant("three-prompts", start.AddMinutes(62), "three"),
                Prompt("three-prompts", start.AddMinutes(92), "system"),
                Assistant("three-prompts", start.AddMinutes(93), "four")
            ],
            lastWrite: Noon);

        var session = Assert.Single((await ReadAsync()).Sessions);

        Assert.Equal(3, session.Waits.Count);
        Assert.All(session.Waits, wait => Assert.Equal(TimeSpan.FromMinutes(30), wait.EndedAt - wait.StartedAt));
    }

    /// <summary>
    /// The regression that shipped once and read as nothing being wrong. Bookkeeping
    /// lines carry a timestamp and land between turns, so admitting them puts an event
    /// inside the very gap a wait is made of: the gap becomes two short ones, no gap is
    /// left for a prompt to end, and the silence is booked as work.
    /// <para>
    /// Measured on the real profile, admitting every timestamped line took a week's
    /// waiting from 105.8 hours to zero — all 103 waits — and pushed agent-active from
    /// 90.5 to 100.8. Nothing on screen would have looked broken. That is why this
    /// asserts the wait survives rather than only that the run is the right length.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Bookkeeping_between_two_turns_does_not_fill_the_gap_a_wait_is_made_of()
    {
        var start = Yesterday;

        GivenClaudeRawTranscript(
            "D--Repos-Backlog",
            "bookkept",
            [
                Assistant("bookkept", start, "one"),

                // Every one of these carries a timestamp, sits inside the gap, and is
                // something the harness wrote rather than something the agent did.
                $$"""{"type":"queue-operation","operation":"enqueue","sessionId":"bookkept","timestamp":"{{Stamp(start.AddMinutes(10))}}"}""",
                $$"""{"type":"pr-link","url":"https://github.com/JSdotNet/Backlog/pull/1","sessionId":"bookkept","timestamp":"{{Stamp(start.AddMinutes(20))}}"}""",
                $$"""{"type":"file-history-delta","path":"src/x.cs","sessionId":"bookkept","timestamp":"{{Stamp(start.AddMinutes(25))}}"}""",

                Prompt("bookkept", start.AddMinutes(30), "typed"),
                Assistant("bookkept", start.AddMinutes(31), "two")
            ],
            lastWrite: Noon);

        var session = Assert.Single((await ReadAsync()).Sessions);

        // The whole half hour, from the agent's last turn to the person's answer — not
        // three short gaps that never became a wait at all.
        var wait = Assert.Single(session.Waits);

        Assert.Equal(TimeSpan.FromMinutes(30), wait.EndedAt - wait.StartedAt);

        // And the bookkeeping did not become work either: two runs of nothing and a
        // minute, not thirty-one minutes of invented activity.
        Assert.Equal(TimeSpan.FromMinutes(1), session.Runs.Aggregate(
            TimeSpan.Zero,
            (running, run) => running + (run.EndedAt - run.StartedAt)));
    }

    /// <summary>
    /// The trap. Absent <c>promptSource</c> usually means a tool result — 22,213 of
    /// them — but 259 lines with no <c>promptSource</c> carry text content: skill
    /// injections, "[Request interrupted by user]", slash-command plumbing. A reader
    /// that inferred "text content means a person typed it" would open a wait on every
    /// one of them.
    /// </summary>
    [Fact]
    public async Task A_line_with_text_content_and_no_prompt_source_is_activity_and_not_a_prompt()
    {
        var start = Yesterday;

        GivenClaudeRawTranscript(
            "D--Repos-Backlog",
            "skill-injected",
            [
                Assistant("skill-injected", start, "one"),
                Assistant("skill-injected", start.AddMinutes(1), "two"),
                SkillInjection("skill-injected", start.AddMinutes(31)),
                Assistant("skill-injected", start.AddMinutes(32), "three")
            ],
            lastWrite: Noon);

        var session = Assert.Single((await ReadAsync()).Sessions);

        // It is activity: it closed one run and opened the next.
        Assert.Equal(2, session.Runs.Count);
        Assert.Equal(start.AddMinutes(32), session.Runs[1].EndedAt);

        // And it is not a person. Nobody was being waited on through that half hour.
        Assert.Empty(session.Waits);
    }

    [Fact]
    public async Task A_tool_result_is_activity()
    {
        GivenClaudeRawTranscript(
            "D--Repos-Backlog",
            "tooling",
            [
                Assistant("tooling", Yesterday, "running the build"),
                ToolResult("tooling", Yesterday.AddMinutes(2))
            ],
            lastWrite: Noon);

        var session = Assert.Single((await ReadAsync()).Sessions);
        var run = Assert.Single(session.Runs);

        // Two minutes of build, not a session with one event and therefore no run at
        // all — which is what a reader that only counted messages would report.
        Assert.Equal(Yesterday, run.StartedAt);
        Assert.Equal(Yesterday.AddMinutes(2), run.EndedAt);
        Assert.Empty(session.Waits);
    }

    /// <summary>
    /// The single most important test here. <c>assistant.turn_start</c> /
    /// <c>assistant.turn_end</c> look like exactly the authority this reader wants and
    /// they are not: 8 of 14,175 bracketed turns hold 58.5% of all bracketed time, and
    /// the longest is 41 hours, because suspending and resuming a session leaves a turn
    /// open across the whole gap. Gap inference over the raw stream is immune to that
    /// and bracket pairing cannot be made immune to it.
    /// </summary>
    [Fact]
    public async Task A_copilot_turn_left_open_across_a_suspend_does_not_become_forty_one_hours_of_work()
    {
        var start = Noon.AddDays(-6);
        var resumed = start.AddHours(41);

        GivenCopilotEvents(
            "suspended",
            [
                (start, "assistant.turn_start"),
                (start.AddSeconds(10), "assistant.message"),

                // No turn_end. The session was suspended here and picked up 41 hours
                // later, and the bracket stayed open across all of it.
                (resumed, "user.message"),
                (resumed.AddSeconds(10), "assistant.message"),
                (resumed.AddSeconds(20), "assistant.turn_end")
            ],
            lastWrite: Noon);

        var session = Assert.Single((await ReadAsync()).Sessions);

        Assert.Equal(2, session.Runs.Count);
        Assert.Equal(start, session.Runs[0].StartedAt);
        Assert.Equal(start.AddSeconds(10), session.Runs[0].EndedAt);
        Assert.Equal(resumed, session.Runs[1].StartedAt);
        Assert.Equal(resumed.AddSeconds(20), session.Runs[1].EndedAt);

        // Thirty seconds of work, not forty-one hours. The gap between the two is a
        // gap, which closes a run rather than being one.
        Assert.Equal(TimeSpan.FromSeconds(30), Total(session));
    }

    /// <summary>
    /// Copilot's <c>user.message</c> fires roughly zero seconds after the event before
    /// it — 919 of them, every gap about nothing — so there is no boundary in this
    /// format meaning "the agent stopped and a person had not yet answered". The right
    /// answer is no waits at all rather than a half-measure presented as a whole one.
    /// </summary>
    [Fact]
    public async Task Copilot_records_no_prompt_boundary_so_it_contributes_no_waiting()
    {
        var start = Yesterday;

        GivenCopilotEvents(
            "chatting",
            [
                (start, "assistant.turn_start"),
                (start.AddSeconds(5), "assistant.message"),
                (start.AddSeconds(6), "assistant.turn_end"),

                // A person came back after half an hour, and Copilot's file cannot say
                // that is what happened.
                (start.AddMinutes(30), "user.message"),
                (start.AddMinutes(30).AddSeconds(4), "assistant.message")
            ],
            lastWrite: Noon);

        var session = Assert.Single((await ReadAsync()).Sessions);

        Assert.Equal(2, session.Runs.Count);
        Assert.Empty(session.Waits);
    }

    /// <summary>
    /// 468 of 691 Copilot session folders on this machine hold no <c>events.jsonl</c>
    /// at all. An entry with no intervals in it would be a session claiming to have
    /// been measured, and the honesty flag on the surface counts exactly these.
    /// </summary>
    [Fact]
    public async Task A_copilot_session_with_no_events_file_contributes_nothing_rather_than_an_empty_entry()
    {
        GivenCopilotSessionFolder("no-events");

        var log = await ReadAsync();

        Assert.Empty(log.Sessions);
        Assert.Empty(log.Unreadable);
    }

    /// <summary>
    /// A transcript last written before the horizon cannot hold an event inside it, so
    /// it is skipped on its own timestamp without being opened. That is what makes a
    /// twelve-week read bounded rather than all-time.
    /// <para>
    /// Two halves to the proof, because either alone would pass for the wrong reason.
    /// The stale file is held open exclusively, so a read that opened it would fail;
    /// and it is filled with events that <em>are</em> inside the horizon, so a read
    /// that opened it successfully would report them. Neither happens.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_transcript_last_written_before_the_horizon_is_not_opened()
    {
        GivenClaudeTranscript(
            "D--Repos-Backlog",
            "stale",
            [(Yesterday, false), (Yesterday.AddMinutes(20), false)],
            lastWrite: Horizon.AddDays(-1));

        GivenClaudeTranscript(
            "D--Repos-Backlog",
            "fresh",
            [(Yesterday, false), (Yesterday.AddMinutes(2), false)],
            lastWrite: Noon);

        await using var _ = new FileStream(
            TranscriptPath("D--Repos-Backlog", "stale"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var log = await ReadAsync();

        var session = Assert.Single(log.Sessions);

        Assert.Equal("fresh", session.Id);
        Assert.Empty(log.Unreadable);
    }

    /// <summary>
    /// Clipped to the horizon rather than dropped at it, so a session that began before
    /// the window reports the part of itself inside it — and a run that finished before
    /// the window reports nothing, because none of it is inside.
    /// </summary>
    [Fact]
    public async Task A_run_that_began_before_the_horizon_is_clipped_to_it()
    {
        GivenClaudeTranscript(
            "D--Repos-Backlog",
            "straddling",
            [
                // Wholly before the horizon: dropped.
                (Horizon.AddHours(-2), false),
                (Horizon.AddHours(-2).AddMinutes(1), false),

                // Across it: truncated.
                (Horizon.AddMinutes(-2), false),
                (Horizon.AddMinutes(3), false),
                (Horizon.AddMinutes(4), false)
            ],
            lastWrite: Noon);

        var session = Assert.Single((await ReadAsync()).Sessions);
        var run = Assert.Single(session.Runs);

        Assert.Equal(Horizon, run.StartedAt);
        Assert.Equal(Horizon.AddMinutes(4), run.EndedAt);
    }

    /// <summary>
    /// The shared <c>ClaudeTranscripts.Newest</c> rule, from the other side. A session
    /// resumed in a worktree it did not start in is filed under two slugs while keeping
    /// its id; two activity records for it would double that session's time in every
    /// figure on the surface.
    /// </summary>
    [Fact]
    public async Task One_session_filed_under_two_slugs_is_one_activity_record()
    {
        const string id = "8c2e93b2-1210-4626-8ee3-c5d8c41ce94c";

        GivenClaudeTranscript(
            "D--Repos-Backlog--claude-worktrees-where-it-started",
            id,
            [(Noon.AddDays(-3), false), (Noon.AddDays(-3).AddMinutes(10), false)],
            lastWrite: Noon.AddDays(-3));

        GivenClaudeTranscript(
            "D--Repos-Backlog--claude-worktrees-where-it-carried-on",
            id,
            [(Yesterday, false), (Yesterday.AddMinutes(2), false)],
            lastWrite: Noon);

        var session = Assert.Single((await ReadAsync()).Sessions);
        var run = Assert.Single(session.Runs);

        // The more recently written file wins, exactly as it does for the session list:
        // it is the copy the agent went on appending to.
        Assert.Equal(Yesterday, run.StartedAt);
        Assert.Equal(TimeSpan.FromMinutes(2), Total(session));
    }

    /// <summary>
    /// A transcript held open by the agent appending to it. The guard is per file for
    /// the reason the session readers state at their own reads: an IOException let out
    /// of here would cost every Claude session rather than the one record it concerns,
    /// and would report the agent as unreadable into the bargain.
    /// </summary>
    [Fact]
    public async Task A_locked_transcript_costs_its_own_record_and_no_more()
    {
        GivenClaudeTranscript(
            "D--Repos-Backlog",
            "locked",
            [(Yesterday, false), (Yesterday.AddMinutes(9), false)],
            lastWrite: Noon);

        GivenClaudeTranscript(
            "D--Repos-Backlog",
            "readable",
            [(Yesterday, false), (Yesterday.AddMinutes(2), false)],
            lastWrite: Noon);

        await using var _ = new FileStream(
            TranscriptPath("D--Repos-Backlog", "locked"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var log = await ReadAsync();

        var session = Assert.Single(log.Sessions);

        Assert.Equal("readable", session.Id);
        Assert.Empty(log.Unreadable);
    }

    [Fact]
    public async Task An_agent_that_was_never_installed_is_not_an_unreadable_source()
    {
        GivenClaudeTranscript(
            "D--Repos-Backlog",
            "only",
            [(Yesterday, false), (Yesterday.AddMinutes(2), false)],
            lastWrite: Noon);

        var log = await ReadAsync();

        Assert.Single(log.Sessions);

        // No Copilot folder at all, which is the ordinary case on a machine that has
        // never run it — not a failure worth a permanent warning on the surface.
        Assert.Empty(log.Unreadable);
    }

    /// <summary>
    /// A folder that is there but cannot be read costs that agent and names it, rather
    /// than blanking the grid. Windows only: denying yourself a directory is the only
    /// way to produce this deliberately, and the repository's own
    /// <c>DpapiDeviceCredentialStoreTests</c> already guards this way.
    /// </summary>
    [Fact]
    public async Task A_folder_that_cannot_be_read_is_named_rather_than_fatal()
    {
        if (!OperatingSystem.IsWindows()) return;

        GivenCopilotEvents(
            "still-fine",
            [(Yesterday, "assistant.turn_start"), (Yesterday.AddMinutes(2), "assistant.turn_end")],
            lastWrite: Noon);

        var projects = Directory.CreateDirectory(Path.Combine(ClaudeHome, "projects"));
        var denial = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory,
            AccessControlType.Deny);

        Deny(projects, denial);

        try
        {
            var log = await ReadAsync();

            Assert.Equal(["Claude"], log.Unreadable);

            // The other agent's answer is intact. Half the picture and a sentence
            // saying which half, rather than none of it.
            var session = Assert.Single(log.Sessions);
            Assert.Equal("still-fine", session.Id);
        }
        finally
        {
            Allow(projects, denial);
        }
    }

    [Fact]
    public async Task Every_activity_record_is_stamped_with_this_machines_identity()
    {
        GivenClaudeTranscript(
            "D--Repos-Backlog",
            "claude-one",
            [(Yesterday, false), (Yesterday.AddMinutes(2), false)],
            lastWrite: Noon);

        GivenCopilotEvents(
            "copilot-one",
            [(Yesterday, "assistant.turn_start"), (Yesterday.AddMinutes(2), "assistant.turn_end")],
            lastWrite: Noon);

        var log = await ReadAsync();

        Assert.Equal(2, log.Sessions.Count);

        // Both facts on both records, from the one source: the id says which
        // environment and the name says what it is called. An activity record and a
        // session row have to be the same machine by identity, and two spellings of one
        // id is how that quietly stops being true.
        Assert.All(log.Sessions, session =>
        {
            Assert.Equal(MachineId, session.EnvironmentId);
            Assert.Equal(Machine, session.Environment);
        });

        Assert.Equal(
            [AgentSessionKind.Claude, AgentSessionKind.Copilot],
            log.Sessions.Select(session => session.Kind).Order());
    }

    /// <summary>
    /// The threshold travels with the answer rather than being a number the surface
    /// keeps its own copy of. The footnote on screen names it, and a second copy free
    /// to drift would let the sentence and the figure disagree.
    /// </summary>
    [Fact]
    public async Task The_threshold_the_runs_were_folded_at_travels_with_them()
    {
        var log = await ReadAsync();

        Assert.Equal(AgentActivityRuns.IdleAfter, log.IdleAfter);
        Assert.Equal(Horizon, log.Since);
    }

    /// <summary>
    /// A finished transcript never changes, so it is parsed once ever. Proved by
    /// locking the file after the first read: a second read that went back to the disk
    /// would find it unopenable and lose the record entirely.
    /// </summary>
    [Fact]
    public async Task A_transcript_read_once_is_not_parsed_again()
    {
        GivenClaudeTranscript(
            "D--Repos-Backlog",
            "remembered",
            [(Yesterday, false), (Yesterday.AddMinutes(4), false)],
            lastWrite: Noon);

        var cache = new RecordingCache();

        Assert.Single((await ReadAsync(cache)).Sessions);

        await using var _ = new FileStream(
            TranscriptPath("D--Repos-Backlog", "remembered"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var session = Assert.Single((await ReadAsync(cache)).Sessions);

        Assert.Equal(TimeSpan.FromMinutes(4), Total(session));

        // Once, not twice. The second read is the one that matters — it is the one a
        // person waits through every time the dashboard refreshes.
        Assert.Equal(1, cache.Writes);
    }

    /// <summary>
    /// The one transcript being appended to right now misses on every refresh, which is
    /// exactly the file whose answer must not be stale. That is what keying on the pair
    /// rather than on the path buys.
    /// </summary>
    [Fact]
    public async Task A_transcript_that_has_been_appended_to_is_parsed_again()
    {
        GivenClaudeTranscript(
            "D--Repos-Backlog",
            "growing",
            [(Yesterday, false), (Yesterday.AddMinutes(4), false)],
            lastWrite: Noon.AddHours(-2));

        var cache = new RecordingCache();

        Assert.Equal(TimeSpan.FromMinutes(4), Total(Assert.Single((await ReadAsync(cache)).Sessions)));

        GivenClaudeTranscript(
            "D--Repos-Backlog",
            "growing",
            [
                (Yesterday, false),
                (Yesterday.AddMinutes(4), false),
                (Yesterday.AddHours(3), false),
                (Yesterday.AddHours(3).AddMinutes(4), false)
            ],
            lastWrite: Noon);

        var session = Assert.Single((await ReadAsync(cache)).Sessions);

        Assert.Equal(2, session.Runs.Count);
        Assert.Equal(TimeSpan.FromMinutes(8), Total(session));
        Assert.Equal(2, cache.Writes);
    }

    /// <summary>
    /// The boundary the whole agents figure rests on, read from the activity side.
    /// <c>AgentSessionSourceTests.Files_a_session_spawned_are_not_sessions_of_their_own</c>
    /// is the same claim read from the session side and stays green untouched: the two
    /// walks read strictly disjoint sets, one of files sitting directly in a project
    /// folder and one of files under a session's own folder beside it.
    /// </summary>
    [Fact]
    public async Task Subagent_transcripts_are_reported_as_agents_and_never_as_sessions()
    {
        GivenClaudeTranscript(
            "D--Repos-Backlog",
            "parent",
            [(Yesterday, false), (Yesterday.AddMinutes(3), false)],
            lastWrite: Noon);

        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "parent",
            "subagents/agent-a16156d26373fd0e8.jsonl",
            Noon,
            Sidechain("parent", "a16156d26373fd0e8", Yesterday, "one"),
            Sidechain("parent", "a16156d26373fd0e8", Yesterday.AddMinutes(2), "two"));

        var log = await ReadAsync();

        // One session, and it is the parent's own transcript rather than the sidechain
        // wearing the parent's id — which is what it would be if the two walks overlapped.
        var session = Assert.Single(log.Sessions);

        Assert.Equal("parent", session.Id);
        Assert.Equal(TimeSpan.FromMinutes(3), Total(session));

        var agent = Assert.Single(log.Subagents);

        Assert.Equal("a16156d26373fd0e8", agent.Id);
        Assert.Equal(AgentSessionKind.Claude, agent.Kind);
        Assert.Equal(MachineId, agent.EnvironmentId);
        Assert.Equal(Machine, agent.Environment);

        var run = Assert.Single(agent.Runs);

        Assert.Equal(Yesterday, run.StartedAt);
        Assert.Equal(Yesterday.AddMinutes(2), run.EndedAt);
    }

    /// <summary>
    /// The one rule somebody will simplify back into a bug. A Workflow's subagent is
    /// filed under <c>subagents/workflows/wf_&lt;runId&gt;/</c> and a directly spawned one
    /// sits in <c>subagents/</c>, and on the profile this was measured against 377 of the
    /// 655 files were the deeper spelling. A shallow walk finds 42% of them and reports a
    /// peak of 9 where the truth is 19 — a plausible number, quietly halved.
    /// </summary>
    [Fact]
    public async Task A_subagent_filed_under_a_workflow_run_is_found_as_well_as_one_filed_beside_it()
    {
        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "parent",
            "subagents/agent-beside.jsonl",
            Noon,
            Sidechain("parent", "beside", Yesterday, "one"),
            Sidechain("parent", "beside", Yesterday.AddMinutes(2), "two"));

        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "parent",
            "subagents/workflows/wf_b0b8ae23-828/agent-deeper.jsonl",
            Noon,
            Sidechain("parent", "deeper", Yesterday, "one"),
            Sidechain("parent", "deeper", Yesterday.AddMinutes(4), "two"));

        var log = await ReadAsync();

        Assert.Equal(["beside", "deeper"], log.Subagents.Select(agent => agent.Id).Order());
    }

    /// <summary>
    /// The session comes off the path — the folder the <c>subagents</c> directory sits in
    /// — rather than out of the file. The fixture states a different id inside every line,
    /// so a reader that opened the file to answer a question the path already answers
    /// fails here rather than on a profile nobody is looking at.
    /// </summary>
    [Fact]
    public async Task The_session_that_spawned_a_subagent_is_named_on_its_record()
    {
        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "the-folder-that-owns-it",
            "subagents/agent-one.jsonl",
            Noon,
            Sidechain("a-session-id-written-inside-the-file", "one", Yesterday, "one"),
            Sidechain("a-session-id-written-inside-the-file", "one", Yesterday.AddMinutes(2), "two"));

        var agent = Assert.Single((await ReadAsync()).Subagents);

        Assert.Equal("the-folder-that-owns-it", agent.SessionId);
    }

    /// <summary>
    /// A Workflow run's ledger of started/result records sits in the same folder and is
    /// not an agent. Admitting files by position rather than by name would make the set
    /// "whatever Claude files under subagents next", which is the open-ended admission the
    /// session walk already refuses.
    /// </summary>
    [Fact]
    public async Task A_journal_beside_a_subagent_transcript_is_not_an_agent()
    {
        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "parent",
            "subagents/workflows/wf_b0b8ae23-828/journal.jsonl",
            Noon,
            """{"type":"started","key":"v2:d68dfac77245080a","agentId":"a16156d26373fd0e8"}""",
            """{"type":"result","key":"v2:d68dfac77245080a","agentId":"a16156d26373fd0e8"}""");

        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "parent",
            "subagents/workflows/wf_b0b8ae23-828/agent-a16156d26373fd0e8.jsonl",
            Noon,
            Sidechain("parent", "a16156d26373fd0e8", Yesterday, "one"),
            Sidechain("parent", "a16156d26373fd0e8", Yesterday.AddMinutes(2), "two"));

        var agent = Assert.Single((await ReadAsync()).Subagents);

        Assert.Equal("a16156d26373fd0e8", agent.Id);
    }

    /// <summary>The sidecar beside a sidechain transcript is not an agent either. It
    /// shares the <c>agent-</c> prefix and differs only in its extension, which is why the
    /// filter is a pattern rather than a prefix test.</summary>
    [Fact]
    public async Task A_meta_file_beside_a_subagent_transcript_is_not_an_agent()
    {
        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "parent",
            "subagents/agent-a16156d26373fd0e8.meta.json",
            Noon,
            """{"agentId":"a16156d26373fd0e8","name":"Explore","model":"claude-opus-5"}""");

        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "parent",
            "subagents/agent-a16156d26373fd0e8.jsonl",
            Noon,
            Sidechain("parent", "a16156d26373fd0e8", Yesterday, "one"),
            Sidechain("parent", "a16156d26373fd0e8", Yesterday.AddMinutes(2), "two"));

        var agent = Assert.Single((await ReadAsync()).Subagents);

        Assert.Equal("a16156d26373fd0e8", agent.Id);
    }

    /// <summary>
    /// A sidechain's user turns are tool results, so there is no prompt in one of these
    /// files to end a gap with — 135,896 turns across every subagent transcript on the
    /// machine this was built against carry no <c>promptSource</c> at all, against
    /// sdk 768 / typed 41 / system 37 on the parent side. The record therefore has no
    /// waits to carry, and none leaks into the sessions either.
    /// </summary>
    [Fact]
    public async Task Subagents_report_no_waits_because_a_sidechain_records_no_prompt()
    {
        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "parent",
            "subagents/agent-quiet.jsonl",
            Noon,
            Sidechain("parent", "quiet", Yesterday, "one"),
            Sidechain("parent", "quiet", Yesterday.AddMinutes(2), "two"),

            // Half an hour later, and the thing that ends the gap is a tool result rather
            // than a prompt — which is the only shape a sidechain's user turn comes in.
            SidechainToolResult("parent", "quiet", Yesterday.AddMinutes(32)),
            Sidechain("parent", "quiet", Yesterday.AddMinutes(34), "three"));

        var log = await ReadAsync();

        // The gap closed a run, exactly as it would in a parent transcript.
        var agent = Assert.Single(log.Subagents);

        Assert.Equal(2, agent.Runs.Count);

        // And nothing anywhere claims somebody was being waited on. The fold may produce
        // a wait; the contract has nowhere to put one, so a subagent can never assert it.
        Assert.Empty(log.Sessions.SelectMany(session => session.Waits));
    }

    /// <summary>
    /// Absent rather than present-and-empty, the rule the sessions already follow. A
    /// subagent whose whole record fell outside the horizon has nothing to contribute to a
    /// duration, and an entry with no intervals would be an agent claiming to have been
    /// measured.
    /// </summary>
    [Fact]
    public async Task A_subagent_whose_whole_record_falls_outside_the_horizon_is_absent_rather_than_empty()
    {
        // Written well inside the horizon, so it is opened rather than skipped on its
        // mtime: this is about the clipping and not about the cheap test in front of it.
        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "parent",
            "subagents/agent-ancient.jsonl",
            Noon,
            Sidechain("parent", "ancient", Horizon.AddHours(-3), "one"),
            Sidechain("parent", "ancient", Horizon.AddHours(-3).AddMinutes(2), "two"));

        Assert.Empty((await ReadAsync()).Subagents);
    }

    /// <summary>Clipped to the horizon rather than dropped at it, so an agent that began
    /// before the window reports the part of itself inside it.</summary>
    [Fact]
    public async Task A_subagent_run_that_began_before_the_horizon_is_clipped_to_it()
    {
        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "parent",
            "subagents/agent-straddling.jsonl",
            Noon,
            Sidechain("parent", "straddling", Horizon.AddMinutes(-2), "one"),
            Sidechain("parent", "straddling", Horizon.AddMinutes(3), "two"),
            Sidechain("parent", "straddling", Horizon.AddMinutes(4), "three"));

        var run = Assert.Single(Assert.Single((await ReadAsync()).Subagents).Runs);

        Assert.Equal(Horizon, run.StartedAt);
        Assert.Equal(Horizon.AddMinutes(4), run.EndedAt);
    }

    /// <summary>
    /// The mtime skip is what keeps a read over 1,146 extra files bounded, and it is
    /// inherited rather than rewritten. Proved the way the sessions prove it: the stale
    /// file is held open exclusively, so a read that opened it would fail, and it is full
    /// of events that are inside the horizon, so a read that opened it successfully would
    /// report them.
    /// </summary>
    [Fact]
    public async Task A_subagent_transcript_untouched_since_before_the_horizon_is_never_opened()
    {
        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "parent",
            "subagents/agent-stale.jsonl",
            Horizon.AddDays(-1),
            Sidechain("parent", "stale", Yesterday, "one"),
            Sidechain("parent", "stale", Yesterday.AddMinutes(20), "two"));

        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "parent",
            "subagents/agent-fresh.jsonl",
            Noon,
            Sidechain("parent", "fresh", Yesterday, "one"),
            Sidechain("parent", "fresh", Yesterday.AddMinutes(2), "two"));

        await using var _ = new FileStream(
            SpawnedPath("D--Repos-Backlog", "parent", "subagents/agent-stale.jsonl"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var log = await ReadAsync();

        Assert.Equal("fresh", Assert.Single(log.Subagents).Id);
        Assert.Empty(log.Unreadable);
    }

    /// <summary>
    /// The same cache, the same key, and no new port. A finished sidechain never changes,
    /// so it is parsed once ever — proved by locking it after the first read, which a
    /// second trip to the disk would fail on.
    /// </summary>
    [Fact]
    public async Task A_subagent_transcript_is_parsed_once_and_read_from_the_cache_after()
    {
        GivenClaudeTranscript(
            "D--Repos-Backlog",
            "parent",
            [(Yesterday, false), (Yesterday.AddMinutes(3), false)],
            lastWrite: Noon);

        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "parent",
            "subagents/agent-remembered.jsonl",
            Noon,
            Sidechain("parent", "remembered", Yesterday, "one"),
            Sidechain("parent", "remembered", Yesterday.AddMinutes(4), "two"));

        var cache = new RecordingCache();

        Assert.Single((await ReadAsync(cache)).Subagents);

        // The parent and the agent, one entry each and no more: the agent went through
        // the same path rather than round it.
        Assert.Equal(2, cache.Writes);

        await using var _ = new FileStream(
            SpawnedPath("D--Repos-Backlog", "parent", "subagents/agent-remembered.jsonl"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var agent = Assert.Single((await ReadAsync(cache)).Subagents);

        Assert.Equal(TimeSpan.FromMinutes(4), agent.Runs.Single().EndedAt - agent.Runs.Single().StartedAt);
        Assert.Equal(2, cache.Writes);
    }

    /// <summary>
    /// One session's permissions cost one session. The enumeration is guarded per session
    /// rather than per agent, so a folder that cannot be listed loses its own agents and
    /// leaves both the next session's agents and Claude's own name off the unreadable
    /// list — naming the whole agent over one folder would blank a grid the rest of the
    /// profile can fill.
    /// <para>
    /// Windows only: denying yourself a directory is the only way to produce this
    /// deliberately, the way <c>DpapiDeviceCredentialStoreTests</c> already guards.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_subagents_folder_that_cannot_be_read_costs_that_session_and_not_the_agent()
    {
        if (!OperatingSystem.IsWindows()) return;

        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "denied",
            "subagents/agent-hidden.jsonl",
            Noon,
            Sidechain("denied", "hidden", Yesterday, "one"),
            Sidechain("denied", "hidden", Yesterday.AddMinutes(2), "two"));

        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "readable",
            "subagents/agent-visible.jsonl",
            Noon,
            Sidechain("readable", "visible", Yesterday, "one"),
            Sidechain("readable", "visible", Yesterday.AddMinutes(2), "two"));

        var folder = new DirectoryInfo(
            Path.Combine(ClaudeHome, "projects", "D--Repos-Backlog", "denied", "subagents"));

        var denial = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory,
            AccessControlType.Deny);

        Deny(folder, denial);

        try
        {
            var log = await ReadAsync();

            Assert.Equal("visible", Assert.Single(log.Subagents).Id);
            Assert.Empty(log.Unreadable);
        }
        finally
        {
            Allow(folder, denial);
        }
    }

    /// <summary>Most sessions spawn nothing, and that is not an absence to report. The
    /// session is itself exactly as it was, and the agents list simply has nothing from
    /// it.</summary>
    [Fact]
    public async Task A_session_with_no_subagents_folder_still_reports_itself_and_reports_no_agents()
    {
        GivenClaudeTranscript(
            "D--Repos-Backlog",
            "solitary",
            [(Yesterday, false), (Yesterday.AddMinutes(3), false)],
            lastWrite: Noon);

        var log = await ReadAsync();

        Assert.Equal("solitary", Assert.Single(log.Sessions).Id);
        Assert.Empty(log.Subagents);
    }

    /// <summary>
    /// The dedupe rule the session walk already carries, applied to the folder beside it.
    /// A session resumed in a worktree it did not start in is filed under a second slug
    /// while keeping its id, and there is no reason that would spare the agents filed
    /// under it. The more recently written copy wins, because it is the one the agent went
    /// on appending to.
    /// </summary>
    [Fact]
    public async Task A_subagent_filed_under_two_project_folders_is_one_agent()
    {
        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog--claude-worktrees-where-it-started",
            "parent",
            "subagents/agent-twice.jsonl",
            Noon.AddDays(-3),
            Sidechain("parent", "twice", Noon.AddDays(-3), "one"),
            Sidechain("parent", "twice", Noon.AddDays(-3).AddMinutes(10), "two"));

        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog--claude-worktrees-where-it-carried-on",
            "parent",
            "subagents/agent-twice.jsonl",
            Noon,
            Sidechain("parent", "twice", Yesterday, "one"),
            Sidechain("parent", "twice", Yesterday.AddMinutes(2), "two"));

        var agent = Assert.Single((await ReadAsync()).Subagents);
        var run = Assert.Single(agent.Runs);

        Assert.Equal(Yesterday, run.StartedAt);
        Assert.Equal(TimeSpan.FromMinutes(2), run.EndedAt - run.StartedAt);
    }

    /// <summary>Copilot spawns none, so there is no Copilot path to invent. An empty list
    /// here is a fact about Copilot rather than a gap in the read.</summary>
    [Fact]
    public async Task Copilot_contributes_no_subagents()
    {
        GivenCopilotEvents(
            "chatting",
            [(Yesterday, "assistant.turn_start"), (Yesterday.AddMinutes(2), "assistant.turn_end")],
            lastWrite: Noon);

        var log = await ReadAsync();

        Assert.Single(log.Sessions);
        Assert.Empty(log.Subagents);
    }

    private Task<AgentActivityLog> ReadAsync(IAgentActivityCache? cache = null) =>
        new LocalAgentActivitySource(ClaudeHome, CopilotHome, MachineId, Machine, cache)
            .GetActivityAsync(Horizon);

    private static TimeSpan Total(AgentSessionActivity session) =>
        session.Runs.Aggregate(TimeSpan.Zero, (total, run) => total + (run.EndedAt - run.StartedAt));

    /// <param name="lines">Each entry is one transcript line: an instant, and whether it
    /// was a prompt. Written in Claude's real line shape — parentUuid, isSidechain,
    /// promptId, type, message, timestamp, and promptSource where there is one — because
    /// the three extraction rules this reader turns on are all rules about which fields a
    /// line has.</param>
    private void GivenClaudeTranscript(
        string slug,
        string id,
        IEnumerable<(DateTimeOffset At, bool IsPrompt)> lines,
        DateTimeOffset lastWrite) =>
        GivenClaudeRawTranscript(
            slug,
            id,
            [.. lines.Select(line => line.IsPrompt
                ? Prompt(id, line.At, "typed")
                : Assistant(id, line.At, "carrying on"))],
            lastWrite);

    /// <summary>The escape hatch for the trap cases: a line with no timestamp, an
    /// isMeta injection, a tool result. Those are shapes the typed builder above
    /// deliberately cannot express, because expressing them is the test.</summary>
    private void GivenClaudeRawTranscript(string slug, string id, string[] lines, DateTimeOffset lastWrite)
    {
        var project = Directory.CreateDirectory(Path.Combine(ClaudeHome, "projects", slug));
        var path = Path.Combine(project.FullName, $"{id}.jsonl");

        File.WriteAllLines(path, lines);
        File.SetLastWriteTimeUtc(path, lastWrite.UtcDateTime);
    }

    private string TranscriptPath(string slug, string id) =>
        Path.Combine(ClaudeHome, "projects", slug, $"{id}.jsonl");

    /// <summary>
    /// One file inside a session's own folder — a sidechain transcript, a Workflow
    /// journal, a meta sidecar. The path is given relative to that folder and spelled
    /// with forward slashes, because the shape of the path <em>is</em> what these facts
    /// are about: a rule that only recognised one of the two depths would pass half of
    /// them.
    /// </summary>
    private void GivenClaudeFileSpawnedBy(
        string slug,
        string sessionId,
        string relativePath,
        DateTimeOffset lastWrite,
        params string[] lines)
    {
        var path = SpawnedPath(slug, sessionId, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        File.SetLastWriteTimeUtc(path, lastWrite.UtcDateTime);
    }

    private string SpawnedPath(string slug, string sessionId, string relativePath) =>
        Path.Combine(
            ClaudeHome,
            "projects",
            slug,
            sessionId,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// One line of a subagent's transcript, in the shape Claude really writes one:
    /// <c>isSidechain</c> true, an <c>agentId</c>, the spawning session's id, and no
    /// <c>promptSource</c> anywhere. The last of those is the point — it is what makes a
    /// subagent's waits structurally impossible rather than merely rare.
    /// </summary>
    private static string Sidechain(string sessionId, string agentId, DateTimeOffset at, string text) =>
        $$$"""
        {"parentUuid":null,"isSidechain":true,"agentId":"{{{agentId}}}","userType":"external","cwd":"D:\\Repos\\Backlog","sessionId":"{{{sessionId}}}","version":"2.1.229","gitBranch":"main","type":"assistant","message":{"id":"msg_01Hs","type":"message","role":"assistant","model":"claude-opus-4-5-20260101","content":[{"type":"text","text":"{{{text}}}"}],"usage":{"input_tokens":4,"output_tokens":9}},"requestId":"req_011CT","uuid":"5d6e7f8a-9b0c-4d1e-8f2a-3b4c5d6e7f80","timestamp":"{{{Stamp(at)}}}"}
        """;

    /// <summary>A sidechain's user turn, which is a tool result and never a prompt. This
    /// is the only shape one comes in, which is why no gap inside one of these files can
    /// end in a wait.</summary>
    private static string SidechainToolResult(string sessionId, string agentId, DateTimeOffset at) =>
        $$"""
        {"parentUuid":"5d6e7f8a-9b0c-4d1e-8f2a-3b4c5d6e7f80","isSidechain":true,"agentId":"{{agentId}}","userType":"external","cwd":"D:\\Repos\\Backlog","sessionId":"{{sessionId}}","version":"2.1.229","gitBranch":"main","type":"user","message":{"role":"user","content":[{"tool_use_id":"toolu_01Wq","type":"tool_result","content":"Done."}]},"uuid":"6e7f8a9b-0c1d-4e2f-9a3b-4c5d6e7f8a91","timestamp":"{{Stamp(at)}}"}
        """;

    private static string Prompt(string id, DateTimeOffset at, string source) =>
        $$"""
        {"parentUuid":null,"isSidechain":false,"userType":"external","cwd":"D:\\Repos\\Backlog","sessionId":"{{id}}","version":"2.1.229","gitBranch":"main","type":"user","message":{"role":"user","content":"go on"},"promptSource":"{{source}}","promptId":"c1f0d1a4-6d1f-4c65-9f36-9a5b0a2d9f11","uuid":"7c1b0f2a-4d2e-4a91-9f2c-1d8a0b3e6c22","timestamp":"{{Stamp(at)}}"}
        """;

    private static string Assistant(string id, DateTimeOffset at, string text) =>
        $$$"""
        {"parentUuid":"7c1b0f2a-4d2e-4a91-9f2c-1d8a0b3e6c22","isSidechain":false,"userType":"external","cwd":"D:\\Repos\\Backlog","sessionId":"{{{id}}}","version":"2.1.229","gitBranch":"main","type":"assistant","message":{"id":"msg_01Hs","type":"message","role":"assistant","model":"claude-opus-4-5-20260101","content":[{"type":"text","text":"{{{text}}}"}],"usage":{"input_tokens":4,"output_tokens":9}},"requestId":"req_011CT","uuid":"1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d","timestamp":"{{{Stamp(at)}}}"}
        """;

    /// <summary>A tool result: a user line with no promptSource and array content.
    /// 22,213 of the 22,472 promptSource-less lines on this machine are these.</summary>
    private static string ToolResult(string id, DateTimeOffset at) =>
        $$"""
        {"parentUuid":"1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d","isSidechain":false,"userType":"external","cwd":"D:\\Repos\\Backlog","sessionId":"{{id}}","version":"2.1.229","gitBranch":"main","type":"user","message":{"role":"user","content":[{"tool_use_id":"toolu_01Wq","type":"tool_result","content":"Build succeeded."}]},"uuid":"2b3c4d5e-6f7a-4b8c-9d0e-1f2a3b4c5d6e","timestamp":"{{Stamp(at)}}"}
        """;

    /// <summary>A skill injection: isMeta, text content, and no promptSource. The 259
    /// lines that make rule three a rule rather than a footnote.</summary>
    private static string SkillInjection(string id, DateTimeOffset at) =>
        $$"""
        {"parentUuid":"2b3c4d5e-6f7a-4b8c-9d0e-1f2a3b4c5d6e","isSidechain":false,"userType":"external","cwd":"D:\\Repos\\Backlog","sessionId":"{{id}}","version":"2.1.229","gitBranch":"main","isMeta":true,"type":"user","message":{"role":"user","content":"<skill-instructions>Follow the checklist.</skill-instructions>"},"uuid":"3c4d5e6f-7a8b-4c9d-0e1f-2a3b4c5d6e7f","timestamp":"{{Stamp(at)}}"}
        """;

    private void GivenCopilotEvents(
        string id,
        IEnumerable<(DateTimeOffset At, string Type)> events,
        DateTimeOffset lastWrite)
    {
        var folder = GivenCopilotSessionFolder(id);
        var path = Path.Combine(folder, "events.jsonl");

        // Copilot's own line shape: a top-level type, a timestamp, and a data object
        // whose contents differ per type and which this reader deliberately never opens.
        File.WriteAllLines(path, [.. events.Select(entry => $$$"""
            {"type":"{{{entry.Type}}}","timestamp":"{{{Stamp(entry.At)}}}","data":{"turn_id":"f4a1c7d2-9b3e-4c15-8a6d-2e7f0b9c4a13"}}
            """)]);

        File.SetLastWriteTimeUtc(path, lastWrite.UtcDateTime);
    }

    /// <summary>A session folder with its descriptor and no events file — 468 of the
    /// 691 on this machine.</summary>
    private string GivenCopilotSessionFolder(string id)
    {
        var folder = Directory.CreateDirectory(Path.Combine(CopilotHome, "session-state", id));

        File.WriteAllText(Path.Combine(folder.FullName, "workspace.yaml"), $"""
            id: {id}
            cwd: D:\Repos\Backlog
            repository: JSdotNet/Backlog
            branch: main
            """);

        return folder.FullName;
    }

    private static string Stamp(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static void Deny(DirectoryInfo folder, FileSystemAccessRule rule)
    {
        if (!OperatingSystem.IsWindows()) return;

        var security = folder.GetAccessControl();
        security.AddAccessRule(rule);
        folder.SetAccessControl(security);
    }

    private static void Allow(DirectoryInfo folder, FileSystemAccessRule rule)
    {
        if (!OperatingSystem.IsWindows()) return;

        var security = folder.GetAccessControl();
        security.RemoveAccessRule(rule);
        folder.SetAccessControl(security);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// The cache, in memory and counting. Not a stand-in for the disk half — that has
    /// its own tests against a real folder — but for what the source does with the port:
    /// whether it asks, and whether it asks twice for a file nobody touched.
    /// </summary>
    private sealed class RecordingCache : IAgentActivityCache
    {
        private readonly Dictionary<string, AgentActivityEntry> _entries = [];

        internal int Writes { get; private set; }

        public AgentActivityEntry? TryRead(string path, DateTimeOffset writtenAt, TimeSpan idleAfter) =>
            _entries.TryGetValue(Key(path, writtenAt, idleAfter), out var entry) ? entry : null;

        public void Write(string path, DateTimeOffset writtenAt, AgentActivityEntry entry)
        {
            Writes++;
            _entries[Key(path, writtenAt, entry.IdleAfter)] = entry;
        }

        public void Forget() => _entries.Clear();

        private static string Key(string path, DateTimeOffset writtenAt, TimeSpan idleAfter) =>
            $"{path}|{writtenAt:O}|{idleAfter}";
    }
}
