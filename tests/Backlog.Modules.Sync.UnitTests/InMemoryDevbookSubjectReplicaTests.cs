using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// A push may never move a subject's state backwards, and an echo is not news.
/// The annotation replica's scenario without its tombstone: one desktop moves a
/// debt record to <c>planned</c>, the other wakes up holding the
/// <c>identified</c> it pulled the day before and pushes that — because what a
/// device received sits above its push watermark exactly as its own changes do.
/// The replica keeps the later state, whichever arrives last.
/// </summary>
public class InMemoryDevbookSubjectReplicaTests
{
    private static readonly OwnerId Mine = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly OwnerScope Desktop = new(Mine, new DeviceId(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")));
    private static readonly OwnerScope Laptop = new(Mine, new DeviceId(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002")));
    private static readonly DateTimeOffset Identified = new(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Planned = new(2026, 10, 30, 11, 30, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_older_state_pushed_after_a_later_one_does_not_replace_it()
    {
        var replica = new InMemoryDevbookSubjectReplica();
        var id = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Debt(id, Identified, "identified")], Cancellation);
        await replica.Upsert(Desktop, [Debt(id, Planned, "planned")], Cancellation);

        var accepted = await replica.Upsert(Laptop, [Debt(id, Identified, "identified")], Cancellation);

        Assert.Equal(0, accepted);
        var stored = await Stored(replica, id);
        Assert.Equal("planned", stored.Change.Subject.State);
        Assert.Equal(Desktop.DeviceId.Value, stored.DeviceId);
    }

    [Fact]
    public async Task The_same_version_twice_is_refused_and_does_not_move_in_the_feed()
    {
        var replica = new InMemoryDevbookSubjectReplica();
        var id = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Debt(id, Planned, "planned")], Cancellation);
        var caughtUp = (await replica.ReadChanges(Mine, cursor: null, maxItems: 10, Cancellation)).Cursor;

        var accepted = await replica.Upsert(Laptop, [Debt(id, Planned, "planned")], Cancellation);

        Assert.Equal(0, accepted);
        Assert.Empty((await replica.ReadChanges(Mine, caughtUp, maxItems: 10, Cancellation)).Changes);
    }

    [Fact]
    public async Task A_later_state_replaces_the_stored_one_whole_and_is_counted()
    {
        var replica = new InMemoryDevbookSubjectReplica();
        var id = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Debt(id, Identified, "identified")], Cancellation);
        var accepted = await replica.Upsert(Laptop, [Debt(id, Planned, "planned", by: "DEV-LAPTOP")], Cancellation);

        Assert.Equal(1, accepted);
        var stored = await Stored(replica, id);
        Assert.Equal("planned", stored.Change.Subject.State);
        Assert.Equal("DEV-LAPTOP", stored.Change.Subject.By);
        Assert.Equal(Laptop.DeviceId.Value, stored.DeviceId);
    }

    [Fact]
    public async Task A_batch_counts_only_what_it_wrote()
    {
        var replica = new InMemoryDevbookSubjectReplica();
        var stale = Guid.CreateVersion7();
        var fresh = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Debt(stale, Planned, "planned")], Cancellation);

        var accepted = await replica.Upsert(Laptop, [Debt(stale, Identified, "identified"), Debt(fresh, Identified, "identified")], Cancellation);

        Assert.Equal(1, accepted);
    }

    [Fact]
    public async Task Another_owners_feed_holds_none_of_it()
    {
        var replica = new InMemoryDevbookSubjectReplica();
        var somebodyElse = new OwnerId(Guid.Parse("99999999-9999-9999-9999-999999999999"));

        await replica.Upsert(Desktop, [Debt(Guid.CreateVersion7(), Planned, "planned")], Cancellation);

        Assert.Empty((await replica.ReadChanges(somebodyElse, cursor: null, maxItems: 10, Cancellation)).Changes);
    }

    [Theory]
    [InlineData(1, true)]   // later
    [InlineData(0, false)]  // the same stamp: the same version, or a race nobody can order
    [InlineData(-1, false)] // older
    public void Precedence_is_the_stamp_alone(int minutesApart, bool expected)
    {
        var id = Guid.CreateVersion7();

        Assert.Equal(
            expected,
            DevbookSubjectChangePrecedence.Supersedes(Debt(id, Planned.AddMinutes(minutesApart), "resolved"), Debt(id, Planned, "planned")));
    }

    /// <summary>The port has no <c>Find</c>, as the annotation port has none,
    /// so the copy held is what the feed hands out for that id.</summary>
    private static async Task<DevbookSubjectChangeRecord> Stored(InMemoryDevbookSubjectReplica replica, Guid id)
    {
        var page = await replica.ReadChanges(Mine, cursor: null, maxItems: 100, Cancellation);

        return Assert.Single(page.Changes, record => record.Change.Id == id);
    }

    private static DevbookSubjectChange Debt(Guid id, DateTimeOffset updatedAt, string state, string by = "DEV-TOWER") =>
        new(id, updatedAt, new DevbookSubjectPayload("JSdotNet/Backlog", "debt", ".devbook/arc42/tdr/0003-paging.md", null, state, by, updatedAt));
}
