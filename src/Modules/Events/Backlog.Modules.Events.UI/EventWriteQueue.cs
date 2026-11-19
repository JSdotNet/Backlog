namespace Backlog.Modules.Events.UI;

/// <summary>
/// Runs the Events pane's writes one at a time, in the order they were asked for.
/// <para>
/// Every write loads the whole event, changes it and saves it back, and Add prep task waits on
/// Tasks between the load and the save. Two writes at once would let the slower one save the
/// document it loaded before the other landed, undoing it: a rename typed while a prep task was
/// being filed came back as the old title. One queue per pane, shared by the editor and its
/// sections, so no write starts before the last one saved.
/// Driven from one renderer, as every handler in the pane is, so the tail needs no lock.
/// </para>
/// </summary>
public sealed class EventWriteQueue
{
    /// <summary>The last write asked for, settled either way, so a refused or failed write never
    /// stops the ones behind it.</summary>
    private Task _tail = Task.CompletedTask;

    /// <summary>The writes asked for and not settled yet. Settled off the renderer, so counted
    /// with interlocked operations.</summary>
    private int _outstanding;

    /// <summary>Whether no write asked for is still out. A change saved elsewhere reads the open
    /// event again only then: one read mid-write would hand the editor the event as it stood
    /// before its own write landed.</summary>
    public bool IsIdle => Volatile.Read(ref _outstanding) == 0;

    /// <summary>Raised when the last write out settles, held, refused or failed, so the queue is
    /// idle. On the thread that settled it; a screen marshals to its own.</summary>
    public event Action? Drained;

    /// <summary>Runs <paramref name="write"/> once every write asked for before it has finished.</summary>
    public Task<T> RunAsync<T>(Func<Task<T>> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        Interlocked.Increment(ref _outstanding);
        var run = RunAfterAsync(_tail, write);
        _tail = run.ContinueWith(
            _ =>
            {
                if (Interlocked.Decrement(ref _outstanding) == 0) Drained?.Invoke();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return run;
    }

    private static async Task<T> RunAfterAsync<T>(Task previous, Func<Task<T>> write)
    {
        // The tail never stops the writes behind it, a Drained handler that threw included.
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        return await write();
    }
}
