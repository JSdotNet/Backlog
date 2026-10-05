using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.Extensions;
using Backlog.Modules.Tasks.Features.SyncLinkedTasks;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Tasks.UnitTests.LinkedTasks;

/// <summary>
/// The desktop's timer for linked tasks: a tick syncs the targets that are due and
/// no others, the on-demand request syncs every enabled one, and a target whose
/// connector this app does not ship is never asked for.
/// </summary>
public sealed class LinkedTaskSyncWorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly ManualTimeProvider _time = new(Now);
    private readonly RecordingSyncHandler _handler = new();

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(-20, null, true)]
    [InlineData(-10, null, false)]
    [InlineData(-20, -5, false)]
    [InlineData(null, -5, false)]
    [InlineData(null, -15, true)]
    public void A_target_is_due_once_its_interval_has_passed_since_its_last_sync_or_attempt(
        int? syncedMinutesAgo, int? attemptedMinutesAgo, bool due)
    {
        var target = new ConnectedTarget("stub", "a") { LastSyncedAt = At(syncedMinutesAgo) };

        Assert.Equal(due, LinkedTaskSyncWorker.IsDue(target, At(attemptedMinutesAgo), Now));

        static DateTimeOffset? At(int? minutes) => minutes is { } value ? Now.AddMinutes(value) : null;
    }

    [Fact]
    public async Task A_tick_syncs_only_the_enabled_targets_that_are_due()
    {
        var targets = new InMemoryConnectedTargets(
            new ConnectedTarget("stub", "never-synced"),
            new ConnectedTarget("stub", "synced-long-ago") { LastSyncedAt = Now.AddHours(-1) },
            new ConnectedTarget("stub", "synced-just-now") { LastSyncedAt = Now.AddMinutes(-1) },
            new ConnectedTarget("stub", "switched-off", Enabled: false),
            new ConnectedTarget("not-shipped", "elsewhere"));
        using var worker = Worker(targets);

        await worker.TickAsync();

        Assert.Equal(["never-synced", "synced-long-ago"], _handler.Commands.Select(command => command.Target));
    }

    [Fact]
    public async Task A_target_that_failed_is_not_asked_again_until_its_interval_has_passed()
    {
        var targets = new InMemoryConnectedTargets(new ConnectedTarget("stub", "down"));
        _handler.Failing.Add("down");
        using var worker = Worker(targets);

        await worker.TickAsync();
        _time.Now = Now.AddMinutes(1);
        await worker.TickAsync();
        _time.Now = Now.AddMinutes(15);
        await worker.TickAsync();

        Assert.Equal(2, _handler.Commands.Count);
    }

    [Fact]
    public async Task A_request_syncs_every_enabled_target_whether_or_not_it_is_due()
    {
        var targets = new InMemoryConnectedTargets(
            new ConnectedTarget("stub", "a") { LastSyncedAt = Now },
            new ConnectedTarget("stub", "b"),
            new ConnectedTarget("stub", "off", Enabled: false));
        using var worker = Worker(targets);

        await worker.RequestSync();

        Assert.Equal(["a", "b"], _handler.Commands.Select(command => command.Target));
    }

    [Fact]
    public void With_a_connector_the_timer_ticks_every_minute()
    {
        using var worker = Worker(new InMemoryConnectedTargets());

        var timer = Assert.Single(_time.Timers);
        Assert.Equal(LinkedTaskSyncWorker.TickPeriod, timer.Period);
    }

    [Fact]
    public async Task With_no_connector_there_is_no_timer_and_nothing_runs()
    {
        var targets = new InMemoryConnectedTargets(new ConnectedTarget("stub", "a"));
        using var worker = new LinkedTaskSyncWorker(new HandlerScopeFactory(_handler), targets, [], _time);

        await worker.RequestSync();

        Assert.Empty(_time.Timers);
        Assert.Empty(_handler.Commands);
    }

    [Fact]
    public async Task A_disposed_worker_runs_nothing()
    {
        var worker = Worker(new InMemoryConnectedTargets(new ConnectedTarget("stub", "a")));
        worker.Dispose();

        await worker.RequestSync();

        Assert.Empty(_handler.Commands);
    }

    [Fact]
    public void The_same_connector_registered_twice_is_one_connector()
    {
        var services = new ServiceCollection();

        services.AddTaskConnector<StubTaskConnector>();
        services.AddTaskConnector<StubTaskConnector>();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ITaskConnector));
    }

    [Fact]
    public void The_sync_trigger_a_screen_asks_is_the_worker_the_timer_runs_in()
    {
        var services = new ServiceCollection();
        services.AddLinkedTaskSync();
        using var worker = Worker(new InMemoryConnectedTargets());

        var trigger = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ILinkedTaskSyncTrigger));

        Assert.Equal(ServiceLifetime.Singleton, trigger.Lifetime);
        Assert.Same(worker, trigger.ImplementationFactory!(new OnlyTheWorker(worker)));
    }

    /// <summary>A container holding nothing but the worker, which is all the
    /// trigger's registration may ask for.</summary>
    private sealed class OnlyTheWorker(LinkedTaskSyncWorker worker) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(LinkedTaskSyncWorker) ? worker : null;
    }

    private LinkedTaskSyncWorker Worker(InMemoryConnectedTargets targets) =>
        new(new HandlerScopeFactory(_handler), targets, [new StubTaskConnector()], _time);
}
