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

    /// <summary>A card's "Sync now" syncs that card's target and no other, and says
    /// what it did.</summary>
    [Fact]
    public async Task A_request_for_one_target_syncs_only_it_and_says_so()
    {
        var targets = new InMemoryConnectedTargets(new ConnectedTarget("stub", "a"), new ConnectedTarget("stub", "b"));
        using var worker = Worker(targets);

        var outcome = await worker.RequestSync("stub", "b");

        Assert.Equal(["b"], _handler.Commands.Select(command => command.Target));
        Assert.True(outcome.Succeeded);
        Assert.Null(outcome.ErrorCode);
        Assert.StartsWith("Synced b", outcome.Message, StringComparison.Ordinal);
    }

    /// <summary>The defect this answers: the page said "Synced." over a sync that
    /// had failed. The failure comes back as the sync's own sentence.</summary>
    [Fact]
    public async Task A_request_for_one_target_whose_sync_failed_answers_the_failure()
    {
        var targets = new InMemoryConnectedTargets(new ConnectedTarget("stub", "down"));
        _handler.Failing.Add("down");
        using var worker = Worker(targets);

        var outcome = await worker.RequestSync("stub", "down");

        Assert.False(outcome.Succeeded);
        Assert.Equal("linked_tasks.fetch_failed", outcome.ErrorCode);
        Assert.Equal("down", outcome.Message);
    }

    /// <summary>A card's Sync now that arrives while the timer's run is in flight is
    /// not skipped, the way a second tick is: it waits for that run, then runs.</summary>
    [Fact]
    public async Task A_request_for_one_target_during_a_timer_run_waits_for_it_then_runs()
    {
        var targets = new InMemoryConnectedTargets(
            new ConnectedTarget("stub", "a"),
            new ConnectedTarget("stub", "b") { LastSyncedAt = Now });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.Holding["a"] = release.Task;
        using var worker = Worker(targets);

        var tick = worker.TickAsync();
        var request = worker.RequestSync("stub", "b");

        Assert.False(request.IsCompleted);
        Assert.Equal(["a"], _handler.Commands.Select(command => command.Target));

        release.SetResult();
        await tick.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var outcome = await request.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(outcome.Succeeded);
        Assert.Equal(["a", "b"], _handler.Commands.Select(command => command.Target));
    }

    /// <summary>The refusals the handler gives before it fetches name no target, so
    /// the one-target answer says which it was about.</summary>
    [Theory]
    [InlineData("linked_tasks.target_not_found")]
    [InlineData("linked_tasks.target_disabled")]
    public async Task A_request_for_one_target_the_sync_refuses_names_the_target(string code)
    {
        var targets = new InMemoryConnectedTargets(new ConnectedTarget("stub", "owner/repo"));
        _handler.Refusing["owner/repo"] = code == SyncLinkedTasksCommandHandler.TargetNotFound.Code
            ? SyncLinkedTasksCommandHandler.TargetNotFound
            : SyncLinkedTasksCommandHandler.TargetDisabled;
        using var worker = Worker(targets);

        var outcome = await worker.RequestSync("stub", "owner/repo");

        Assert.False(outcome.Succeeded);
        Assert.Equal(code, outcome.ErrorCode);
        Assert.Contains("owner/repo", outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_for_a_target_whose_connector_is_not_installed_names_the_target()
    {
        using var worker = Worker(new InMemoryConnectedTargets(new ConnectedTarget("jira", "PROJ")));

        var outcome = await worker.RequestSync("jira", "PROJ");

        Assert.False(outcome.Succeeded);
        Assert.Equal(SyncLinkedTasksCommandHandler.ConnectorNotFound.Code, outcome.ErrorCode);
        Assert.Contains("PROJ", outcome.Message, StringComparison.Ordinal);
        Assert.Empty(_handler.Commands);
    }

    [Fact]
    public async Task A_request_for_one_target_after_the_worker_stopped_answers_that_nothing_ran()
    {
        var worker = Worker(new InMemoryConnectedTargets(new ConnectedTarget("stub", "a")));
        worker.Dispose();

        var outcome = await worker.RequestSync("stub", "a");

        Assert.False(outcome.Succeeded);
        Assert.Empty(_handler.Commands);
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

        var trigger = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ILinkedTaskSync));

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
