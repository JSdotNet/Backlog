using System.Net;
using System.Text.Json;

using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// A note on the task feed (.devbook/arc42/06-runtime-view.md#mobile-note-sync):
/// a pulled <c>note</c> document goes to the Inbox's note port and never to the task
/// store; a push sends every note the Inbox changed since the note mark, as note
/// documents; and a note's acknowledgement goes out as a note tombstone, not a
/// capture one.
/// </summary>
public sealed class NoteSyncTests
{
    private static readonly Guid Phone = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Noon = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_pulled_note_goes_to_the_note_port_whole_and_not_to_the_task_store()
    {
        var tasks = new InMemoryTaskStore();
        var notes = new RecordingNoteReplication();
        var attachment = new AttachmentMetadata(Guid.CreateVersion7(), "slide.jpg", "image/jpeg", 12, new string('a', 64));
        var change = NoteReplicaDocuments.ToChange(new InboxNoteDto(
            Guid.CreateVersion7(), "Standup", "- shipped", "mobile", Noon, Noon.AddMinutes(3), Tags: ["team"])) is var built
            ? built with { Task = built.Task with { Attachments = [attachment] } }
            : null!;

        var merged = await new TaskReplicaMerge(tasks, notes: notes)
            .ApplyAsync([new TaskChangeRecord(change, Phone, 10)], Cancellation);

        Assert.Equal(1, merged.Applied);
        Assert.Empty(tasks.Writes);
        var received = Assert.Single(notes.Received);
        Assert.Equal(change.Id, received.Id);
        Assert.Equal("Standup", received.Title);
        Assert.Equal("- shipped", received.BodyMd);
        Assert.Equal("mobile", received.Channel);
        Assert.Equal(Noon, received.CapturedAt);
        Assert.Equal(Noon.AddMinutes(3), received.UpdatedAt);
        Assert.Equal(["team"], received.Tags);
        Assert.Equal(attachment.Id, Assert.Single(received.Attachments!).Id);
    }

    [Fact]
    public async Task A_note_on_a_head_without_the_port_is_held_on_the_replica()
    {
        var tasks = new InMemoryTaskStore();
        var change = NoteReplicaDocuments.ToChange(new InboxNoteDto(Guid.CreateVersion7(), "Standup", null, "mobile", Noon, Noon));

        var merged = await new TaskReplicaMerge(tasks).ApplyAsync([new TaskChangeRecord(change, Phone, 10)], Cancellation);

        Assert.Equal(0, merged.Applied);
        Assert.Equal(0, merged.Skipped);
        Assert.Empty(tasks.Writes);
    }

    [Theory]
    [InlineData(InboxIntakeOutcome.Received, 1, null)]
    [InlineData(InboxIntakeOutcome.Updated, 1, "edited")]
    [InlineData(InboxIntakeOutcome.Withdrawn, 1, "archived")]
    [InlineData(InboxIntakeOutcome.AlreadyKnown, 0, null)]
    public async Task What_the_port_answered_is_what_is_counted_and_logged(InboxIntakeOutcome answer, int applied, string? note)
    {
        var activity = new SyncActivityLog();
        var notes = new RecordingNoteReplication { Answer = answer };
        var change = NoteReplicaDocuments.ToChange(new InboxNoteDto(Guid.CreateVersion7(), "Standup", null, "mobile", Noon, Noon));

        var merged = await new TaskReplicaMerge(new InMemoryTaskStore(), activity: activity, notes: notes)
            .ApplyAsync([new TaskChangeRecord(change, Phone, 10)], Cancellation);

        Assert.Equal(applied, merged.Applied);

        if (applied == 0)
        {
            Assert.Empty(activity.Snapshot());
            return;
        }

        var entry = Assert.Single(activity.Snapshot());
        Assert.Equal(SyncItemKind.Note, entry.Kind);
        Assert.Equal(SyncDirection.Received, entry.Direction);
        Assert.Equal(note, entry.Note);
    }

    [Fact]
    public async Task A_push_sends_the_pending_notes_as_note_documents_and_marks_them_pushed()
    {
        var notes = new RecordingNoteReplication();
        var first = new InboxNoteDto(Guid.CreateVersion7(), "Standup", "edited here", "mobile", Noon, Noon.AddMinutes(5));
        var second = new InboxNoteDto(Guid.CreateVersion7(), "Retro", null, "mobile", Noon, Noon.AddMinutes(9));
        notes.Pending.AddRange([first, second]);

        using var fixture = Fixture.Create(new InMemoryTaskSyncStateStore(), notes, outbox: null);

        var pushed = await fixture.Session.PushAsync(Cancellation);

        Assert.True(pushed.IsSuccess);
        Assert.Equal(2, pushed.Value.Pushed);
        var sent = Pushed(Assert.Single(fixture.Bodies));
        Assert.Equal([first.Id, second.Id], sent.Select(change => change.Id));
        Assert.All(sent, change => Assert.Equal("note", change.Task.Type));
        Assert.Equal("edited here", sent[0].Task.ContentMd);
        Assert.Equal(Noon.AddMinutes(5), sent[0].UpdatedAt);
        Assert.Equal("mobile", sent[0].Task.SourceInboxId);
        Assert.Equal([first, second], notes.MarkedPushed);

        // Nothing pending since: the next push sends nothing.
        var again = await fixture.Session.PushAsync(Cancellation);

        Assert.Equal(0, again.Value.Pushed);
        Assert.Single(fixture.Bodies);
    }

    [Fact]
    public async Task A_failed_push_leaves_the_notes_pending()
    {
        var notes = new RecordingNoteReplication();
        notes.Pending.Add(new InboxNoteDto(Guid.CreateVersion7(), "Standup", null, "mobile", Noon, Noon));

        using var fixture = Fixture.Create(new InMemoryTaskSyncStateStore(), notes, outbox: null, status: HttpStatusCode.ServiceUnavailable);

        var pushed = await fixture.Session.PushAsync(Cancellation);

        Assert.True(pushed.IsFailure);
        Assert.Empty(notes.MarkedPushed);
        Assert.Single(notes.Pending);
    }

    [Fact]
    public async Task A_notes_acknowledgement_goes_out_as_a_note_tombstone()
    {
        var outbox = new RecordingInboxOutbox();
        var id = Guid.CreateVersion7();
        outbox.Pending.Add(new InboxCaptureAckDto(id, "Standup", Noon, Noon.AddHours(1), Kind: "note"));
        outbox.Pending.Add(new InboxCaptureAckDto(Guid.CreateVersion7(), "Call the dentist", Noon, Noon.AddHours(1)));

        using var fixture = Fixture.Create(new InMemoryTaskSyncStateStore(), notes: null, outbox);

        await fixture.Session.PushAsync(Cancellation);

        var sent = Pushed(Assert.Single(fixture.Bodies));
        var note = Assert.Single(sent, change => change.Id == id);
        Assert.Equal("note", note.Task.Type);
        Assert.Equal(Noon.AddHours(1), note.DeletedAt);
        Assert.Equal("capture", Assert.Single(sent, change => change.Id != id).Task.Type);
    }

    private static IReadOnlyList<TaskChange> Pushed(string body) =>
        JsonSerializer.Deserialize<PushTasksRequest>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Tasks;

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;

        private Fixture(HttpClient http, TaskSyncSession session, List<string> bodies)
        {
            _http = http;
            Session = session;
            Bodies = bodies;
        }

        public TaskSyncSession Session { get; }

        public List<string> Bodies { get; }

        public static Fixture Create(
            ITaskSyncStateStore state,
            IInboxNoteReplication? notes,
            IInboxCaptureOutbox? outbox,
            HttpStatusCode status = HttpStatusCode.OK)
        {
            var bodies = new List<string>();
            var handler = new StubHttpMessageHandler((request, _) =>
            {
                var body = request.Content is null
                    ? string.Empty
                    : request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
                bodies.Add(body);

                if (status != HttpStatusCode.OK)
                {
                    return StubHttpMessageHandler.Json(status, """{"title":"Unavailable","code":"sync.replica_unavailable"}""");
                }

                var count = JsonSerializer.Deserialize<PushTasksRequest>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Tasks.Count;
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"accepted":{{count}}}""");
            });

            var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };
            var tasks = new InMemoryTaskStore();

            var session = new TaskSyncSession(
                new TaskSyncClient(http),
                new TaskReplicaMerge(tasks, notes: notes),
                tasks,
                state,
                new InMemoryDeviceCredentialStore(new DeviceCredential(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "Workshop PC",
                    "a-registration-credential")),
                new FakeTimeProvider(Noon.AddHours(6)),
                outbox,
                notes: notes);

            return new Fixture(http, session, bodies);
        }

        public void Dispose() => _http.Dispose();
    }
}
