using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The one place remote data overwrites local canonical data. Two things are
/// worth asserting here and nothing else in the product asserts either: that two
/// devices reach the same answer about which copy is later, and that a task
/// deleted on this machine is not brought back by a copy the other machine has
/// been holding since before the deletion.
/// </summary>
public sealed class TaskReplicaMergeTests
{
    private static readonly Guid Earlier = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Later = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The watermark of a device that has never had a push accepted, so
    /// every local row it holds reads as an un-pushed local edit. The
    /// conservative end of the rule, and what the cases below want unless they
    /// are about the watermark itself.</summary>
    private static readonly DateTimeOffset NothingPushedYet = DateTimeOffset.MinValue;

    // --- The tie-break ------------------------------------------------------

    [Fact]
    public void The_replicas_own_stamp_decides_first()
    {
        var id = Guid.NewGuid();

        // Everything else says the loser should win: a later local edit, and the
        // higher device id. The store's own order still comes first, because it
        // is the only one of the three that no device's clock can move.
        var inbound = TaskChanges.Record(TaskChanges.Change("Newer to the replica", Noon, id), Earlier, serverTimestamp: 200);
        var against = TaskChanges.Record(TaskChanges.Change("Older to the replica", Noon.AddHours(1), id), Later, serverTimestamp: 100);

        Assert.True(TaskReplicaMerge.Wins(inbound, against));
        Assert.False(TaskReplicaMerge.Wins(against, inbound));
    }

    /// <summary>
    /// Cosmos stamps <c>_ts</c> to the second, so a tie on the first field is
    /// routine rather than theoretical — two devices coming back online together
    /// land in the same second regularly. The device's own stamp breaks it.
    /// </summary>
    [Fact]
    public void Within_one_second_of_the_replica_the_later_edit_decides()
    {
        var id = Guid.NewGuid();

        var inbound = TaskChanges.Record(TaskChanges.Change("Edited later", Noon.AddMinutes(5), id), Earlier, serverTimestamp: 100);
        var against = TaskChanges.Record(TaskChanges.Change("Edited earlier", Noon, id), Later, serverTimestamp: 100);

        Assert.True(TaskReplicaMerge.Wins(inbound, against));
        Assert.False(TaskReplicaMerge.Wins(against, inbound));
    }

    /// <summary>
    /// Arbitrary, and that is what it is for: once the first two are equal there
    /// is nothing left that means anything, and what is needed is a rule both
    /// devices apply to the same two values and reach the same answer from.
    /// </summary>
    [Fact]
    public void Two_edits_in_the_same_instant_are_decided_by_the_device()
    {
        var id = Guid.NewGuid();

        var higher = TaskChanges.Record(TaskChanges.Change("From the later device", Noon, id), Later, serverTimestamp: 100);
        var lower = TaskChanges.Record(TaskChanges.Change("From the earlier device", Noon, id), Earlier, serverTimestamp: 100);

        Assert.True(TaskReplicaMerge.Wins(higher, lower));
        Assert.False(TaskReplicaMerge.Wins(lower, higher));
    }

    /// <summary>
    /// Equal on all three is the same write arriving twice, and nothing happens.
    /// This is what makes a replayed page idempotent — a pull interrupted after
    /// applying a page but before saving its cursor replays that page next run.
    /// </summary>
    [Fact]
    public void A_record_does_not_beat_itself()
    {
        var record = TaskChanges.Record(TaskChanges.Change("The same write", Noon), Earlier, serverTimestamp: 100);

        Assert.False(TaskReplicaMerge.Wins(record, record));
    }

    // --- Applying -----------------------------------------------------------

    [Fact]
    public async Task A_task_this_machine_has_never_seen_is_written()
    {
        var store = new InMemoryTaskStore();
        var merge = new TaskReplicaMerge(store);

        var record = TaskChanges.Record(TaskChanges.Change("From the other machine", Noon), Earlier, serverTimestamp: 100);

        var outcome = await merge.ApplyAsync([record], NothingPushedYet, TestContext.Current.CancellationToken);

        Assert.Equal(1, outcome.Applied);
        Assert.Equal("From the other machine", store.Tasks[record.Change.Id].Title);
    }

    /// <summary>
    /// The failure class ADR 0005 exists to remove. <c>GetAsync</c> answers null
    /// for a task deleted here and for one never seen here alike, so a merge
    /// asking through it would read the deletion as an absence and take the stale
    /// live document the other machine was still holding.
    /// </summary>
    [Fact]
    public async Task A_task_deleted_here_is_not_resurrected_by_a_stale_live_copy()
    {
        var store = new InMemoryTaskStore();
        var merge = new TaskReplicaMerge(store);

        var id = Guid.NewGuid();
        var tombstone = TaskChanges.Task("Deleted here", Noon, id);
        tombstone.LoadStamps(Noon.AddHours(2), Noon.AddHours(2));
        store.Seed(tombstone);

        // What the other machine still has: the task alive, last edited before
        // the deletion.
        var stale = TaskChanges.Record(TaskChanges.Change("Still alive over there", Noon, id), Later, serverTimestamp: 999);

        var outcome = await merge.ApplyAsync([stale], NothingPushedYet, TestContext.Current.CancellationToken);

        Assert.Equal(0, outcome.Applied);
        Assert.Empty(store.Writes);
        Assert.NotNull(store.Tasks[id].DeletedAt);
    }

    /// <summary>A deletion that happened after this machine's last edit does
    /// travel, which is the other half of the same rule.</summary>
    [Fact]
    public async Task A_deletion_from_the_other_machine_is_applied()
    {
        var store = new InMemoryTaskStore();
        var merge = new TaskReplicaMerge(store);

        var id = Guid.NewGuid();
        store.Seed(TaskChanges.Task("Alive here", Noon, id));

        var change = TaskChanges.Change("Deleted over there", Noon.AddHours(1), id);
        var deletion = TaskChanges.Record(
            change with { DeletedAt = Noon.AddHours(1) },
            Later,
            serverTimestamp: 100);

        var outcome = await merge.ApplyAsync([deletion], NothingPushedYet, TestContext.Current.CancellationToken);

        Assert.Equal(1, outcome.Applied);
        Assert.Equal(Noon.AddHours(1), store.Tasks[id].DeletedAt);
    }

    /// <summary>
    /// The same page twice writes once. Not an optimisation: the second apply
    /// would carry the same document, so what this protects is the store from a
    /// write per replay rather than the data from being wrong.
    /// </summary>
    [Fact]
    public async Task A_replayed_page_writes_nothing_the_second_time()
    {
        var store = new InMemoryTaskStore();
        var merge = new TaskReplicaMerge(store);

        var page = new[] { TaskChanges.Record(TaskChanges.Change("Once", Noon), Earlier, serverTimestamp: 100) };

        Assert.Equal(1, (await merge.ApplyAsync(page, NothingPushedYet, TestContext.Current.CancellationToken)).Applied);
        Assert.Equal(0, (await merge.ApplyAsync(page, NothingPushedYet, TestContext.Current.CancellationToken)).Applied);
        Assert.Single(store.Writes);
    }

    /// <summary>Two records for one task in one page cost one write, and it is
    /// the winner's — the intermediate state never reaches disk.</summary>
    [Fact]
    public async Task One_page_holding_a_task_twice_writes_the_winner_once()
    {
        var store = new InMemoryTaskStore();
        var merge = new TaskReplicaMerge(store);

        var id = Guid.NewGuid();
        var older = TaskChanges.Record(TaskChanges.Change("Older", Noon, id), Earlier, serverTimestamp: 100);
        var newer = TaskChanges.Record(TaskChanges.Change("Newer", Noon.AddMinutes(1), id), Later, serverTimestamp: 200);

        var outcome = await merge.ApplyAsync([older, newer], NothingPushedYet, TestContext.Current.CancellationToken);

        Assert.Equal(1, outcome.Applied);
        Assert.Single(store.Writes);
        Assert.Equal("Newer", store.Tasks[id].Title);
    }

    // --- The apply-against-local decision -----------------------------------

    /// <summary>
    /// Two devices editing one task while one of them is offline, and the state
    /// they are in once both have synced. The replica is what they have to agree
    /// with: whichever push reached the service last is what every device ends up
    /// holding, per .arc42/adr/0005 section "The sync model" — <i>"Ordering
    /// authority is the server, not the device clock."</i>
    /// <para>
    /// The laptop's edit is the older one by the wall clock and it still wins,
    /// because it is the one that reached the service last. That discards the
    /// desktop's edit, which the ADR accepts under Negative consequences. What it
    /// does not accept anywhere is the alternative this replaces: the desktop
    /// refusing the inbound copy for being older, holding 10:00 forever while the
    /// replica and the laptop hold 09:00, with no amount of syncing able to close
    /// the gap.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Two_devices_converge_on_whichever_push_reached_the_replica_last()
    {
        var id = Guid.NewGuid();
        var replica = new ReplicaDouble();

        // The laptop edited at nine and went offline without syncing.
        var laptop = new InMemoryTaskStore();
        laptop.Seed(TaskChanges.Task("Edited on the laptop at nine", Noon.AddHours(-3), id));

        // The desktop edited at ten and synced straight away.
        var desktop = new InMemoryTaskStore();
        desktop.Seed(TaskChanges.Task("Edited on the desktop at ten", Noon.AddHours(-2), id));
        replica.Push(desktop.Tasks[id], Later);
        var desktopWatermark = Noon.AddHours(-2);

        // Evening: the laptop finally syncs. It pushes first, so its nine
        // o'clock copy is the last write the service saw.
        replica.Push(laptop.Tasks[id], Earlier);
        var laptopWatermark = Noon.AddHours(-3);

        await new TaskReplicaMerge(laptop)
            .ApplyAsync(replica.Feed(), laptopWatermark, TestContext.Current.CancellationToken);

        await new TaskReplicaMerge(desktop)
            .ApplyAsync(replica.Feed(), desktopWatermark, TestContext.Current.CancellationToken);

        Assert.Equal("Edited on the laptop at nine", laptop.Tasks[id].Title);
        Assert.Equal("Edited on the laptop at nine", desktop.Tasks[id].Title);
    }

    /// <summary>
    /// The same shape with a deletion, which is the worse half of it: the desktop
    /// keeping a task the person deleted, permanently, because a tombstone
    /// refused once is never offered again — the replica has moved on and the
    /// feed does not repeat it.
    /// </summary>
    [Fact]
    public async Task A_tombstone_that_reached_the_replica_last_deletes_the_task_on_the_other_device()
    {
        var id = Guid.NewGuid();
        var replica = new ReplicaDouble();

        var laptop = new InMemoryTaskStore();
        laptop.Seed(TaskChanges.Task(
            "Deleted on the laptop at nine", Noon.AddHours(-3), id, deletedAt: Noon.AddHours(-3)));

        var desktop = new InMemoryTaskStore();
        desktop.Seed(TaskChanges.Task("Edited on the desktop at ten", Noon.AddHours(-2), id));
        replica.Push(desktop.Tasks[id], Later);
        var desktopWatermark = Noon.AddHours(-2);

        replica.Push(laptop.Tasks[id], Earlier);

        await new TaskReplicaMerge(desktop)
            .ApplyAsync(replica.Feed(), desktopWatermark, TestContext.Current.CancellationToken);

        Assert.NotNull(desktop.Tasks[id].DeletedAt);
        Assert.Null(await desktop.GetAsync(id, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The one thing the replica may not overwrite: work this machine has done
    /// and not yet sent. It sits above the push watermark, so it will win on its
    /// own next push, and taking the older inbound copy now would lose it before
    /// it ever left.
    /// </summary>
    [Fact]
    public async Task An_unpushed_local_edit_survives_an_older_inbound_document()
    {
        var store = new InMemoryTaskStore();
        var merge = new TaskReplicaMerge(store);

        var id = Guid.NewGuid();
        store.Seed(TaskChanges.Task("Edited here and not sent yet", Noon.AddHours(2), id));

        var inbound = TaskChanges.Record(
            TaskChanges.Change("Older, from the replica", Noon.AddHours(1), id), Later, serverTimestamp: 900);

        var outcome = await merge.ApplyAsync([inbound], Noon, TestContext.Current.CancellationToken);

        Assert.Equal(0, outcome.Applied);
        Assert.Empty(store.Writes);
        Assert.Equal("Edited here and not sent yet", store.Tasks[id].Title);
    }

    /// <summary>
    /// A row this device received rather than wrote is not an un-pushed local
    /// edit, however far above the watermark its stamp sits. A rule that read it
    /// as one would leave a device that pulls twice without pushing in between --
    /// which is every page after the first of one pull — refusing everything
    /// after the first version it was handed.
    /// </summary>
    [Fact]
    public async Task A_later_document_still_lands_on_a_device_that_has_never_pushed()
    {
        var store = new InMemoryTaskStore();
        var merge = new TaskReplicaMerge(store);

        var id = Guid.NewGuid();
        var first = TaskChanges.Record(TaskChanges.Change("First version", Noon, id), Later, serverTimestamp: 100);
        var second = TaskChanges.Record(
            TaskChanges.Change("Second version", Noon.AddMinutes(5), id), Later, serverTimestamp: 200);

        Assert.Equal(1, (await merge.ApplyAsync([first], NothingPushedYet, TestContext.Current.CancellationToken)).Applied);
        Assert.Equal(1, (await merge.ApplyAsync([second], NothingPushedYet, TestContext.Current.CancellationToken)).Applied);

        Assert.Equal("Second version", store.Tasks[id].Title);
    }

    // --- Documents this build cannot read -----------------------------------

    /// <summary>
    /// A token this build has no member for. It is a real arrival rather than a
    /// hypothetical one — the wire carries the store's own opaque tokens and
    /// this repository has already retired one (<c>follow_up</c>), so a newer
    /// build or the phone can write a word this one has never seen.
    /// <para>
    /// Throwing would escape the <c>Result</c> contract and leave the pull cursor
    /// where it was, which means the same page replays and throws again on every
    /// future sync: one document stops the device receiving anything, ever. The
    /// rest of the page still lands and the run carries on.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_unreadable_document_is_skipped_and_the_rest_of_the_page_still_lands()
    {
        var store = new InMemoryTaskStore();
        var merge = new TaskReplicaMerge(store);

        var unreadable = TaskChanges.Record(Retokenized(TaskChanges.Change("From a newer build", Noon)), Later, 100);
        var readable = TaskChanges.Record(TaskChanges.Change("From a build like this one", Noon), Earlier, 101);

        var outcome = await merge.ApplyAsync([unreadable, readable], NothingPushedYet, TestContext.Current.CancellationToken);

        Assert.Equal(1, outcome.Applied);
        Assert.Equal(1, outcome.Skipped);
        Assert.Equal("From a build like this one", store.Tasks[readable.Change.Id].Title);
        Assert.DoesNotContain(unreadable.Change.Id, store.Tasks.Keys);
    }

    /// <summary>A status this build cannot parse. Any of the three enum-ish
    /// fields would do; the status is the one whose vocabulary has actually
    /// changed here before.</summary>
    private static TaskChange Retokenized(TaskChange change) =>
        change with { Task = change.Task with { Status = "awaiting_review" } };

    // --- Mapping ------------------------------------------------------------

    /// <summary>
    /// The stamp survives the mapping. It cannot unless
    /// <see cref="TaskItem.LoadStamps"/> is called after every setter: they are
    /// ordinary command-side mutators and each one restamps <c>UpdatedAt</c> to
    /// now, so a mapping that ended anywhere else would give every arriving task
    /// this machine's clock and destroy last-write-wins in both directions.
    /// </summary>
    [Fact]
    public void The_stamps_survive_because_they_are_loaded_last()
    {
        // Carries a sub-item and a projection on purpose: those are mapped by
        // LoadSubItem and AddProjectionRef, the last two things the mapping does
        // before it loads the stamps. Moving LoadStamps above either of them
        // leaves this task stamped with the clock of the machine that read it.
        var source = TaskChanges.Task("Stamped over there", Noon);
        source.AddSubItem("A step");
        source.AddProjectionRef(new ProjectionRef("JSdotNet/Backlog", "42", "issue"));
        source.LoadStamps(Noon, Noon.AddMinutes(30));

        var task = TaskReplicaMerge.ToTaskItem(TaskReplicaMerge.ToChange(source));

        Assert.Equal(Noon, task.UpdatedAt);
        Assert.Equal(Noon.AddMinutes(30), task.DeletedAt);
    }

    /// <summary>Every field a task carries goes out and comes back. A field
    /// added to one side of the mapping and forgotten on the other survives the
    /// push and vanishes on the pull, which looks like the other machine
    /// clearing it.</summary>
    [Fact]
    public void A_task_round_trips_through_the_wire_shape()
    {
        var original = TaskChanges.Task("Round trip", Noon);
        original.UpdateContent("The body.");
        original.ChangePriority(Priority.High);
        original.SetStatus(EntryStatus.InProgress);
        original.SetOrder(7);
        original.SetArea("repos");
        original.SetEffort(5);
        original.SetDueOn(new DateOnly(2026, 9, 30));
        original.SetReminder(new DateTime(2026, 9, 29, 9, 0, 0, DateTimeKind.Unspecified));
        original.SetInMyDayOn(new DateOnly(2026, 9, 7));
        original.SetView(EntryView.Steps);
        original.SetTags(["release"]);
        original.SetRepoIds(["JSdotNet/Backlog"]);
        original.SetDependsOn(["another-task"]);
        original.SetImportPlanId("plan-1");
        original.SetImportItemId("item-1");
        original.SetAttachment(new Attachment(@"C:\notes"));
        original.AddSubItem("A step", "with a note");
        original.AddProjectionRef(new ProjectionRef("JSdotNet/Backlog", "42", "issue"));
        original.LoadStamps(Noon, null);

        var restored = TaskReplicaMerge.ToTaskItem(TaskReplicaMerge.ToChange(original));

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Title, restored.Title);
        Assert.Equal(original.ContentMd, restored.ContentMd);
        Assert.Equal(original.Type, restored.Type);
        Assert.Equal(original.Status, restored.Status);
        Assert.Equal(original.Priority, restored.Priority);
        Assert.Equal(original.Order, restored.Order);
        Assert.Equal(original.Area, restored.Area);
        Assert.Equal(original.CreatedAt, restored.CreatedAt);
        Assert.Equal(original.Effort, restored.Effort);
        Assert.Equal(original.DueOn, restored.DueOn);
        Assert.Equal(original.RemindAt, restored.RemindAt);
        Assert.Equal(original.InMyDayOn, restored.InMyDayOn);
        Assert.Equal(original.View, restored.View);
        Assert.Equal(original.Tags, restored.Tags);
        Assert.Equal(original.RepoIds, restored.RepoIds);
        Assert.Equal(original.DependsOn, restored.DependsOn);
        Assert.Equal(original.ImportPlanId, restored.ImportPlanId);
        Assert.Equal(original.ImportItemId, restored.ImportItemId);
        Assert.Equal(original.Attachment?.Path, restored.Attachment?.Path);
        Assert.Equal(original.UpdatedAt, restored.UpdatedAt);

        var subItem = Assert.Single(restored.SubItems);
        Assert.Equal(original.SubItems[0].Id, subItem.Id);
        Assert.Equal("A step", subItem.Title);
        Assert.Equal("with a note", subItem.Notes);

        var projection = Assert.Single(restored.ProjectionRefs);
        Assert.Equal("42", projection.ExternalId);
    }

    /// <summary>The opaque tokens the payload carries are the ones the local
    /// store already writes, so a status crosses the wire as the same word the
    /// database holds rather than as a number whose meaning moves.</summary>
    [Fact]
    public void The_wire_carries_the_stores_own_tokens()
    {
        var task = TaskChanges.Task("Tokenized", Noon);
        task.SetStatus(EntryStatus.InProgress);
        task.LoadStamps(Noon, null);

        TaskPayload payload = TaskReplicaMerge.ToPayload(task);

        Assert.Equal("in_progress", payload.Status);
        Assert.Equal("task", payload.Type);
        Assert.Equal("medium", payload.Priority);
    }
}

/// <summary>
/// The replica as these tests need it: whole documents, last write wins, and a
/// sequence number standing in for the Cosmos <c>_ts</c> that orders the feed.
/// Deliberately not a double for <c>ITaskReplica</c> — what the convergence
/// cases turn on is the order in which pushes reached the service, and recording
/// that is the whole of what they need.
/// </summary>
internal sealed class ReplicaDouble
{
    private readonly Dictionary<Guid, TaskChangeRecord> _documents = [];
    private long _sequence;

    public void Push(TaskItem task, Guid deviceId)
    {
        var change = TaskReplicaMerge.ToChange(task);

        _documents[change.Id] = new TaskChangeRecord(change, deviceId, ++_sequence);
    }

    public IReadOnlyList<TaskChangeRecord> Feed() =>
        [.. _documents.Values.OrderBy(document => document.ServerTimestamp)];
}
