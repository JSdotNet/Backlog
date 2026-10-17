using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The adapters behind the blocked-task policy (local ADR 0035 §5–6): what Tasks answers as
/// blocked, and the watcher that runs the policy on every local task write — so a
/// <c>blocked:</c> token and a <c>set_blocked</c> call, both task writes, raise an
/// impediment without Tasks ever calling Progress.
/// </summary>
public sealed class BlockedTaskWatcherTests
{
    private static readonly DateOnly Since = new(2026, 10, 17);

    private static TaskItemDto Entry(string title, EntryStatus status, DateOnly? blockedSince) =>
        new(
            Guid.NewGuid(),
            title,
            Body: string.Empty,
            EntryType.Prompt,
            Priority.Medium,
            status,
            Area: null,
            [],
            Order: 0,
            TotalSubItems: 0,
            CompletedSubItems: 0,
            [],
            BlockedSince: blockedSince);

    [Fact]
    public void Blocked_reads_the_marked_tasks_that_are_not_finished()
    {
        var blocked = Entry("Bank import mapping", EntryStatus.InProgress, Since);
        var ready = Entry("Ready and blocked", EntryStatus.Ready, Since.AddDays(-2));

        var read = BlockedTaskSource.Select(
        [
            blocked,
            ready,
            Entry("Not blocked", EntryStatus.InProgress, null),
            Entry("Done", EntryStatus.Done, Since),
            Entry("Archived", EntryStatus.Archived, Since),
        ]);

        Assert.Equal(
            [new BlockedTaskDto(blocked.Id, "Bank import mapping", Since), new BlockedTaskDto(ready.Id, "Ready and blocked", Since.AddDays(-2))],
            read);
    }

    [Fact]
    public async Task A_task_write_runs_the_policy_in_a_scope_of_its_own()
    {
        var (watcher, signal, policy, provider) = Compose();
        using var _ = provider;
        using var __ = watcher;

        signal.Raise();
        await watcher.Idle;

        Assert.Equal(1, policy.Runs);
    }

    [Fact]
    public async Task Writes_during_a_run_fold_into_one_more_run()
    {
        var (watcher, signal, policy, provider) = Compose();
        using var _ = provider;
        using var __ = watcher;
        var hold = new TaskCompletionSource();
        policy.Hold = hold;

        signal.Raise();
        await policy.Started.Task;
        signal.Raise();
        signal.Raise();
        signal.Raise();
        hold.SetResult();

        await WaitForAsync(() => policy.Runs == 2);
        await watcher.Idle;
        Assert.Equal(2, policy.Runs);
    }

    [Fact]
    public async Task A_failed_run_never_reaches_the_writer_and_the_next_write_runs_again()
    {
        var (watcher, signal, policy, provider) = Compose();
        using var _ = provider;
        using var __ = watcher;
        policy.Throw = true;

        signal.Raise();
        await watcher.Idle;

        policy.Throw = false;
        signal.Raise();
        await watcher.Idle;

        Assert.Equal(2, policy.Runs);
    }

    [Fact]
    public async Task Once_disposed_it_hears_nothing()
    {
        var (watcher, signal, policy, provider) = Compose();
        using var _ = provider;

        watcher.Dispose();
        signal.Raise();
        await watcher.Idle;

        Assert.Equal(0, policy.Runs);
    }

    private static (BlockedTaskWatcher Watcher, FakeSignal Signal, CountingPolicy Policy, ServiceProvider Provider) Compose()
    {
        var signal = new FakeSignal();
        var policy = new CountingPolicy();
        var services = new ServiceCollection();
        services.AddSingleton<ITaskChangeSignal>(signal);
        services.AddScoped<IBlockedTaskPolicy>(_ => policy);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var watcher = new BlockedTaskWatcher(provider.GetRequiredService<IServiceScopeFactory>(), signal);
        return (watcher, signal, policy, provider);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
    }

    private sealed class FakeSignal : ITaskChangeSignal
    {
        public event Action? Changed;

        public void Raise() => Changed?.Invoke();

        public IDisposable Suppress() => throw new NotSupportedException();
    }

    private sealed class CountingPolicy : IBlockedTaskPolicy
    {
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        public bool Throw { get; set; }

        public TaskCompletionSource? Hold { get; set; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<ImpedimentDto>> ReactAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _runs);
            Started.TrySetResult();

            if (Hold is { } hold)
            {
                Hold = null;
                await hold.Task;
            }

            if (Throw) throw new InvalidOperationException("The store is gone.");

            return [];
        }
    }
}
