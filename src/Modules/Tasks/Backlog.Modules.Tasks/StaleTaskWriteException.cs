namespace Backlog.Modules.Tasks;

/// <summary>
/// A save refused because the store no longer holds the version the task was read
/// at: somebody else saved it in between, and writing this copy would put the older
/// version back over theirs with nothing said (see <see cref="DomainModels.TaskItem.LoadedUpdatedAt"/>).
/// Nothing was written.
/// <para>
/// An exception at the port rather than a return value, so a writer that does not
/// handle the refusal fails loudly instead of reporting a save that never happened.
/// <c>TaskItems</c> turns it into <c>tasks.stale_write</c> for every published write
/// that answers a <c>Result</c>, and retries a usage record once; delete and reorder,
/// which answer nothing, let it through to callers that already catch their failures.
/// </para>
/// </summary>
public sealed class StaleTaskWriteException(Guid taskId)
    : InvalidOperationException($"Task {taskId} changed in the store after it was read; the save was refused.")
{
    /// <summary>The task whose save was refused.</summary>
    public Guid TaskId { get; } = taskId;
}
