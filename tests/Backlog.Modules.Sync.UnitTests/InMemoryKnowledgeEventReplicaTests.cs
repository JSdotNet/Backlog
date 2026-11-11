using System.Text.Json;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// Knowledge events travel between a person's desktops and on to the phone under
/// whole-document last-write-wins (local ADR 0050 §2): every desktop may write, the later
/// stamp wins, and an echo is not news. One desktop links a talk note to an event, the other
/// wakes up holding the copy it pulled the day before and pushes that, because what a
/// device received sits above its push watermark exactly as its own changes do. The
/// replica keeps the later copy, whichever arrives last.
/// </summary>
public class InMemoryKnowledgeEventReplicaTests
{
    private static readonly OwnerId Mine = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly OwnerScope Desktop = new(Mine, new DeviceId(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")));
    private static readonly OwnerScope Laptop = new(Mine, new DeviceId(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002")));
    private static readonly DateTimeOffset Planned = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Attended = new(2026, 11, 9, 17, 30, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_older_copy_pushed_after_a_later_one_does_not_replace_it()
    {
        var replica = new InMemoryKnowledgeEventReplica();
        var id = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Event(id, Planned, "Planned")], Cancellation);
        await replica.Upsert(Desktop, [Event(id, Attended, "Attended")], Cancellation);

        var accepted = await replica.Upsert(Laptop, [Event(id, Planned, "Planned")], Cancellation);

        Assert.Equal(0, accepted);
        var stored = await Stored(replica, id);
        Assert.Equal("Attended", stored.Change.Document.GetProperty("state").GetString());
        Assert.Equal(Desktop.DeviceId.Value, stored.DeviceId);
    }

    [Fact]
    public async Task The_same_version_twice_is_refused_and_does_not_move_in_the_feed()
    {
        var replica = new InMemoryKnowledgeEventReplica();
        var id = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Event(id, Attended, "Attended")], Cancellation);
        var caughtUp = (await replica.ReadChanges(Mine, cursor: null, maxItems: 10, Cancellation)).Cursor;

        var accepted = await replica.Upsert(Laptop, [Event(id, Attended, "Attended")], Cancellation);

        Assert.Equal(0, accepted);
        Assert.Empty((await replica.ReadChanges(Mine, caughtUp, maxItems: 10, Cancellation)).Changes);
    }

    [Fact]
    public async Task A_later_copy_replaces_the_stored_one_whole_and_is_counted()
    {
        var replica = new InMemoryKnowledgeEventReplica();
        var id = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Event(id, Planned, "Planned")], Cancellation);
        var accepted = await replica.Upsert(Laptop, [Event(id, Attended, "Attended")], Cancellation);

        Assert.Equal(1, accepted);
        var stored = await Stored(replica, id);
        Assert.Equal("Attended", stored.Change.Document.GetProperty("state").GetString());
        Assert.Equal(Attended, stored.Change.UpdatedAt);
        Assert.Equal(Laptop.DeviceId.Value, stored.DeviceId);
    }

    [Fact]
    public async Task A_series_rides_the_same_feed_under_its_own_kind()
    {
        var replica = new InMemoryKnowledgeEventReplica();
        var series = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Series(series, Planned)], Cancellation);

        var stored = await Stored(replica, series);
        Assert.Equal(KnowledgeEventKinds.EventSeries, stored.Change.Kind);
        Assert.Equal("NDC Oslo", stored.Change.Document.GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_batch_counts_only_what_it_wrote()
    {
        var replica = new InMemoryKnowledgeEventReplica();
        var stale = Guid.CreateVersion7();
        var fresh = Guid.CreateVersion7();

        await replica.Upsert(Desktop, [Event(stale, Attended, "Attended")], Cancellation);

        var accepted = await replica.Upsert(Laptop, [Event(stale, Planned, "Planned"), Event(fresh, Planned, "Planned")], Cancellation);

        Assert.Equal(1, accepted);
    }

    [Fact]
    public async Task Another_owners_feed_holds_none_of_it()
    {
        var replica = new InMemoryKnowledgeEventReplica();
        var somebodyElse = new OwnerId(Guid.Parse("99999999-9999-9999-9999-999999999999"));

        await replica.Upsert(Desktop, [Event(Guid.CreateVersion7(), Planned, "Planned")], Cancellation);

        Assert.Empty((await replica.ReadChanges(somebodyElse, cursor: null, maxItems: 10, Cancellation)).Changes);
    }

    [Fact]
    public async Task The_feed_pages_from_its_cursor_until_it_is_drained()
    {
        var replica = new InMemoryKnowledgeEventReplica();
        Guid[] ids = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];

        await replica.Upsert(Desktop, [.. ids.Select(id => Event(id, Planned, "Planned"))], Cancellation);

        var first = await replica.ReadChanges(Mine, cursor: null, maxItems: 2, Cancellation);
        var second = await replica.ReadChanges(Mine, first.Cursor, maxItems: 2, Cancellation);

        Assert.True(first.HasMore);
        Assert.False(second.HasMore);
        Assert.Equal(ids, first.Changes.Concat(second.Changes).Select(record => record.Change.Id).ToArray());
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
            KnowledgeEventChangePrecedence.Supersedes(Event(id, Attended.AddMinutes(minutesApart), "Skipped"), Event(id, Attended, "Attended")));
    }

    /// <summary>The port has no <c>Find</c>, as the devbook subject port has none, so the copy
    /// held is what the feed hands out for that id.</summary>
    private static async Task<KnowledgeEventChangeRecord> Stored(InMemoryKnowledgeEventReplica replica, Guid id)
    {
        var page = await replica.ReadChanges(Mine, cursor: null, maxItems: 100, Cancellation);

        return Assert.Single(page.Changes, record => record.Change.Id == id);
    }

    private static KnowledgeEventChange Event(Guid id, DateTimeOffset updatedAt, string state) =>
        new(id, KnowledgeEventKinds.KnowledgeEvent, updatedAt, Json($$"""{"title":"NDC Oslo 2026","state":"{{state}}"}"""));

    private static KnowledgeEventChange Series(Guid id, DateTimeOffset updatedAt) =>
        new(id, KnowledgeEventKinds.EventSeries, updatedAt, Json("""{"name":"NDC Oslo"}"""));

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
