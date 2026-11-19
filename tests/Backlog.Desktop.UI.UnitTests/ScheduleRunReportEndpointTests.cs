using System.Text;
using System.Text.Json;

using Backlog.Desktop.UI.Mcp;
using Backlog.Desktop.UI.Shell;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;
using Backlog.Tests;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The route a scheduled run's hook posts to (local ADR 0051 §1): <c>200</c> with the
/// schedule's state, <c>400</c> for a body that is not a report, <c>413</c> for one too
/// large, <c>503</c> when the register cannot be written, and <c>404</c> recording nothing
/// while <c>sessions</c> is off.
/// </summary>
public class ScheduleRunReportEndpointTests
{
    private static readonly DateTimeOffset Due = new(2026, 11, 11, 5, 30, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<ScheduleRunReportEndpoint.Reply> PostAsync(
        string json,
        IScheduleRegister register,
        bool sessionsOn = true,
        long? contentLength = null)
    {
        var bytes = Encoding.UTF8.GetBytes(json);

        return ScheduleRunReportEndpoint.AcceptAsync(
            new MemoryStream(bytes),
            contentLength ?? bytes.Length,
            register,
            new SessionsFlag(sessionsOn),
            sessions: null,
            new FakeTimeProvider(Due),
            Ct,
            TimeZoneInfo.Utc);
    }

    [Fact]
    public void The_route_is_schedule_runs()
    {
        Assert.Equal("/schedule-runs", ScheduleRunReportEndpoint.RoutePath);
    }

    [Fact]
    public async Task A_start_report_is_recorded_and_answered_with_the_schedules_state()
    {
        var register = new InMemoryScheduleRegister();

        var reply = await PostAsync(
            """{"scheduleId":"weekly-update","sessionId":"run-1","event":"started","title":"Weekly update","repository":"JSdotNet/Backlog","cron":"30 5 * * 3"}""",
            register);

        Assert.Equal(200, reply.StatusCode);
        Assert.Equal(1, register.Recorded);

        using var json = JsonDocument.Parse(reply.Body);
        Assert.Equal("weekly-update", json.RootElement.GetProperty("scheduleId").GetString());
        Assert.Equal("Weekly update", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("no-summary-yet", json.RootElement.GetProperty("state").GetString());
        Assert.Equal("no-summary-yet", json.RootElement.GetProperty("runState").GetString());

        var schedule = Assert.Single(await register.ListAsync(Ct)).Schedule;
        Assert.Equal("30 5 * * 3", schedule.Cron);
        Assert.Equal("JSdotNet/Backlog", schedule.Repository);
    }

    [Fact]
    public async Task A_finish_carries_its_verdict_and_needs_you_lines()
    {
        var register = new InMemoryScheduleRegister();

        var reply = await PostAsync(
            """{"scheduleId":"weekly-update","sessionId":"run-1","event":"finished","verdict":"needs-you","needsYou":["Review two pull requests","Follow up on two issues"]}""",
            register);

        Assert.Equal(200, reply.StatusCode);
        using var json = JsonDocument.Parse(reply.Body);
        Assert.Equal("needs-you", json.RootElement.GetProperty("state").GetString());

        var run = Assert.Single(Assert.Single(await register.ListAsync(Ct)).Runs);
        Assert.Equal(["Review two pull requests", "Follow up on two issues"], run.NeedsYou);
        Assert.Equal("needs-you", run.Verdict);
    }

    [Fact]
    public async Task A_skip_names_the_run_that_held_it()
    {
        var register = new InMemoryScheduleRegister();

        var reply = await PostAsync("""{"scheduleId":"sweep","sessionId":"monday","event":"skipped","heldBy":"friday"}""", register);

        using var json = JsonDocument.Parse(reply.Body);
        Assert.Equal("held", json.RootElement.GetProperty("state").GetString());
        Assert.Equal("friday", Assert.Single(Assert.Single(await register.ListAsync(Ct)).Runs).HeldBy);
    }

    [Fact]
    public async Task While_sessions_is_off_it_answers_not_found_and_records_nothing()
    {
        var register = new InMemoryScheduleRegister();

        var reply = await PostAsync("""{"scheduleId":"weekly-update","sessionId":"run-1","event":"started"}""", register, sessionsOn: false);

        Assert.Equal(404, reply.StatusCode);
        Assert.Equal(0, register.Recorded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{"scheduleId":"weekly-update","sessionId":"run-1","event":"begun"}""")]
    [InlineData("""{"scheduleId":"weekly-update","event":"started"}""")]
    [InlineData("""{"sessionId":"run-1","event":"started"}""")]
    [InlineData("""{"scheduleId":"weekly-update","sessionId":"run-1","event":"started","needsYou":"one line"}""")]
    public async Task A_body_that_is_not_a_report_is_refused_and_records_nothing(string body)
    {
        var register = new InMemoryScheduleRegister();

        var reply = await PostAsync(body, register);

        Assert.Equal(400, reply.StatusCode);
        Assert.Equal(0, register.Recorded);
        using var json = JsonDocument.Parse(reply.Body);
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task A_body_larger_than_a_report_is_refused()
    {
        var register = new InMemoryScheduleRegister();
        var huge = "{\"scheduleId\":\"s\",\"sessionId\":\"r\",\"event\":\"finished\",\"verdict\":\"" + new string('x', ScheduleRunReportEndpoint.MaxBodyBytes) + "\"}";

        Assert.Equal(413, (await PostAsync(huge, register)).StatusCode);
        Assert.Equal(413, (await PostAsync(huge, register, contentLength: -1)).StatusCode);
        Assert.Equal(0, register.Recorded);
    }

    [Fact]
    public async Task A_register_that_cannot_be_written_asks_the_hook_to_retry()
    {
        var failure = new IOException("database is locked");
        var register = new InMemoryScheduleRegister { Failure = failure };

        var reply = await PostAsync("""{"scheduleId":"weekly-update","sessionId":"run-1","event":"started"}""", register);

        Assert.Equal(503, reply.StatusCode);
        Assert.Same(failure, reply.Failure);
        Assert.Equal(ScheduleRunReportEndpoint.RetryAfterSeconds, reply.RetryAfterSeconds);
    }

    /// <summary>Every key on but <c>sessions</c>, which the test sets.</summary>
    private sealed class SessionsFlag(bool on) : IAppFeatureSettings
    {
        public event Action? Changed;

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "(replaced for this test)";

        public bool IsEnabled(string key) => !string.Equals(key, SessionFeatures.Sessions, StringComparison.Ordinal) || on;

        public string? SetEnabled(string key, bool enabled)
        {
            Changed?.Invoke();
            return null;
        }
    }
}
