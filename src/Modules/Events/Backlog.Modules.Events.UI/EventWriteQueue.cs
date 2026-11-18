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

    /// <summary>Runs <paramref name="write"/> once every write asked for before it has finished.</summary>
    public Task<T> RunAsync<T>(Func<Task<T>> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var run = RunAfterAsync(_tail, write);
        _tail = run.ContinueWith(
            static _ => { },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return run;
    }

    private static async Task<T> RunAfterAsync<T>(Task previous, Func<Task<T>> write)
    {
        await previous;
        return await write();
    }
}
