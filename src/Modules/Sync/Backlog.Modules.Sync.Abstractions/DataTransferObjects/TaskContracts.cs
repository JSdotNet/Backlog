using System.Text.Json;
using System.Text.Json.Serialization;

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
/// day the desktop learns a new status, and .devbook/arc42/adr/0005 says the replica
/// runs no domain logic: a device writes the token, another device reads it
/// back, and the middle stays ignorant.
/// </para>
/// <para>
/// Every member is a plain value rather than a domain type for the same reason.
/// The Sync module has no reference to Backlog.Modules.Tasks and must not grow
/// one — the two contexts share a shape, not a model.
/// </para>
/// <para>
/// <b>A field this build has no member for is carried, not dropped.</b> See
/// <see cref="Unrecognised"/>.
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
    IReadOnlyList<ProjectionPayload> ProjectionRefs,
    // Last, and defaulted, because it arrived after the contract did: a document
    // written by an older build carries no such property and reads as unticked,
    // which is what it was.
    DateOnly? CompletedOn = null,
    // Only a capture carries these — the files it names in the attachment store
    // (local ADR 0014), metadata only. Defaulted for the reason CompletedOn is,
    // and null rather than empty on every document that has none, so a task
    // document serialises exactly as it did before.
    IReadOnlyList<AttachmentMetadata>? Attachments = null,
    // The day work first moved to in progress. Last and defaulted for the same
    // reason: an older document carries none and reads as never stamped.
    DateOnly? StartedOn = null,
    // The Devbook pages and chapters the task points at, as normalised
    // `path` or `path#anchor` strings. Last and defaulted for the same reason,
    // and null rather than empty on a task that points at none, so its document
    // serialises exactly as it did before; the service never reads one.
    IReadOnlyList<string>? DevbookReferences = null,
    // The day a person marked the task blocked by hand — not the Blocked
    // readiness a dependency chain derives, which is never stored or sent. Its
    // own member rather than left to ContentMd, because ContentMd is the body
    // alone and the `blocked:` token lives on the metadata line, which never
    // crosses the wire as text. Defaulted for the reason StartedOn is:
    // an older document carries none and reads as not marked, and a service
    // built before it passes it through in Unrecognised rather than dropping it.
    DateOnly? BlockedSince = null,
    // Where a linked task came from (local ADR 0020, §2). Last and defaulted for
    // the same reason: an older document carries none and reads as local work,
    // and null on every local task, so its document serialises as it did before.
    SourceRefPayload? SourceRef = null)
{
    /// <summary>
    /// Every property the document carried that this build has no member for,
    /// kept so it is written back out exactly as it arrived.
    /// <para>
    /// The service is deployed on its own schedule, and it reads a push into
    /// this record and writes the record to the store. Without this, a field a
    /// newer desktop added would be dropped by a service built before it: the
    /// stored document would lack it, every other device would pull it without
    /// it, and the next edit there would overwrite the originating device with
    /// the field gone. That is how <see cref="CompletedOn"/> was lost from every
    /// synced task until the service was redeployed. With the field held here,
    /// the service passes through what it cannot read, which is what
    /// .devbook/arc42/adr/0005's ignorant middle was always meant to do.
    /// </para>
    /// <para>
    /// Null when there was nothing unrecognised, so a payload a device builds
    /// compares and serialises as it did before.
    /// </para>
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Unrecognised { get; init; }
}

/// <summary>One checklist line under a task. <c>Status</c> is the same opaque
/// token the local store writes — <c>pending</c> or <c>done</c>.</summary>
public sealed record SubItemPayload(Guid Id, string Title, string Status, string? Notes, int Order)
{
    /// <summary>What this build has no member for, carried through as it
    /// arrived — see <see cref="TaskPayload.Unrecognised"/>.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Unrecognised { get; init; }
}

/// <summary>One recorded interaction with a task, kept because the desktop
/// ranks on it. The service never reads <c>Action</c>.</summary>
public sealed record UsageEventPayload(DateTimeOffset Timestamp, string Action)
{
    /// <summary>What this build has no member for, carried through as it
    /// arrived — see <see cref="TaskPayload.Unrecognised"/>.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Unrecognised { get; init; }
}

/// <summary>A task's link to something in a repository — an issue, a pull
/// request. Ids stay strings: they belong to the remote system's namespace and
/// nothing here resolves them.</summary>
public sealed record ProjectionPayload(string RepoId, string ExternalId, string TargetType)
{
    /// <summary>What this build has no member for, carried through as it
    /// arrived — see <see cref="TaskPayload.Unrecognised"/>.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Unrecognised { get; init; }
}

/// <summary>A linked task's pointer at the item it follows in another system.
/// Every member is the opaque value the device wrote: the connector id is a
/// string the service never resolves, and <c>Flags</c> are the Backlog-owned sync
/// flags as written, so a flag this build has no name for still travels.
/// <c>NormalisedState</c> is the word the device last normalised the source's
/// state to (<c>open</c>, <c>active</c>, <c>done</c>, <c>dropped</c>), last and
/// defaulted because it arrived after the rest: an older document carries none,
/// and the device reading it treats the state as moved, once. <c>SourceTitle</c>
/// is the item's title at the source when last synced, which tells a local rename
/// from a stale title; last and defaulted for the same reason. <c>Blocked</c> and
/// <c>BlockedReason</c> are the source's own word that the item cannot be worked
/// on now, defaulted after those for the same reason. <c>WaitsOn</c> is the
/// external ids of the items the source says this one waits on, defaulted last
/// for the same reason: an older document waits on nothing.</summary>
public sealed record SourceRefPayload(
    string ConnectorId,
    string Target,
    string ExternalId,
    string Url,
    string DisplayKey,
    string? Assignee,
    string SourceState,
    DateTimeOffset SourceUpdatedAt,
    IReadOnlyList<string>? Flags = null,
    string? NormalisedState = null,
    string? SourceTitle = null,
    bool? Blocked = null,
    string? BlockedReason = null,
    IReadOnlyList<string>? WaitsOn = null)
{
    /// <summary>What this build has no member for, carried through as it
    /// arrived — see <see cref="TaskPayload.Unrecognised"/>.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Unrecognised { get; init; }
}

/// <summary>
/// One task's state at one moment, as a device pushes it or pulls it.
/// <para>
/// It carries no owner and no device id, and that absence is the security
/// property: both come from the caller's validated token, so there is no field
/// a client could set to write into somebody else's data
/// (.devbook/arc42/adr/0005 §Identity). <see cref="TaskChangeRecord"/> is where the
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
