using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Backlog.Infrastructure.Cosmos;

/// <summary>
/// How the task, annotation and session replicas write a pushed batch: several
/// documents at once, never two writes to one document at once.
/// <para>
/// Each document is still its own request — the replicas give the reason a
/// transactional batch will not do — and each write is still the replica's own
/// read-compare-write. What this adds is that a two-hundred-item push no longer
/// waits on four hundred Gateway round trips end to end. The bound is a
/// constant rather than a setting, and it is per push: one push stays well
/// inside the client's Gateway connection limit, but the client is shared, so
/// several large pushes at once can queue for connections, and that wait
/// counts against the request timeout.
/// </para>
/// <para>
/// What the one-at-a-time loop promised about a batch that succeeds is kept.
/// Writes to one document run in the order they were sent, so the later of two
/// versions in one batch is the one that lands, and a session record sent twice
/// ends as the last copy. The count is the number of writes the replica
/// accepted.
/// </para>
/// <para>
/// A batch that fails is looser than the loop was, in two ways. Documents
/// after the failing one may already be written, so what landed is no longer a
/// prefix of the batch; every write is idempotent against the version it
/// carries, so the device's retry is still right, but a caller with a side
/// effect per landed document has to check what landed rather than assume.
/// And the failure reported is the earliest in the batch among the failures
/// that happened: a quick failure on a later document stops an earlier one
/// still in flight, which then never gets to fail. It is always one of the
/// coded exceptions a write raised, never an aggregate, and nothing starts
/// after it.
/// </para>
/// </summary>
internal static class ReplicaWrites
{
    /// <summary>How many documents of one batch are written at once.</summary>
    internal const int Concurrency = 8;

    /// <summary>
    /// Writes every item, documents side by side and each document's own items
    /// in input order, and returns how many writes the replica accepted.
    /// </summary>
    /// <param name="items">The batch, in the order the device sent it.</param>
    /// <param name="key">The document an item writes to. Items with one key
    /// never run at the same time.</param>
    /// <param name="write">One item's write; true when the replica took it.</param>
    /// <param name="cancellationToken">The caller's; cancelling it surfaces as
    /// <see cref="OperationCanceledException"/>.</param>
    internal static async Task<int> WriteAllAsync<T>(
        IReadOnlyList<T> items,
        Func<T, string> key,
        Func<T, CancellationToken, Task<bool>> write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(write);

        // Cancelled on the first failure as well as by the caller, so writes
        // still in flight stop and none starts after a failure — the batch is
        // failing whatever they would have done.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var failures = new ConcurrentDictionary<int, ExceptionDispatchInfo>();
        var accepted = 0;
        var interrupted = false;

        await Parallel.ForEachAsync(
            ByDocument(items, key),
            new ParallelOptions { MaxDegreeOfParallelism = Concurrency },
            async (document, _) =>
            {
                foreach (var index in document)
                {
                    if (stop.IsCancellationRequested)
                    {
                        Volatile.Write(ref interrupted, true);
                        return;
                    }

                    try
                    {
                        if (await write(items[index], stop.Token).ConfigureAwait(false))
                        {
                            Interlocked.Increment(ref accepted);
                        }
                    }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested)
                    {
                        // Stopped by a failure beside it or by the caller; this
                        // document did not fail, so it is not what gets reported.
                        Volatile.Write(ref interrupted, true);
                        return;
                    }
                    catch (Exception failure)
                    {
                        failures[index] = ExceptionDispatchInfo.Capture(failure);
                        await stop.CancelAsync().ConfigureAwait(false);
                        return;
                    }
                }
            }).ConfigureAwait(false);

        if (!failures.IsEmpty)
        {
            // By position rather than by which landed first: of the failures
            // that happened, the earliest in the batch is the one the
            // one-at-a-time loop would have been nearest to reporting.
            failures[failures.Keys.Min()].Throw();
        }

        if (interrupted)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return accepted;
    }

    /// <summary>The batch's indexes, one list per document in the order each
    /// document first appears, each list in input order.</summary>
    private static List<List<int>> ByDocument<T>(IReadOnlyList<T> items, Func<T, string> key)
    {
        var documents = new List<List<int>>();
        var byKey = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        for (var index = 0; index < items.Count; index++)
        {
            var documentKey = key(items[index]);
            if (!byKey.TryGetValue(documentKey, out var document))
            {
                document = [];
                byKey.Add(documentKey, document);
                documents.Add(document);
            }

            document.Add(index);
        }

        return documents;
    }
}
