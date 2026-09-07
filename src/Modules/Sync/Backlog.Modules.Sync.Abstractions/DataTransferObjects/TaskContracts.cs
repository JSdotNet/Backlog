namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>
/// One task as it crosses the wire, field for field as the local SQLite store
/// already writes it.
/// <para>
/// The enum-ish members — <see cref="Type"/>, <see cref="Status"/>,
/// <see cref="Priority"/>, <see cref="Recurrence"/> and <see cref="View"/> — are
/// strings here on purpose. They carry the same opaque tokens the SQLite
/// columns hold (<c>task</c>, <c>draft</c>, <c>in_progress</c>, <c>medium</c>,
/// <c>weekdays</c>, <c>steps</c>, and the rest), and the sync service never
/// parses one. A service that understood them would have to be redeployed the
/// day the desktop learns a new status, and .arc42/adr/0005 says the replica
/// runs no domain logic: a device writes the token, another device reads it
/// back, and the middle stays ignorant.
/// </para>
/// <para>
/// Every member is a plain value rather than a domain type for the same reason.
/// The Sync module has no reference to Backlog.Modules.Tasks and must not grow
/// one — the two contexts share a shape, not a model.
/// </para>
/// </summary>
public sealed record TaskPayload(
    string Title,
    string ContentMd,
    string Type,
    string Status,
    string Priority,
    int Order,
    string? Area,
    DateTimeOffset CreatedAt,
    string? SourceInboxId,
    Guid? RecurrenceSourceId,
    DateOnly? DueOn,
    DateTime? RemindAt,
    string? Recurrence,
    DateOnly? InMyDayOn,
    string? View,
    int? Effort,
    string? ImportPlanId,
    string? ImportItemId,
    string? AttachmentPath,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> RepoIds,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<SubItemPayload> SubItems,
    IReadOnlyList<UsageEventPayload> UsageEvents,
    IReadOnlyList<ProjectionPayload> ProjectionRefs);

/// <summary>One checklist line under a task. <c>Status</c> is the same opaque
/// token the local store writes — <c>pending</c> or <c>done</c>.</summary>
public sealed record SubItemPayload(Guid Id, string Title, string Status, string? Notes, int Order);

/// <summary>One recorded interaction with a task, kept because the desktop
/// ranks on it. The service never reads <c>Action</c>.</summary>
public sealed record UsageEventPayload(DateTimeOffset Timestamp, string Action);

/// <summary>A task's link to something in a repository — an issue, a pull
/// request. Ids stay strings: they belong to the remote system's namespace and
/// nothing here resolves them.</summary>
public sealed record ProjectionPayload(string RepoId, string ExternalId, string TargetType);

/// <summary>
/// One task's state at one moment, as a device pushes it or pulls it.
/// <para>
/// It carries no owner and no device id, and that absence is the security
/// property: both come from the caller's validated token, so there is no field
/// a client could set to write into somebody else's data
/// (.arc42/adr/0005 §Identity). <see cref="TaskChangeRecord"/> is where the
/// service adds them back on the way out.
/// </para>
/// <para>
/// A deletion is a change like any other, with <see cref="DeletedAt"/> set —
/// a tombstone rather than an absence, because a device that has been offline
/// has no way to tell "deleted" from "never seen" and the whole document is the
/// unit of last-write-wins.
/// </para>
/// </summary>
public sealed record TaskChange(Guid Id, DateTimeOffset UpdatedAt, DateTimeOffset? DeletedAt, TaskPayload Task);

/// <summary>
/// A change as it comes back out of the replica: the change itself, the device
/// that wrote it, and the store's own ordering stamp.
/// <para>
/// <paramref name="DeviceId"/> lets a client drop its own echo instead of
/// re-applying what it just pushed. <paramref name="ServerTimestamp"/> is the
/// store's stamp and not a clock a device controls, so two devices with skewed
/// clocks still agree on the order the service saw.
/// </para>
/// </summary>
public sealed record TaskChangeRecord(TaskChange Change, Guid DeviceId, long ServerTimestamp);

/// <summary>A batch of changes from one device. Batched because a device that
/// has been offline for a week has a week of them.</summary>
public sealed record PushTasksRequest(IReadOnlyList<TaskChange> Tasks);

/// <summary>How many changes the replica took. There is no per-item result: a
/// whole-document last-write-wins upsert has nothing to reject.</summary>
public sealed record PushTasksResponse(int Accepted);

/// <summary>
/// One page of the owner's change feed, and where to resume.
/// <para>
/// <paramref name="Since"/> is always a fresh cursor, including on a drained
/// feed — the feed's position advances whether or not the page had anything in
/// it, and a client that kept its old cursor would re-scan from there forever.
/// <paramref name="HasMore"/> is the service's own "there is more of your feed
/// after this page", and false means the store has said the feed is drained
/// rather than that the page came back short. A client keeps pulling until it is
/// false, and may not stop early on a page smaller than it asked for — a page
/// size is a hint to the store, not a promise.
/// </para>
/// </summary>
public sealed record PullTasksResponse(IReadOnlyList<TaskChangeRecord> Tasks, string Since, bool HasMore);
