// What is testable about the notification adapter without Cosmos: the shape of the two
// documents, the names the containers are partitioned and queried on, and the TTL rule
// (local ADR 0039 §3). Change-feed delivery and TTL expiry are not, for the reasons
// TaskDocumentTests gives at its head.

using System.Text.Json;
using Backlog.Infrastructure.Cosmos.Notifications;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Infrastructure.Cosmos.UnitTests;

public class NotificationDocumentTests
{
    private static readonly OwnerScope Scope = new(
        new OwnerId(Guid.Parse("2a1d1b8e-2f2c-4a4b-9c3d-5e6f70819293")),
        new DeviceId(Guid.Parse("3b2e2c9f-3a3d-4b5c-8d4e-6f708192a3b4")));

    private static readonly Guid NotificationId = Guid.Parse("5d4a4ebb-5c5f-4d7e-af60-8192a3b4c5d6");

    private static readonly Guid Instance = Guid.Parse("7f3a0000-0000-0000-0000-0000000000aa");

    /// <summary>Not a deadline — the owner re-pushes an open notification weekly — but an
    /// owner unpaired for good leaves nothing behind.</summary>
    [Fact]
    public void An_open_notification_carries_the_ninety_day_backstop()
    {
        var document = NotificationDocumentFactory.From(Scope, Change(NotificationStates.Open), new CosmosOptions());

        Assert.Equal(90 * 24 * 60 * 60, document.Ttl);
    }

    [Theory]
    [InlineData(NotificationStates.Acknowledged)]
    [InlineData(NotificationStates.Answered)]
    [InlineData(NotificationStates.Closed)]
    public void A_notification_that_left_open_expires_thirty_days_later(string state)
    {
        var document = NotificationDocumentFactory.From(Scope, Change(state), new CosmosOptions());

        Assert.Equal(30 * 24 * 60 * 60, document.Ttl);
    }

    [Fact]
    public void A_notification_round_trips_and_names_the_device_that_wrote_it()
    {
        var change = Change(NotificationStates.Open);

        var record = NotificationDocumentFactory.ToRecord(NotificationDocumentFactory.From(Scope, change, new CosmosOptions()));

        Assert.NotNull(record);
        Assert.Equal(change.Id, record.Change.Id);
        Assert.Equal(Instance, record.Change.OwnerInstanceId);
        Assert.Equal(Scope.DeviceId.Value, record.DeviceId);
        Assert.Equal("Run the migration?", record.Change.Notification.GetProperty("title").GetString());
    }

    [Fact]
    public void The_notification_document_serialises_with_the_names_the_container_is_partitioned_on()
    {
        var document = NotificationDocumentFactory.From(Scope, Change(NotificationStates.Closed), new CosmosOptions());

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(document, ReplicaDocumentSerialization.Options));
        var root = json.RootElement;

        Assert.Equal(NotificationId.ToString("D"), root.GetProperty("id").GetString());
        Assert.Equal(Scope.OwnerId.Value.ToString("D"), root.GetProperty("ownerId").GetString());
        Assert.Equal(Scope.DeviceId.Value.ToString("D"), root.GetProperty("deviceId").GetString());
        Assert.Equal(NotificationStates.Closed, root.GetProperty("state").GetString());
        Assert.Equal(2_592_000, root.GetProperty("ttl").GetInt32());
        Assert.Equal("Run the migration?", root.GetProperty("notification").GetProperty("title").GetString());
    }

    [Fact]
    public void A_reply_is_stamped_with_its_owner_and_poster_and_addressed_to_the_owner_instance()
    {
        var reply = new NotificationReplyPost(Guid.CreateVersion7(), NotificationId, Instance, ["yes"], null, DateTimeOffset.UtcNow);

        var document = NotificationDocumentFactory.From(Scope, reply, new CosmosOptions());

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(document, ReplicaDocumentSerialization.Options));
        var root = json.RootElement;
        Assert.Equal(Scope.OwnerId.Value.ToString("D"), root.GetProperty("ownerId").GetString());
        Assert.Equal(Instance.ToString("D"), root.GetProperty("ownerInstanceId").GetString());
        Assert.Equal(Scope.DeviceId.Value.ToString("D"), root.GetProperty("fromDeviceId").GetString());
        Assert.Equal(2_592_000, root.GetProperty("ttl").GetInt32());

        var record = NotificationDocumentFactory.ToRecord(document);
        Assert.NotNull(record);
        Assert.Equal(reply.Id, record.Reply.Id);
        Assert.Equal(Scope.OwnerId.Value, record.OwnerId);
        Assert.Equal(Scope.DeviceId.Value, record.FromDeviceId);
        Assert.Equal(["yes"], record.Reply.ChoiceIds);
    }

    [Fact]
    public void A_reply_keeps_the_note_written_beside_its_answer()
    {
        var reply = new NotificationReplyPost(Guid.CreateVersion7(), NotificationId, Instance, ["half-even"], null, DateTimeOffset.UtcNow, "Only for invoice totals");

        var document = NotificationDocumentFactory.From(Scope, reply, new CosmosOptions());

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(document, ReplicaDocumentSerialization.Options));
        Assert.Equal("Only for invoice totals", json.RootElement.GetProperty("note").GetString());

        var record = NotificationDocumentFactory.ToRecord(document);
        Assert.NotNull(record);
        Assert.Equal(["half-even"], record.Reply.ChoiceIds);
        Assert.Equal("Only for invoice totals", record.Reply.Note);
    }

    private static NotificationChange Change(string state) =>
        new(NotificationId, Instance, state, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { title = "Run the migration?" }));
}
