using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// A push may never move an annotation backwards. The same scenario the task
/// replica's precedence tests pin for the first container, over the third:
/// one desktop resolves or deletes a remark and
/// pushes the tombstone, the other wakes up still holding the live copy it
/// pulled days ago and pushes that first — because everything a device
/// received sits above its push watermark exactly as its own edits do. The
/// replica has to keep the tombstone, or the remark comes back on both
/// machines.
/// </summary>
public class InMemoryAnnotationReplicaPrecedenceTests
{
    private static readonly OwnerId Mine = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly OwnerScope Desktop = new(Mine, new DeviceId(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")));
    private static readonly OwnerScope Laptop = new(Mine, new DeviceId(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002")));
    private static readonly DateTimeOffset Written = new(2026, 9, 14, 17, 53, 22, TimeSpan.Zero);
    private static readonly DateTimeOffset Deleted = new(2026, 9, 17, 21, 55, 5, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_older_live_copy_pushed_after_a_tombstone_does_not_replace_it()
    {
        var replica = new InMemoryAnnotationReplica();
        var id = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Live(id, Written)], Cancellation);
        await replica.Upsert(Desktop, [Tombstone(id, Deleted)], Cancellation);

        // The laptop's echo of the version it pulled on the 14th.
        var accepted = await replica.Upsert(Laptop, [Live(id, Written)], Cancellation);

        Assert.Equal(0, accepted);
        var stored = await Stored(replica, id);
        Assert.Equal(Deleted, stored.Change.DeletedAt);
        Assert.Equal(Desktop.DeviceId.Value, stored.DeviceId);
    }

    [Fact]
    public async Task A_refused_push_does_not_move_the_document_in_the_feed()
    {
        var replica = new InMemoryAnnotationReplica();
        var id = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Live(id, Written)], Cancellation);
        var caughtUp = (await replica.ReadChanges(Mine, cursor: null, maxItems: 10, Cancellation)).Cursor;

        await replica.Upsert(Laptop, [Live(id, Written)], Cancellation);

        // Nothing new happened, so a device that was caught up stays caught up
        // rather than being handed its own echo as a change.
        var page = await replica.ReadChanges(Mine, caughtUp, maxItems: 10, Cancellation);
        Assert.Empty(page.Changes);
    }

    [Fact]
    public async Task A_later_edit_replaces_the_stored_copy_and_is_counted()
    {
        var replica = new InMemoryAnnotationReplica();
        var id = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Live(id, Written)], Cancellation);
        var accepted = await replica.Upsert(Laptop, [Live(id, Written.AddHours(1), "Reworded on the laptop.")], Cancellation);

        Assert.Equal(1, accepted);
        var stored = await Stored(replica, id);
        Assert.Equal("Reworded on the laptop.", stored.Change.Annotation.Body);
        Assert.Equal(Laptop.DeviceId.Value, stored.DeviceId);
    }

    [Fact]
    public async Task A_tombstone_of_the_very_version_held_is_taken_on_a_tied_stamp()
    {
        var replica = new InMemoryAnnotationReplica();
        var id = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Live(id, Written)], Cancellation);
        var accepted = await replica.Upsert(Desktop, [Tombstone(id, Written)], Cancellation);

        Assert.Equal(1, accepted);
        Assert.NotNull((await Stored(replica, id)).Change.DeletedAt);
    }

    [Fact]
    public async Task A_batch_counts_only_what_it_wrote()
    {
        var replica = new InMemoryAnnotationReplica();
        var stale = Guid.CreateVersion7();
        var fresh = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Live(stale, Written), Tombstone(stale, Deleted)], Cancellation);

        var accepted = await replica.Upsert(Laptop, [Live(stale, Written), Live(fresh, Written)], Cancellation);

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
        var stored = storedDeleted ? Tombstone(id, Written) : Live(id, Written);
        var at = Written.AddHours(hoursApart);
        var inbound = inboundDeleted ? Tombstone(id, at) : Live(id, at);

        Assert.Equal(expected, AnnotationChangePrecedence.Supersedes(inbound, stored));
    }

    /// <summary>The port has no <c>Find</c> — by design (.arc42/adr/0011) — so
    /// the copy held is what the feed hands out for that id, read from the
    /// beginning.</summary>
    private static async Task<AnnotationChangeRecord> Stored(InMemoryAnnotationReplica replica, Guid id)
    {
        var page = await replica.ReadChanges(Mine, cursor: null, maxItems: 100, Cancellation);

        return Assert.Single(page.Changes, record => record.Change.Id == id);
    }

    private static AnnotationChange Live(Guid id, DateTimeOffset updatedAt, string body = "Say which team owns this.") =>
        new(id, updatedAt, DeletedAt: null, Payload(body));

    private static AnnotationChange Tombstone(Guid id, DateTimeOffset at) =>
        new(id, at, DeletedAt: at, Payload("Say which team owns this."));

    private static AnnotationPayload Payload(string body) =>
        new("JSdotNet/Backlog", ".domain/devbook/features.md", BlockIndex: 2, body, "DEV-TOWER", Written, Resolved: false);
}
