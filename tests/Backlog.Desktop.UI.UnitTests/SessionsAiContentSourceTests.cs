using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI;
using Backlog.SharedKernel.Ai;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Sessions context's Ask AI content: the catalog, one line per session,
/// and nothing that was said in any of them.
/// </summary>
public class SessionsAiContentSourceTests
{
    private static readonly DateTimeOffset Started = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_record_is_the_session_facts_most_recent_first()
    {
        var source = new SessionsAiContentSource(new FixedSessions(
        [
            Session("Older", Started.AddDays(-1), Started.AddDays(-1).AddMinutes(20), AgentSessionState.Finished, turns: 5),
            Session("Newer", Started, Started.AddHours(2).AddMinutes(5), AgentSessionState.Finished, turns: 1)
        ]));

        var content = await source.ComposeAsync(new AiContentRequest("nothing shared", 6000), TestContext.Current.CancellationToken);

        Assert.Equal("sessions", content.AreaKey);
        Assert.Equal(
            "Sessions: 2 entries.\n"
            + "Session: Newer — Claude, finished, repository JSdotNet/Backlog, branch claude/ask-ai, folder D:\\Repos\\Backlog, started 2026-09-21 09:00, lasted 2h 5m, last active 2026-09-21 11:05, 1 turn, on DEV-TOWER\n"
            + "---\n"
            + "Session: Older — Claude, finished, repository JSdotNet/Backlog, branch claude/ask-ai, folder D:\\Repos\\Backlog, started 2026-09-20 09:00, lasted 20m, last active 2026-09-20 09:20, 5 turns, on DEV-TOWER",
            content.Body);
    }

    /// <summary>The readers cap what they return; the total on the first line is
    /// what they found, so "200 of 842" is the truth about the catalog. Files
    /// they could not open are said on the line under it.</summary>
    [Fact]
    public async Task The_total_is_what_was_discovered_and_unreadable_files_are_said()
    {
        var source = new SessionsAiContentSource(new FixedSessions(
            [Session("Only one listed", Started, Started.AddMinutes(1), AgentSessionState.Finished, turns: 1)],
            discovered: 842,
            unreadable: ["broken.jsonl"]));

        var content = await source.ComposeAsync(new AiContentRequest("nothing shared", 6000), TestContext.Current.CancellationToken);

        Assert.StartsWith(
            "Sessions: 1 of 842 entries, selected by relevance to the question.\n1 session file could not be read.\nSession: Only one listed",
            content.Body,
            StringComparison.Ordinal);
        Assert.Equal(842, content.Total);
        Assert.True(content.Trimmed);
    }

    [Fact]
    public async Task A_running_session_has_no_duration_yet()
    {
        var source = new SessionsAiContentSource(new FixedSessions(
            [Session("Live", Started, Started.AddMinutes(3), AgentSessionState.Running, turns: null)]));

        var content = await source.ComposeAsync(new AiContentRequest("live", 6000), TestContext.Current.CancellationToken);

        Assert.Contains("Session: Live — Claude, running,", content.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("lasted", content.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("turn", content.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The source holds the catalog port and nothing else, and the catalog carries
    /// no transcript text. Pinned by construction: the only way a transcript line
    /// could reach the body is through a field the record does not have.
    /// </summary>
    [Fact]
    public void The_source_reads_the_catalog_port_only_and_never_the_transcripts()
    {
        var dependencies = typeof(SessionsAiContentSource)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToList();

        Assert.Equal([typeof(IAgentSessionSource)], dependencies);
        Assert.DoesNotContain(typeof(IAgentActivitySource), dependencies);
        Assert.DoesNotContain(typeof(ITranscriptFactsCache), dependencies);
    }

    private static AgentSession Session(string title, DateTimeOffset startedAt, DateTimeOffset lastActivityAt, AgentSessionState state, int? turns) =>
        new(
            Guid.NewGuid().ToString("n"),
            AgentSessionKind.Claude,
            "machine-1",
            "DEV-TOWER",
            title,
            @"D:\Repos\Backlog",
            "JSdotNet/Backlog",
            "claude/ask-ai",
            startedAt,
            lastActivityAt,
            state,
            turns,
            AgentSessionOrigin.Local);

    private sealed class FixedSessions(IReadOnlyList<AgentSession> sessions, int? discovered = null, IReadOnlyList<string>? unreadable = null) : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionCatalog(sessions, unreadable ?? [], discovered ?? sessions.Count));
    }
}
