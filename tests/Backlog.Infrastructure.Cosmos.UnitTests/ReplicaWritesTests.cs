using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

/// <summary>
/// How the task, annotation and session replicas write a pushed batch: several
/// documents at once rather than one at a time, two changes to one document in
/// the order they were sent, and the earliest failure that actually happened
/// reported on its own. None of it needs Cosmos — the write is the
/// replica's, and what is pinned here is everything around it.
/// </summary>
public sealed class ReplicaWritesTests
{
    [Fact]
    public async Task The_count_is_the_number_of_writes_the_replica_accepted()
    {
        // Two refused — a stale copy, an echo — among five.
        var items = new[] { ("a", true), ("b", false), ("c", true), ("d", false), ("e", true) };

        var accepted = await ReplicaWrites.WriteAllAsync(
            items,
            item => item.Item1,
            (item, _) => Task.FromResult(item.Item2),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, accepted);
    }

    /// <summary>
    /// Every write is held open until the bound — eight — is in flight, then
    /// given time for one more to start if anything would let it. A loop that
    /// writes one at a time never reaches two; one with no bound reaches twenty.
    /// </summary>
    [Fact]
    public async Task Writes_run_as_many_at_a_time_as_the_bound_allows_and_no_more()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var items = Enumerable.Range(0, 20).ToArray();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = 0;
        var peak = 0;

        var writing = ReplicaWrites.WriteAllAsync(
            items,
            item => $"task-{item}",
            async (_, _) =>
            {
                var now = Interlocked.Increment(ref inFlight);
                InterlockedMax(ref peak, now);
                await release.Task.ConfigureAwait(false);
                Interlocked.Decrement(ref inFlight);
                return true;
            },
            cancellationToken);

        try
        {
            await Polling.WaitUntilAsync(
                () => Task.FromResult(Volatile.Read(ref inFlight) >= ReplicaWrites.Concurrency),
                () => $"Only {Volatile.Read(ref inFlight)} writes were in flight at once.");
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
        finally
        {
            release.TrySetResult();
        }

        Assert.Equal(20, await writing);
        Assert.Equal(ReplicaWrites.Concurrency, peak);
    }

    /// <summary>
    /// Three versions of one task in one batch, with two other tasks beside
    /// them. Every write yields before it finishes, so a loop that let the
    /// three run side by side would show it — and a version written out of
    /// order would leave an older copy as the last one the replica saw.
    /// </summary>
    [Fact]
    public async Task Changes_to_one_document_are_written_in_the_order_they_were_sent()
    {
        var items = new[] { ("a", 1), ("b", 1), ("a", 2), ("c", 1), ("a", 3) };
        var written = new List<int>();
        var gate = new object();
        var inFlightForA = 0;
        var overlapped = false;

        await ReplicaWrites.WriteAllAsync(
            items,
            item => item.Item1,
            async (item, token) =>
            {
                if (item.Item1 != "a")
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(5), token).ConfigureAwait(false);
                    return true;
                }

                overlapped |= Interlocked.Increment(ref inFlightForA) > 1;
                await Task.Delay(TimeSpan.FromMilliseconds(20), token).ConfigureAwait(false);
                lock (gate)
                {
                    written.Add(item.Item2);
                }

                Interlocked.Decrement(ref inFlightForA);
                return true;
            },
            TestContext.Current.CancellationToken);

        Assert.False(overlapped, "Two writes to one document were in flight at once.");
        Assert.Equal([1, 2, 3], written);
    }

    /// <summary>
    /// Of two failures that both happen, the one earlier in the batch is
    /// reported — by position, not by which landed first. The second document
    /// already has its 413 in hand, past the last point its token could stop
    /// it, when the fourth document's 429 lands; the 413 is what the device
    /// hears, as the coded exception itself rather than wrapped in an aggregate.
    /// </summary>
    [Fact]
    public async Task Of_two_failures_the_one_earlier_in_the_batch_is_reported_even_when_it_lands_second()
    {
        var tooLarge = new SyncReplicaException(SyncErrorCodes.TaskTooLarge, "too large");
        var busy = new SyncReplicaException(SyncErrorCodes.ReplicaBusy, "busy");
        var busyThrown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var items = Enumerable.Range(0, 5).ToArray();

        var thrown = await Assert.ThrowsAsync<SyncReplicaException>(() => ReplicaWrites.WriteAllAsync(
            items,
            item => $"task-{item}",
            async (item, _) =>
            {
                switch (item)
                {
                    case 1:
                        await busyThrown.Task.ConfigureAwait(false);
                        throw tooLarge;
                    case 3:
                        busyThrown.SetResult();
                        throw busy;
                    default:
                        return true;
                }
            },
            TestContext.Current.CancellationToken));

        Assert.Same(tooLarge, thrown);
    }

    /// <summary>
    /// The other half of that: an earlier write still waiting on its token
    /// when a later one fails is stopped, never gets to fail, and the later
    /// failure is what the device hears — not the stop, and not nothing. The
    /// one-at-a-time loop would have reported the earlier document; this is
    /// the looser guarantee the replicas now give.
    /// </summary>
    [Fact]
    public async Task A_later_failure_stops_an_earlier_write_still_waiting_and_is_what_is_reported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var busy = new SyncReplicaException(SyncErrorCodes.ReplicaBusy, "busy");
        var firstWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var items = Enumerable.Range(0, 5).ToArray();

        var writing = ReplicaWrites.WriteAllAsync(
            items,
            item => $"task-{item}",
            async (item, token) =>
            {
                switch (item)
                {
                    case 1:
                        firstWaiting.SetResult();
                        await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                        return true;
                    case 3:
                        await firstWaiting.Task.WaitAsync(token).ConfigureAwait(false);
                        throw busy;
                    default:
                        return true;
                }
            },
            cancellationToken);

        var thrown = await Assert.ThrowsAsync<SyncReplicaException>(
            () => writing.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken));

        Assert.Same(busy, thrown);
    }

    /// <summary>
    /// The first document fails once eight writes are in flight. The seven
    /// beside it are waiting on the token they were handed and must be stopped
    /// by it — their cancellation is not the failure reported — and the twelve
    /// still queued must never start.
    /// </summary>
    [Fact]
    public async Task Nothing_starts_after_a_write_fails_and_the_writes_beside_it_are_stopped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var busy = new SyncReplicaException(SyncErrorCodes.ReplicaBusy, "busy");
        var allSlotsBusy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var items = Enumerable.Range(0, 20).ToArray();

        var writing = ReplicaWrites.WriteAllAsync(
            items,
            item => $"task-{item}",
            async (item, token) =>
            {
                if (Interlocked.Increment(ref started) == ReplicaWrites.Concurrency)
                {
                    allSlotsBusy.SetResult();
                }

                if (item == 0)
                {
                    await allSlotsBusy.Task.ConfigureAwait(false);
                    throw busy;
                }

                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);

        var thrown = await Assert.ThrowsAsync<SyncReplicaException>(
            () => writing.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken));

        Assert.Same(busy, thrown);
        Assert.Equal(ReplicaWrites.Concurrency, Volatile.Read(ref started));
    }

    [Fact]
    public async Task Cancelling_the_push_surfaces_as_cancellation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var push = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var started = 0;
        var items = Enumerable.Range(0, 20).ToArray();

        var writing = ReplicaWrites.WriteAllAsync(
            items,
            item => $"task-{item}",
            async (_, token) =>
            {
                Interlocked.Increment(ref started);
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                return true;
            },
            push.Token);

        await Polling.WaitUntilAsync(() => Task.FromResult(Volatile.Read(ref started) >= ReplicaWrites.Concurrency));
        await push.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => writing.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken));
    }

    [Fact]
    public async Task An_empty_batch_writes_nothing()
    {
        var writes = 0;

        var accepted = await ReplicaWrites.WriteAllAsync(
            Array.Empty<string>(),
            item => item,
            (_, _) =>
            {
                writes++;
                return Task.FromResult(true);
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(0, accepted);
        Assert.Equal(0, writes);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var seen = Volatile.Read(ref target);
        while (value > seen)
        {
            var previous = Interlocked.CompareExchange(ref target, value, seen);
            if (previous == seen)
            {
                return;
            }

            seen = previous;
        }
    }
}
