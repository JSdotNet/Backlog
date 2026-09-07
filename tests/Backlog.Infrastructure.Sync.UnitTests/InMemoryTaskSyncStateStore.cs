namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The sync-state port with no file behind it, recording every state it was
/// handed. The order of the saves is the assertion in more than one test — a
/// cursor saved after each page rather than once at the end is the whole
/// difference between a pull that resumes and one that restarts.
/// </summary>
internal sealed class InMemoryTaskSyncStateStore : ITaskSyncStateStore
{
    public InMemoryTaskSyncStateStore(TaskSyncState? initial = null) =>
        Current = initial ?? new TaskSyncState(DateTimeOffset.MinValue, null);

    public event Action? Changed;

    public TaskSyncState Current { get; private set; }

    public List<TaskSyncState> Saved { get; } = [];

    public string StorePath => "in memory";

    public void Save(TaskSyncState state)
    {
        Current = state;
        Saved.Add(state);
        Changed?.Invoke();
    }
}
