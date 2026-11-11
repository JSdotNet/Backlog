using Backlog.Infrastructure.Sqlite.Sessions;
using Backlog.Modules.Sessions.Abstractions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The Loop Register in <c>backlog.db</c> (local ADR 0051 §3): the latest check-in per loop
/// session comes back as it went in, a later one replaces it and keeps the person's command,
/// and a command survives a fresh register over the same file.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteLoopRegisterTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-sqlite-loop-register", Guid.NewGuid().ToString("n"));
    private readonly ManualClock _clock = new(Noon);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SqliteLoopRegister Register() => new(_root, _clock);

    private static LoopCheckIn CheckIn(string sessionId, DateTimeOffset at, LoopActivity activity = LoopActivity.Listening, LoopKind kind = LoopKind.Dispatcher) =>
        LoopSessions.CheckIn(sessionId, kind, activity, 30, new LoopHeldWork(1, 2, 3, 4), at);

    [Fact]
    public async Task A_check_in_comes_back_whole()
    {
        var register = Register();
        var checkIn = CheckIn("manager-session", Noon);

        var recorded = await register.CheckInAsync(checkIn, Ct);

        Assert.Equal(new LoopSession(checkIn, LoopCommand.None, null), recorded);
        Assert.Equal(recorded, await register.GetAsync("manager-session", Ct));
        Assert.Equal([recorded], await register.ListAsync(Ct));
        Assert.Equal(Path.Combine(_root, "backlog.db"), register.DatabasePath);
    }

    [Fact]
    public async Task A_later_check_in_replaces_the_earlier_and_keeps_the_command()
    {
        var register = Register();
        await register.CheckInAsync(CheckIn("manager-session", Noon), Ct);
        _clock.Now = Noon + TimeSpan.FromMinutes(1);
        await register.SetCommandAsync("manager-session", LoopCommand.Pause, Ct);

        var later = CheckIn("manager-session", Noon + TimeSpan.FromMinutes(2), LoopActivity.Holding);
        await register.CheckInAsync(later, Ct);

        var read = await Register().GetAsync("manager-session", Ct);
        Assert.NotNull(read);
        Assert.Equal(later, read.CheckIn);
        Assert.Equal(LoopCommand.Pause, read.Command);
        Assert.Equal(Noon + TimeSpan.FromMinutes(1), read.CommandSetAt);
        Assert.Single(await register.ListAsync(Ct));
    }

    [Fact]
    public async Task A_command_on_a_session_that_never_checked_in_is_not_stored()
    {
        var register = Register();

        Assert.Null(await register.SetCommandAsync("nobody", LoopCommand.Stop, Ct));
        Assert.Empty(await register.ListAsync(Ct));
    }

    [Fact]
    public async Task The_list_holds_every_loop_most_recent_check_in_first()
    {
        var register = Register();
        await register.CheckInAsync(CheckIn("brain-session", Noon, kind: LoopKind.BrainTender), Ct);
        await register.CheckInAsync(CheckIn("manager-session", Noon + TimeSpan.FromMinutes(1)), Ct);

        Assert.Equal(["manager-session", "brain-session"], (await register.ListAsync(Ct)).Select(loop => loop.SessionId));
        Assert.Equal(LoopKind.BrainTender, (await register.GetAsync("brain-session", Ct))!.CheckIn.Kind);
    }

    /// <summary>A row a later build wrote reads as nothing here, and a check-in over it
    /// still keeps the command the person set on it.</summary>
    [Fact]
    public async Task A_check_in_over_a_row_this_build_cannot_read_keeps_its_command()
    {
        var register = Register();
        await register.CheckInAsync(CheckIn("manager-session", Noon), Ct);

        await using (var connection = new SqliteConnection($"Data Source={register.DatabasePath}"))
        {
            await connection.OpenAsync(Ct);
            await using var update = connection.CreateCommand();
            update.CommandText = "UPDATE loop_check_ins SET kind = 'Scheduler', command = 'Snooze', command_set_at = '2026-11-11T11:00:00.0000000+00:00';";
            await update.ExecuteNonQueryAsync(Ct);
        }

        Assert.Null(await register.GetAsync("manager-session", Ct));

        await register.CheckInAsync(CheckIn("manager-session", Noon + TimeSpan.FromMinutes(1)), Ct);

        await using (var connection = new SqliteConnection($"Data Source={register.DatabasePath}"))
        {
            await connection.OpenAsync(Ct);
            await using var read = connection.CreateCommand();
            read.CommandText = "SELECT kind, command FROM loop_check_ins WHERE session_id = 'manager-session';";
            await using var reader = await read.ExecuteReaderAsync(Ct);

            Assert.True(await reader.ReadAsync(Ct));
            Assert.Equal("Dispatcher", reader.GetString(0));
            Assert.Equal("Snooze", reader.GetString(1));
        }
    }

    [Fact]
    public void AddSqlite_registers_a_rooted_register_as_a_singleton()
    {
        var services = new ServiceCollection();
        services.AddSqlite(_ => _root);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ILoopRegister));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.IsType<RootedSqliteLoopRegister>(provider.GetRequiredService<ILoopRegister>());

        // The monitor a screen reads the loops through sits beside it, and needs no session list.
        Assert.NotNull(provider.GetRequiredService<LoopSessionMonitor>());
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
