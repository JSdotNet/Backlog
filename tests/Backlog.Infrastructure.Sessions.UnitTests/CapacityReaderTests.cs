using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// The app measuring a Capacity Reading from its own stores (local ADR 0051 §4): every
/// figure it can read is passed through, and a store that is missing or fails leaves only
/// its own figure unavailable.
/// </summary>
public sealed class CapacityReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private const string Manager = "manager-1";

    private readonly FakeTimeProvider _clock = new(Now);

    private readonly HeldRequests _requests = new(
    [
        Request("local_a", Manager),
        Request("local_b", "manager-2")
    ]);

    private readonly HeldSessions _sessions = new(
    [
        Session("local_a", AgentSessionState.Running),
        Session("local_b", AgentSessionState.Stalled)
    ]);

    // The all-models refusal 19 days ago sized the weekly limit at $10; this week has
    // spent $4 of it, so plan usage reads 40 %.
    private readonly HeldApiRequests _apiRequests = new(
    [
        ApiRequest("calibration", Now.AddDays(-20), 10_000_000),
        ApiRequest("this-week", Now.AddHours(-1), 4_000_000)
    ]);

    private readonly HeldRecords _records = new([Record(new AgentLimitHit(Now.AddDays(-19), AgentLimitKind.Weekly, null) { ResetsAt = Now.AddDays(-18) })]);

    private readonly HeldLoad _load = new(new MachineLoad(42.5, 7.25));

    private static SessionRequest Request(string sessionId, string claimedBy) =>
        new(Guid.NewGuid(), SessionRequestKind.Entry, Guid.NewGuid(), null, "owner/repo", @"D:\repo", SessionRequestState.Started, claimedBy, null, sessionId, Now.AddHours(-2));

    private static AgentSession Session(string desktopId, AgentSessionState state) =>
        new($"id-{desktopId}", AgentSessionKind.Claude, "env", "This PC", "A session", @"D:\repo", "owner/repo", "main", Now.AddHours(-2), Now, state, null, AgentSessionOrigin.Local)
        {
            DesktopSessionId = desktopId
        };

    private static ClaudeApiRequest ApiRequest(string id, DateTimeOffset at, long micros) =>
        new(id, "s", at, "claude-opus-5-5", "high", micros, 10, 100, 1000, 100, 500, "repl_main_thread", null, null, null);

    private static AgentSessionRecord Record(AgentLimitHit hit) =>
        new(
            Session("local_old", AgentSessionState.Finished),
            new AgentSessionActivity("id-local_old", AgentSessionKind.Claude, "env", "This PC", [], []) { LimitHits = [hit] },
            Now.AddDays(-19));

    private CapacityReader Reader(
        bool requests = true,
        bool apiRequests = true,
        bool records = true,
        bool load = true) =>
        new(
            _sessions,
            requests ? _requests : null,
            apiRequests ? _apiRequests : null,
            records ? _records : null,
            load ? _load : null,
            _clock);

    [Fact]
    public async Task Every_figure_the_stores_answer_is_passed_through()
    {
        var reading = await Reader().ReadAsync(CapacityLimits.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, reading.WorkingSessions);
        Assert.Equal(40, reading.PlanUsagePercent!.Value, 6);
        Assert.Equal(42.5, reading.CpuPercent);
        Assert.Equal(7.25, reading.FreeMemoryGb);
        Assert.Equal(CapacityLimits.Default, reading.Limits);
    }

    [Fact]
    public async Task Given_a_dispatcher_only_its_sessions_count()
    {
        var reading = await Reader().ReadAsync(CapacityLimits.Default, Manager, TestContext.Current.CancellationToken);

        Assert.Equal(1, reading.WorkingSessions);
    }

    [Fact]
    public async Task Without_a_request_store_only_the_working_figure_is_unavailable()
    {
        var reading = await Reader(requests: false).ReadAsync(CapacityLimits.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(reading.WorkingSessions);
        Assert.NotNull(reading.PlanUsagePercent);
        Assert.NotNull(reading.CpuPercent);
    }

    [Fact]
    public async Task Without_an_api_request_store_only_plan_usage_is_unavailable()
    {
        var reading = await Reader(apiRequests: false).ReadAsync(CapacityLimits.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(reading.PlanUsagePercent);
        Assert.Equal(2, reading.WorkingSessions);
        Assert.NotNull(reading.FreeMemoryGb);
    }

    [Fact]
    public async Task Without_session_records_no_refusal_sizes_a_limit_so_plan_usage_is_unavailable()
    {
        var reading = await Reader(records: false).ReadAsync(CapacityLimits.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(reading.PlanUsagePercent);
        Assert.Equal(2, reading.WorkingSessions);
    }

    [Fact]
    public async Task Without_a_machine_load_cpu_and_memory_are_unavailable()
    {
        var reading = await Reader(load: false).ReadAsync(CapacityLimits.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(reading.CpuPercent);
        Assert.Null(reading.FreeMemoryGb);
        Assert.Equal(2, reading.WorkingSessions);
        Assert.NotNull(reading.PlanUsagePercent);
    }

    [Fact]
    public async Task A_failing_request_store_leaves_only_the_working_figure_unavailable()
    {
        _requests.Fails = true;

        var reading = await Reader().ReadAsync(CapacityLimits.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(reading.WorkingSessions);
        Assert.NotNull(reading.PlanUsagePercent);
        Assert.NotNull(reading.CpuPercent);
    }

    [Fact]
    public async Task A_failing_session_source_leaves_only_the_working_figure_unavailable()
    {
        _sessions.Fails = true;

        var reading = await Reader().ReadAsync(CapacityLimits.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(reading.WorkingSessions);
        Assert.NotNull(reading.PlanUsagePercent);
    }

    [Fact]
    public async Task A_failing_api_request_store_leaves_only_plan_usage_unavailable()
    {
        _apiRequests.Fails = true;

        var reading = await Reader().ReadAsync(CapacityLimits.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(reading.PlanUsagePercent);
        Assert.Equal(2, reading.WorkingSessions);
    }

    [Fact]
    public async Task Failing_session_records_lose_only_the_refusals()
    {
        _records.Fails = true;

        var reading = await Reader().ReadAsync(CapacityLimits.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(reading.PlanUsagePercent);
        Assert.Equal(2, reading.WorkingSessions);
    }

    [Fact]
    public async Task A_failing_machine_load_leaves_cpu_and_memory_unavailable()
    {
        _load.Fails = true;

        var reading = await Reader().ReadAsync(CapacityLimits.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(reading.CpuPercent);
        Assert.Null(reading.FreeMemoryGb);
        Assert.Equal(2, reading.WorkingSessions);
    }

    [Fact]
    public async Task A_cancelled_read_throws_rather_than_answering_unavailable()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reader().ReadAsync(CapacityLimits.Default, cancellationToken: cancelled.Token));
    }

    [Fact]
    public async Task A_host_that_registers_no_machine_load_reads_cpu_and_memory_unavailable()
    {
        var services = new ServiceCollection();
        services.AddAgentSessionSource();
        // The relay-applied port stands in for the disk readers, so nothing reads the profile.
        services.AddSingleton<IAgentSessionSource>(_sessions);
        services.AddSingleton<ISessionRequestStore>(_requests);

        await using var provider = services.BuildServiceProvider();

        var reading = await provider.GetRequiredService<ICapacityReader>()
            .ReadAsync(CapacityLimits.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(reading.CpuPercent);
        Assert.Null(reading.FreeMemoryGb);
        Assert.Null(reading.PlanUsagePercent);
        Assert.Equal(2, reading.WorkingSessions);
    }

    private sealed class HeldRequests(IReadOnlyList<SessionRequest> held) : ISessionRequestStore
    {
        public bool Fails { get; set; }

        public Task<IReadOnlyList<SessionRequest>> ListAsync(SessionRequestState? state = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Fails ? throw new IOException("The request store could not be read.") : Task.FromResult(held);
        }

        public Task<SessionRequest> RequestAsync(SessionRequest candidate, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SessionRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SessionRequest?> NextAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SessionRequestOutcome> ClaimAsync(Guid id, string dispatcher, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SessionRequestOutcome> ReleaseAsync(Guid id, string dispatcher, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SessionRequestOutcome> ReportStartedAsync(Guid id, string dispatcher, string sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SessionRequestOutcome> CancelAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<SessionRequest>> LinkAsync(Guid entryId, IReadOnlyCollection<string> planTags, string sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class HeldSessions(IReadOnlyList<AgentSession> held) : IAgentSessionSource
    {
        public bool Fails { get; set; }

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            GetSessionsAsync(AgentSessionQuery.Newest, cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Fails
                ? throw new IOException("The sessions could not be read.")
                : Task.FromResult(new AgentSessionCatalog(held, [], held.Count));
        }
    }

    private sealed class HeldApiRequests(IReadOnlyList<ClaudeApiRequest> held) : IClaudeApiRequestStore
    {
        public bool Fails { get; set; }

        public Task<int> AddAsync(IReadOnlyList<ClaudeApiRequest> requests, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ClaudeApiRequest>> ListAsync(string? sessionId = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Fails ? throw new IOException("The request telemetry could not be read.") : Task.FromResult(held);
        }
    }

    private sealed class HeldRecords(IReadOnlyList<AgentSessionRecord> held) : IAgentSessionRecordStore
    {
        public bool Fails { get; set; }

        public IReadOnlyList<AgentSessionRecord> All() => Fails ? throw new IOException("The records could not be read.") : held;

        public SessionRecordUpdate Save(IReadOnlyList<AgentSessionRecord> readings) => throw new NotSupportedException();
    }

    private sealed class HeldLoad(MachineLoad load) : IMachineLoad
    {
        public bool Fails { get; set; }

        public Task<MachineLoad> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A broken implementation; the contract says it never throws.
            return Fails ? throw new InvalidOperationException("The load could not be read.") : Task.FromResult(load);
        }
    }
}
