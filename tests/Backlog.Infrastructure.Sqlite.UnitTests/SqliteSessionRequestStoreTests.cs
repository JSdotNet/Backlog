using Backlog.Infrastructure.Sqlite.Sessions;
using Backlog.Modules.Sessions.Abstractions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The session request queue in <c>backlog.db</c> (local ADR 0038): a request comes
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
    public async Task A_moved_request_is_handed_out_next_and_listed_first()
    {
        var store = Store();
        var older = await store.RequestAsync(Entry(at: Noon.AddMinutes(-5)), Ct);
        var newer = await store.RequestAsync(Entry(at: Noon.AddMinutes(-1)), Ct);

        var moved = await store.MoveAsync(newer.Id, 0, Ct);

        Assert.True(moved.Succeeded);
        Assert.Equal(newer.Id, (await store.NextAsync(Ct))!.Id);
        Assert.Equal([newer.Id, older.Id], (await store.ListAsync(SessionRequestState.Requested, Ct)).Select(r => r.Id));
    }

    [Fact]
    public async Task The_order_the_person_set_survives_a_new_store()
    {
        var first = await Store().RequestAsync(Entry(at: Noon.AddMinutes(-3)), Ct);
        var second = await Store().RequestAsync(Entry(at: Noon.AddMinutes(-2)), Ct);
        var third = await Store().RequestAsync(Entry(at: Noon.AddMinutes(-1)), Ct);

        await Store().MoveAsync(third.Id, 0, Ct);

        SqliteConnection.ClearAllPools();

        Assert.Equal([third.Id, first.Id, second.Id], (await Store().ListAsync(SessionRequestState.Requested, Ct)).Select(r => r.Id));
    }

    [Fact]
    public async Task A_request_queued_after_a_move_joins_the_end()
    {
        var store = Store();
        var older = await store.RequestAsync(Entry(at: Noon.AddMinutes(-5)), Ct);
        var newer = await store.RequestAsync(Entry(at: Noon.AddMinutes(-1)), Ct);
        await store.MoveAsync(newer.Id, 0, Ct);

        var latest = await store.RequestAsync(Entry(at: Noon.AddMinutes(-10)), Ct);

        Assert.Equal([newer.Id, older.Id, latest.Id], (await store.ListAsync(SessionRequestState.Requested, Ct)).Select(r => r.Id));
    }

    [Fact]
    public async Task A_move_of_a_claimed_request_is_refused_and_changes_nothing()
    {
        var store = Store();
        var older = await store.RequestAsync(Entry(at: Noon.AddMinutes(-5)), Ct);
        var newer = await store.RequestAsync(Entry(at: Noon.AddMinutes(-1)), Ct);
        await store.ClaimAsync(newer.Id, "manager-1", Ct);

        var moved = await store.MoveAsync(newer.Id, 0, Ct);

        Assert.False(moved.Succeeded);
        Assert.Equal(SessionRequestState.Claimed, (await store.GetAsync(newer.Id, Ct))!.State);
        Assert.Equal([older.Id], (await store.ListAsync(SessionRequestState.Requested, Ct)).Select(r => r.Id));
        Assert.Null((await store.GetAsync(older.Id, Ct))!.Rank);
    }

    [Fact]
    public async Task A_request_released_after_a_move_keeps_its_place_by_when_it_was_asked()
    {
        var store = Store();
        var a = await store.RequestAsync(Entry(at: Noon.AddMinutes(-3)), Ct);
        var b = await store.RequestAsync(Entry(at: Noon.AddMinutes(-2)), Ct);
        var c = await store.RequestAsync(Entry(at: Noon.AddMinutes(-1)), Ct);
        await store.ClaimAsync(a.Id, "manager-1", Ct);

        await store.MoveAsync(c.Id, 0, Ct);
        await store.ReleaseAsync(a.Id, "manager-1", Ct);
        var d = await store.RequestAsync(Entry(), Ct);

        Assert.Equal([a.Id, c.Id, b.Id, d.Id], (await store.ListAsync(SessionRequestState.Requested, Ct)).Select(r => r.Id));
        Assert.Equal(a.Id, (await store.NextAsync(Ct))!.Id);
    }

    [Fact]
    public async Task A_move_and_a_claim_at_once_never_leave_the_claimed_request_moved()
    {
        var older = await Store().RequestAsync(Entry(at: Noon.AddMinutes(-5)), Ct);
        var newer = await Store().RequestAsync(Entry(at: Noon.AddMinutes(-1)), Ct);

        var move = Task.Run(() => Store().MoveAsync(newer.Id, 0, Ct), Ct);
        var claim = Task.Run(() => Store().ClaimAsync(newer.Id, "manager-1", Ct), Ct);
        await Task.WhenAll(move, claim);

        Assert.True((await claim).Succeeded);

        // Either the move came first and the claim took the new head, or the claim came
        // first and the move was refused: the claimed request is the moved one either way,
        // and what is still Requested is the other.
        Assert.Equal([older.Id], (await Store().ListAsync(SessionRequestState.Requested, Ct)).Select(r => r.Id));
        Assert.Equal(older.Id, (await Store().NextAsync(Ct))!.Id);
    }

    [Fact]
    public async Task A_database_from_before_the_order_gains_it_and_lists_its_rows_oldest_first()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "backlog.db");
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync(Ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                CREATE TABLE session_requests (
                    id TEXT PRIMARY KEY NOT NULL, kind TEXT NOT NULL, entry_id TEXT NULL, plan_tag TEXT NULL,
                    repository TEXT NOT NULL, clone_folder TEXT NOT NULL, state TEXT NOT NULL, claimed_by TEXT NULL,
                    lease_ends_at TEXT NULL, session_id TEXT NULL, created_at TEXT NOT NULL);
                CREATE INDEX ix_session_requests_created ON session_requests (created_at, id);
                INSERT INTO session_requests VALUES
                    ('{newer:D}', 'Entry', '{Guid.NewGuid():D}', NULL, 'JSdotNet/Backlog', 'D:\Repos\Backlog', 'Requested', NULL, NULL, NULL, '2026-10-22T11:59:00.0000000+00:00'),
                    ('{older:D}', 'Entry', '{Guid.NewGuid():D}', NULL, 'JSdotNet/Backlog', 'D:\Repos\Backlog', 'Requested', NULL, NULL, NULL, '2026-10-22T11:55:00.0000000+00:00');
                """;
            await command.ExecuteNonQueryAsync(Ct);
        }

        SqliteConnection.ClearAllPools();

        var store = Store();

        Assert.Equal([older, newer], (await store.ListAsync(SessionRequestState.Requested, Ct)).Select(r => r.Id));
        Assert.True((await store.MoveAsync(newer, 0, Ct)).Succeeded);
        Assert.Equal([newer, older], (await store.ListAsync(SessionRequestState.Requested, Ct)).Select(r => r.Id));
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

        var reclaimed = await store.ClaimAsync(request.Id, "manager-2", Ct);
        Assert.True(reclaimed.Succeeded);
        Assert.Equal("manager-2", (await store.GetAsync(request.Id, Ct))!.ClaimedBy);
    }

    [Fact]
    public async Task A_late_report_from_the_lapsed_holder_writes_Started_while_nobody_else_claimed()
    {
        var store = Store();
        var request = await store.RequestAsync(Entry(), Ct);
        await store.ClaimAsync(request.Id, "manager-1", Ct);

        _clock.Now = Noon.AddMinutes(3);

        var outcome = await store.ReportStartedAsync(request.Id, "manager-1", "session-a", Ct);

        Assert.True(outcome.Succeeded);

        var stored = (await store.GetAsync(request.Id, Ct))!;
        Assert.Equal(SessionRequestState.Started, stored.State);
        Assert.Equal("session-a", stored.SessionId);
        Assert.Null(await store.NextAsync(Ct));
    }

    [Fact]
    public async Task A_late_report_is_refused_once_another_dispatcher_claimed_in_between()
    {
        var store = Store();
        var request = await store.RequestAsync(Entry(), Ct);
        await store.ClaimAsync(request.Id, "manager-1", Ct);

        _clock.Now = Noon.AddMinutes(3);
        await store.ClaimAsync(request.Id, "manager-2", Ct);

        var outcome = await store.ReportStartedAsync(request.Id, "manager-1", "session-a", Ct);

        Assert.False(outcome.Succeeded);
        Assert.Equal("Another dispatcher holds the claim.", outcome.Refusal);

        var stored = (await store.GetAsync(request.Id, Ct))!;
        Assert.Equal(SessionRequestState.Claimed, stored.State);
        Assert.Equal("manager-2", stored.ClaimedBy);
        Assert.Null(stored.SessionId);
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

    // --- a paused queue ---------------------------------------------------------

    [Fact]
    public async Task A_fresh_queue_runs()
    {
        Assert.False(await Store().IsPausedAsync(Ct));
    }

    [Fact]
    public async Task A_pause_and_a_resume_are_written_and_survive_a_new_store()
    {
        await Store().SetPausedAsync(true, Ct);

        Assert.True(await Store().IsPausedAsync(Ct));

        await Store().SetPausedAsync(false, Ct);

        Assert.False(await Store().IsPausedAsync(Ct));
    }

    [Fact]
    public async Task A_database_written_before_the_pause_existed_reads_as_running()
    {
        Directory.CreateDirectory(_root);

        await using (var old = new SqliteConnection($"Data Source={Path.Combine(_root, "backlog.db")}"))
        {
            await old.OpenAsync(Ct);
            await using var command = old.CreateCommand();
            command.CommandText = """
                CREATE TABLE session_requests (
                    id TEXT PRIMARY KEY NOT NULL, kind TEXT NOT NULL, entry_id TEXT NULL, plan_tag TEXT NULL,
                    repository TEXT NOT NULL, clone_folder TEXT NOT NULL, state TEXT NOT NULL, claimed_by TEXT NULL,
                    lease_ends_at TEXT NULL, session_id TEXT NULL, created_at TEXT NOT NULL);
                CREATE TABLE schema_stamps (owner TEXT PRIMARY KEY NOT NULL, fingerprint TEXT NOT NULL);
                INSERT INTO schema_stamps (owner, fingerprint) VALUES ('session_requests', 'before-the-pause');
                """;
            await command.ExecuteNonQueryAsync(Ct);
        }

        SqliteConnection.ClearAllPools();

        var store = Store();

        Assert.False(await store.IsPausedAsync(Ct));
        await store.SetPausedAsync(true, Ct);
        Assert.True(await store.IsPausedAsync(Ct));
    }

    [Fact]
    public async Task A_paused_queue_hands_out_no_next_request_and_resuming_hands_out_the_oldest_again()
    {
        var store = Store();
        var older = await store.RequestAsync(Entry(at: Noon.AddMinutes(-5)), Ct);
        await store.RequestAsync(Entry(at: Noon.AddMinutes(-1)), Ct);

        await store.SetPausedAsync(true, Ct);

        Assert.Null(await store.NextAsync(Ct));

        await store.SetPausedAsync(false, Ct);

        Assert.Equal(older.Id, (await store.NextAsync(Ct))!.Id);
    }

    [Fact]
    public async Task A_claim_on_a_paused_queue_is_refused_and_the_request_stays_Requested()
    {
        var store = Store();
        var request = await store.RequestAsync(Entry(), Ct);
        await store.SetPausedAsync(true, Ct);

        var outcome = await store.ClaimAsync(request.Id, "manager-1", Ct);

        Assert.False(outcome.Succeeded);
        Assert.Equal("The session queue is paused.", outcome.Refusal);
        Assert.Equal(SessionRequestState.Requested, (await store.GetAsync(request.Id, Ct))!.State);
    }

    [Fact]
    public async Task A_pause_between_two_claims_refuses_the_second()
    {
        var store = Store();
        var first = await store.RequestAsync(Entry(), Ct);
        var second = await store.RequestAsync(Entry(), Ct);

        Assert.True((await store.ClaimAsync(first.Id, "manager-1", Ct)).Succeeded);

        await store.SetPausedAsync(true, Ct);

        Assert.False((await store.ClaimAsync(second.Id, "manager-1", Ct)).Succeeded);
    }

    [Fact]
    public async Task A_pause_does_not_revoke_a_claim_already_held()
    {
        var store = Store();
        var started = await store.RequestAsync(Entry(), Ct);
        var released = await store.RequestAsync(Entry(), Ct);
        await store.ClaimAsync(started.Id, "manager-1", Ct);
        await store.ClaimAsync(released.Id, "manager-1", Ct);

        await store.SetPausedAsync(true, Ct);

        Assert.True((await store.ReportStartedAsync(started.Id, "manager-1", "session-a", Ct)).Succeeded);
        Assert.True((await store.ReleaseAsync(released.Id, "manager-1", Ct)).Succeeded);
    }

    [Fact]
    public async Task A_lapsed_holder_still_reports_late_on_a_paused_queue_but_nobody_reclaims()
    {
        var store = Store();
        var request = await store.RequestAsync(Entry(), Ct);
        await store.ClaimAsync(request.Id, "manager-1", Ct);
        await store.SetPausedAsync(true, Ct);

        _clock.Now = Noon.AddMinutes(3);

        Assert.Equal(SessionRequestState.Requested, (await store.GetAsync(request.Id, Ct))!.StateAt(_clock.Now));
        Assert.False((await store.ClaimAsync(request.Id, "manager-2", Ct)).Succeeded);
        Assert.True((await store.ReportStartedAsync(request.Id, "manager-1", "session-a", Ct)).Succeeded);
    }

    [Fact]
    public async Task A_paused_queue_still_takes_a_new_request_and_a_cancel()
    {
        var store = Store();
        await store.SetPausedAsync(true, Ct);
        var entryId = Guid.NewGuid();

        var queued = await store.RequestAsync(Entry(entryId), Ct);

        Assert.Equal(queued.Id, (await store.RequestAsync(Entry(entryId, Noon.AddMinutes(1)), Ct)).Id);
        Assert.Equal(SessionRequestState.Requested, (await store.GetAsync(queued.Id, Ct))!.State);
        Assert.True((await store.CancelAsync(queued.Id, Ct)).Succeeded);
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
