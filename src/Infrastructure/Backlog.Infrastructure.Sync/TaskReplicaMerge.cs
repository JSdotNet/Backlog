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
/// three fields. It answers a different question instead: is the local row work
/// this device has not sent yet? See that method's own remarks.
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
/// </summary>
public sealed class TaskReplicaMerge(ITaskRepository tasks, ILogger<TaskReplicaMerge>? log = null)
{
    private readonly ITaskRepository _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));

    /// <summary>Optional so a test can build one in a line, and defaulted rather
    /// than left null so the skip path below cannot itself be the thing that
    /// throws. A host that has logging gets it injected.</summary>
    private readonly ILogger _log = log ?? NullLogger<TaskReplicaMerge>.Instance;

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
    /// <paramref name="pushWatermark"/> is how far this device has had its own
    /// writes accepted, and it decides the case below. It is passed in rather
    /// than read from the state store here, so the rule can be exercised without
    /// one and this class goes on talking to exactly one thing.
    /// </para>
    /// </summary>
    public async Task<TaskMergeOutcome> ApplyAsync(
        IReadOnlyList<TaskChangeRecord> page,
        DateTimeOffset pushWatermark,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        var applied = 0;
        var skipped = 0;

        foreach (var record in Winners(page))
        {
            switch (await ApplyOneAsync(record, pushWatermark, cancellationToken).ConfigureAwait(false))
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
            [.. task.ProjectionRefs.Select(p => new ProjectionPayload(p.RepoId, p.ExternalId, p.TargetType))]);
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
    /// Writes one record, unless the local row is work this device has not sent
    /// yet.
    /// <para>
    /// <b>The replica is authoritative.</b> .arc42/adr/0005 section "The sync
    /// model" puts ordering authority on the server rather than on a device
    /// clock, so a document that reached the service after this device's last
    /// push is what this device takes — even when its own copy carries the later
    /// <c>UpdatedAt</c>. Deciding on <c>UpdatedAt</c> alone would let a stale
    /// push win at the replica and be refused by every device that pulled it,
    /// which is not a lost edit but a permanent, silent disagreement: the feed
    /// never offers that document again, so no amount of syncing closes it.
    /// </para>
    /// <para>
    /// <b>The one exception is an un-pushed local edit</b> — a local
    /// <c>UpdatedAt</c> above <paramref name="pushWatermark"/>, which means this
    /// device changed the task and has not sent it. That edit wins on its own
    /// next push, so taking an older inbound copy over it now would discard work
    /// before it ever left the machine. A local tombstone is a stamp like any
    /// other here, which is why the read includes one: a deletion made here and
    /// not yet pushed stops a live document the other machine has been holding.
    /// </para>
    /// <para>
    /// <b>The same version arriving again writes nothing.</b> Identical stamps on
    /// one task id are this device's own echo or a replayed page, and skipping
    /// them is what keeps a pull idempotent — a run that dies after applying a
    /// page but before saving its cursor replays that page next time.
    /// </para>
    /// <para>
    /// A strictly later inbound document is always taken, whatever the watermark
    /// says. A row this device received rather than wrote also sits above the
    /// watermark, and reading that as an un-pushed edit would leave a device that
    /// pulls twice without pushing in between — which is every page after the
    /// first of a single pull — refusing everything after the first version it
    /// was handed.
    /// </para>
    /// </summary>
    private async Task<ApplyOutcome> ApplyOneAsync(
        TaskChangeRecord record,
        DateTimeOffset pushWatermark,
        CancellationToken cancellationToken)
    {
        var local = await _tasks
            .GetIncludingDeletedAsync(record.Change.Id, cancellationToken)
            .ConfigureAwait(false);

        if (!ShouldApply(record.Change, local, pushWatermark)) return ApplyOutcome.Held;

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

        await _tasks.SaveAsync(task, cancellationToken).ConfigureAwait(false);

        return ApplyOutcome.Written;
    }

    /// <summary>The apply-against-local decision on its own, by the rule in
    /// <see cref="ApplyOneAsync"/>'s remarks.</summary>
    private static bool ShouldApply(TaskChange inbound, TaskItem? local, DateTimeOffset pushWatermark)
    {
        if (local is null) return true;

        // The same document coming back round. Both stamps, because a tombstone
        // and the live task it replaced can share an UpdatedAt only if one of
        // them never happened.
        if (inbound.UpdatedAt == local.UpdatedAt && inbound.DeletedAt == local.DeletedAt) return false;

        if (inbound.UpdatedAt > local.UpdatedAt) return true;

        // Older than the local copy, so it may only overwrite one this device has
        // already sent. Everything above the watermark is due to be pushed and
        // will win there.
        return local.UpdatedAt <= pushWatermark;
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
