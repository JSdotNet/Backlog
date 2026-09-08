using System.Globalization;
using Backlog.Modules.Sessions.UI.Adapters;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the two agents leave on disk, and what a row ends up saying. This is the
/// seam worth testing directly rather than only through the pane: a timestamp read
/// out of the wrong field, a repository guessed from a path, or a finished session
/// reported as running are all wrong in a way that renders perfectly.
/// <para>
/// Fixture folders rather than this machine's own profile. The real folders are what
/// the surface is validated against, but a test that read them would assert
/// whatever the person running it had been doing that morning.
/// </para>
/// </summary>
public sealed class AgentSessionSourceTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private const string Machine = "DEV-TOWER";

    /// <summary>The device id every session read here is stamped with. A Guid in the
    /// form the store writes, because that is what a host actually passes in and an id
    /// spelled two ways is the failure this stamp exists to prevent.</summary>
    private const string MachineId = "6b8e6f0c-1a4f-4a2e-9f4b-6a2c0f5d3a71";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "backlog-agent-session-tests",
        Guid.NewGuid().ToString("n"));

    private string ClaudeHome => Path.Combine(_root, ".claude");

    private string CopilotHome => Path.Combine(_root, ".copilot");

    [Fact]
    public async Task A_running_claude_session_is_read_from_its_live_file()
    {
        GivenClaudeLiveSession(
            "5905cf2d-28a0-4e71-86c8-2ecd270f404a",
            @"D:\Repos\Backlog\.claude\worktrees\keen-bose-667825",
            name: "keen-bose-667825-b7",
            startedAt: Noon.AddHours(-2),
            lastWrite: Noon.AddMinutes(-4));

        var session = Assert.Single((await ReadAsync()).Sessions);

        Assert.Equal("5905cf2d-28a0-4e71-86c8-2ecd270f404a", session.Id);
        Assert.Equal(AgentSessionKind.Claude, session.Kind);
        // Both, and they are two different facts: the id says which environment and
        // the name says what it is called. A session found here ran here.
        Assert.Equal(MachineId, session.EnvironmentId);
        Assert.Equal(Machine, session.Environment);

        // The name the agent gave itself, because it is the only human-chosen thing
        // in the file.
        Assert.Equal("keen-bose-667825-b7", session.Title);
        Assert.Equal(@"D:\Repos\Backlog\.claude\worktrees\keen-bose-667825", session.WorkingFolder);
        Assert.Equal(Noon.AddHours(-2), session.StartedAt);
        Assert.Equal(Noon.AddMinutes(-4), session.LastActivityAt);
        Assert.Equal(AgentSessionState.Running, session.State);

        // Claude records neither, and a repository guessed from a path leaf would be
        // a wrong fact rather than a missing one.
        Assert.Null(session.Repository);
    }

    /// <summary>
    /// A live file that has not moved for longer than the threshold. Still
    /// registered as running — the file is there — but nothing has happened, which is
    /// what Stalled means and why it is not Finished.
    /// </summary>
    [Fact]
    public async Task A_live_file_that_has_gone_quiet_is_stalled()
    {
        GivenClaudeLiveSession(
            "quiet",
            @"D:\Repos\Backlog",
            name: "left open",
            startedAt: Noon.AddHours(-9),
            lastWrite: Noon - AgentSessionStates.StaleAfter - TimeSpan.FromMinutes(1));

        var session = Assert.Single((await ReadAsync()).Sessions);

        Assert.Equal(AgentSessionState.Stalled, session.State);
    }

    [Fact]
    public async Task A_transcript_becomes_a_finished_session_with_the_folder_it_states()
    {
        GivenClaudeTranscript(
            "D--Repos-Backlog--claude-worktrees-bold-bell-1f9ca7",
            "25554c05-3745-4632-af58-9eba10b62743",
            folder: @"D:\Repos\Backlog\.claude\worktrees\bold-bell-1f9ca7",
            branch: "claude/bold-bell-1f9ca7",
            lastWrite: Noon.AddDays(-4));

        var session = Assert.Single((await ReadAsync()).Sessions);

        Assert.Equal("25554c05-3745-4632-af58-9eba10b62743", session.Id);

        // A session out of the history is stamped with this device too — the machine
        // that holds the transcript is the machine it ran on.
        Assert.Equal(MachineId, session.EnvironmentId);
        Assert.Equal(Machine, session.Environment);

        // The folder is read out of the transcript rather than decoded from the
        // slug. The slug flattens separators, colons and dots all to hyphens, so
        // several paths produce the same one and only one of them is right.
        Assert.Equal(@"D:\Repos\Backlog\.claude\worktrees\bold-bell-1f9ca7", session.WorkingFolder);
        Assert.Equal("claude/bold-bell-1f9ca7", session.Branch);

        // The folder's leaf, which for this product's sessions is the worktree name.
        Assert.Equal("bold-bell-1f9ca7", session.Title);
        Assert.Equal(AgentSessionState.Finished, session.State);
        Assert.Equal(Noon.AddDays(-4), session.LastActivityAt);
    }

    /// <summary>
    /// The transcript's first lines are queued prompts and hooks, which state no
    /// folder. Stopping at the first line would leave every session's folder empty.
    /// </summary>
    [Fact]
    public async Task The_folder_is_found_past_the_lines_that_do_not_state_one()
    {
        var project = Directory.CreateDirectory(Path.Combine(ClaudeHome, "projects", "D--Repos-Backlog"));
        var path = Path.Combine(project.FullName, "late.jsonl");

        await File.WriteAllLinesAsync(path,
        [
            """{"type":"queue-operation","operation":"enqueue","content":"do the thing"}""",
            "not json at all",
            """{"type":"hook","name":"SessionStart"}""",
            """{"type":"user","cwd":"D:\\Repos\\Backlog","gitBranch":"main"}"""
        ], TestContext.Current.CancellationToken);

        var session = Assert.Single((await ReadAsync()).Sessions);

        Assert.Equal(@"D:\Repos\Backlog", session.WorkingFolder);
        Assert.Equal("main", session.Branch);
    }

    /// <summary>
    /// A running session also has a transcript. Listing both would show one session
    /// twice, once as running and once as finished — and the finished one would be a
    /// lie about a session that is still going.
    /// </summary>
    [Fact]
    public async Task A_session_that_is_both_live_and_transcribed_is_listed_once_as_live()
    {
        GivenClaudeLiveSession("shared", @"D:\Repos\Backlog", "live one", Noon.AddHours(-1), Noon.AddMinutes(-3));
        GivenClaudeTranscript("D--Repos-Backlog", "shared", @"D:\Repos\Backlog", "main", Noon.AddMinutes(-3));

        var session = Assert.Single((await ReadAsync()).Sessions);

        Assert.Equal(AgentSessionState.Running, session.State);
        Assert.Equal("live one", session.Title);
    }

    /// <summary>
    /// The live folder holds a file per process, not per session, so a resumed session
    /// comes back under a new process id while the old file is still sitting there.
    /// <para>
    /// Two rows for one session is wrong on its own terms, and it was worse than wrong
    /// on screen: the pane keys its rows by session id, two siblings with the same key
    /// corrupt Blazor's keyed diff, and the circuit went down. Found on a real profile,
    /// where one of seventeen live files was a leftover — which is why this test exists
    /// with a fixture rather than as a comment.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Two_live_files_claiming_one_session_are_one_row()
    {
        GivenClaudeLiveSession(
            "4e9507af-255b-440e-98cf-76b677327dd2",
            @"D:\Repos\Backlog",
            name: "the leftover",
            startedAt: Noon.AddHours(-9),
            lastWrite: Noon.AddHours(-8),
            pid: 1111);

        GivenClaudeLiveSession(
            "4e9507af-255b-440e-98cf-76b677327dd2",
            @"D:\Repos\Backlog",
            name: "the live one",
            startedAt: Noon.AddHours(-9),
            lastWrite: Noon.AddMinutes(-2),
            pid: 2222);

        var catalog = await ReadAsync();
        var session = Assert.Single(catalog.Sessions);

        // The most recently written file wins, because that is the process actually
        // running.
        Assert.Equal("the live one", session.Title);
        Assert.Equal(AgentSessionState.Running, session.State);

        // One session discovered, not two: the leftover was never a second session.
        Assert.Equal(1, catalog.Discovered);
    }

    /// <summary>
    /// A transcript is filed under the folder the session ran in, so a session whose
    /// working folder changed — resumed in a worktree it did not start in — leaves a
    /// transcript under each folder it touched. One id, two files, one session.
    /// <para>
    /// The same defect as the two live files above, and the same consequence: the pane
    /// keys its rows by session id, two siblings with one key corrupt Blazor's keyed
    /// diff, and the circuit goes down. Found on a real profile, where one of 377
    /// transcripts was filed under two worktrees.
    /// </para>
    /// </summary>
    [Fact]
    public async Task One_session_filed_under_two_folders_is_one_row_not_two()
    {
        const string id = "8c2e93b2-1210-4626-8ee3-c5d8c41ce94c";

        GivenClaudeTranscript(
            "D--Repos-Backlog--claude-worktrees-where-it-started",
            id,
            folder: @"D:\Repos\Backlog\.claude\worktrees\where-it-started",
            branch: "claude/where-it-started",
            lastWrite: Noon.AddHours(-9));

        GivenClaudeTranscript(
            "D--Repos-Backlog--claude-worktrees-where-it-carried-on",
            id,
            folder: @"D:\Repos\Backlog\.claude\worktrees\where-it-carried-on",
            branch: "claude/where-it-carried-on",
            lastWrite: Noon.AddHours(-2));

        var catalog = await ReadAsync();
        var session = Assert.Single(catalog.Sessions);

        // The more recently written transcript wins. It is the more current record of
        // the one session, and the folder it names is where that session ended up.
        Assert.Equal(@"D:\Repos\Backlog\.claude\worktrees\where-it-carried-on", session.WorkingFolder);
        Assert.Equal("claude/where-it-carried-on", session.Branch);
        Assert.Equal("where-it-carried-on", session.Title);
        Assert.Equal(Noon.AddHours(-2), session.LastActivityAt);

        // One session discovered, not two. The second file was never a second session,
        // and a subtitle counting it would overstate what this machine has.
        Assert.Equal(1, catalog.Discovered);
        Assert.False(catalog.Capped);
    }

    /// <summary>
    /// The live file and both of that session's transcripts at once. The two dedupes
    /// have to compose: dropping the transcript that matches a live session does not
    /// on its own stop the session's other transcript becoming a second row.
    /// </summary>
    [Fact]
    public async Task A_live_session_with_two_transcripts_is_still_one_row()
    {
        GivenClaudeLiveSession(
            "carried",
            @"D:\Repos\Backlog\.claude\worktrees\second",
            name: "the live one",
            startedAt: Noon.AddHours(-9),
            lastWrite: Noon.AddMinutes(-1));

        GivenClaudeTranscript(
            "D--Repos-Backlog--claude-worktrees-first",
            "carried",
            @"D:\Repos\Backlog\.claude\worktrees\first",
            "main",
            Noon.AddHours(-6));

        GivenClaudeTranscript(
            "D--Repos-Backlog--claude-worktrees-second",
            "carried",
            @"D:\Repos\Backlog\.claude\worktrees\second",
            "main",
            Noon.AddMinutes(-1));

        var catalog = await ReadAsync();
        var session = Assert.Single(catalog.Sessions);

        // The live file is still the better record of the three.
        Assert.Equal("the live one", session.Title);
        Assert.Equal(AgentSessionState.Running, session.State);
        Assert.Equal(1, catalog.Discovered);
    }

    /// <summary>
    /// The cap counts sessions, not files. A duplicate transcript must not consume a
    /// place in the capped list, or a machine with duplicates would show fewer sessions
    /// than the cap allows while claiming to have hit it.
    /// </summary>
    [Fact]
    public async Task A_duplicate_transcript_does_not_take_up_a_place_under_the_cap()
    {
        for (var index = 0; index < AgentSessionLimits.PerAgent; index++)
        {
            GivenClaudeTranscript(
                "D--Repos-Backlog",
                $"session-{index:000}",
                @"D:\Repos\Backlog",
                "main",
                Noon.AddMinutes(-index));
        }

        // The oldest of them, filed a second time under another worktree.
        GivenClaudeTranscript(
            "D--Repos-Backlog--claude-worktrees-elsewhere",
            $"session-{AgentSessionLimits.PerAgent - 1:000}",
            @"D:\Repos\Backlog\.claude\worktrees\elsewhere",
            "main",
            Noon.AddMinutes(-1));

        var catalog = await ReadAsync();

        Assert.Equal(AgentSessionLimits.PerAgent, catalog.Sessions.Count);
        Assert.Equal(AgentSessionLimits.PerAgent, catalog.Discovered);
        Assert.False(catalog.Capped);

        // Deduped before the cap, not after: the collapsed session is the newer file's
        // record of it, and it is still one of the hundred.
        var collapsed = Assert.Single(
            catalog.Sessions,
            session => session.Id == $"session-{AgentSessionLimits.PerAgent - 1:000}");

        Assert.Equal(@"D:\Repos\Backlog\.claude\worktrees\elsewhere", collapsed.WorkingFolder);
    }

    [Fact]
    public async Task A_copilot_descriptor_becomes_a_session_with_its_repository_and_branch()
    {
        GivenCopilotSession(
            "0012e2c7-aa39-4e43-9e57-e74a0ab62517",
            folder: @"C:\Users\jobsc\.copilot\repos\project-guidelines-mcp",
            repository: "JSdotNet/Project-Guidelines-MCP",
            branch: "main",
            created: Noon.AddDays(-22),
            updated: Noon.AddDays(-22).AddMinutes(1));

        var session = Assert.Single((await ReadAsync()).Sessions);

        Assert.Equal("0012e2c7-aa39-4e43-9e57-e74a0ab62517", session.Id);
        Assert.Equal(AgentSessionKind.Copilot, session.Kind);

        // The same stamp as Claude's, from the same source: both readers are handed
        // this device's identity rather than each deciding what an environment is.
        Assert.Equal(MachineId, session.EnvironmentId);
        Assert.Equal(Machine, session.Environment);

        // Copilot records the repository, so this column is filled from what it
        // wrote rather than left empty as Claude's is.
        Assert.Equal("JSdotNet/Project-Guidelines-MCP", session.Repository);
        Assert.Equal("main", session.Branch);
        Assert.Equal("JSdotNet/Project-Guidelines-MCP", session.Title);
        Assert.Equal(@"C:\Users\jobsc\.copilot\repos\project-guidelines-mcp", session.WorkingFolder);
        Assert.Equal(Noon.AddDays(-22), session.StartedAt);
        Assert.Equal(Noon.AddDays(-22).AddMinutes(1), session.LastActivityAt);

        // Copilot leaves the folder exactly as it is when a session ends, so an old
        // descriptor is over. There is no evidence on disk that would make it
        // Stalled instead, and inventing some would be the wrong kind of helpful.
        Assert.Equal(AgentSessionState.Finished, session.State);
    }

    [Fact]
    public async Task A_copilot_session_updated_moments_ago_is_running()
    {
        GivenCopilotSession(
            "recent",
            folder: @"D:\Repos\Backlog",
            repository: "JSdotNet/Backlog",
            branch: "main",
            created: Noon.AddHours(-1),
            updated: Noon.AddMinutes(-5));

        var session = Assert.Single((await ReadAsync()).Sessions);

        Assert.Equal(AgentSessionState.Running, session.State);
    }

    [Fact]
    public async Task Both_agents_answer_into_one_list()
    {
        GivenClaudeLiveSession("c1", @"D:\Repos\Backlog", "worktree", Noon.AddHours(-1), Noon.AddMinutes(-2));
        GivenCopilotSession("p1", @"D:\Repos\Backlog", "JSdotNet/Backlog", "main", Noon.AddHours(-3), Noon.AddHours(-2));

        var catalog = await ReadAsync();

        Assert.Equal(2, catalog.Sessions.Count);
        Assert.Empty(catalog.Unreadable);
        Assert.Equal(
            [AgentSessionKind.Claude, AgentSessionKind.Copilot],
            catalog.Sessions.Select(session => session.Kind).Order());
    }

    /// <summary>
    /// A machine with only one of the two agents installed is the ordinary case, not
    /// a failure. An absent folder must not be reported as unreadable, or the pane
    /// would carry a permanent warning on every machine that has never run Copilot.
    /// </summary>
    [Fact]
    public async Task An_agent_that_was_never_installed_is_not_an_unreadable_source()
    {
        GivenClaudeLiveSession("only", @"D:\Repos\Backlog", "worktree", Noon.AddHours(-1), Noon.AddMinutes(-2));

        var catalog = await ReadAsync();

        Assert.Single(catalog.Sessions);
        Assert.Empty(catalog.Unreadable);
    }

    [Fact]
    public async Task Nothing_on_the_machine_is_an_empty_catalog_rather_than_a_throw()
    {
        var catalog = await ReadAsync();

        Assert.Empty(catalog.Sessions);
        Assert.Empty(catalog.Unreadable);
    }

    /// <summary>
    /// A live file being rewritten by the agent while it is read, or one that is not
    /// what this reader expects, costs its own row and nothing else.
    /// </summary>
    [Fact]
    public async Task A_live_file_that_is_not_a_session_is_skipped_rather_than_fatal()
    {
        var folder = Directory.CreateDirectory(Path.Combine(ClaudeHome, "sessions"));

        await File.WriteAllTextAsync(Path.Combine(folder.FullName, "half-written.json"), "{\"pid\":123,\"sess", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(folder.FullName, "not-a-session.json"), "[]", TestContext.Current.CancellationToken);
        GivenClaudeLiveSession("good", @"D:\Repos\Backlog", "worktree", Noon.AddHours(-1), Noon.AddMinutes(-2));

        var catalog = await ReadAsync();

        var session = Assert.Single(catalog.Sessions);
        Assert.Equal("good", session.Id);
        Assert.Empty(catalog.Unreadable);
    }

    /// <summary>
    /// A live file held open by the process that owns it. The same event as the
    /// half-written one above and it has to cost the same: one row. It is the
    /// <em>open</em> rather than the parse that fails here, which is why the two are
    /// separate tests — a guard around only the parse leaves this one blanking every
    /// Claude session and reporting the agent as unreadable.
    /// </summary>
    [Fact]
    public async Task A_locked_claude_live_file_costs_its_own_row_and_no_more()
    {
        GivenClaudeLiveSession("locked", @"D:\Repos\Other", "other", Noon.AddHours(-2), Noon.AddMinutes(-9), pid: 4242);
        GivenClaudeLiveSession("good", @"D:\Repos\Backlog", "worktree", Noon.AddHours(-1), Noon.AddMinutes(-2));

        await using var _ = new FileStream(
            Path.Combine(ClaudeHome, "sessions", "4242.json"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var catalog = await ReadAsync();

        var session = Assert.Single(catalog.Sessions);
        Assert.Equal("good", session.Id);
        Assert.Empty(catalog.Unreadable);
    }

    /// <summary>A Copilot descriptor open for writing, for the same reason and with
    /// the same answer.</summary>
    [Fact]
    public async Task A_locked_copilot_descriptor_costs_its_own_row_and_no_more()
    {
        GivenCopilotSession("locked", @"D:\Repos\Other", "JSdotNet/Other", "main", Noon.AddHours(-4), Noon.AddHours(-3));
        GivenCopilotSession("good", @"D:\Repos\Backlog", "JSdotNet/Backlog", "main", Noon.AddHours(-3), Noon.AddHours(-2));

        await using var _ = new FileStream(
            Path.Combine(CopilotHome, "session-state", "locked", "workspace.yaml"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var catalog = await ReadAsync();

        var session = Assert.Single(catalog.Sessions);
        Assert.Equal("good", session.Id);
        Assert.Empty(catalog.Unreadable);
    }

    /// <summary>
    /// The cap is on work and on the length of a list, not on truth: every transcript
    /// costs an open and a few reads, and a developer's profile holds hundreds. What
    /// is dropped is the oldest history, which is the part a reader scanning a session
    /// list is least likely to be after — and the count of what existed comes back
    /// beside what was kept, so the surface can say so.
    /// </summary>
    [Fact]
    public async Task Only_the_most_recent_claude_sessions_are_read_and_the_rest_are_counted()
    {
        const int extra = 5;

        for (var index = 0; index < AgentSessionLimits.PerAgent + extra; index++)
        {
            GivenClaudeTranscript(
                "D--Repos-Backlog",
                $"session-{index:000}",
                @"D:\Repos\Backlog",
                "main",
                Noon.AddMinutes(-index));
        }

        var catalog = await ReadAsync();

        Assert.Equal(AgentSessionLimits.PerAgent, catalog.Sessions.Count);
        Assert.Equal(AgentSessionLimits.PerAgent + extra, catalog.Discovered);
        Assert.True(catalog.Capped);

        // The newest survived and the oldest did not, which is the half of the cap
        // worth asserting: a cap that dropped an arbitrary set would pass a count.
        Assert.Contains(catalog.Sessions, session => session.Id == "session-000");
        Assert.DoesNotContain(
            catalog.Sessions,
            session => session.Id == $"session-{AgentSessionLimits.PerAgent + extra - 1:000}");
    }

    /// <summary>
    /// Copilot keeps a folder per session forever, so it is the agent that overruns
    /// first — on the machine this was validated against, 705 folders against Claude's
    /// 137. Its own cap is what stops one agent's history crowding the other's
    /// sessions out of a surface whose whole point is showing both.
    /// </summary>
    [Fact]
    public async Task Only_the_most_recent_copilot_sessions_are_read_and_the_rest_are_counted()
    {
        const int extra = 7;

        for (var index = 0; index < AgentSessionLimits.PerAgent + extra; index++)
        {
            GivenCopilotSession(
                $"copilot-{index:000}",
                @"D:\Repos\Backlog",
                "JSdotNet/Backlog",
                "main",
                Noon.AddDays(-2),
                Noon.AddDays(-2).AddMinutes(-index),
                descriptorWritten: Noon.AddMinutes(-index));
        }

        var catalog = await ReadAsync();

        Assert.Equal(AgentSessionLimits.PerAgent, catalog.Sessions.Count);
        Assert.Equal(AgentSessionLimits.PerAgent + extra, catalog.Discovered);
        Assert.Contains(catalog.Sessions, session => session.Id == "copilot-000");
    }

    /// <summary>
    /// A live session is never what the cap drops. It is the row a reader opened this
    /// surface for, and there are only ever as many of them as the machine is running.
    /// </summary>
    [Fact]
    public async Task The_cap_never_costs_a_running_session()
    {
        GivenClaudeLiveSession("running-now", @"D:\Repos\Backlog", "live", Noon.AddHours(-1), Noon.AddMinutes(-1));

        for (var index = 0; index < AgentSessionLimits.PerAgent + 20; index++)
        {
            GivenClaudeTranscript(
                "D--Repos-Backlog",
                $"old-{index:000}",
                @"D:\Repos\Backlog",
                "main",
                Noon.AddMinutes(-index - 5));
        }

        var catalog = await ReadAsync();

        Assert.Contains(catalog.Sessions, session => session.Id == "running-now");
        Assert.Equal(AgentSessionLimits.PerAgent, catalog.Sessions.Count);
    }

    [Fact]
    public async Task A_list_that_fits_is_not_reported_as_capped()
    {
        GivenClaudeLiveSession("only", @"D:\Repos\Backlog", "worktree", Noon.AddHours(-1), Noon.AddMinutes(-2));

        var catalog = await ReadAsync();

        Assert.Single(catalog.Sessions);
        Assert.Equal(1, catalog.Discovered);
        Assert.False(catalog.Capped);
    }

    /// <summary>
    /// The turn count is how many prompts the person sent, and this fixture is built
    /// so that every cheaper way of arriving at a number gets a different one.
    /// <para>
    /// Counting the substring <c>"type":"user"</c> over the file is the first cheaper
    /// way, and the assistant line here quotes a transcript line back inside its own
    /// text so that count over-reads. Parsing every line and counting a root
    /// <c>type</c> of <c>user</c> is the second, and the tool-result lines defeat it:
    /// Claude files a tool's output as a user-role message, and on this machine's real
    /// transcripts those outnumber the person's prompts by more than thirty to one. A
    /// skill body injected as <c>isMeta</c> and a subagent's task prompt marked
    /// <c>isSidechain</c> are user-role too, and neither was typed by anyone.
    /// </para>
    /// <para>
    /// What survives all of that is what <see cref="AgentSession.TurnCount"/> defines:
    /// an exchange the person initiated. Two of them here — the typed prompt, and the
    /// one with a pasted image beside its text.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_transcript_counts_the_prompts_the_person_sent_and_nothing_else()
    {
        GivenClaudeTranscriptOf(
            "D--Repos-Backlog",
            "counted",
            Noon.AddHours(-1),
            """{"type":"queue-operation","operation":"enqueue","content":"a queued prompt"}""",
            """{"parentUuid":null,"isSidechain":false,"type":"user","cwd":"D:\\Repos\\Backlog","gitBranch":"main","message":{"role":"user","content":"the first prompt"},"uuid":"1"}""",
            """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"a transcript line looks like {\"type\":\"user\",\"message\":{}} and that is why"}]}}""",
            """{"type":"user","isSidechain":false,"toolUseResult":{"files":2},"message":{"role":"user","content":[{"tool_use_id":"toolu_01","type":"tool_result","content":"src/one.cs"}]}}""",
            """{"type":"user","isSidechain":false,"toolUseResult":{"files":9},"message":{"role":"user","content":[{"tool_use_id":"toolu_02","type":"tool_result","content":"src/two.cs"}]}}""",
            """{"type":"attachment","attachment":{"type":"user","content":"an attached file, filed under a line that is not a turn"}}""",
            """{"type":"user","isMeta":true,"isSidechain":false,"message":{"role":"user","content":[{"type":"text","text":"Base directory for this skill: C:\\Users\\x"}]}}""",
            """{"type":"user","isSidechain":false,"message":{"role":"user","content":[{"type":"image","source":{"type":"base64","media_type":"image/png","data":"iVBOR"}},{"type":"text","text":"the second prompt"}]}}""",
            """{"type":"user","isSidechain":true,"message":{"role":"user","content":"a subagent's own task prompt, written by an agent"}}""");

        var session = Assert.Single((await ReadAsync()).Sessions);

        Assert.Equal(2, session.TurnCount);
    }

    /// <summary>
    /// A transcript the person never spoke in — a prompt queued and never sent, a
    /// session that opened and closed. Null rather than 0, because 0 is a count and a
    /// count is a claim: it would say someone sat here and said nothing, when what the
    /// file supports is that there is nothing here to count.
    /// </summary>
    [Fact]
    public async Task A_transcript_with_nothing_the_person_said_has_no_turn_count()
    {
        GivenClaudeTranscriptOf(
            "D--Repos-Backlog",
            "silent",
            Noon.AddHours(-2),
            """{"type":"queue-operation","operation":"enqueue","content":"never sent"}""",
            """{"type":"user","cwd":"D:\\Repos\\Backlog","gitBranch":"main"}""",
            """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"hello"}]}}""");

        var session = Assert.Single((await ReadAsync()).Sessions);

        Assert.Null(session.TurnCount);
    }

    /// <summary>
    /// A file of things that are not JSON at all. The count is null and the session is
    /// still a row: a transcript this reader cannot make sense of costs the facts it
    /// would have read out of it, and nothing else.
    /// </summary>
    [Fact]
    public async Task A_malformed_transcript_has_no_turn_count_and_is_still_a_session()
    {
        GivenClaudeTranscriptOf(
            "D--Repos-Backlog",
            "malformed",
            Noon.AddHours(-3),
            "not json at all",
            "{ truncated because the disk filled",
            "\"type\":\"user\" but not an object");

        var catalog = await ReadAsync();
        var session = Assert.Single(catalog.Sessions);

        Assert.Equal("malformed", session.Id);
        Assert.Null(session.TurnCount);
        Assert.Empty(catalog.Unreadable);
    }

    /// <summary>
    /// A transcript held open by the agent still appending to it. Counting turns reads
    /// the whole file where the folder needed only its head, so this reader now runs a
    /// race it used to run a fortieth of — and the answer has to stay the size of the
    /// problem. One row keeps its place with a null count, the other rows are
    /// untouched, and Claude is not named as an unreadable source, which is what would
    /// happen if the count were allowed to throw out of here.
    /// <para>
    /// A partial count is the wrong answer rather than a nearly-right one: a file
    /// interrupted at line nine of ninety reports a number lower than the truth with
    /// nothing on the row to say so, and a reader comparing two sessions would be
    /// comparing one real number against one artefact of a lost race.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_locked_transcript_has_no_turn_count_and_costs_no_other_row()
    {
        GivenClaudeTranscriptOf(
            "D--Repos-Backlog",
            "locked",
            Noon.AddHours(-1),
            """{"type":"user","cwd":"D:\\Repos\\Backlog","gitBranch":"main","message":{"role":"user","content":"the prompt nobody can read"}}""");

        GivenClaudeTranscriptOf(
            "D--Repos-Backlog",
            "readable",
            Noon.AddHours(-2),
            """{"type":"user","cwd":"D:\\Repos\\Backlog","gitBranch":"main","message":{"role":"user","content":"a prompt"}}""");

        await using var _ = new FileStream(
            Path.Combine(ClaudeHome, "projects", "D--Repos-Backlog", "locked.jsonl"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var catalog = await ReadAsync();

        Assert.Equal(2, catalog.Sessions.Count);
        Assert.Empty(catalog.Unreadable);
        Assert.Null(Assert.Single(catalog.Sessions, session => session.Id == "locked").TurnCount);
        Assert.Equal(1, Assert.Single(catalog.Sessions, session => session.Id == "readable").TurnCount);
    }

    /// <summary>
    /// What a session spawned is not a session. Claude files a transcript at
    /// <c>projects/&lt;slug&gt;/&lt;id&gt;.jsonl</c> and gives that session a folder of its
    /// own beside it for everything it ran: a sidechain transcript per subagent, and a
    /// <c>journal.jsonl</c> of started/result records per Workflow run.
    /// <para>
    /// The journal is the one that shows: it states no <c>cwd</c> and no
    /// <c>gitBranch</c> anywhere, so it surfaced as a row called <c>journal</c> with a
    /// blank folder and no branch. The sidechain beside it is the same defect and the
    /// harder one to see — it states the <em>spawning</em> session's id, cwd and branch,
    /// so it renders as a plausible session that never existed, and there were 1,135 of
    /// those against 460 real transcripts on the profile this was found on.
    /// </para>
    /// <para>
    /// So the fixture asserts through <see cref="SessionReading.Discovered"/> as well as
    /// through the rows. Discovered is how many sessions there were before the cap, and
    /// it is read off the same set — a rule that filtered only what gets rendered would
    /// leave the subtitle counting files that no row is allowed to show.
    /// </para>
    /// <para>
    /// Both spellings are here on purpose. A Workflow's subagent is filed under
    /// <c>subagents/workflows/wf_&lt;id&gt;/</c> and a directly spawned one under
    /// <c>subagents/</c>, and a rule that recognised only the deeper path would pass this
    /// test while leaving the commoner case in the list.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Files_a_session_spawned_are_not_sessions_of_their_own()
    {
        GivenClaudeTranscriptOf(
            "D--Repos-Backlog",
            "0d377e3f-9687-4e7b-9a64-15f7d8b19053",
            Noon.AddHours(-4),
            """{"type":"user","cwd":"D:\\Repos\\Backlog","gitBranch":"main","message":{"role":"user","content":"the prompt"}}""");

        // Written after the session's own transcript, so a walk that admits it puts it
        // at the top of the list rather than somewhere a lenient assertion could miss.
        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "0d377e3f-9687-4e7b-9a64-15f7d8b19053",
            "subagents/workflows/wf_b0b8ae23-828/journal.jsonl",
            Noon.AddHours(-1),
            """{"type":"started","key":"v2:d68dfac77245080a","agentId":"a16156d26373fd0e8"}""",
            """{"type":"result","key":"v2:d68dfac77245080a","agentId":"a16156d26373fd0e8","result":{"summary":"the finding"}}""");

        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "0d377e3f-9687-4e7b-9a64-15f7d8b19053",
            "subagents/workflows/wf_b0b8ae23-828/agent-a16156d26373fd0e8.jsonl",
            Noon.AddHours(-2),
            """{"parentUuid":null,"isSidechain":true,"agentId":"a16156d26373fd0e8","sessionId":"0d377e3f-9687-4e7b-9a64-15f7d8b19053","cwd":"D:\\Repos\\Backlog","gitBranch":"main","type":"user","message":{"role":"user","content":"the task prompt, written by an agent"}}""");

        GivenClaudeFileSpawnedBy(
            "D--Repos-Backlog",
            "0d377e3f-9687-4e7b-9a64-15f7d8b19053",
            "subagents/agent-ae40b7e4276d8b85d.jsonl",
            Noon.AddHours(-3),
            """{"parentUuid":null,"isSidechain":true,"agentId":"ae40b7e4276d8b85d","sessionId":"0d377e3f-9687-4e7b-9a64-15f7d8b19053","cwd":"D:\\Repos\\Backlog","gitBranch":"main","type":"user","message":{"role":"user","content":"another task prompt"}}""");

        var catalog = await ReadAsync();
        var session = Assert.Single(catalog.Sessions);

        Assert.Equal("0d377e3f-9687-4e7b-9a64-15f7d8b19053", session.Id);
        Assert.Equal(@"D:\Repos\Backlog", session.WorkingFolder);
        Assert.Equal(1, catalog.Discovered);
    }

    /// <summary>
    /// A running session's own file says nothing about how much has been said in it or
    /// which branch it is on, so both come from the transcript the dedupe has already
    /// matched to it. The row stays the live one — that file is still the better record
    /// of what the session is called and where it is — carrying the two facts only the
    /// transcript holds.
    /// <para>
    /// The branch is asserted here rather than left to the history tests, because a
    /// running session is the row a reader opened this surface for and it is the one
    /// whose branch was silently dropped: the transcript was already being opened for
    /// the count, and the branch it also answered went on the floor. It is not a
    /// cosmetic loss — <c>SessionRecordMapping.ToRecord</c> puts this field on the wire,
    /// so a null here is a null in every other machine's session log.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_live_session_takes_its_turn_count_and_branch_from_its_own_transcript()
    {
        GivenClaudeLiveSession("shared", @"D:\Repos\Backlog", "live one", Noon.AddHours(-1), Noon.AddMinutes(-3));

        GivenClaudeTranscriptOf(
            "D--Repos-Backlog",
            "shared",
            Noon.AddMinutes(-3),
            """{"type":"user","cwd":"D:\\Repos\\Backlog","gitBranch":"main","message":{"role":"user","content":"the first prompt"}}""",
            """{"type":"user","message":{"role":"user","content":[{"tool_use_id":"toolu_01","type":"tool_result","content":"a file"}]}}""",
            """{"type":"user","message":{"role":"user","content":"the second prompt"}}""");

        var catalog = await ReadAsync();
        var session = Assert.Single(catalog.Sessions);

        Assert.Equal(AgentSessionState.Running, session.State);
        Assert.Equal("live one", session.Title);
        Assert.Equal(2, session.TurnCount);
        Assert.Equal("main", session.Branch);

        // The folder still comes from the live file. Both files state one, and the live
        // file's is the current one: a session resumed somewhere else keeps the folder
        // its transcript's header stated, which is where it started rather than where
        // it is.
        Assert.Equal(@"D:\Repos\Backlog", session.WorkingFolder);

        // Still one session. Taking a fact off the transcript must not also list it.
        Assert.Equal(1, catalog.Discovered);
    }

    /// <summary>
    /// A session registered as live before it has written a transcript. Null on both
    /// counts, because the live file holds nothing to count and states no branch, and
    /// this reader does not answer a question it was not told the answer to.
    /// <para>
    /// The branch is the one worth asserting rather than assuming: a folder is right
    /// there on the live file and a leaf that looks like a worktree name is the obvious
    /// thing to reach for, but a branch derived from a path is a guess that arrives on
    /// another machine indistinguishable from a recorded one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_live_session_with_no_transcript_yet_has_no_turn_count_or_branch()
    {
        GivenClaudeLiveSession("fresh", @"D:\Repos\Backlog", "just started", Noon.AddMinutes(-1), Noon);

        var session = Assert.Single((await ReadAsync()).Sessions);

        Assert.Null(session.TurnCount);
        Assert.Null(session.Branch);
    }

    /// <summary>
    /// Copilot records no turn or message count anywhere this reader looks, so a
    /// Copilot row's count is absent — Copilot's silence rather than this reader's
    /// laziness, the same shape as the liveness marker it also does not write.
    /// <para>
    /// Asserted rather than left implicit, because "no count" is a fact about the
    /// source that a later reader will otherwise take for a gap to be filled in from
    /// timestamps or file sizes. Either of those would be an invented number wearing a
    /// real one's name.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_copilot_session_has_no_turn_count()
    {
        GivenCopilotSession(
            "0012e2c7-aa39-4e43-9e57-e74a0ab62517",
            folder: @"D:\Repos\Backlog",
            repository: "JSdotNet/Backlog",
            branch: "main",
            created: Noon.AddHours(-4),
            updated: Noon.AddMinutes(-5));

        var session = Assert.Single((await ReadAsync()).Sessions);

        Assert.Null(session.TurnCount);
    }

    /// <summary>
    /// Everything this source produces was read off this machine's own disk, so every
    /// row says <see cref="AgentSessionOrigin.Local"/> — the live file, the transcript
    /// and the Copilot descriptor alike. A row here saying otherwise would have come
    /// from a source that is not this one.
    /// </summary>
    [Fact]
    public async Task Every_session_this_source_produces_is_stamped_local()
    {
        GivenClaudeLiveSession("live", @"D:\Repos\Backlog", "running", Noon.AddHours(-1), Noon.AddMinutes(-2));
        GivenClaudeTranscript("D--Repos-Backlog", "past", @"D:\Repos\Backlog", "main", Noon.AddHours(-5));
        GivenCopilotSession("copilot", @"D:\Repos\Backlog", "JSdotNet/Backlog", "main", Noon.AddHours(-3), Noon.AddHours(-2));

        var catalog = await ReadAsync();

        Assert.Equal(3, catalog.Sessions.Count);
        Assert.All(catalog.Sessions, session => Assert.Equal(AgentSessionOrigin.Local, session.Origin));
    }

    private Task<AgentSessionCatalog> ReadAsync() =>
        new LocalAgentSessionSource(ClaudeHome, CopilotHome, MachineId, Machine, new FixedClock(Noon))
            .GetSessionsAsync();

    private void GivenClaudeLiveSession(
        string id,
        string folder,
        string name,
        DateTimeOffset startedAt,
        DateTimeOffset lastWrite,
        int? pid = null)
    {
        var sessions = Directory.CreateDirectory(Path.Combine(ClaudeHome, "sessions"));

        // The file is named after the process, not the session — which is the whole
        // reason two of them can claim one session id.
        var process = pid ?? Math.Abs(id.GetHashCode());
        var path = Path.Combine(sessions.FullName, $"{process}.json");

        // The shape Claude Code actually writes, fields and all: a reader tested
        // against a tidied-up version of a file is a reader tested against nothing.
        File.WriteAllText(path, $$"""
            {"pid":{{process}},"sessionId":"{{id}}","cwd":"{{folder.Replace(@"\", @"\\")}}","startedAt":{{startedAt.ToUnixTimeMilliseconds()}},"version":"2.1.229","kind":"interactive","entrypoint":"claude-desktop","name":"{{name}}","nameSource":"derived"}
            """);

        File.SetLastWriteTimeUtc(path, lastWrite.UtcDateTime);
    }

    private void GivenClaudeTranscript(
        string slug,
        string id,
        string folder,
        string branch,
        DateTimeOffset lastWrite)
    {
        var project = Directory.CreateDirectory(Path.Combine(ClaudeHome, "projects", slug));
        var path = Path.Combine(project.FullName, $"{id}.jsonl");

        File.WriteAllLines(path,
        [
            $$"""{"type":"queue-operation","operation":"enqueue","sessionId":"{{id}}","content":"a queued prompt"}""",
            $$"""{"type":"user","sessionId":"{{id}}","cwd":"{{folder.Replace(@"\", @"\\")}}","gitBranch":"{{branch}}"}"""
        ]);

        File.SetLastWriteTimeUtc(path, lastWrite.UtcDateTime);
    }

    /// <summary>
    /// A transcript of exactly the lines a test hands it, for the tests that are about
    /// what is in the file rather than where it sits. The two-line fixture above stays
    /// as it is: it is the shape most of these tests need, and rewriting them in terms
    /// of this one would make every test that only cares about a folder carry a
    /// transcript's worth of JSON to say so.
    /// </summary>
    private void GivenClaudeTranscriptOf(
        string slug,
        string id,
        DateTimeOffset lastWrite,
        params string[] lines)
    {
        var project = Directory.CreateDirectory(Path.Combine(ClaudeHome, "projects", slug));
        var path = Path.Combine(project.FullName, $"{id}.jsonl");

        File.WriteAllLines(path, lines);
        File.SetLastWriteTimeUtc(path, lastWrite.UtcDateTime);
    }

    /// <summary>
    /// A file Claude wrote underneath a session's own folder rather than beside it. The
    /// path is given whole, because what this fixture is about is exactly where the file
    /// sits — naming the pieces would let a helper decide the shape the test is asserting
    /// on.
    /// </summary>
    private void GivenClaudeFileSpawnedBy(
        string slug,
        string sessionId,
        string relativePath,
        DateTimeOffset lastWrite,
        params string[] lines)
    {
        var path = Path.Combine(
            ClaudeHome,
            "projects",
            slug,
            sessionId,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        File.SetLastWriteTimeUtc(path, lastWrite.UtcDateTime);
    }

    private void GivenCopilotSession(
        string id,
        string folder,
        string repository,
        string branch,
        DateTimeOffset created,
        DateTimeOffset updated,
        DateTimeOffset? descriptorWritten = null)
    {
        var session = Directory.CreateDirectory(Path.Combine(CopilotHome, "session-state", id));

        File.WriteAllText(Path.Combine(session.FullName, "workspace.yaml"), $"""
            id: {id}
            cwd: {folder}
            git_root: {folder}
            repository: {repository}
            host_type: github
            branch: {branch}
            client_name: github/autopilot
            user_named: false
            summary_count: 0
            created_at: {created.ToString("O", CultureInfo.InvariantCulture)}
            updated_at: {updated.ToString("O", CultureInfo.InvariantCulture)}
            """);

        // Which descriptors are read is decided by the file's own timestamp, because
        // that is knowable without opening it. Separable from updated_at so the cap
        // and the reported activity can be tested independently.
        if (descriptorWritten is { } written)
        {
            File.SetLastWriteTimeUtc(Path.Combine(session.FullName, "workspace.yaml"), written.UtcDateTime);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A clock that does not move, so "stalled" has a boundary a test can
    /// stand either side of.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
