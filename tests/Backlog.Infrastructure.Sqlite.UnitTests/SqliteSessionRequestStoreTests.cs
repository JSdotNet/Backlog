using Backlog.Infrastructure.Sqlite.Sessions;
using Backlog.Modules.Sessions.Abstractions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The session request queue in <c>backlog.db</c> (local ADR 0037): a request comes
/// back as it went in, a second ask returns the open one, the queue hands out the
/// oldest first, a lapsed claim reads as Requested, and every transition is written.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteSessionRequestStoreTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-sqlite-session-requests", Guid.NewGuid().ToString("n"));
    private readonly ManualClock _clock = new(Noon);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SqliteSessionRequestStore Store() => new(_root, _clock);

    private static SessionRequest Entry(Guid? entryId = null, DateTimeOffset? at = null) =>
        SessionRequests.ForEntry(entryId ?? Guid.NewGuid(), "JSdotNet/Backlog", @"D:\Repos\Backlog", at ?? Noon);

    [Fact]
    public async Task A_queued_request_comes_back_whole()
    {
        var store = Store();
        var request = Entry();

        Assert.Same(request, await store.RequestAsync(request, Ct));

        Assert.Equal([request], await store.ListAsync(cancellationToken: Ct));
        Assert.Equal(request, await store.GetAsync(request.Id, Ct));
        Assert.Equal(Path.Combine(_root, "backlog.db"), store.DatabasePath);
    }

    [Fact]
    public async Task A_plan_request_comes_back_whole()
    {
        var store = Store();
        var request = SessionRequests.ForPlan("+session-dispatch", "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon);

        await store.RequestAsync(request, Ct);

        Assert.Equal(request, await store.GetAsync(request.Id, Ct));
    }

    [Fact]
    public async Task A_second_ask_for_an_entry_with_an_open_request_returns_the_open_one_and_stores_nothing()
    {
        var store = Store();
        var entryId = Guid.NewGuid();
        var first = await store.RequestAsync(Entry(entryId), Ct);

        var second = await store.RequestAsync(Entry(entryId, Noon.AddMinutes(1)), Ct);
        Assert.Equal(first, second);

        await store.ClaimAsync(first.Id, "manager-1", Ct);
        Assert.Equal(first.Id, (await store.RequestAsync(Entry(entryId), Ct)).Id);

        await store.ReportStartedAsync(first.Id, "manager-1", "session-a", Ct);
        Assert.Equal(first.Id, (await store.RequestAsync(Entry(entryId), Ct)).Id);

        Assert.Single(await store.ListAsync(cancellationToken: Ct));
    }

    [Fact]
    public async Task A_new_ask_after_a_Cancelled_or_Linked_request_queues_a_new_one()
    {
        var store = Store();
        var entryId = Guid.NewGuid();
        var cancelled = await store.RequestAsync(Entry(entryId), Ct);
        await store.CancelAsync(cancelled.Id, Ct);

        var next = await store.RequestAsync(Entry(entryId, Noon.AddMinutes(1)), Ct);

        Assert.NotEqual(cancelled.Id, next.Id);
        Assert.Equal(2, (await store.ListAsync(cancellationToken: Ct)).Count);
    }

    [Fact]
    public async Task The_next_request_is_the_oldest_Requested_one()
    {
        var store = Store();
        var newer = await store.RequestAsync(Entry(at: Noon.AddMinutes(-1)), Ct);
        var older = await store.RequestAsync(Entry(at: Noon.AddMinutes(-5)), Ct);

        Assert.Equal(older.Id, (await store.NextAsync(Ct))!.Id);

        await store.ClaimAsync(older.Id, "manager-1", Ct);

        Assert.Equal(newer.Id, (await store.NextAsync(Ct))!.Id);
        Assert.Equal([older.Id, newer.Id], (await store.ListAsync(cancellationToken: Ct)).Select(r => r.Id));
    }

    [Fact]
    public async Task With_nothing_Requested_there_is_no_next_request()
    {
        var store = Store();

        Assert.Null(await store.NextAsync(Ct));

        var request = await store.RequestAsync(Entry(), Ct);
        await store.ClaimAsync(request.Id, "manager-1", Ct);

        Assert.Null(await store.NextAsync(Ct));
    }

    [Fact]
    public async Task A_claim_is_written_and_a_second_claim_is_refused()
    {
        var store = Store();
        var request = await store.RequestAsync(Entry(), Ct);

        var first = await store.ClaimAsync(request.Id, "manager-1", Ct);
        var second = await store.ClaimAsync(request.Id, "manager-2", Ct);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);

        var stored = (await store.GetAsync(request.Id, Ct))!;
        Assert.Equal(SessionRequestState.Claimed, stored.State);
        Assert.Equal("manager-1", stored.ClaimedBy);
        Assert.Equal(Noon.AddMinutes(2), stored.LeaseEndsAt);
    }

    [Fact]
    public async Task Two_dispatchers_claiming_at_once_get_one_claim_between_them()
    {
        var request = await Store().RequestAsync(Entry(), Ct);

        var outcomes = await Task.WhenAll(
            Enumerable.Range(1, 6).Select(n => Task.Run(() => Store().ClaimAsync(request.Id, $"manager-{n}", Ct), Ct)));

        Assert.Single(outcomes, outcome => outcome.Succeeded);
    }

    [Fact]
    public async Task A_lapsed_claim_reads_as_Requested_and_is_handed_out_again()
    {
        var store = Store();
        var request = await store.RequestAsync(Entry(), Ct);
        await store.ClaimAsync(request.Id, "manager-1", Ct);

        Assert.Empty(await store.ListAsync(SessionRequestState.Requested, Ct));

        _clock.Now = Noon.AddMinutes(2);

        Assert.Equal(request.Id, (await store.NextAsync(Ct))!.Id);
        Assert.Equal([request.Id], (await store.ListAsync(SessionRequestState.Requested, Ct)).Select(r => r.Id));
        Assert.False((await store.ReportStartedAsync(request.Id, "manager-1", "session-a", Ct)).Succeeded);

        var reclaimed = await store.ClaimAsync(request.Id, "manager-2", Ct);
        Assert.True(reclaimed.Succeeded);
        Assert.Equal("manager-2", (await store.GetAsync(request.Id, Ct))!.ClaimedBy);
    }

    [Fact]
    public async Task A_released_claim_is_Requested_again()
    {
        var store = Store();
        var request = await store.RequestAsync(Entry(), Ct);
        await store.ClaimAsync(request.Id, "manager-1", Ct);

        Assert.True((await store.ReleaseAsync(request.Id, "manager-1", Ct)).Succeeded);

        var stored = (await store.GetAsync(request.Id, Ct))!;
        Assert.Equal(SessionRequestState.Requested, stored.State);
        Assert.Null(stored.ClaimedBy);
    }

    [Fact]
    public async Task A_report_writes_Started_with_the_session_id()
    {
        var store = Store();
        var request = await store.RequestAsync(Entry(), Ct);
        await store.ClaimAsync(request.Id, "manager-1", Ct);

        var outcome = await store.ReportStartedAsync(request.Id, "manager-1", "session-a", Ct);

        Assert.True(outcome.Succeeded);

        var stored = (await store.GetAsync(request.Id, Ct))!;
        Assert.Equal(SessionRequestState.Started, stored.State);
        Assert.Equal("session-a", stored.SessionId);
        Assert.Null(stored.LeaseEndsAt);
    }

    [Fact]
    public async Task Link_moves_the_Started_request_of_that_entry_and_session_to_Linked()
    {
        var store = Store();
        var entryId = Guid.NewGuid();
        var mine = await store.RequestAsync(Entry(entryId), Ct);
        var other = await store.RequestAsync(Entry(), Ct);

        foreach (var (request, session) in new[] { (mine, "session-a"), (other, "session-b") })
        {
            await store.ClaimAsync(request.Id, "manager-1", Ct);
            await store.ReportStartedAsync(request.Id, "manager-1", session, Ct);
        }

        var linked = await store.LinkAsync(entryId, [], "session-a", Ct);

        Assert.Equal([mine.Id], linked.Select(r => r.Id));
        Assert.Equal(SessionRequestState.Linked, (await store.GetAsync(mine.Id, Ct))!.State);
        Assert.Equal(SessionRequestState.Started, (await store.GetAsync(other.Id, Ct))!.State);
        Assert.Empty(await store.LinkAsync(entryId, [], "session-a", Ct));
    }

    [Fact]
    public async Task Link_moves_a_Started_plan_request_when_its_session_links_an_entry_of_the_plan()
    {
        var store = Store();
        var plan = await store.RequestAsync(SessionRequests.ForPlan("+session-dispatch", "JSdotNet/Backlog", @"D:\Repos\Backlog", Noon), Ct);
        await store.ClaimAsync(plan.Id, "manager-1", Ct);
        await store.ReportStartedAsync(plan.Id, "manager-1", "plan-session", Ct);

        var linked = await store.LinkAsync(Guid.NewGuid(), ["+session-dispatch"], "plan-session", Ct);

        Assert.Equal([plan.Id], linked.Select(r => r.Id));
        Assert.Equal(SessionRequestState.Linked, (await store.GetAsync(plan.Id, Ct))!.State);
    }

    [Fact]
    public async Task Cancel_is_written_and_a_Cancelled_request_is_never_handed_out()
    {
        var store = Store();
        var request = await store.RequestAsync(Entry(), Ct);

        Assert.True((await store.CancelAsync(request.Id, Ct)).Succeeded);

        Assert.Equal(SessionRequestState.Cancelled, (await store.GetAsync(request.Id, Ct))!.State);
        Assert.Null(await store.NextAsync(Ct));
        Assert.False((await store.ClaimAsync(request.Id, "manager-1", Ct)).Succeeded);
        Assert.False((await store.CancelAsync(request.Id, Ct)).Succeeded);
    }

    [Fact]
    public async Task An_operation_on_an_unknown_id_is_refused_without_a_request()
    {
        var outcome = await Store().ClaimAsync(Guid.NewGuid(), "manager-1", Ct);

        Assert.False(outcome.Succeeded);
        Assert.Null(outcome.Request);
    }

    [Fact]
    public async Task A_Requested_request_is_still_waiting_a_year_on()
    {
        var store = Store();
        var request = await store.RequestAsync(Entry(), Ct);

        _clock.Now = Noon.AddYears(1);

        Assert.Equal(request.Id, (await store.NextAsync(Ct))!.Id);
    }

    [Fact]
    public async Task The_table_shares_the_file_with_the_tasks_without_touching_them()
    {
        var tasks = new SqliteTaskRepository(_root);

        await Store().RequestAsync(Entry(), Ct);

        Assert.Empty(await tasks.ListAsync(Ct));
    }

    [Fact]
    public void AddSqlite_registers_a_rooted_queue_as_a_singleton()
    {
        var services = new ServiceCollection();
        services.AddSqlite(_ => _root);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ISessionRequestStore));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var store = Assert.IsType<RootedSqliteSessionRequestStore>(provider.GetRequiredService<ISessionRequestStore>());
        Assert.Equal(Path.Combine(_root, "backlog.db"), store.DatabasePath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
