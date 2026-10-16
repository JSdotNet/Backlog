namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// A cache holding at most a fixed number of entries, dropping the least
/// recently used. Each entry is the read's task, so two callers asking for one
/// key at once share one read.
/// <para>
/// Lifted out of <see cref="DevbookBranchReview"/> so the branch overview and the
/// review share one comparison cache rather than two copies of the same rules.
/// </para>
/// </summary>
internal sealed class BoundedTaskCache<T>(int capacity)
{
    private readonly Dictionary<string, LinkedListNode<(string Key, Task<T> Task)>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, Task<T> Task)> _recency = new();
    private readonly Lock _gate = new();

    /// <summary>
    /// A shared read, started on no caller's token so no caller can cancel it for
    /// the others; each caller stops waiting on its own. A read that failed or was
    /// cancelled is dropped, so the next ask tries again rather than meeting the
    /// same failure from the cache.
    /// </summary>
    public async Task<T> ReadAsync(string key, Func<Task<T>> read, CancellationToken cancellationToken)
    {
        var task = GetOrStart(key, read);

        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (task.IsFaulted || task.IsCanceled)
        {
            Evict(key, task);
            throw;
        }
    }

    /// <summary>
    /// The read already cached for the key, or a new one. Only a placeholder
    /// is taken under the lock: the read itself starts after it is released,
    /// because its synchronous prelude — resolving a credential, starting the
    /// GitHub CLI — would otherwise hold every other window's lookup.
    /// </summary>
    public Task<T> GetOrStart(string key, Func<Task<T>> read)
    {
        TaskCompletionSource<T> placeholder;

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var found))
            {
                _recency.Remove(found);
                _recency.AddFirst(found);
                return found.Value.Task;
            }

            placeholder = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _entries[key] = _recency.AddFirst((key, placeholder.Task));

            while (_entries.Count > capacity && _recency.Last is { } oldest)
            {
                _recency.RemoveLast();
                _entries.Remove(oldest.Value.Key);
            }
        }

        _ = RunAsync(key, placeholder, read);
        return placeholder.Task;
    }

    /// <summary>
    /// Runs the read into its placeholder. A read that fails is dropped when
    /// it fails, not when somebody next asks: every waiter may have left
    /// already, and the next ask must not be handed the old failure. So the
    /// entry goes before the failure is set, never after — a continuation on
    /// the placeholder would run a thread-pool hop later, because the
    /// placeholder runs its continuations asynchronously, and an ask in that
    /// hop found the failure still cached.
    /// </summary>
    private async Task RunAsync(string key, TaskCompletionSource<T> placeholder, Func<Task<T>> read)
    {
        try
        {
            placeholder.TrySetResult(await read().ConfigureAwait(false));
        }
        catch (OperationCanceledException cancelled)
        {
            Evict(key, placeholder.Task);
            placeholder.TrySetCanceled(cancelled.CancellationToken);
        }
        catch (Exception exception)
        {
            Evict(key, placeholder.Task);
            placeholder.TrySetException(exception);

            // Observed here, because nobody may be left to: a failure no one
            // read would surface later as an unobserved task exception.
            _ = placeholder.Task.Exception;
        }
    }

    public void Evict(string key, Task<T> task)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var found) || !ReferenceEquals(found.Value.Task, task)) return;

            _recency.Remove(found);
            _entries.Remove(key);
        }
    }
}
