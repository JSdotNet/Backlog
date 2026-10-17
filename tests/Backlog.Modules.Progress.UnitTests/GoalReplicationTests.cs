using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Extensions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// Goals as documents that travel between desktops
/// (<c>.devbook/domain/progress/features.md#syncing-goals-and-impediments</c>): the newer
/// copy wins per goal, an echo and an older copy write nothing, a copy that does not read
/// leaves the local one alone, and only a person's change is announced to the sync loop.
/// </summary>
public sealed class GoalReplicationTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryGoalReplicaStore _store = new();
    private readonly InMemoryGoalRepository _repository = new();
    private readonly FakeGoalFeatures _features = new(goalsOn: false);
    private readonly ServiceProvider _provider;

    public GoalReplicationTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGoalReplicaStore>(_store);
        services.AddSingleton<IGoalRepository>(_repository);
        services.AddSingleton<ISprintSeriesRepository>(new InMemorySprintSeriesRepository());
        services.AddSingleton<ISprintTaskSource>(new FakeTaskSource());
        services.AddSingleton<IAppFeatureSettings>(_features);
        services.AddProgressModule();
        _provider = services.BuildServiceProvider();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private IGoalReplication Replication => _provider.GetRequiredService<IGoalReplication>();

    private static GoalReplicaCopyDto Copy(Guid id, string title, DateTimeOffset at) =>
        new(id, title, $$"""{"id":"{{id}}","title":"{{title}}"}""", at);

    [Fact]
    public async Task A_later_copy_is_taken_whole_and_an_echo_or_an_older_one_writes_nothing()
    {
        var id = Guid.NewGuid();
        var first = Copy(id, "Customers get paid faster", Morning);

        Assert.Equal(GoalReplicaOutcome.Taken, await Replication.ApplyAsync(first, Cancellation));
        Assert.Equal(GoalReplicaOutcome.Echo, await Replication.ApplyAsync(first, Cancellation));
        Assert.Equal(GoalReplicaOutcome.Refused, await Replication.ApplyAsync(Copy(id, "Older", Morning.AddMinutes(-1)), Cancellation));

        var later = Copy(id, "Paid in a day", Morning.AddMinutes(5));
        Assert.Equal(GoalReplicaOutcome.Taken, await Replication.ApplyAsync(later, Cancellation));

        Assert.Equal(later, Assert.Single(await Replication.ReadAllAsync(Cancellation)));
    }

    [Fact]
    public async Task A_copy_that_does_not_read_leaves_the_local_one_as_it_was()
    {
        var id = Guid.NewGuid();
        var held = Copy(id, "Customers get paid faster", Morning);
        await Replication.ApplyAsync(held, Cancellation);

        var outcome = await Replication.ApplyAsync(new GoalReplicaCopyDto(id, "x", "{not json", Morning.AddHours(1)), Cancellation);

        Assert.Equal(GoalReplicaOutcome.Unreadable, outcome);
        Assert.Equal(held, Assert.Single(_store.Rows));
    }

    /// <summary>Goals travel whether the switch is on or off: the replication port is not
    /// behind it, and this fixture has goals off.</summary>
    [Fact]
    public async Task Goals_travel_while_the_switch_is_off()
    {
        Assert.False(_features.GoalsOn);

        Assert.Equal(GoalReplicaOutcome.Taken, await Replication.ApplyAsync(Copy(Guid.NewGuid(), "G", Morning), Cancellation));
    }

    /// <summary>A copy taken from the replica is not a change to tell the replica about,
    /// but a screen still hears it; a person's save is told to both.</summary>
    [Fact]
    public async Task Only_a_persons_change_reaches_the_sync_loop_and_every_change_reaches_the_screens()
    {
        _features.GoalsOn = true;
        var toSync = 0;
        var toScreens = 0;
        Replication.Changed += () => toSync++;
        using var scope = _provider.CreateScope();
        var goals = scope.ServiceProvider.GetRequiredService<IGoals>();
        goals.Changed += () => toScreens++;

        await Replication.ApplyAsync(Copy(Guid.NewGuid(), "Pulled", Morning), Cancellation);
        Assert.Equal((0, 1), (toSync, toScreens));

        await goals.CreateAsync(new SetGoalRequest("Set here", null, MakeGoal.Due), Cancellation);
        Assert.Equal((1, 2), (toSync, toScreens));
    }

    [Fact]
    public async Task A_head_that_keeps_no_goals_sends_nothing_and_reads_every_copy_as_unreadable()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISprintSeriesRepository>(new InMemorySprintSeriesRepository());
        services.AddProgressModule();
        using var provider = services.BuildServiceProvider();
        var replication = provider.GetRequiredService<IGoalReplication>();

        Assert.Empty(await replication.ReadAllAsync(Cancellation));
        Assert.Equal(GoalReplicaOutcome.Unreadable, await replication.ApplyAsync(Copy(Guid.NewGuid(), "G", Morning), Cancellation));
    }

    public void Dispose() => _provider.Dispose();
}
