using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// Wire records built the way the client builds them — through a real
/// <see cref="TaskItem"/> and <see cref="TaskReplicaMerge.ToChange"/> — rather
/// than by filling twenty-five positional arguments by hand. A literal payload
/// would go stale silently the day a field is added, and would let a test pass
/// against a shape the client never produces.
/// </summary>
internal static class TaskChanges
{
    /// <summary>A task with the stamps a test wants it to carry — live unless
    /// <paramref name="deletedAt"/> says otherwise. The stamps are loaded last,
    /// exactly as storage and the merge load them, because every setter on the
    /// aggregate restamps.
    /// <para>
    /// A tombstone is made this way rather than through <c>MarkDeleted</c>, which
    /// stamps <c>DateTimeOffset.UtcNow</c>: a deletion whose stamp is the wall
    /// clock cannot be placed either side of a watermark, and where it sits
    /// relative to one is the whole of what these tests assert.
    /// </para>
    /// </summary>
    public static TaskItem Task(
        string title,
        DateTimeOffset updatedAt,
        Guid? id = null,
        DateTimeOffset? deletedAt = null)
    {
        var task = new TaskItem(
            id ?? Guid.NewGuid(),
            title,
            string.Empty,
            EntryType.Task,
            EntryStatus.Draft,
            Priority.Medium,
            repoIds: null,
            tags: null,
            sourceInboxId: null,
            createdAt: updatedAt);

        task.LoadStamps(updatedAt, deletedAt);

        return task;
    }

    public static TaskChange Change(string title, DateTimeOffset updatedAt, Guid? id = null) =>
        TaskReplicaMerge.ToChange(Task(title, updatedAt, id));

    public static TaskChangeRecord Record(
        TaskChange change,
        Guid deviceId,
        long serverTimestamp) =>
        new(change, deviceId, serverTimestamp);
}
