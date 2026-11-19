using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// A push may never move a document backwards. The scenario every test here
/// is a corner of: one machine deletes a task and pushes the tombstone, the
/// other machine wakes up still holding the live copy it pulled days ago and
/// pushes that first — because everything a device received sits above its
/// push watermark exactly as its own edits do. The replica has to keep the
/// tombstone, or the deletion comes back on both machines.
/// </summary>
public class InMemoryTaskReplicaPrecedenceTests
{
    private static readonly OwnerId Mine = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly OwnerScope Desktop = new(Mine, new DeviceId(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")));
    private static readonly OwnerScope Laptop = new(Mine, new DeviceId(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002")));
    private static readonly DateTimeOffset Imported = new(2026, 9, 14, 17, 53, 22, TimeSpan.Zero);
    private static readonly DateTimeOffset Deleted = new(2026, 9, 17, 21, 55, 5, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_older_live_copy_pushed_after_a_tombstone_does_not_replace_it()
    {
        var replica = new InMemoryTaskReplica();
        var id = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Live(id, Imported)], Cancellation);
        await replica.Upsert(Desktop, [Tombstone(id, Deleted)], Cancellation);

        // The laptop's echo of the version it pulled on the 14th.
        var accepted = await replica.Upsert(Laptop, [Live(id, Imported)], Cancellation);

        Assert.Equal(0, accepted);
        var stored = await replica.Find(Mine, id, Cancellation);
        Assert.NotNull(stored);
        Assert.Equal(Deleted, stored.Change.DeletedAt);
        Assert.Equal(Desktop.DeviceId.Value, stored.DeviceId);
    }

    [Fact]
    public async Task A_refused_push_does_not_move_the_document_in_the_feed()
    {
        var replica = new InMemoryTaskReplica();
        var id = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Live(id, Imported)], Cancellation);
        var caughtUp = (await replica.ReadChanges(Mine, cursor: null, maxItems: 10, Cancellation)).Cursor;

        await replica.Upsert(Laptop, [Live(id, Imported)], Cancellation);

        // Nothing new happened, so a device that was caught up stays caught up
        // rather than being handed its own echo as a change.
        var page = await replica.ReadChanges(Mine, caughtUp, maxItems: 10, Cancellation);
        Assert.Empty(page.Changes);
    }

    [Fact]
    public async Task A_later_edit_replaces_the_stored_copy_and_is_counted()
    {
        var replica = new InMemoryTaskReplica();
        var id = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Live(id, Imported)], Cancellation);
        var accepted = await replica.Upsert(Laptop, [Live(id, Imported.AddHours(1), "Retitled on the laptop")], Cancellation);

        Assert.Equal(1, accepted);
        var stored = await replica.Find(Mine, id, Cancellation);
        Assert.Equal("Retitled on the laptop", stored!.Change.Task.Title);
        Assert.Equal(Laptop.DeviceId.Value, stored.DeviceId);
    }

    [Fact]
    public async Task A_tombstone_of_the_very_version_held_is_taken_on_a_tied_stamp()
    {
        var replica = new InMemoryTaskReplica();
        var id = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Live(id, Imported)], Cancellation);
        var accepted = await replica.Upsert(Desktop, [Tombstone(id, Imported)], Cancellation);

        Assert.Equal(1, accepted);
        Assert.NotNull((await replica.Find(Mine, id, Cancellation))!.Change.DeletedAt);
    }

    [Fact]
    public async Task A_batch_counts_only_what_it_wrote()
    {
        var replica = new InMemoryTaskReplica();
        var stale = Guid.CreateVersion7();
        var fresh = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Live(stale, Imported), Tombstone(stale, Deleted)], Cancellation);

        var accepted = await replica.Upsert(Laptop, [Live(stale, Imported), Live(fresh, Imported)], Cancellation);

        Assert.Equal(1, accepted);
    }

    [Theory]
    [InlineData(1, false, false, true)]   // later live copy over an older one
    [InlineData(1, true, false, true)]    // later tombstone over a live copy
    [InlineData(-1, false, true, false)]  // the echo: older live copy over a tombstone
    [InlineData(-1, false, false, false)] // older live copy over a newer live copy
    [InlineData(0, false, false, false)]  // the same version twice
    [InlineData(0, true, true, false)]    // the same tombstone twice
    [InlineData(0, true, false, true)]    // deleting exactly the version held
    [InlineData(0, false, true, false)]   // a live copy never undeletes on a tie
    public void Precedence_is_the_stamp_then_the_tombstone(int hoursApart, bool inboundDeleted, bool storedDeleted, bool expected)
    {
        var id = Guid.CreateVersion7();
        var stored = storedDeleted ? Tombstone(id, Imported) : Live(id, Imported);
        var at = Imported.AddHours(hoursApart);
        var inbound = inboundDeleted ? Tombstone(id, at) : Live(id, at);

        Assert.Equal(expected, TaskChangePrecedence.Supersedes(inbound, stored));
    }

    /// <summary>A capture's tombstone is final. The desktop decides at T1, the
    /// phone's edit lands at T2, and the desktop's tombstone stamped T1 arrives
    /// last: it still wins, and no later echo of the edited live copy brings the
    /// capture back. A capture is never made live again on purpose — a resent
    /// capture writes nothing — so nothing legitimate is lost.</summary>
    [Theory]
    [InlineData(-1, true, false, true)]   // the desktop's earlier tombstone over the phone's later edit
    [InlineData(1, false, true, false)]   // another device's echo of the edit over the tombstone
    [InlineData(1, true, false, true)]    // a later tombstone, as before
    [InlineData(1, false, false, true)]   // a later edit of a live capture, as before
    [InlineData(-1, false, false, false)] // an older live capture, as before
    public void A_capture_tombstone_wins_over_a_live_capture_whatever_the_stamps(int hoursApart, bool inboundDeleted, bool storedDeleted, bool expected)
    {
        var id = Guid.CreateVersion7();
        var stored = storedDeleted ? Tombstone(id, Imported, "capture") : Live(id, Imported, type: "capture");
        var at = Imported.AddHours(hoursApart);
        var inbound = inboundDeleted ? Tombstone(id, at, "capture") : Live(id, at, type: "capture");

        Assert.Equal(expected, TaskChangePrecedence.Supersedes(inbound, stored));
    }

    /// <summary>Only a capture's: a task's or a note's tombstone keeps the stamp
    /// rule, because both of those come back to life on purpose.</summary>
    [Theory]
    [InlineData("task")]
    [InlineData("note")]
    public void Any_other_tombstone_still_yields_to_a_later_live_copy(string type)
    {
        var id = Guid.CreateVersion7();

        Assert.True(TaskChangePrecedence.Supersedes(Live(id, Imported.AddHours(1), type: type), Tombstone(id, Imported, type)));
        Assert.False(TaskChangePrecedence.Supersedes(Tombstone(id, Imported.AddHours(-1), type), Live(id, Imported, type: type)));
    }

    [Fact]
    public async Task The_desktop_tombstone_stamped_before_a_phone_edit_still_tombstones_the_capture()
    {
        var replica = new InMemoryTaskReplica();
        var id = Guid.CreateVersion7();

        await replica.Upsert(Laptop, [Live(id, Imported, type: "capture")], Cancellation);
        await replica.Upsert(Laptop, [Live(id, Imported.AddHours(2), "Reworded on the phone", "capture")], Cancellation);

        var accepted = await replica.Upsert(Desktop, [Tombstone(id, Imported.AddHours(1), "capture")], Cancellation);

        Assert.Equal(1, accepted);
        Assert.Empty(await replica.ListCaptures(Mine, Cancellation));
        Assert.Equal(0, await replica.Upsert(Laptop, [Live(id, Imported.AddHours(2), "Reworded on the phone", "capture")], Cancellation));
        Assert.Empty(await replica.ListCaptures(Mine, Cancellation));
    }

    private static TaskChange Live(Guid id, DateTimeOffset updatedAt, string title = "Expose a Backlog MCP server", string type = "task") =>
        new(id, updatedAt, DeletedAt: null, Payload(title, type));

    private static TaskChange Tombstone(Guid id, DateTimeOffset at, string type = "task") =>
        new(id, at, DeletedAt: at, Payload("Expose a Backlog MCP server", type));

    private static TaskPayload Payload(string title, string type = "task") => new(
        title,
        ContentMd: string.Empty,
        type,
        Status: "ready",
        Priority: "medium",
        Order: 0,
        Area: null,
        Imported,
        SourceInboxId: null,
        RecurrenceSourceId: null,
        DueOn: null,
        RemindAt: null,
        Recurrence: null,
        InMyDayOn: null,
        View: null,
        Effort: null,
        ImportPlanId: "run-plan-item-mcp",
        ImportItemId: "mcp-server",
        AttachmentPath: null,
        Tags: ["run-plan-item-mcp"],
        RepoIds: [],
        DependsOn: [],
        SubItems: [],
        UsageEvents: [],
        ProjectionRefs: []);
}
