using System.Text.Json;

using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Events.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>Events' attachment port, answering whatever the test said and keeping what it was
/// offered.</summary>
internal sealed class RecordingEventAttachments : IEventAttachmentRequests
{
    public List<EventAttachmentRequestDto> Offered { get; } = [];

    public EventAttachmentOutcome Answer { get; set; } = EventAttachmentOutcome.Attached;

    public Task<EventAttachmentOutcome> ApplyAsync(EventAttachmentRequestDto request, CancellationToken cancellationToken = default)
    {
        Offered.Add(request);
        return Task.FromResult(Answer);
    }
}

/// <summary>
/// The <c>event-attachment-request</c> the phone writes on the task feed to attach a Talk note
/// to a knowledge event (<c>.devbook/domain/events/domain.md#attachment-request</c>): handed to
/// Events, then retired with a tombstone once Events has answered — on the terms the
/// <c>impediment-edit</c> travels on (local ADR 0035 §8). Never a task; held on the replica by a
/// desktop without Events or with the <c>events</c> area off.
/// </summary>
public sealed class EventAttachmentRequestSyncTests
{
    private static readonly Guid Phone = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid EventId = Guid.Parse("0c3a5e7f-2222-4b2c-9d8e-000000000002");
    private static readonly Guid NoteId = Guid.Parse("0c3a5e7f-3333-4b2c-9d8e-000000000003");
    private static readonly DateTimeOffset Morning = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static EventAttachmentRequestDto Request(Guid? id = null) =>
        new(id ?? Guid.NewGuid(), EventId, NoteId, "Keynote: Aspire in production");

    private static TaskReplicaMerge Merge(
        InMemoryTaskStore tasks,
        IEventAttachmentRequests? attachments,
        ImpedimentEditRetirements retirements,
        bool eventsOn = true,
        SyncActivityLog? activity = null,
        TimeProvider? time = null,
        KeyedFeatureSettings? features = null) =>
        new(tasks,
            activity: activity,
            retirements: retirements,
            time: time,
            eventAttachments: attachments,
            features: features ?? new KeyedFeatureSettings(syncOn: true, eventsOn: eventsOn));

    [Fact]
    public async Task A_request_from_the_phone_goes_to_events_and_its_tombstone_waits_for_the_push()
    {
        var tasks = new InMemoryTaskStore();
        var attachments = new RecordingEventAttachments();
        var retirements = new ImpedimentEditRetirements();
        var activity = new SyncActivityLog();
        var request = Request();
        var change = EventAttachmentReplicaDocuments.ToChange(request, Morning);

        var merged = await Merge(tasks, attachments, retirements, activity: activity, time: new FakeTimeProvider(Morning.AddMinutes(10)))
            .ApplyAsync([TaskChanges.Record(change, Phone, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(1, 0), merged);
        Assert.Empty(tasks.Writes);
        Assert.Equal(request, Assert.Single(attachments.Offered));
        var tombstone = Assert.Single(retirements.Waiting());
        Assert.Equal(change.Id, tombstone.Id);
        Assert.Equal(Morning.AddMinutes(10), tombstone.DeletedAt);
        Assert.Equal(Morning.AddMinutes(10), tombstone.UpdatedAt);
        var entry = Assert.Single(activity.Snapshot());
        Assert.Equal(SyncItemKind.Note, entry.Kind);
        Assert.Equal("Keynote: Aspire in production", entry.Title);
    }

    [Theory]
    [InlineData(EventAttachmentOutcome.AlreadyAttached)]
    [InlineData(EventAttachmentOutcome.Incomplete)]
    public async Task A_request_events_answered_without_attaching_is_retired_all_the_same(EventAttachmentOutcome answer)
    {
        var retirements = new ImpedimentEditRetirements();

        var merged = await Merge(new InMemoryTaskStore(), new RecordingEventAttachments { Answer = answer }, retirements)
            .ApplyAsync([TaskChanges.Record(EventAttachmentReplicaDocuments.ToChange(Request(), Morning), Phone, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 0), merged);
        Assert.Single(retirements.Waiting());
    }

    [Fact]
    public async Task A_desktop_without_events_or_with_the_events_area_off_holds_the_request_on_the_replica()
    {
        var tasks = new InMemoryTaskStore();
        var retirements = new ImpedimentEditRetirements();
        var attachments = new RecordingEventAttachments();
        var page = new[] { TaskChanges.Record(EventAttachmentReplicaDocuments.ToChange(Request(), Morning), Phone, 100) };

        var withoutEvents = await Merge(tasks, attachments: null, retirements).ApplyAsync(page, Cancellation);
        var areaOff = await Merge(tasks, attachments, retirements, eventsOn: false).ApplyAsync(page, Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 0), withoutEvents);
        Assert.Equal(new TaskMergeOutcome(0, 0), areaOff);
        Assert.Empty(attachments.Offered);
        Assert.Empty(retirements.Waiting());
        Assert.Empty(tasks.Writes);
    }

    [Fact]
    public async Task A_retired_request_is_never_offered_again()
    {
        var retirements = new ImpedimentEditRetirements();
        var attachments = new RecordingEventAttachments();
        var change = EventAttachmentReplicaDocuments.Retire(EventAttachmentReplicaDocuments.ToChange(Request(), Morning), Morning.AddHours(1));

        var merged = await Merge(new InMemoryTaskStore(), attachments, retirements)
            .ApplyAsync([TaskChanges.Record(change, Phone, 101)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 0), merged);
        Assert.Empty(attachments.Offered);
        Assert.Empty(retirements.Waiting());
    }

    [Fact]
    public async Task A_request_met_before_its_event_is_offered_again_once_the_pull_is_done()
    {
        var retirements = new ImpedimentEditRetirements();
        var attachments = new RecordingEventAttachments { Answer = EventAttachmentOutcome.UnknownEvent };
        var merge = Merge(new InMemoryTaskStore(), attachments, retirements);
        var change = EventAttachmentReplicaDocuments.ToChange(Request(), Morning);

        await merge.ApplyAsync([TaskChanges.Record(change, Phone, 100)], Cancellation);
        Assert.Empty(retirements.Waiting());
        Assert.Equal(0, await merge.RetryDeferredRequestsAsync(Cancellation));
        Assert.Empty(retirements.Waiting());

        attachments.Answer = EventAttachmentOutcome.Attached;
        Assert.Equal(1, await merge.RetryDeferredRequestsAsync(Cancellation));

        Assert.Equal(3, attachments.Offered.Count);
        Assert.Equal(change.Id, Assert.Single(retirements.Waiting()).Id);
        Assert.Equal(0, await merge.RetryDeferredRequestsAsync(Cancellation));
    }

    [Fact]
    public async Task A_request_met_while_the_events_area_is_off_is_applied_once_the_area_is_on_again()
    {
        var retirements = new ImpedimentEditRetirements();
        var attachments = new RecordingEventAttachments();
        var features = new KeyedFeatureSettings(syncOn: true, eventsOn: false);
        var merge = Merge(new InMemoryTaskStore(), attachments, retirements, features: features);
        var change = EventAttachmentReplicaDocuments.ToChange(Request(), Morning);

        await merge.ApplyAsync([TaskChanges.Record(change, Phone, 100)], Cancellation);
        Assert.Equal(0, await merge.RetryDeferredRequestsAsync(Cancellation));
        Assert.Empty(attachments.Offered);
        Assert.Empty(retirements.Waiting());

        features.SetEnabled(EventsFeatures.Events, true);

        Assert.Equal(1, await merge.RetryDeferredRequestsAsync(Cancellation));
        Assert.Single(attachments.Offered);
        Assert.Equal(change.Id, Assert.Single(retirements.Waiting()).Id);
    }

    [Fact]
    public async Task A_request_deferred_for_an_unknown_event_stays_deferred_while_the_events_area_is_off()
    {
        var retirements = new ImpedimentEditRetirements();
        var attachments = new RecordingEventAttachments { Answer = EventAttachmentOutcome.UnknownEvent };
        var features = new KeyedFeatureSettings(syncOn: true, eventsOn: true);
        var merge = Merge(new InMemoryTaskStore(), attachments, retirements, features: features);
        var change = EventAttachmentReplicaDocuments.ToChange(Request(), Morning);

        await merge.ApplyAsync([TaskChanges.Record(change, Phone, 100)], Cancellation);
        features.SetEnabled(EventsFeatures.Events, false);
        Assert.Equal(0, await merge.RetryDeferredRequestsAsync(Cancellation));

        features.SetEnabled(EventsFeatures.Events, true);
        attachments.Answer = EventAttachmentOutcome.Attached;

        Assert.Equal(1, await merge.RetryDeferredRequestsAsync(Cancellation));
        Assert.Equal(2, attachments.Offered.Count);
        Assert.Equal(change.Id, Assert.Single(retirements.Waiting()).Id);
    }

    [Fact]
    public async Task A_document_whose_text_is_no_request_is_counted_unreadable_and_left_on_the_replica()
    {
        var retirements = new ImpedimentEditRetirements();
        var attachments = new RecordingEventAttachments();
        var change = EventAttachmentReplicaDocuments.ToChange(Request(), Morning);
        var garbled = change with { Task = change.Task with { ContentMd = "not json" } };

        var merged = await Merge(new InMemoryTaskStore(), attachments, retirements)
            .ApplyAsync([TaskChanges.Record(garbled, Phone, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 1), merged);
        Assert.Empty(attachments.Offered);
        Assert.Empty(retirements.Waiting());
    }

    [Fact]
    public void A_request_travels_under_its_own_id_stamped_when_it_was_asked_and_reads_back_whole()
    {
        var request = Request();

        var change = EventAttachmentReplicaDocuments.ToChange(request, Morning);

        Assert.Equal(request.RequestId, change.Id);
        Assert.Equal("event-attachment-request", change.Task.Type);
        Assert.Equal(Morning, change.UpdatedAt);
        Assert.Null(change.DeletedAt);
        Assert.Equal(request, EventAttachmentReplicaDocuments.ToRequest(change));

        using var text = JsonDocument.Parse(change.Task.ContentMd!);
        Assert.Equal(request.RequestId, text.RootElement.GetProperty("requestId").GetGuid());
        Assert.Equal(EventId, text.RootElement.GetProperty("eventId").GetGuid());
        Assert.Equal(NoteId, text.RootElement.GetProperty("noteId").GetGuid());
        Assert.Equal("Keynote: Aspire in production", text.RootElement.GetProperty("noteTitle").GetString());

        // A desktop clock behind the phone's still retires it as the later write.
        Assert.True(EventAttachmentReplicaDocuments.Retire(change, Morning.AddMinutes(-3)).UpdatedAt > change.UpdatedAt);
    }

    [Fact]
    public void A_document_naming_another_request_than_its_own_id_does_not_read()
    {
        var change = EventAttachmentReplicaDocuments.ToChange(Request(), Morning);

        Assert.Null(EventAttachmentReplicaDocuments.ToRequest(change with { Id = Guid.NewGuid() }));
    }

    [Fact]
    public void A_document_missing_the_event_or_the_note_reads_with_an_empty_id_for_events_to_refuse()
    {
        var change = EventAttachmentReplicaDocuments.ToChange(Request(), Morning);
        var text = $$"""{"requestId":"{{change.Id:D}}","noteId":"{{NoteId:D}}"}""";

        var read = EventAttachmentReplicaDocuments.ToRequest(change with { Task = change.Task with { ContentMd = text } });

        Assert.NotNull(read);
        Assert.Equal(Guid.Empty, read.EventId);
        Assert.Null(read.NoteTitle);
    }
}
