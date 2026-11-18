using Backlog.Infrastructure.Sqlite.Sessions;
using Backlog.Modules.Sessions.Abstractions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The Schedule Register in the workspace's <c>backlog.db</c> (local ADR 0051 §1): a
/// schedule enters with its first report, a run's reports land on one row, and both come
/// back as they were written, across a reopen.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteScheduleRegisterTests : IDisposable
{
    private static readonly DateTimeOffset Due = new(2026, 11, 11, 5, 30, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-sqlite-schedules", Guid.NewGuid().ToString("n"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SqliteScheduleRegister Register() => new(_root);

    private static ScheduledRunReport Report(ScheduledRunEvent @event, string session = "run-1", string schedule = "weekly-update") =>
        new(schedule, "Weekly update", "JSdotNet/Backlog", "30 5 * * 3", session, @event);

    [Fact]
    public async Task A_first_report_enters_the_schedule_and_its_run_in_the_workspace_database()
    {
        var register = Register();

        var record = await register.RecordAsync(Report(ScheduledRunEvent.Started), Due, Ct);

        Assert.Equal(new Schedule("weekly-update", "Weekly update", "JSdotNet/Backlog", "30 5 * * 3", Due, Due), record.Schedule);
        var run = Assert.Single(record.Runs);
        Assert.Equal(Due, run.StartedAt);
        Assert.Null(run.FinishedAt);
        Assert.Equal(Path.Combine(_root, "backlog.db"), register.DatabasePath);
    }

    [Fact]
    public async Task A_runs_reports_land_on_one_row_and_read_back_whole_after_a_reopen()
    {
        await Register().RecordAsync(Report(ScheduledRunEvent.Started), Due, Ct);
        await Register().RecordAsync(Report(ScheduledRunEvent.Skipped) with { HeldBy = "friday" }, Due.AddMinutes(1), Ct);
        await Register().RecordAsync(
            Report(ScheduledRunEvent.Finished) with { Verdict = "needs-you", NeedsYou = ["Review two pull requests", "Follow up on two issues"] },
            Due.AddMinutes(20),
            Ct);

        var listed = Assert.Single(await Register().ListAsync(Ct));

        Assert.Equal(
            new ScheduledRun("run-1", "weekly-update", Due, Due, Due.AddMinutes(20), "needs-you",
                ["Review two pull requests", "Follow up on two issues"], Failed: false, Skipped: true, HeldBy: "friday"),
            Assert.Single(listed.Runs));
        Assert.Equal(Due.AddMinutes(20), listed.Schedule.LastReportedAt);
    }

    [Fact]
    public async Task A_failure_is_kept_with_its_error()
    {
        var record = await Register().RecordAsync(Report(ScheduledRunEvent.Failed) with { Verdict = "No disk" }, Due, Ct);

        var run = Assert.Single(record.Runs);
        Assert.True(run.Failed);
        Assert.Equal("No disk", run.Verdict);
        Assert.Equal(Due, run.FinishedAt);
        Assert.Null(run.StartedAt);
    }

    [Fact]
    public async Task Each_schedule_answers_its_own_runs()
    {
        var register = Register();
        await register.RecordAsync(Report(ScheduledRunEvent.Started, "a-1", "alpha"), Due, Ct);
        await register.RecordAsync(Report(ScheduledRunEvent.Started, "a-2", "alpha"), Due.AddDays(1), Ct);
        var beta = await register.RecordAsync(Report(ScheduledRunEvent.Started, "b-1", "beta"), Due, Ct);

        Assert.Equal(["b-1"], beta.Runs.Select(run => run.SessionId));

        var listed = await register.ListAsync(Ct);
        Assert.Equal(["alpha", "beta"], listed.Select(record => record.Schedule.Id));
        Assert.Equal(["a-1", "a-2"], listed[0].Runs.Select(run => run.SessionId));
    }

    [Fact]
    public async Task A_run_keeps_the_schedule_it_first_reported_under()
    {
        var register = Register();
        await register.RecordAsync(Report(ScheduledRunEvent.Started, "run-1", "alpha"), Due, Ct);

        var record = await register.RecordAsync(
            Report(ScheduledRunEvent.Finished, "run-1", "beta") with { Title = "Beta", Cron = "0 * * * *" },
            Due.AddMinutes(5),
            Ct);

        Assert.Equal("alpha", record.Schedule.Id);
        Assert.Equal("Weekly update", record.Schedule.Title);
        Assert.Equal("30 5 * * 3", record.Schedule.Cron);
        Assert.Equal(["alpha"], (await register.ListAsync(Ct)).Select(listed => listed.Schedule.Id));
    }

    [Fact]
    public async Task The_registration_follows_the_workspace_root()
    {
        var services = new ServiceCollection();
        services.AddSqlite(_ => _root);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IScheduleRegister));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.IsType<RootedSqliteScheduleRegister>(provider.GetRequiredService<IScheduleRegister>());

        var register = provider.GetRequiredService<IScheduleRegister>();
        await register.RecordAsync(Report(ScheduledRunEvent.Started), Due, Ct);

        Assert.Single(await Register().ListAsync(Ct));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
