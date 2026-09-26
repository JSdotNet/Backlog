using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// What applying one page did: how many documents were written, and how many
/// this build could not read.
/// <para>
/// The two are counted apart because they mean opposite things about the health
/// of a sync. Nothing applied is the ordinary state of a device that is already
/// up to date; anything skipped is a document that will not land until this
/// build learns the token it carries, and a person looking at a sync that keeps
/// receiving and never applying deserves to be able to tell those apart.
/// </para>
/// </summary>
public readonly record struct TaskMergeOutcome(int Applied, int Skipped);

/// <summary>
/// The one place remote data overwrites local canonical data.
/// <para>
/// It is deliberately the only caller of <see cref="ITaskRepository.SaveAsync"/>
/// in this project. A second writer would be a second answer to "which copy
/// wins", and the two would disagree the first time somebody edited a task on
/// both machines in the same minute.
/// </para>
/// <para>
/// <b>Two decisions, and they are not the same one.</b> Reducing a page to one
/// record per task is <see cref="Wins"/>, below. Deciding that record against
/// what this machine already holds is <c>ApplyOneAsync</c>, and it cannot use
/// <see cref="Wins"/> because nothing local records which device wrote a task or
/// where the replica had got to when it did — the local side has one of the
/// three fields. It answers on the one it has: is the inbound copy a later
/// version than the local one? See that method's own remarks.
/// </para>
/// <para>
/// <b>Reducing a page.</b> Whole documents, last write wins, decided on three
/// fields in order — the inbound copy wins only if it is strictly greater on the
/// first field that differs:
/// </para>
/// <list type="number">
/// <item><see cref="TaskChangeRecord.ServerTimestamp"/> descending. The store's
/// own stamp, not a clock any device controls, so two machines with skewed
/// clocks still agree on the order the service saw.</item>
/// <item><see cref="TaskChange.UpdatedAt"/> descending. Cosmos writes
/// <c>_ts</c> at one-second granularity, so a tie on the first field is a
/// routine event rather than a theoretical one: two devices coming back online
/// together land in the same second regularly, and without this the winner
/// would be whichever the feed happened to list first.</item>
/// <item><see cref="TaskChangeRecord.DeviceId"/> descending, compared
/// <see cref="StringComparison.Ordinal"/>. Arbitrary, and that is the point —
/// it is a rule both devices apply to the same two values and therefore reach
/// the same answer from, which no comparison of timestamps can promise once
/// those are equal.</item>
/// </list>
/// <para>
/// Equal on all three is the same write arriving twice, and the later of two
/// identical records is neither.
/// </para>
/// <para>
/// <b>Tombstones.</b> The local side is read through
/// <see cref="ITaskRepository.GetIncludingDeletedAsync"/> and never through
/// <c>GetAsync</c>. <c>GetAsync</c> answers null for a task deleted here and for
/// a task never seen here alike, and a merge that could not tell those apart
/// would read an un-pushed local deletion as an absence and take the stale live
/// document the other machine was still holding.
/// </para>
/// <para>
/// <b>A document this build cannot read is skipped, never thrown.</b> The
/// vocabulary tokens on the wire are opaque to the service, so a newer build or
/// the phone can write one this build has no member for. Throwing would escape
/// the <c>Result</c> contract and, worse, would leave the pull cursor where it
/// was — the same page would be replayed and would throw again on every future
/// sync, so one unreadable document would stop this device receiving anything
/// ever again. The service side already takes this stance in
/// <c>TaskDocumentFactory.ToRecord</c>; this is the device half of it.
/// </para>
/// <para>
/// <b>A capture is not a task, and never reaches the task store.</b> A document
/// whose kind token is <c>capture</c> is handed to the Inbox's intake whole,
/// before the local task is read and before <see cref="ShouldApply"/> is
/// consulted: the intake decides by id and status, and it never parses a
/// token, so a capture with a status this build cannot read still lands. A
/// head composed without an intake — the phone — holds captures on the replica
/// rather than writing them anywhere, which is what a head without an inbox
/// store should do with one.
/// </para>
/// </summary>
public sealed class TaskReplicaMerge(
    ITaskRepository tasks,
    IInboxIntake? inbox = null,
    ILogger<TaskReplicaMerge>? log = null,
    SyncActivityLog? activity = null,
    ITaskChangeSignal? changes = null)
{
    /// <summary>The kind token the service writes on a capture document. Three
    /// literals, not a reference: the service's <c>CaptureInboxItemCommandHandler</c>
    /// writes it, its two replicas filter on it, and this reads it — and the
    /// reason they are literals rather than one constant is the reason that
    /// handler gives: the Sync module may not reference the task domain, and this
    /// project seeing the service's implementation would be the reverse leak.</summary>
    private const string CaptureType = "capture";

    private readonly ITaskRepository _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));

    /// <summary>Optional by construction, not by omission: a head without an
    /// inbox store leaves it null and captures stay on the replica.</summary>
    private readonly IInboxIntake? _inbox = inbox;

    /// <summary>The signal the host's repository raises on every write, held
    /// here only to be silenced. A document arriving from the replica is not a
    /// local change, and a loop that heard it as one would start a cycle on the
    /// heels of every cycle that received anything. Optional, because a head or a
    /// test without the loop has nothing to silence.</summary>
    private readonly ITaskChangeSignal? _changes = changes;

    /// <summary>Optional so a test can build one in a line, and defaulted rather
    /// than left null so the skip path below cannot itself be the thing that
    /// throws. A host that has logging gets it injected.</summary>
    private readonly ILogger _log = log ?? NullLogger<TaskReplicaMerge>.Instance;

    /// <summary>Where a document that was actually written is recorded by name,
    /// or null on a head with nowhere to show one. Recorded here and not by the
    /// session that pulled the page, because this is the one place that knows
    /// the difference between a document that arrived and one that was kept —
    /// an echo of this device's own push arrives too, and a log that listed it
    /// as received would say the backlog moved when it did not.</summary>
    private readonly SyncActivityLog? _activity = activity;

    /// <summary>
    /// Whether <paramref name="inbound"/> is the later of two records for the
    /// same task, by the three-field rule in the class remarks.
    /// <para>
    /// Static and pure so the rule can be asserted on its own. It is the one
    /// piece of this class both devices have to agree on exactly, and a rule that
    /// could only be exercised through a repository would be a rule nobody
    /// checked the ties of.
    /// </para>
    /// </summary>
    public static bool Wins(TaskChangeRecord inbound, TaskChangeRecord against)
    {
        ArgumentNullException.ThrowIfNull(inbound);
        ArgumentNullException.ThrowIfNull(against);

        if (inbound.ServerTimestamp != against.ServerTimestamp)
        {
            return inbound.ServerTimestamp > against.ServerTimestamp;
        }

        if (inbound.Change.UpdatedAt != against.Change.UpdatedAt)
        {
            return inbound.Change.UpdatedAt > against.Change.UpdatedAt;
        }

        // Ordinal on the canonical "D" form: an ordering, not a meaning. Culture
        // rules over hex digits would be a comparison whose answer could differ
        // between two machines, which is the one thing a tie-break may not do.
        return string.Compare(
            inbound.DeviceId.ToString("D"),
            against.DeviceId.ToString("D"),
            StringComparison.Ordinal) > 0;
    }

    /// <summary>
    /// Applies a page of the change feed, and answers how many documents it
    /// wrote and how many it could not read.
    /// <para>
    /// A page may carry more than one record for the same task — two devices
    /// wrote it, or one device pushed it twice — so the page is reduced to a
    /// single winner per task by <see cref="Wins"/> before anything is read. Two
    /// writes for one task in one page cost one round trip to the store rather
    /// than two, and the intermediate state never reaches disk.
    /// </para>
    /// <para>
    /// Nothing about this device's own progress is consulted. The push watermark
    /// used to be, to let an older inbound copy overwrite a local row that had
    /// already been sent; <c>ApplyOneAsync</c>'s remarks say why that is no
    /// longer a question a device answers.
    /// </para>
    /// </summary>
    public async Task<TaskMergeOutcome> ApplyAsync(
        IReadOnlyList<TaskChangeRecord> page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        var applied = 0;
        var skipped = 0;

        foreach (var record in Winners(page))
        {
            switch (await ApplyOneAsync(record, cancellationToken).ConfigureAwait(false))
            {
                case ApplyOutcome.Written:
                    applied++;
                    break;

                case ApplyOutcome.Unreadable:
                    skipped++;
                    break;

                default:
                    break;
            }
        }

        return new TaskMergeOutcome(applied, skipped);
    }

    /// <summary>The task as it crosses the wire. The inverse of
    /// <see cref="ToTaskItem"/>, and the two are read together: a field added to
    /// one and forgotten in the other is a field that survives the push and
    /// vanishes on the pull.</summary>
    public static TaskChange ToChange(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return new TaskChange(task.Id, task.UpdatedAt, task.DeletedAt, ToPayload(task));
    }

    /// <summary>The wire shape of a task's fields, without its id or its stamps —
    /// those are the change's, because they are what the exchange is ordered
    /// by rather than what the task says about itself.</summary>
    public static TaskPayload ToPayload(TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return new TaskPayload(
            task.Title,
            task.ContentMd,
            EnumMap.ToWire(task.Type),
            EnumMap.ToWire(task.Status),
            EnumMap.ToWire(task.Priority),
            task.Order,
            task.Area,
            task.CreatedAt,
            task.SourceInboxId,
            task.RecurrenceSourceId,
            task.DueOn,
            task.RemindAt,
            task.Recurrence is { } recurrence ? EntryTextParser.RepeatToken(recurrence) : null,
            task.InMyDayOn,
            task.View is { } view ? EntryTextParser.ViewToken(view) : null,
            task.Effort,
            task.ImportPlanId,
            task.ImportItemId,
            task.Attachment?.Path,
            [.. task.Tags],
            [.. task.RepoIds],
            [.. task.DependsOn],
            [.. task.SubItems.Select(s => new SubItemPayload(s.Id, s.Title, EnumMap.ToWire(s.Status), s.Notes, s.Order))],
            [.. task.UsageEvents.Select(u => new UsageEventPayload(u.Timestamp, u.Action))],
            [.. task.ProjectionRefs.Select(p => new ProjectionPayload(p.RepoId, p.ExternalId, p.TargetType))],
            task.CompletedOn,
            task.StartedOn);
    }

    /// <summary>
    /// The aggregate a change describes.
    /// <para>
    /// Built the way <c>SqliteTaskRepository.Read</c> builds one, and for the
    /// same reason: there is no load-only path onto the aggregate, so every
    /// setter below is an ordinary command-side mutator that restamps
    /// <c>UpdatedAt</c> to now. <see cref="TaskItem.LoadStamps"/> is therefore
    /// called <b>last</b>, and moving it — or adding a setter after it — would
    /// give every arriving task this machine's clock instead of the stamp the
    /// exchange is ordered by, which quietly destroys last-write-wins in both
    /// directions.
    /// </para>
    /// </summary>
    public static TaskItem ToTaskItem(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var payload = change.Task;

        var task = new TaskItem(
            change.Id,
            payload.Title,
            payload.ContentMd,
            EnumMap.ParseType(payload.Type),
            EnumMap.ParseStatus(payload.Status),
            EnumMap.ParsePriority(payload.Priority),
            payload.RepoIds,
            payload.Tags,
            payload.SourceInboxId,
            payload.CreatedAt,
            payload.RecurrenceSourceId);

        task.SetOrder(payload.Order);
        task.SetArea(payload.Area);
        task.SetDueOn(payload.DueOn);
        task.SetReminder(payload.RemindAt);
        task.SetRecurrence(EntryTextParser.ParseRepeat(payload.Recurrence));
        task.SetInMyDayOn(payload.InMyDayOn);
        task.SetCompletedOn(payload.CompletedOn);
        task.SetStartedOn(payload.StartedOn);
        task.SetView(EntryTextParser.ParseView(payload.View));
        task.SetDependsOn(payload.DependsOn);
        task.SetEffort(payload.Effort);
        task.SetImportPlanId(payload.ImportPlanId);
        task.SetImportItemId(payload.ImportItemId);
        task.SetAttachment(Attachment.From(payload.AttachmentPath));

        foreach (var subItem in payload.SubItems.OrderBy(s => s.Order))
        {
            task.LoadSubItem(task.CreateSubItemForLoad(
                subItem.Id,
                subItem.Title,
                EnumMap.ParseSubItemStatus(subItem.Status),
                subItem.Notes,
                subItem.Order));
        }

        foreach (var usage in payload.UsageEvents)
        {
            task.LoadUsageEvent(new UsageEvent(usage.Timestamp, usage.Action));
        }

        foreach (var projection in payload.ProjectionRefs)
        {
            task.AddProjectionRef(new ProjectionRef(projection.RepoId, projection.ExternalId, projection.TargetType));
        }

        // LAST, and it has to be last. See the remarks above.
        task.LoadStamps(change.UpdatedAt, change.DeletedAt);

        return task;
    }

    /// <summary>One record per task, each the later of whatever the page held for
    /// it.</summary>
    private static IEnumerable<TaskChangeRecord> Winners(IReadOnlyList<TaskChangeRecord> page)
    {
        var winners = new Dictionary<Guid, TaskChangeRecord>();

        foreach (var record in page)
        {
            if (!winners.TryGetValue(record.Change.Id, out var standing) || Wins(record, standing))
            {
                winners[record.Change.Id] = record;
            }
        }

        return winners.Values;
    }

    /// <summary>
    /// Writes one record, unless the local row is already at least that version.
    /// <para>
    /// <b>A pull may never move a document backwards</b> — the same rule the
    /// replica applies to a push (<c>TaskChangePrecedence</c> in the Sync
    /// module, restated in <see cref="ShouldApply"/> because this project sees
    /// the service's contracts and not its domain). The inbound copy is taken
    /// when it is strictly later by <c>UpdatedAt</c>, or is a tombstone of the
    /// very version held; an identical version is this device's own echo or a
    /// replayed page and writes nothing, which is what keeps a pull idempotent —
    /// a run that dies after applying a page but before saving its cursor
    /// replays that page next time. An older copy is refused whatever the push
    /// watermark says about the local row.
    /// </para>
    /// <para>
    /// The watermark used to decide the older case: a local row at or below it
    /// had been pushed, so under .devbook/arc42/adr/0005's original "the replica is
    /// authoritative for anything already sent" an older inbound copy replaced
    /// it. That was written for a replica that kept whichever push arrived
    /// last. Since the replica refuses a push that is not a later version, the
    /// feed can only hand a device an older copy of a row it pushed when
    /// something has gone wrong — and the branch that took it un-completed a
    /// task on the very machine that had completed it. The ADR's amendment of
    /// 2026-09-18 records the change of authority; this is the device half of
    /// it.
    /// </para>
    /// <para>
    /// The read includes a local tombstone for the same reason it always did: a
    /// deletion made here carries a stamp like any other, and a live document
    /// the other machine has been holding since before it is an older version.
    /// </para>
    /// </summary>
    private async Task<ApplyOutcome> ApplyOneAsync(
        TaskChangeRecord record,
        CancellationToken cancellationToken)
    {
        // Only a document carrying the capture kind token takes this branch. A
        // capture written before the token existed — a `task` document with the
        // phone's name in sourceInboxId, which is what the mobile head wrote
        // until this change — is not recognised here and goes down the task
        // path below, landing as the draft task it claims to be with "mobile"
        // kept as its source. Accepted as a one-time behaviour rather than
        // special-cased: one user, both ends upgraded together, and the handful
        // of such documents are a draft each to tidy in the backlog, where a
        // rule to tell them apart would be carried by every merge for ever.
        if (IsCapture(record.Change))
        {
            // Held rather than Unreadable on a head without an intake: nothing
            // is wrong with the document, this head simply has nowhere to put it.
            if (_inbox is null) return ApplyOutcome.Held;

            InboxIntakeOutcome outcome;

            try
            {
                outcome = await _inbox.ReceiveAsync(ToCapture(record), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is FormatException or ArgumentException)
            {
                // The intake decides by id and status and parses no token, but
                // the aggregate behind it still has invariants — a title, for
                // one — and the push endpoint validates nothing. Refused here
                // means skipped, for the reason the task path gives: a throw
                // would leave the cursor on this page and replay it for ever.
                _log.LogWarning(
                    failure,
                    "Skipping capture {CaptureId} from the replica: the inbox could not take the document. "
                    + "It will be offered again the next time the replica hands it out.",
                    record.Change.Id);

                return ApplyOutcome.Unreadable;
            }

            // Received and Withdrawn wrote something; AlreadyKnown and Ignored
            // are the capture's echo or replay, and a replayed page writes
            // nothing — the same idempotency the task path keeps below.
            switch (outcome)
            {
                case InboxIntakeOutcome.Received:
                    _activity?.Record(
                        SyncDirection.Received, SyncItemKind.Capture, record.Change.Id.ToString("D"), record.Change.Task.Title);
                    return ApplyOutcome.Written;

                case InboxIntakeOutcome.Withdrawn:
                    _activity?.Record(
                        SyncDirection.Received, SyncItemKind.Capture, record.Change.Id.ToString("D"), record.Change.Task.Title, "withdrawn");
                    return ApplyOutcome.Written;

                default:
                    return ApplyOutcome.Held;
            }
        }

        var local = await _tasks
            .GetIncludingDeletedAsync(record.Change.Id, cancellationToken)
            .ConfigureAwait(false);

        if (!ShouldApply(record.Change, local)) return ApplyOutcome.Held;

        TaskItem task;

        try
        {
            // Mapped after the decision rather than before it, so an echo this
            // device is about to discard costs no parsing — and so an
            // unreadable document is only ever reported when it was going to be
            // written.
            task = ToTaskItem(record.Change);
        }
        catch (Exception failure) when (failure is FormatException or ArgumentException)
        {
            _log.LogWarning(
                failure,
                "Skipping task {TaskId} from the replica: this build cannot read the document. "
                + "It will be offered again the next time the replica hands it out.",
                record.Change.Id);

            return ApplyOutcome.Unreadable;
        }

        // Inside a suppression, so the write is not announced as this machine's
        // own edit - see ITaskChangeSignal.Suppress for the loop it would start.
        using (_changes?.Suppress())
        {
            await _tasks.SaveAsync(task, cancellationToken).ConfigureAwait(false);
        }

        _activity?.Record(
            SyncDirection.Received, SyncItemKind.Task, task.Id.ToString("D"), task.Title,
            task.DeletedAt is null ? null : "deleted");

        return ApplyOutcome.Written;
    }

    private static bool IsCapture(TaskChange change) =>
        string.Equals(change.Task.Type, CaptureType, StringComparison.Ordinal);

    /// <summary>The capture as the Inbox wants it: the document's id, title,
    /// source, stamps, body, tags and person, and nothing else of the task shape
    /// around them. The tombstone stamp travels as <c>WithdrawnAt</c>; a source
    /// the service did not record is filed as unknown rather than dropped.
    /// <para>
    /// The service writes the capture's person among the document's tags as
    /// <c>@name</c> — the task shape has no field for one — and refuses any other
    /// tag carrying the sigil, so the first such tag is the person and the rest
    /// are tags. A second one cannot come from the service, and is dropped
    /// here rather than stored as a tag.
    /// </para></summary>
    private static InboxCaptureDto ToCapture(TaskChangeRecord record)
    {
        var tags = record.Change.Task.Tags ?? [];
        var person = tags.FirstOrDefault(tag => tag.StartsWith('@'));

        return new(
            record.Change.Id,
            record.Change.Task.Title,
            record.Change.Task.SourceInboxId ?? "unknown",
            record.Change.Task.CreatedAt,
            record.Change.UpdatedAt,
            record.Change.DeletedAt,
            BodyMd: string.IsNullOrWhiteSpace(record.Change.Task.ContentMd) ? null : record.Change.Task.ContentMd,
            Tags: [.. tags.Where(tag => !tag.StartsWith('@'))],
            Person: person);
    }

    /// <summary>The apply-against-local decision on its own, by the rule in
    /// <see cref="ApplyOneAsync"/>'s remarks. Word for word the replica's
    /// <c>TaskChangePrecedence.Supersedes</c>, over the local aggregate instead
    /// of a stored change: a version the service would refuse to take from this
    /// device is one this device refuses to take from the service.</summary>
    private static bool ShouldApply(TaskChange inbound, TaskItem? local)
    {
        if (local is null) return true;

        if (inbound.UpdatedAt != local.UpdatedAt)
        {
            return inbound.UpdatedAt > local.UpdatedAt;
        }

        // Same stamp: only a deletion of the very version held says anything
        // new. A tombstone and the live task it replaced can share an UpdatedAt
        // only if one of them never happened, so the pair is read together.
        return inbound.DeletedAt is not null && local.DeletedAt is null;
    }

    /// <summary>What became of one record. Three outcomes rather than a bool,
    /// because "this device already holds something it has not sent" and "this
    /// build cannot read the document" are two different things to say to
    /// somebody reading a sync summary.</summary>
    private enum ApplyOutcome
    {
        /// <summary>The local copy stands.</summary>
        Held,

        /// <summary>The inbound document was written.</summary>
        Written,

        /// <summary>The document carries something this build has no member for,
        /// so it was left where it is.</summary>
        Unreadable,
    }
}
