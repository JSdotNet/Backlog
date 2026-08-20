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
        ]);

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

        await File.WriteAllTextAsync(Path.Combine(folder.FullName, "half-written.json"), "{\"pid\":123,\"sess");
        await File.WriteAllTextAsync(Path.Combine(folder.FullName, "not-a-session.json"), "[]");
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

    private Task<AgentSessionCatalog> ReadAsync() =>
        new LocalAgentSessionSource(ClaudeHome, CopilotHome, Machine, new FixedClock(Noon))
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
