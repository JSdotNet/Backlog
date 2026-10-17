using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Extensions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// Each sprint series travels between devices as one whole document, the newer write
/// winning per series, on the terms Roadmap's plan travels on (local ADR 0018). The port
/// decides by stamp alone; whether a copy reads as a series is the store's answer.
/// </summary>
public sealed class SprintSeriesReplicationTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemorySprintSeriesRepository _repository = new();
    private readonly FakeReplicaStore _store = new();
    private readonly FakeFeatures _features = new(progressOn: true);
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public SprintSeriesReplicationTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Morning));
        services.AddSingleton<ISprintSeriesRepository>(_repository);
        services.AddSingleton<IGoalRepository>(new InMemoryGoalRepository());
        services.AddSingleton<ISprintSeriesReplicaStore>(_store);
        services.AddSingleton<ISprintTaskSource>(new FakeTaskSource());
        services.AddSingleton<IGoalPullRequestReader>(new FakePullRequests());
        services.AddSingleton<IAppFeatureSettings>(_features);
        services.AddProgressModule();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _provider.CreateScope();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private ISprintSeriesReplication Replication => _provider.GetRequiredService<ISprintSeriesReplication>();

    private ISprintSeriesPlanning Planning => _scope.ServiceProvider.GetRequiredService<ISprintSeriesPlanning>();

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    [Fact]
    public async Task Every_held_series_is_read_as_stored()
    {
        var first = Copy(Guid.NewGuid(), "{}", Morning);
        var second = Copy(Guid.NewGuid(), "{}", Morning.AddMinutes(1));
        _store.Held[first.Id] = first;
        _store.Held[second.Id] = second;

        var all = await Replication.ReadAllAsync(Cancellation);

        Assert.Equal([first, second], all);
    }

    [Fact]
    public async Task A_series_this_device_never_held_is_taken()
    {
        var inbound = Copy(Guid.NewGuid(), "{\"name\":\"Platform\"}", Morning);

        var outcome = await Replication.ApplyAsync(inbound, Cancellation);

        Assert.Equal(SprintSeriesReplicaOutcome.Taken, outcome);
        Assert.Equal(inbound, _store.Held[inbound.Id]);
    }

    [Fact]
    public async Task A_later_copy_replaces_the_local_one_whole()
    {
        var id = Guid.NewGuid();
        _store.Held[id] = Copy(id, "local", Morning);
        var inbound = Copy(id, "remote", Morning.AddMinutes(5));

        var outcome = await Replication.ApplyAsync(inbound, Cancellation);

        Assert.Equal(SprintSeriesReplicaOutcome.Taken, outcome);
        Assert.Equal(inbound, _store.Held[id]);
    }

    [Fact]
    public async Task An_equal_stamp_is_an_echo_and_writes_nothing()
    {
        var id = Guid.NewGuid();
        var local = Copy(id, "local", Morning);
        _store.Held[id] = local;

        var outcome = await Replication.ApplyAsync(Copy(id, "remote", Morning), Cancellation);

        Assert.Equal(SprintSeriesReplicaOutcome.Echo, outcome);
        Assert.Equal(local, _store.Held[id]);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task An_older_copy_is_refused_and_the_local_one_stands()
    {
        var id = Guid.NewGuid();
        var local = Copy(id, "local", Morning.AddMinutes(5));
        _store.Held[id] = local;

        var outcome = await Replication.ApplyAsync(Copy(id, "remote", Morning), Cancellation);

        Assert.Equal(SprintSeriesReplicaOutcome.Refused, outcome);
        Assert.Equal(local, _store.Held[id]);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task A_copy_the_store_cannot_read_is_unreadable()
    {
        _store.Readable = false;

        var outcome = await Replication.ApplyAsync(Copy(Guid.NewGuid(), "not a series", Morning), Cancellation);

        Assert.Equal(SprintSeriesReplicaOutcome.Unreadable, outcome);
        Assert.Empty(_store.Held);
    }

    [Fact]
    public async Task Without_a_store_there_is_nothing_to_send_and_every_copy_is_unreadable()
    {
        var services = new ServiceCollection();
        services.AddProgressModule();
        await using var provider = services.BuildServiceProvider();
        var replication = provider.GetRequiredService<ISprintSeriesReplication>();

        Assert.Empty(await replication.ReadAllAsync(Cancellation));
        Assert.Equal(
            SprintSeriesReplicaOutcome.Unreadable,
            await replication.ApplyAsync(Copy(Guid.NewGuid(), "{}", Morning), Cancellation));
    }

    /// <summary>A person's change is announced to the sync loop, so it is pushed within
    /// seconds: setting a series up, marking a goal, writing a note.</summary>
    [Fact]
    public async Task Every_saved_change_is_announced()
    {
        var heard = 0;
        Replication.Changed += () => heard++;

        var series = (await Planning.CreateSeriesAsync(new CreateSprintSeries(["backlog"]), Cancellation)).Value;
        await Planning.StartSprintAsync(series.Id, Make.Monday, "Ship sync", Cancellation);

        Assert.Equal(2, heard);
    }

    /// <summary>A refused change saves nothing and announces nothing.</summary>
    [Fact]
    public async Task A_refused_change_is_not_announced()
    {
        var heard = 0;
        Replication.Changed += () => heard++;

        var refused = await Planning.SetSprintGoalAsync(Guid.NewGuid(), 1, "No such series", Cancellation);

        Assert.True(refused.IsFailure);
        Assert.Equal(0, heard);
    }

    /// <summary>A copy that arrived from the replica is not something to tell the
    /// replica about: a loop that heard it would start a cycle on the heels of every
    /// cycle that received one.</summary>
    [Fact]
    public async Task A_taken_copy_is_not_announced_as_a_local_change()
    {
        var heard = 0;
        Replication.Changed += () => heard++;

        await Replication.ApplyAsync(Copy(Guid.NewGuid(), "{}", Morning), Cancellation);

        Assert.Equal(0, heard);
    }

    /// <summary>The planning port's notice is the screens' one: it is raised for a copy
    /// taken from the replica, so every screen drawing the series reads it again, and for
    /// a person's change made through any scope — never for an echo or a refused copy,
    /// which wrote nothing.</summary>
    [Fact]
    public async Task The_screens_hear_a_taken_copy_and_a_local_change_but_not_an_echo()
    {
        var heard = 0;
        Planning.Changed += () => heard++;

        var id = Guid.NewGuid();
        await Replication.ApplyAsync(Copy(id, "{}", Morning), Cancellation);
        Assert.Equal(1, heard);

        await Replication.ApplyAsync(Copy(id, "{}", Morning), Cancellation);
        await Replication.ApplyAsync(Copy(id, "{}", Morning.AddMinutes(-1)), Cancellation);
        Assert.Equal(1, heard);

        using var other = _provider.CreateScope();
        await other.ServiceProvider.GetRequiredService<ISprintSeriesPlanning>()
            .CreateSeriesAsync(new CreateSprintSeries(["docs"]), Cancellation);
        Assert.Equal(2, heard);
    }

    /// <summary>A pulled copy waits for a command that holds the series, so a command's
    /// save can never write over it from the copy it loaded first.</summary>
    [Fact]
    public async Task A_pulled_copy_waits_for_a_command_in_flight()
    {
        var gate = _provider.GetRequiredService<Services.ProgressGate>();
        var held = await gate.EnterAsync(Cancellation);

        var applying = Replication.ApplyAsync(Copy(Guid.NewGuid(), "{}", Morning), Cancellation);
        await Task.Delay(50, Cancellation);
        Assert.False(applying.IsCompleted);

        held.Dispose();
        Assert.Equal(SprintSeriesReplicaOutcome.Taken, await applying);
    }

    private static SprintSeriesReplicaCopyDto Copy(Guid id, string content, DateTimeOffset stamp) =>
        new(id, "Platform", content, stamp);

    /// <summary>The store as the SQLite row would answer it, with whether it can read a
    /// copy set by the test.</summary>
    private sealed class FakeReplicaStore : ISprintSeriesReplicaStore
    {
        public Dictionary<Guid, SprintSeriesReplicaCopyDto> Held { get; } = [];

        public bool Readable { get; set; } = true;

        public int Writes { get; private set; }

        public Task<IReadOnlyList<SprintSeriesReplicaCopyDto>> ReadAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SprintSeriesReplicaCopyDto>>([.. Held.Values]);

        public Task<SprintSeriesReplicaCopyDto?> ReadAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Held.TryGetValue(id, out var copy) ? copy : null);

        public Task<bool> TryWriteAsync(SprintSeriesReplicaCopyDto copy, CancellationToken cancellationToken = default)
        {
            if (!Readable) return Task.FromResult(false);

            Writes++;
            Held[copy.Id] = copy;
            return Task.FromResult(true);
        }
    }
}
