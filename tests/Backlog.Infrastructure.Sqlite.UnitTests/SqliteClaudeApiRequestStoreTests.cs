using Backlog.Infrastructure.Sqlite.Sessions;
using Backlog.Modules.Sessions.Abstractions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The Claude Code api_request events in <c>backlog.db</c> (local ADR 0024): every
/// field comes back as it went in, an unknown figure stays unknown, and storing the
/// same request twice stores it once.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteClaudeApiRequestStoreTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-sqlite-api-requests", Guid.NewGuid().ToString("n"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ClaudeApiRequest Request(string id, string? session = "session-a", DateTimeOffset? at = null) =>
        new(id, session, at ?? Noon, "claude-opus-5-5", "high", 193755, 3, 1420, 40211, 15876, 9120,
            "agent:custom", "Explore", "delivery:phase-review", "prompt-1");

    [Fact]
    public async Task A_stored_request_comes_back_whole()
    {
        var store = new SqliteClaudeApiRequestStore(_root);
        var request = Request("req_1");

        Assert.Equal(1, await store.AddAsync([request], Ct));

        Assert.Equal([request], await store.ListAsync(cancellationToken: Ct));
        Assert.Equal(Path.Combine(_root, "backlog.db"), store.DatabasePath);
    }

    [Fact]
    public async Task An_unknown_figure_is_kept_unknown_rather_than_zero()
    {
        var store = new SqliteClaudeApiRequestStore(_root);
        var request = new ClaudeApiRequest("req_bare", null, Noon, null, null, null, null, null, null, null, null, null, null, null, null);

        await store.AddAsync([request], Ct);

        Assert.Equal([request], await store.ListAsync(cancellationToken: Ct));
    }

    [Fact]
    public async Task The_same_request_twice_is_stored_once_and_the_first_copy_wins()
    {
        var store = new SqliteClaudeApiRequestStore(_root);
        var first = Request("req_1");

        Assert.Equal(1, await store.AddAsync([first], Ct));
        Assert.Equal(1, await store.AddAsync([first with { Model = "something else" }, Request("req_2")], Ct));
        Assert.Equal(0, await store.AddAsync([first, first], Ct));

        var stored = await store.ListAsync(cancellationToken: Ct);
        Assert.Equal(["req_1", "req_2"], stored.Select(r => r.RequestId));
        Assert.Equal("claude-opus-5-5", stored[0].Model);
    }

    [Fact]
    public async Task One_session_s_requests_are_listed_oldest_first()
    {
        var store = new SqliteClaudeApiRequestStore(_root);

        await store.AddAsync(
        [
            Request("req_late", at: Noon.AddMinutes(5)),
            Request("req_other", session: "session-b"),
            Request("req_early", at: Noon.AddMinutes(-5))
        ], Ct);

        var session = await store.ListAsync("session-a", Ct);

        Assert.Equal(["req_early", "req_late"], session.Select(r => r.RequestId));
    }

    [Fact]
    public async Task The_table_shares_the_file_with_the_tasks_without_touching_them()
    {
        var tasks = new SqliteTaskRepository(_root);
        var store = new SqliteClaudeApiRequestStore(_root);

        await store.AddAsync([Request("req_1")], Ct);

        Assert.Empty(await tasks.ListAsync(Ct));
        Assert.Single(await store.ListAsync(cancellationToken: Ct));
    }

    [Fact]
    public void AddSqlite_registers_a_rooted_store_as_a_singleton()
    {
        var services = new ServiceCollection();
        services.AddSqlite(_ => _root);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IClaudeApiRequestStore));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var store = Assert.IsType<RootedSqliteClaudeApiRequestStore>(provider.GetRequiredService<IClaudeApiRequestStore>());
        Assert.Equal(Path.Combine(_root, "backlog.db"), store.DatabasePath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
