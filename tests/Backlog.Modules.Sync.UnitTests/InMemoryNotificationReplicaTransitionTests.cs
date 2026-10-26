using System.Text.Json;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// The replica says which notifications in a push were published or moved state, because only
/// those reach a phone (local ADR 0038 §4). The owner re-pushes every open notification every
/// ten minutes to show it is present, and once a week to refresh it; none of that may buzz a
/// phone again.
/// </summary>
public class InMemoryNotificationReplicaTransitionTests
{
    private static readonly OwnerId Mine = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly OwnerScope Desktop = new(Mine, new DeviceId(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")));
    private static readonly Guid Instance = Guid.Parse("7f3a0000-0000-0000-0000-00000000000a");
    private static readonly DateTimeOffset Published = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_notification_published_now_is_a_transition()
    {
        var replica = new InMemoryNotificationReplica();
        var id = Guid.CreateVersion7();

        var result = await replica.Upsert(Desktop, [Change(id, NotificationStates.Open, Published)], Cancellation);

        Assert.Equal(id, Assert.Single(result.Transitions).Id);
    }

    [Fact]
    public async Task A_state_change_is_a_transition()
    {
        var replica = new InMemoryNotificationReplica();
        var id = Guid.CreateVersion7();
        await replica.Upsert(Desktop, [Change(id, NotificationStates.Open, Published)], Cancellation);

        var result = await replica.Upsert(Desktop, [Change(id, NotificationStates.Answered, Published.AddMinutes(3))], Cancellation);

        var moved = Assert.Single(result.Transitions);
        Assert.Equal(NotificationStates.Answered, moved.State);
    }

    /// <summary>The ten-minute presence refresh, the weekly refresh, and a rejected reply's
    /// update while the question stays open: written, and no transition.</summary>
    [Fact]
    public async Task A_re_push_in_the_same_state_is_written_but_is_no_transition()
    {
        var replica = new InMemoryNotificationReplica();
        var id = Guid.CreateVersion7();
        await replica.Upsert(Desktop, [Change(id, NotificationStates.Open, Published)], Cancellation);

        var result = await replica.Upsert(Desktop, [Change(id, NotificationStates.Open, Published.AddMinutes(10))], Cancellation);

        Assert.Equal(1, result.Accepted);
        Assert.Empty(result.Transitions);
    }

    [Fact]
    public async Task A_stale_change_is_no_transition()
    {
        var replica = new InMemoryNotificationReplica();
        var id = Guid.CreateVersion7();
        await replica.Upsert(Desktop, [Change(id, NotificationStates.Closed, Published.AddMinutes(5))], Cancellation);

        var result = await replica.Upsert(Desktop, [Change(id, NotificationStates.Open, Published)], Cancellation);

        Assert.Equal(0, result.Accepted);
        Assert.Empty(result.Transitions);
    }

    [Fact]
    public async Task A_change_from_another_owner_instance_is_no_transition()
    {
        var replica = new InMemoryNotificationReplica();
        var id = Guid.CreateVersion7();
        await replica.Upsert(Desktop, [Change(id, NotificationStates.Open, Published)], Cancellation);

        var result = await replica.Upsert(
            Desktop,
            [Change(id, NotificationStates.Closed, Published.AddMinutes(5)) with { OwnerInstanceId = Guid.CreateVersion7() }],
            Cancellation);

        Assert.Equal([id], result.NotOwned);
        Assert.Empty(result.Transitions);
    }

    /// <summary>The preview is for the send and is not stored — the Cosmos replica drops it, so
    /// the stand-in does too, and a pull never returns one.</summary>
    [Fact]
    public async Task The_push_preview_is_not_stored()
    {
        var replica = new InMemoryNotificationReplica();
        var id = Guid.CreateVersion7();
        var previewed = Change(id, NotificationStates.Open, Published) with
        {
            Preview = new NotificationPushPreview(NotificationKinds.Alert, NotificationSeverities.Info, "Build finished", "All green.", IsPrivate: false),
        };

        await replica.Upsert(Desktop, [previewed], Cancellation);

        var pulled = Assert.Single((await replica.ReadChanges(Mine, cursor: null, maxItems: 10, Cancellation)).Changes);
        Assert.Null(pulled.Change.Preview);
    }

    private static NotificationChange Change(Guid id, string state, DateTimeOffset updatedAt) =>
        new(id, Instance, state, updatedAt, JsonSerializer.SerializeToElement(new { title = "Run the migration?" }));
}
