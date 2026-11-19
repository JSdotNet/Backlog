using Backlog.Modules.Sessions.Abstractions;
using Backlog.Tests;

using Microsoft.Extensions.Time.Testing;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// <c>report_schedule_run</c> (local ADR 0051 §1): a scheduled run's report is recorded in
/// the Schedule Register and answered with the schedule's state and the run's, Handled
/// read from the desktop record's archived flag.
/// </summary>
public class ScheduleToolsTests
{
    private static readonly DateTimeOffset Due = new(2026, 11, 11, 5, 30, 0, TimeSpan.Zero);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static ScheduleTools Tools(InMemoryScheduleRegister register, FakeTimeProvider clock, params AgentSession[] sessions) =>
        new(register, new FakeAgentSessionSource(new AgentSessionCatalog(sessions, [], sessions.Length)), clock) { Zone = TimeZoneInfo.Utc };

    private static AgentSession Session(string id, bool archived) =>
        new(id, AgentSessionKind.Claude, "dev", "DEV", id, @"D:\Repos\Backlog", null, null, null, Due, AgentSessionState.Finished, null, AgentSessionOrigin.Local)
        {
            Archived = archived,
        };

    [Fact]
    public async Task A_start_enters_the_schedule_and_answers_no_summary_yet()
    {
        var register = new InMemoryScheduleRegister();
        var tools = Tools(register, new FakeTimeProvider(Due));

        var answer = await tools.ReportScheduleRunAsync(
            "weekly-update", "run-1", "started", "Weekly update", "JSdotNet/Backlog", "30 5 * * 3", cancellationToken: Cancel);

        Assert.Equal("weekly-update", answer.ScheduleId);
        Assert.Equal("Weekly update", answer.Title);
        Assert.Equal("JSdotNet/Backlog", answer.Repository);
        Assert.Equal("30 5 * * 3", answer.Cron);
        Assert.Equal("no-summary-yet", answer.State);
        Assert.Equal("no-summary-yet", answer.RunState);
        Assert.Equal(Due.AddDays(7), answer.DueAt);
        Assert.Equal(Due.AddDays(7), answer.NextRunAt);
        Assert.Equal(1, answer.Runs);
        Assert.Equal(1, register.Recorded);
    }

    [Fact]
    public async Task A_finish_with_needs_you_lines_answers_needs_you()
    {
        var clock = new FakeTimeProvider(Due);
        var tools = Tools(new InMemoryScheduleRegister(), clock);
        await tools.ReportScheduleRunAsync("weekly-update", "run-1", "started", cancellationToken: Cancel);
        clock.Advance(TimeSpan.FromMinutes(20));

        var answer = await tools.ReportScheduleRunAsync(
            "weekly-update", "run-1", "FINISHED", verdict: "needs-you", needsYou: ["Review two pull requests"], cancellationToken: Cancel);

        Assert.Equal("needs-you", answer.State);
        Assert.Equal("needs-you", answer.RunState);
    }

    [Fact]
    public async Task A_skip_answers_held_and_a_failure_failed()
    {
        var tools = Tools(new InMemoryScheduleRegister(), new FakeTimeProvider(Due));

        Assert.Equal("held", (await tools.ReportScheduleRunAsync("sweep", "monday", "skipped", heldBy: "friday", cancellationToken: Cancel)).State);
        Assert.Equal("failed", (await tools.ReportScheduleRunAsync("update", "run-9", "failed", verdict: "No disk", cancellationToken: Cancel)).State);
    }

    [Fact]
    public async Task A_skip_naming_the_holder_by_its_desktop_id_is_held_by_the_run_that_reported_under_it()
    {
        var register = new InMemoryScheduleRegister();
        var friday = Session("friday-transcript", archived: false) with { DesktopSessionId = "local_friday" };
        var tools = Tools(register, new FakeTimeProvider(Due), friday);
        await tools.ReportScheduleRunAsync("sweep", "friday-transcript", "finished", needsYou: ["Validate #5"], cancellationToken: Cancel);

        await tools.ReportScheduleRunAsync("sweep", "monday", "skipped", heldBy: "local_friday", cancellationToken: Cancel);
        await tools.ReportScheduleRunAsync("sweep", "tuesday", "skipped", heldBy: "friday-transcript", cancellationToken: Cancel);
        await tools.ReportScheduleRunAsync("sweep", "wednesday", "skipped", heldBy: "local_unknown", cancellationToken: Cancel);

        var runs = (await register.ListAsync(Cancel)).Single().Runs.ToDictionary(run => run.SessionId);
        Assert.Equal("friday-transcript", runs["monday"].HeldBy);
        Assert.Equal("friday-transcript", runs["tuesday"].HeldBy);
        Assert.Equal("local_unknown", runs["wednesday"].HeldBy);
    }

    [Fact]
    public async Task A_skip_is_recorded_with_its_holder_as_sent_when_the_session_list_cannot_be_read()
    {
        var register = new InMemoryScheduleRegister();
        var tools = new ScheduleTools(register, new FailingSessions(), new FakeTimeProvider(Due)) { Zone = TimeZoneInfo.Utc };

        var answer = await tools.ReportScheduleRunAsync("sweep", "monday", "skipped", heldBy: "local_friday", cancellationToken: Cancel);

        Assert.Equal("held", answer.State);
        Assert.Equal("local_friday", (await register.ListAsync(Cancel)).Single().Runs.Single().HeldBy);
    }

    [Fact]
    public async Task A_run_whose_session_the_desktop_app_archived_answers_handled()
    {
        var tools = Tools(new InMemoryScheduleRegister(), new FakeTimeProvider(Due), Session("run-1", archived: true));

        var answer = await tools.ReportScheduleRunAsync("weekly-update", "run-1", "finished", needsYou: ["Review"], cancellationToken: Cancel);

        Assert.Equal("handled", answer.State);
    }

    [Fact]
    public async Task A_finish_that_asks_nothing_answers_review_ready()
    {
        var tools = Tools(new InMemoryScheduleRegister(), new FakeTimeProvider(Due));

        var answer = await tools.ReportScheduleRunAsync("weekly-update", "run-1", "finished", verdict: "review-ready", cancellationToken: Cancel);

        Assert.Equal("review-ready", answer.State);
    }

    [Fact]
    public async Task A_schedule_whose_next_run_never_started_answers_missed_on_the_next_report()
    {
        var clock = new FakeTimeProvider(Due.AddDays(-7));
        var tools = Tools(new InMemoryScheduleRegister(), clock);
        await tools.ReportScheduleRunAsync("weekly-update", "last-week", "started", cron: "30 5 * * 3", cancellationToken: Cancel);
        clock.SetUtcNow(Due.AddMinutes(45));

        // The late finish of last week's run, reported after this week's due time passed.
        var answer = await tools.ReportScheduleRunAsync("weekly-update", "last-week", "finished", cancellationToken: Cancel);

        Assert.Equal("missed", answer.State);
        Assert.Equal("review-ready", answer.RunState);
        Assert.Equal(Due, answer.DueAt);
    }

    [Fact]
    public async Task A_session_list_that_cannot_be_read_still_answers_the_recorded_report()
    {
        var register = new InMemoryScheduleRegister();
        var tools = new ScheduleTools(register, new FailingSessions(), new FakeTimeProvider(Due)) { Zone = TimeZoneInfo.Utc };

        var answer = await tools.ReportScheduleRunAsync("weekly-update", "run-1", "finished", needsYou: ["Review"], cancellationToken: Cancel);

        Assert.Equal("needs-you", answer.State);
        Assert.Equal(1, register.Recorded);
    }

    [Theory]
    [InlineData("begun")]
    [InlineData("")]
    public async Task An_event_outside_the_four_is_refused_and_records_nothing(string @event)
    {
        var register = new InMemoryScheduleRegister();
        var tools = Tools(register, new FakeTimeProvider(Due));

        var refusal = await Assert.ThrowsAsync<McpException>(() => tools.ReportScheduleRunAsync("weekly-update", "run-1", @event, cancellationToken: Cancel));

        Assert.Contains("started, skipped, finished or failed", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(0, register.Recorded);
    }

    [Fact]
    public async Task A_report_without_its_ids_is_refused_and_records_nothing()
    {
        var register = new InMemoryScheduleRegister();
        var tools = Tools(register, new FakeTimeProvider(Due));

        var refusal = await Assert.ThrowsAsync<McpException>(() => tools.ReportScheduleRunAsync(" ", "run-1", "started", cancellationToken: Cancel));

        Assert.Contains("scheduleId is required.", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(0, register.Recorded);
    }

    private sealed class FailingSessions : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            throw new IOException("The session store is locked.");

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            throw new IOException("The session store is locked.");
    }
}
