// The Cosmos replica's half of "only a new notification or a state change reaches a phone"
// (local ADR 0039 §4). The read-compare-write loop itself needs an emulator; the decision it
// makes between the read and the write does not, and it is what decides a transition.

using System.Text.Json;
using Backlog.Infrastructure.Cosmos.Notifications;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

public class CosmosNotificationReplicaTransitionTests
{
    private static readonly OwnerScope Scope = new(
        new OwnerId(Guid.Parse("2a1d1b8e-2f2c-4a4b-9c3d-5e6f70819293")),
        new DeviceId(Guid.Parse("3b2e2c9f-3a3d-4b5c-8d4e-6f708192a3b4")));

    private static readonly Guid NotificationId = Guid.Parse("5d4a4ebb-5c5f-4d7e-af60-8192a3b4c5d6");

    private static readonly Guid Instance = Guid.Parse("7f3a0000-0000-0000-0000-0000000000aa");

    private static readonly DateTimeOffset Published = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_notification_the_replica_does_not_hold_is_a_transition()
    {
        Assert.Equal(
            CosmosNotificationReplica.WriteOutcome.Transition,
            CosmosNotificationReplica.Decide(held: null, Change(NotificationStates.Open, Published)));
    }

    [Fact]
    public void A_state_change_is_a_transition()
    {
        var held = Held(NotificationStates.Open, Published);

        Assert.Equal(
            CosmosNotificationReplica.WriteOutcome.Transition,
            CosmosNotificationReplica.Decide(held, Change(NotificationStates.Answered, Published.AddMinutes(3))));
    }

    /// <summary>The owner's ten-minute presence refresh: written, and no phone is pushed.</summary>
    [Fact]
    public void A_re_push_in_the_state_held_is_written_but_is_no_transition()
    {
        var held = Held(NotificationStates.Open, Published);

        Assert.Equal(
            CosmosNotificationReplica.WriteOutcome.Written,
            CosmosNotificationReplica.Decide(held, Change(NotificationStates.Open, Published.AddMinutes(10))));
    }

    [Fact]
    public void A_change_older_than_the_one_held_is_stale()
    {
        var held = Held(NotificationStates.Closed, Published.AddMinutes(5));

        Assert.Equal(
            CosmosNotificationReplica.WriteOutcome.Stale,
            CosmosNotificationReplica.Decide(held, Change(NotificationStates.Open, Published)));
    }

    [Fact]
    public void A_change_from_another_owner_instance_is_not_owned()
    {
        var held = Held(NotificationStates.Open, Published);

        Assert.Equal(
            CosmosNotificationReplica.WriteOutcome.NotOwned,
            CosmosNotificationReplica.Decide(held, Change(NotificationStates.Closed, Published.AddMinutes(5)) with { OwnerInstanceId = Guid.CreateVersion7() }));
    }

    private static NotificationDocument Held(string state, DateTimeOffset updatedAt) =>
        NotificationDocumentFactory.From(Scope, Change(state, updatedAt), new CosmosOptions());

    private static NotificationChange Change(string state, DateTimeOffset updatedAt) =>
        new(NotificationId, Instance, state, updatedAt, JsonSerializer.SerializeToElement(new { title = "Run the migration?" }));
}
