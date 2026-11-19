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

    private readonly TemporaryDirectory _temp = new("backlog-sqlite-loop-register");
    private string _root => _temp.Path;
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

    /// <summary>The check-in's limits, the reading the app took at it and the work waiting
    /// then (local ADR 0051 §4) come back as they went in, an unavailable figure included.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_check_ins_limits_reading_and_work_waiting_come_back_whole(bool everyFigure)
    {
        var limits = new CapacityLimits(2, 70.5, 90, 8);
        var checkIn = CheckIn("manager-session", Noon) with
        {
            Limits = limits,
            Reading = everyFigure ? new CapacityReading(2, 71.25, 20.5, 16.75, limits) : new CapacityReading(null, null, null, null, limits),
            WorkWaiting = 4
        };

        await Register().CheckInAsync(checkIn, Ct);

        Assert.Equal(checkIn, (await Register().GetAsync("manager-session", Ct))!.CheckIn);
    }

    [Fact]
    public async Task A_check_in_with_no_reading_comes_back_with_none()
    {
        var checkIn = CheckIn("brain-session", Noon, kind: LoopKind.BrainTender) with { Limits = new CapacityLimits(5, 90, 95, 2) };

        await Register().CheckInAsync(checkIn, Ct);

        var read = (await Register().GetAsync("brain-session", Ct))!.CheckIn;
        Assert.Null(read.Reading);
        Assert.Equal(checkIn, read);
    }

    [Fact]
    public async Task A_later_check_in_replaces_the_earlier_limits_and_reading()
    {
        var register = Register();
        var limits = new CapacityLimits(2, 70, 90, 8);
        await register.CheckInAsync(CheckIn("manager-session", Noon) with { Limits = limits, Reading = new CapacityReading(2, 71, 20, 16, limits), WorkWaiting = 4 }, Ct);

        var later = CheckIn("manager-session", Noon + TimeSpan.FromMinutes(1));
        await register.CheckInAsync(later, Ct);

        Assert.Equal(later, (await Register().GetAsync("manager-session", Ct))!.CheckIn);
    }

    /// <summary>A row written before the limits were stored reads the default limits, no
    /// reading and no work waiting.</summary>
    [Fact]
    public async Task A_row_from_before_the_limits_reads_the_defaults_and_no_reading()
    {
        var register = Register();
        await register.CheckInAsync(CheckIn("manager-session", Noon) with
        {
            Limits = new CapacityLimits(2, 70, 90, 8),
            Reading = new CapacityReading(2, 71, 20, 16, new CapacityLimits(2, 70, 90, 8)),
            WorkWaiting = 4
        }, Ct);

        await using (var connection = new SqliteConnection($"Data Source={register.DatabasePath}"))
        {
            await connection.OpenAsync(Ct);
            await using var update = connection.CreateCommand();
            update.CommandText =
                "UPDATE loop_check_ins SET limit_sessions = NULL, limit_usage_percent = NULL, limit_cpu_percent = NULL, "
                + "limit_min_free_gb = NULL, reading_taken = NULL, reading_working_sessions = NULL, reading_plan_usage_percent = NULL, "
                + "reading_cpu_percent = NULL, reading_free_memory_gb = NULL, work_waiting = NULL;";
            await update.ExecuteNonQueryAsync(Ct);
        }

        Assert.Equal(CheckIn("manager-session", Noon), (await Register().GetAsync("manager-session", Ct))!.CheckIn);
    }

    /// <summary>A <c>backlog.db</c> the build before the limits wrote — the eleven-column
    /// table with a row in it, stamped with that build's fingerprint — opens, its row reads
    /// the default limits and no reading, and a check-in with limits then round-trips.</summary>
    [Fact]
    public async Task A_table_the_previous_schema_created_opens_and_its_row_reads_the_defaults()
    {
        Directory.CreateDirectory(_root);
        var databasePath = Path.Combine(_root, "backlog.db");

        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync(Ct);
            await using var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE loop_check_ins (
                    session_id        TEXT PRIMARY KEY NOT NULL,
                    kind              TEXT NOT NULL,
                    activity          TEXT NOT NULL,
                    heartbeat_minutes INTEGER NOT NULL,
                    held_requests     INTEGER NOT NULL,
                    held_deliveries   INTEGER NOT NULL,
                    held_sessions     INTEGER NOT NULL,
                    held_notes        INTEGER NOT NULL,
                    checked_in_at     TEXT NOT NULL,
                    command           TEXT NOT NULL,
                    command_set_at    TEXT NULL
                );
                INSERT INTO loop_check_ins VALUES
                    ('manager-session', 'Dispatcher', 'Holding', 30, 1, 2, 3, 4, '2026-11-11T12:00:00.0000000+00:00', 'Pause', '2026-11-11T11:00:00.0000000+00:00');
                CREATE TABLE schema_stamps (owner TEXT PRIMARY KEY NOT NULL, fingerprint TEXT NOT NULL);
                INSERT INTO schema_stamps VALUES ('loop_check_ins', 'the-previous-build');
                """;
            await create.ExecuteNonQueryAsync(Ct);
        }

        SqliteConnection.ClearAllPools();

        var read = await Register().GetAsync("manager-session", Ct);

        Assert.Equal(
            new LoopSession(CheckIn("manager-session", Noon, LoopActivity.Holding), LoopCommand.Pause, Noon - TimeSpan.FromHours(1)),
            read);
        Assert.Equal(CapacityLimits.Default, read!.CheckIn.Limits);
        Assert.Null(read.CheckIn.Reading);
        Assert.Null(read.CheckIn.WorkWaiting);

        var limits = new CapacityLimits(2, 70, 90, 8);
        var later = CheckIn("manager-session", Noon + TimeSpan.FromMinutes(1)) with { Limits = limits, Reading = new CapacityReading(2, 71, 20, 16, limits), WorkWaiting = 1 };
        await Register().CheckInAsync(later, Ct);

        Assert.Equal(new LoopSession(later, LoopCommand.Pause, Noon - TimeSpan.FromHours(1)), await Register().GetAsync("manager-session", Ct));
    }

    [Fact]
    public async Task Setting_a_command_keeps_the_limits_the_reading_and_the_work_waiting()
    {
        var register = Register();
        var limits = new CapacityLimits(2, 70, 90, 8);
        var checkIn = CheckIn("manager-session", Noon, LoopActivity.Holding) with
        {
            Limits = limits,
            Reading = new CapacityReading(2, 71, 20, 16, limits),
            WorkWaiting = 3
        };
        await register.CheckInAsync(checkIn, Ct);
        _clock.Now = Noon + TimeSpan.FromMinutes(1);

        await register.SetCommandAsync("manager-session", LoopCommand.Pause, Ct);

        Assert.Equal(new LoopSession(checkIn, LoopCommand.Pause, Noon + TimeSpan.FromMinutes(1)), await Register().GetAsync("manager-session", Ct));
    }

    [Fact]
    public async Task A_wait_keeps_the_limits_and_the_reading()
    {
        var register = Register();
        var limits = new CapacityLimits(2, 70, 90, 8);
        var checkIn = CheckIn("manager-session", Noon, LoopActivity.Holding) with
        {
            Limits = limits,
            Reading = new CapacityReading(2, 71, 20, 16, limits),
            WorkWaiting = 3
        };
        await register.CheckInAsync(checkIn, Ct);

        await register.RecordWaitAsync("manager-session", Noon + TimeSpan.FromMinutes(5), Ct);

        Assert.Equal(checkIn with { At = Noon + TimeSpan.FromMinutes(5), ReportedAt = Noon }, (await Register().GetAsync("manager-session", Ct))!.CheckIn);
    }

    /// <summary>The time of the last full check-in survives a wait and a fresh register, so
    /// a Holding it reported lapses at its own heartbeat however many waits follow.</summary>
    [Fact]
    public async Task The_full_check_ins_time_survives_a_wait_and_a_new_register()
    {
        var register = Register();
        var limits = new CapacityLimits(2, 70, 90, 8);
        await register.CheckInAsync(CheckIn("manager-session", Noon, LoopActivity.Holding) with
        {
            Limits = limits,
            Reading = new CapacityReading(2, 71, 20, 16, limits),
            WorkWaiting = 3
        }, Ct);
        await register.RecordWaitAsync("manager-session", Noon + TimeSpan.FromMinutes(20), Ct);
        await Register().RecordWaitAsync("manager-session", Noon + TimeSpan.FromMinutes(35), Ct);

        var read = (await Register().GetAsync("manager-session", Ct))!.CheckIn;

        Assert.Equal(Noon + TimeSpan.FromMinutes(35), read.At);
        Assert.Equal(LoopState.Listening, LoopSessions.StateAt(read, LoopProcess.Alive, Noon + TimeSpan.FromMinutes(35)));
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

    /// <summary>A dispatch wait (local ADR 0051 §3) for a session the register does not
    /// know is no check-in: nothing to move, and no row is made from a session id alone.</summary>
    [Fact]
    public async Task A_wait_for_a_session_that_never_checked_in_answers_null_and_writes_no_row()
    {
        var register = Register();

        Assert.Null(await register.RecordWaitAsync("nobody", Noon, Ct));
        Assert.Null(await register.RecordWaitAsync("  ", Noon, Ct));
        Assert.Empty(await register.ListAsync(Ct));
    }

    [Fact]
    public async Task A_wait_moves_only_the_check_in_time_keeps_the_full_check_ins_own_and_the_command()
    {
        var register = Register();
        var checkIn = CheckIn("manager-session", Noon, LoopActivity.Holding);
        await register.CheckInAsync(checkIn, Ct);
        _clock.Now = Noon + TimeSpan.FromMinutes(1);
        await register.SetCommandAsync("manager-session", LoopCommand.Pause, Ct);

        var waited = await register.RecordWaitAsync(" manager-session ", Noon + TimeSpan.FromMinutes(5), Ct);

        var expected = new LoopSession(
            checkIn with { At = Noon + TimeSpan.FromMinutes(5), ReportedAt = Noon }, LoopCommand.Pause, Noon + TimeSpan.FromMinutes(1));
        Assert.Equal(expected, waited);
        Assert.Equal(expected, await Register().GetAsync("manager-session", Ct));
    }

    [Fact]
    public async Task A_wait_earlier_than_the_last_check_in_leaves_it_where_it_is()
    {
        var register = Register();
        var checkIn = CheckIn("manager-session", Noon + TimeSpan.FromMinutes(5));
        await register.CheckInAsync(checkIn, Ct);

        var waited = await register.RecordWaitAsync("manager-session", Noon + TimeSpan.FromMinutes(2), Ct);

        Assert.Equal(new LoopSession(checkIn, LoopCommand.None, null), waited);
        Assert.Equal(checkIn, (await Register().GetAsync("manager-session", Ct))!.CheckIn);
    }

    [Fact]
    public async Task A_full_check_in_after_a_wait_records_it_whole_and_keeps_the_command()
    {
        var register = Register();
        await register.CheckInAsync(CheckIn("manager-session", Noon), Ct);
        _clock.Now = Noon + TimeSpan.FromMinutes(1);
        await register.SetCommandAsync("manager-session", LoopCommand.Pause, Ct);
        await register.RecordWaitAsync("manager-session", Noon + TimeSpan.FromMinutes(2), Ct);

        var later = CheckIn("manager-session", Noon + TimeSpan.FromMinutes(3), LoopActivity.Working);
        await register.CheckInAsync(later, Ct);

        Assert.Equal(new LoopSession(later, LoopCommand.Pause, Noon + TimeSpan.FromMinutes(1)), await Register().GetAsync("manager-session", Ct));
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

        _temp.Dispose();
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
