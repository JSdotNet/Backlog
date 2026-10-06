using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Extensions;
using Backlog.Modules.Tasks.Features.CompleteLinkedTask;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Tasks.UnitTests.LinkedTasks;

/// <summary>
/// The requester behind <see cref="ILinkedTaskWriteBack"/>: the save hands it a task
/// id and goes on, and the write-back runs on the thread pool from a scope of its
/// own. What is worth holding is that the caller never waits on it and never hears
/// from it — not its answer, and not its failure — that a tick taken back within
/// the grace sends nothing, that a burst of ticks reaches the sources two at a time,
/// and that a burst of successes asks for one sync, not one each.
/// </summary>
public sealed class LinkedTaskWriteBackTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly RecordingLinkedTaskSync _sync = new();

    [Fact]
    public async Task A_request_returns_at_once_and_the_write_back_runs_once_the_grace_has_passed()
    {
        var handler = new GatedHandler();
        handler.Gate.SetResult();
        using var writeBack = WriteBack(handler);
        var id = Guid.NewGuid();

        writeBack.Request(id);
        _time.Advance(LinkedTaskWriteBack.Grace - TimeSpan.FromMilliseconds(1));

        Assert.Equal(0, handler.Entered);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await writeBack.WhenIdleAsync();

        Assert.Equal([id], handler.Finished);
    }

    [Fact]
    public async Task A_write_back_that_throws_is_not_the_callers_failure()
    {
        var handler = new GatedHandler { Failure = new InvalidOperationException("The store is locked.") };
        handler.Gate.SetResult();
        using var writeBack = WriteBack(handler);

        writeBack.Request(Guid.NewGuid());
        _time.Advance(LinkedTaskWriteBack.Grace);
        await writeBack.WhenIdleAsync();

        Assert.Empty(handler.Finished);
    }

    /// <summary>The Tasks pane marks its own saves in an async-local and ignores the
    /// task writes it hears inside that mark. The write-back's own write records the
    /// answer the source gave, which the pane has not seen: had the mark flowed into
    /// the background run, the refusal would reach the list only on a reload.</summary>
    [Fact]
    public async Task The_write_back_does_not_run_inside_the_requesters_async_local_state()
    {
        var mark = new AsyncLocal<bool>();
        var handler = new GatedHandler { Observe = () => mark.Value };
        handler.Gate.SetResult();
        using var writeBack = WriteBack(handler);

        mark.Value = true;
        writeBack.Request(Guid.NewGuid());
        _time.Advance(LinkedTaskWriteBack.Grace);
        await writeBack.WhenIdleAsync();

        Assert.Equal([false], handler.Observed);
    }

    /// <summary>A caller that already suppressed the flow is not refused: suppressing
    /// a second time would throw.</summary>
    [Fact]
    public async Task A_request_from_a_caller_that_already_suppressed_the_flow_still_runs()
    {
        var handler = new GatedHandler();
        handler.Gate.SetResult();
        using var writeBack = WriteBack(handler);
        var id = Guid.NewGuid();

        using (ExecutionContext.SuppressFlow())
        {
            writeBack.Request(id);
        }

        _time.Advance(LinkedTaskWriteBack.Grace);
        await writeBack.WhenIdleAsync();

        Assert.Equal([id], handler.Finished);
    }

    [Fact]
    public async Task With_nothing_requested_there_is_nothing_to_wait_for()
    {
        using var writeBack = WriteBack(new GatedHandler());

        await writeBack.WhenIdleAsync();
    }

    // --- The grace ------------------------------------------------------------------

    /// <summary>A tick by mistake, taken back within the grace, sends nothing: the
    /// handler asks the source only for a task still Done when it runs.</summary>
    [Fact]
    public async Task A_task_reopened_within_the_grace_is_never_sent_to_the_source()
    {
        var (handler, connector, task) = RealHandler();
        using var writeBack = WriteBack(handler);

        writeBack.Request(task.Id);
        _time.Advance(TimeSpan.FromSeconds(2));
        task.SetStatus(EntryStatus.Ready, DateOnly.FromDateTime(Now.Date));
        _time.Advance(LinkedTaskWriteBack.Grace);
        await writeBack.WhenIdleAsync();

        Assert.Empty(connector.Completed);
        _time.Advance(LinkedTaskWriteBack.SyncQuietPeriod);
        Assert.Equal(0, _sync.Requests);
    }

    /// <summary>End to end below the container: the requester finds the real handler
    /// in its scope, the handler completes the item, and the sync is asked for once
    /// the quiet period after the success has passed.</summary>
    [Fact]
    public async Task A_request_runs_the_handler_that_completes_the_item_and_then_asks_for_a_sync()
    {
        var (handler, connector, task) = RealHandler();
        using var writeBack = WriteBack(handler);

        writeBack.Request(task.Id);
        _time.Advance(LinkedTaskWriteBack.Grace);
        await writeBack.WhenIdleAsync();

        Assert.Equal(task.SourceRef, Assert.Single(connector.Completed));
        Assert.Equal(0, _sync.Requests);

        _time.Advance(LinkedTaskWriteBack.SyncQuietPeriod);
        Assert.Equal(1, _sync.Requests);
    }

    // --- The pace -------------------------------------------------------------------

    [Fact]
    public async Task A_burst_of_requests_reaches_the_sources_two_at_a_time()
    {
        var handler = new GatedHandler();
        using var writeBack = WriteBack(handler);

        for (var i = 0; i < 5; i++) writeBack.Request(Guid.NewGuid());
        _time.Advance(LinkedTaskWriteBack.Grace);
        await handler.Running(LinkedTaskWriteBack.MaxConcurrent).WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LinkedTaskWriteBack.MaxConcurrent, handler.MostAtOnce);

        handler.Gate.SetResult();
        await writeBack.WhenIdleAsync();

        Assert.Equal(5, handler.Finished.Count);
        Assert.Equal(LinkedTaskWriteBack.MaxConcurrent, handler.MostAtOnce);
    }

    [Fact]
    public async Task A_burst_of_successes_asks_for_one_sync_after_the_last_of_them()
    {
        var handler = new GatedHandler();
        handler.Gate.SetResult();
        using var writeBack = WriteBack(handler);

        for (var i = 0; i < 5; i++) writeBack.Request(Guid.NewGuid());
        _time.Advance(LinkedTaskWriteBack.Grace);
        await writeBack.WhenIdleAsync();
        Assert.Equal(0, _sync.Requests);

        _time.Advance(LinkedTaskWriteBack.SyncQuietPeriod);
        _time.Advance(LinkedTaskWriteBack.SyncQuietPeriod);

        Assert.Equal(5, handler.Finished.Count);
        Assert.Equal(1, _sync.Requests);
    }

    [Fact]
    public async Task A_refusal_asks_for_no_sync()
    {
        var handler = new GatedHandler { Outcome = LinkedTaskWriteBackOutcome.Refused };
        handler.Gate.SetResult();
        using var writeBack = WriteBack(handler);

        writeBack.Request(Guid.NewGuid());
        _time.Advance(LinkedTaskWriteBack.Grace);
        await writeBack.WhenIdleAsync();
        _time.Advance(LinkedTaskWriteBack.SyncQuietPeriod);

        Assert.Equal(0, _sync.Requests);
    }

    // --- Shutting down ----------------------------------------------------------------

    [Fact]
    public async Task A_write_back_still_in_its_grace_when_the_app_closes_is_never_sent()
    {
        var handler = new GatedHandler();
        handler.Gate.SetResult();
        var writeBack = WriteBack(handler);

        writeBack.Request(Guid.NewGuid());
        writeBack.Dispose();
        _time.Advance(LinkedTaskWriteBack.Grace);
        await writeBack.WhenIdleAsync();

        Assert.Equal(0, handler.Entered);
    }

    [Fact]
    public async Task A_write_back_running_when_the_app_closes_is_asked_to_stop()
    {
        var handler = new GatedHandler { HonourCancellation = true };
        var writeBack = WriteBack(handler);

        writeBack.Request(Guid.NewGuid());
        _time.Advance(LinkedTaskWriteBack.Grace);
        await handler.Running(1).WaitAsync(TestContext.Current.CancellationToken);
        writeBack.Dispose();
        await writeBack.WhenIdleAsync();

        Assert.Empty(handler.Finished);
        Assert.True(handler.SawCancellation);
    }

    /// <summary>The registration a desktop head makes: the save's port is one
    /// requester for the app, and the handler it runs is scoped, resolved per
    /// request.</summary>
    [Fact]
    public void The_linked_task_registration_brings_the_write_back_with_it()
    {
        var services = new ServiceCollection();

        services.AddLinkedTaskSync();

        var port = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ILinkedTaskWriteBack));
        Assert.Equal(ServiceLifetime.Singleton, port.Lifetime);
        Assert.Equal(typeof(LinkedTaskWriteBack), port.ImplementationType);

        var handler = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(ICommandHandler<CompleteLinkedTaskCommand, Result<LinkedTaskWriteBackOutcome>>));
        Assert.Equal(ServiceLifetime.Scoped, handler.Lifetime);
        Assert.Equal(typeof(CompleteLinkedTaskCommandHandler), handler.ImplementationType);
    }

    // --- Helpers ----------------------------------------------------------------------

    private LinkedTaskWriteBack WriteBack(object handler) =>
        new(new HandlerScopeFactory(handler), _time, _sync);

    private static (CompleteLinkedTaskCommandHandler Handler, CompletingConnector Connector, TaskItem Task) RealHandler()
    {
        var connector = new CompletingConnector();
        var task = new TaskItem(Guid.NewGuid(), "Ship the installer", string.Empty, EntryType.Task, EntryStatus.Done, Priority.Medium, null, null, null, Now);
        task.SetSourceRef(new SourceRef(CompletingConnector.Id, "JSdotNet/Backlog", "I_1", "u", "#1", null, "open", Now, normalisedState: NormalisedSourceState.Open));
        var handler = new CompleteLinkedTaskCommandHandler(
            [connector],
            new InMemoryTaskRepository(task),
            new InMemoryConnectedTargets(new ConnectedTarget(CompletingConnector.Id, "JSdotNet/Backlog") { CompleteAtSource = true }));
        return (handler, connector, task);
    }

    /// <summary>The handler as the requester sees it: held at a gate until the test
    /// opens it, then recording the task it finished or throwing. Counts how many
    /// runs were inside it at once.</summary>
    private sealed class GatedHandler : ICommandHandler<CompleteLinkedTaskCommand, Result<LinkedTaskWriteBackOutcome>>
    {
        private readonly Lock _lock = new();
        private readonly List<(int Count, TaskCompletionSource Reached)> _waits = [];
        private int _inside;

        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? Failure { get; init; }

        public LinkedTaskWriteBackOutcome Outcome { get; init; } = LinkedTaskWriteBackOutcome.Completed;

        /// <summary>Waits at the gate with the token, so a cancellation ends the wait.</summary>
        public bool HonourCancellation { get; init; }

        public bool SawCancellation { get; private set; }

        public List<Guid> Finished { get; } = [];

        public Func<bool>? Observe { get; init; }

        public List<bool> Observed { get; } = [];

        public int Entered { get; private set; }

        public int MostAtOnce { get; private set; }

        /// <summary>Completes once <paramref name="count"/> runs are inside at once.</summary>
        public Task Running(int count)
        {
            lock (_lock)
            {
                var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (_inside >= count) reached.SetResult();
                else _waits.Add((count, reached));
                return reached.Task;
            }
        }

        public async Task<Result<LinkedTaskWriteBackOutcome>> Handle(CompleteLinkedTaskCommand command, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                Entered++;
                _inside++;
                MostAtOnce = Math.Max(MostAtOnce, _inside);
                foreach (var wait in _waits.Where(wait => _inside >= wait.Count)) wait.Reached.TrySetResult();
                if (Observe is not null) Observed.Add(Observe());
            }

            try
            {
                if (HonourCancellation)
                {
                    try
                    {
                        await Gate.Task.WaitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        SawCancellation = true;
                        throw;
                    }
                }
                else
                {
                    await Gate.Task;
                }

                if (Failure is not null) throw Failure;
                lock (_lock) Finished.Add(command.TaskId);
                return Outcome;
            }
            finally
            {
                lock (_lock) _inside--;
            }
        }
    }
}
