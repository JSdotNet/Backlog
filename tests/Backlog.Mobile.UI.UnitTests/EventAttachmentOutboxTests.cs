using System.Net;
using System.Net.Http.Json;

using Backlog.Infrastructure.Sync;
using Backlog.Mobile.UI.Events;
using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Progress;
using Backlog.Mobile.UI.Tasks;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A Talk note attached to an event from the phone (<c>.devbook/domain/events/features.md#attach-a-talk-note</c>):
/// one <c>event-attachment-request</c> document queued in the device outbox under the request's
/// id, naming the event and the note with the note's title, and pushed to the task feed — never
/// an event write, and never a task or a note on the phone's own folds.
/// </summary>
public sealed class EventAttachmentOutboxTests
{
    private static readonly Guid EventId = Guid.Parse("0c3a5e7f-2222-4b2c-9d8e-000000000002");
    private static readonly Guid OtherEventId = Guid.Parse("0c3a5e7f-2222-4b2c-9d8e-000000000009");
    private static readonly Guid NoteId = Guid.Parse("0c3a5e7f-3333-4b2c-9d8e-000000000003");
    private static readonly DateTimeOffset Keynote = new(2026, 10, 27, 9, 40, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Attaching_queues_one_request_naming_the_event_and_the_note_with_its_title()
    {
        var (outbox, attachments, _) = Attachments();

        var requestId = await attachments.AttachAsync(EventId, NoteId, " Keynote: Aspire in production ", Cancellation);

        var entry = Assert.Single(outbox.Entries);
        Assert.Equal("event-attachment-request", entry.Kind);
        Assert.Equal(requestId, entry.Id);
        var change = TaskOutboxKind.Read(entry);
        Assert.Equal(requestId, change.Id);
        Assert.Equal("event-attachment-request", change.Task.Type);
        Assert.Equal(Keynote, change.UpdatedAt);
        Assert.Equal(new EventAttachmentRequestDto(requestId, EventId, NoteId, "Keynote: Aspire in production"), EventAttachmentReplicaDocuments.ToRequest(change));
    }

    [Fact]
    public async Task Attaching_the_same_note_to_the_same_event_again_while_it_waits_queues_nothing_new()
    {
        var (outbox, attachments, _) = Attachments();

        var first = await attachments.AttachAsync(EventId, NoteId, "Keynote", Cancellation);
        var again = await attachments.AttachAsync(EventId, NoteId, "Keynote", Cancellation);

        Assert.Equal(first, again);
        Assert.Single(outbox.Entries);
    }

    [Fact]
    public async Task Attaching_again_tries_a_parked_request_at_once_and_replaces_a_refused_one()
    {
        var clock = new FakeTimeProvider(Keynote);
        var store = new InMemoryDeviceStore();
        var parked = Queued(EventId) with { Attempts = DeviceOutbox.MaxAttempts, LastError = "No such host is known." };
        var refused = Queued(OtherEventId) with { Refused = true, LastError = "The request names no event." };
        await store.AddAsync(parked, Cancellation);
        await store.AddAsync(refused, Cancellation);
        var network = new Network();
        var kind = new EventAttachmentRequestOutboxKind(TestCloudSync.Client(_ => network.Online
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { accepted = 1 }) }
            : throw new HttpRequestException("No such host is known.")));
        using var outbox = new DeviceOutbox(store, [kind], clock);
        var attachments = new EventNoteAttachments(outbox, clock);

        Assert.Equal(new PendingEventAttachment(OtherEventId, refused), attachments.PendingFor(NoteId));

        // The refused one will be refused again: a new request goes in its place.
        var fresh = await attachments.AttachAsync(OtherEventId, NoteId, "Keynote", Cancellation);
        Assert.NotEqual(refused.Id, fresh);
        Assert.Equal(fresh, attachments.PendingFor(NoteId)!.Entry.Id);

        // The parked one is the same request, sent again from no attempts.
        network.Online = true;
        var again = await attachments.AttachAsync(EventId, NoteId, "Keynote", Cancellation);
        Assert.Equal(parked.Id, again);
        await outbox.WhenIdleAsync();
        Assert.Equal([refused.Id], outbox.Entries.Select(entry => entry.Id));
    }

    [Fact]
    public async Task The_note_waits_on_the_event_it_was_last_attached_to_until_the_request_leaves_the_phone()
    {
        var (outbox, attachments, network) = Attachments();
        Assert.Null(attachments.PendingEventFor(NoteId));

        await attachments.AttachAsync(EventId, NoteId, "Keynote", Cancellation);
        var second = await attachments.AttachAsync(OtherEventId, NoteId, "Keynote", Cancellation);

        Assert.Equal(OtherEventId, attachments.PendingEventFor(NoteId));
        Assert.Null(attachments.PendingEventFor(Guid.NewGuid()));
        Assert.Equal(2, outbox.Entries.Count);

        network.Online = true;
        await outbox.ResumeAsync(Cancellation);

        Assert.Empty(outbox.Entries);
        Assert.Null(attachments.PendingEventFor(NoteId));
        Assert.NotEqual(Guid.Empty, second);
    }

    [Fact]
    public async Task One_attempt_pushes_the_request_to_the_task_feed_under_its_own_id_and_a_retry_sends_the_same_document()
    {
        var bodies = new List<PushTasksRequest>();
        var kind = new EventAttachmentRequestOutboxKind(TestCloudSync.Client(request =>
        {
            bodies.Add(request.Content!.ReadFromJsonAsync<PushTasksRequest>().GetAwaiter().GetResult()!);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { accepted = 1 }) };
        }));
        var requestId = Guid.NewGuid();
        var change = EventAttachmentReplicaDocuments.ToChange(new EventAttachmentRequestDto(requestId, EventId, NoteId, "Keynote"), Keynote);
        var entry = new OutboxEntry(requestId, EventAttachmentRequestOutboxKind.Token, TaskOutboxKind.Write(change), 0, null, Keynote);

        var first = await kind.SendAsync(entry, Cancellation);
        var retry = await kind.SendAsync(entry, Cancellation);

        Assert.Equal(OutboxDeliveryKind.Delivered, first.Kind);
        Assert.Equal(OutboxDeliveryKind.Delivered, retry.Kind);
        Assert.Equal("event-attachment-request", kind.Kind);
        Assert.Equal(2, bodies.Count);
        Assert.All(bodies, body =>
        {
            var sent = Assert.Single(body.Tasks);
            Assert.Equal((change.Id, change.UpdatedAt, change.Task.Type), (sent.Id, sent.UpdatedAt, sent.Task.Type));
            Assert.Equal(EventAttachmentReplicaDocuments.ToRequest(change), EventAttachmentReplicaDocuments.ToRequest(sent));
        });
    }

    [Fact]
    public void An_attachment_request_on_the_feed_is_never_a_task_a_note_or_a_progress_row()
    {
        var change = EventAttachmentReplicaDocuments.ToChange(new EventAttachmentRequestDto(Guid.NewGuid(), EventId, NoteId, "Keynote"), Keynote);
        var record = new TaskChangeRecord(change, Guid.NewGuid(), 1);
        var tasks = new Dictionary<Guid, TaskViewRow>();
        var notes = new Dictionary<Guid, NoteViewRow>();

        Assert.Empty(TaskFold.Apply(tasks, [record]));
        Assert.Empty(NoteFold.Apply(notes, [record]));
        Assert.False(ProgressFold.Keeps(change.Task.Type));
        Assert.Empty(tasks);
        Assert.Empty(notes);
    }

    /// <summary>An outbox whose pushes find no network until the test says it is back, so a
    /// queued request stays on the phone while the test looks at it.</summary>
    private static (DeviceOutbox Outbox, EventNoteAttachments Attachments, Network Network) Attachments()
    {
        var clock = new FakeTimeProvider(Keynote);
        var network = new Network();
        var kind = new EventAttachmentRequestOutboxKind(TestCloudSync.Client(_ => network.Online
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { accepted = 1 }) }
            : throw new HttpRequestException("No such host is known.")));
        var outbox = new DeviceOutbox(new InMemoryDeviceStore(), [kind], clock);

        return (outbox, new EventNoteAttachments(outbox, clock), network);
    }

    /// <summary>A request for the note queued on an earlier visit, as the store kept it.</summary>
    private static OutboxEntry Queued(Guid eventId)
    {
        var requestId = Guid.CreateVersion7();
        var change = EventAttachmentReplicaDocuments.ToChange(new EventAttachmentRequestDto(requestId, eventId, NoteId, "Keynote"), Keynote);
        return new OutboxEntry(requestId, EventAttachmentRequestOutboxKind.Token, TaskOutboxKind.Write(change), 0, null, Keynote);
    }

    private sealed class Network
    {
        public bool Online { get; set; }
    }
}
