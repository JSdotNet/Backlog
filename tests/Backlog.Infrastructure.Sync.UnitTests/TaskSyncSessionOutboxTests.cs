using System.Net;
using System.Text.Json;

using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The Inbox's acknowledgements leave the machine on the push, as tombstones
/// of the capture documents, and are only marked sent once the replica has
/// accepted them. A failed push has to leave them exactly where they were —
/// an acknowledgement that never travels is a capture the phone offers forever.
/// </summary>
public sealed class TaskSyncSessionOutboxTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Pending_acknowledgements_are_pushed_as_tombstones_and_marked_sent()
    {
        var outbox = new RecordingInboxOutbox();
        var capture = Guid.CreateVersion7();
        outbox.Pending.Add(new InboxCaptureAckDto(capture, "Call the dentist", Noon, Noon.AddHours(1)));

        using var fixture = Fixture.Create(new InMemoryTaskStore(), new InMemoryTaskSyncStateStore(), outbox,
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}"""));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Pushed);

        var change = Assert.Single(Pushed(fixture.Bodies.Single()));
        Assert.Equal(capture, change.Id);
        Assert.Equal(Noon.AddHours(1), change.DeletedAt);
        Assert.Equal(Noon.AddHours(1), change.UpdatedAt);
        Assert.Equal("capture", change.Task.Type);
        Assert.Equal("Call the dentist", change.Task.Title);
        Assert.Equal(Noon, change.Task.CreatedAt);

        Assert.Equal([capture], outbox.MarkedSent);
        Assert.Empty(outbox.Pending);
    }

    [Fact]
    public async Task Acknowledgements_go_after_the_tasks_and_are_counted_with_them()
    {
        var store = new InMemoryTaskStore();
        store.Seed(TaskChanges.Task("Edited here", Noon));
        var outbox = new RecordingInboxOutbox();
        outbox.Pending.Add(new InboxCaptureAckDto(Guid.CreateVersion7(), "Archived here", Noon, Noon.AddHours(1)));

        using var fixture = Fixture.Create(store, new InMemoryTaskSyncStateStore(), outbox,
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}"""));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Value.Pushed);
        Assert.Equal(2, fixture.Bodies.Count);
        Assert.Contains("Edited here", fixture.Bodies[0], StringComparison.Ordinal);
        Assert.Contains("Archived here", fixture.Bodies[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_push_leaves_the_acknowledgements_pending()
    {
        var outbox = new RecordingInboxOutbox();
        outbox.Pending.Add(new InboxCaptureAckDto(Guid.CreateVersion7(), "Call the dentist", Noon, Noon.AddHours(1)));

        using var fixture = Fixture.Create(new InMemoryTaskStore(), new InMemoryTaskSyncStateStore(), outbox,
            (_, _) => StubHttpMessageHandler.Problem(HttpStatusCode.ServiceUnavailable, "sync.replica_unavailable", "later"));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Empty(outbox.MarkedSent);
        Assert.Single(outbox.Pending);
    }

    /// <summary>The service refuses a push over five hundred changes, so a
    /// desktop that archived a long backlog of captures offline would push one
    /// request it can never get accepted. The acknowledgements go in the same
    /// batches the tasks do, and each batch is marked sent as it lands.</summary>
    [Fact]
    public async Task Many_acknowledgements_go_in_batches_and_every_batch_is_marked_sent()
    {
        var outbox = new RecordingInboxOutbox();
        for (var i = 0; i < 450; i++)
        {
            outbox.Pending.Add(new InboxCaptureAckDto(Guid.CreateVersion7(), $"Capture {i}", Noon, Noon.AddHours(1)));
        }

        using var fixture = Fixture.Create(new InMemoryTaskStore(), new InMemoryTaskSyncStateStore(), outbox,
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}"""));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, fixture.Bodies.Count);
        Assert.Equal([200, 200, 50], fixture.Bodies.Select(body => Pushed(body).Count));
        Assert.Equal(450, outbox.MarkedSent.Count);
        Assert.Empty(outbox.Pending);
    }

    [Fact]
    public async Task A_batch_that_fails_leaves_it_and_every_later_one_pending_but_not_the_ones_before()
    {
        var outbox = new RecordingInboxOutbox();
        for (var i = 0; i < 450; i++)
        {
            outbox.Pending.Add(new InboxCaptureAckDto(Guid.CreateVersion7(), $"Capture {i}", Noon, Noon.AddHours(1)));
        }

        var first = outbox.Pending.Take(200).Select(ack => ack.CaptureId).ToList();

        using var fixture = Fixture.Create(new InMemoryTaskStore(), new InMemoryTaskSyncStateStore(), outbox, (_, index) => index switch
        {
            0 => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":200}"""),
            _ => StubHttpMessageHandler.Problem(HttpStatusCode.ServiceUnavailable, "sync.replica_unavailable", "later"),
        });

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(2, fixture.Bodies.Count);
        Assert.Equal(first, outbox.MarkedSent);
        Assert.Equal(250, outbox.Pending.Count);
    }

    [Fact]
    public async Task Without_an_outbox_nothing_extra_is_pushed()
    {
        using var fixture = Fixture.Create(new InMemoryTaskStore(), new InMemoryTaskSyncStateStore(), outbox: null,
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":0}"""));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Pushed);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task An_empty_outbox_costs_no_request()
    {
        using var fixture = Fixture.Create(new InMemoryTaskStore(), new InMemoryTaskSyncStateStore(), new RecordingInboxOutbox(),
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":0}"""));

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.Empty(fixture.Handler.Requests);
    }

    private static IReadOnlyList<TaskChange> Pushed(string body) =>
        JsonSerializer.Deserialize<PushTasksRequest>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Tasks;

    /// <summary>The same shape as <c>TaskSyncSessionTests.Fixture</c>, with the
    /// outbox threaded through; kept separate so that fixture stays the picture
    /// of a session without an inbox.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;

        private Fixture(HttpClient http, StubHttpMessageHandler handler, TaskSyncSession session, List<string> bodies)
        {
            _http = http;
            Handler = handler;
            Session = session;
            Bodies = bodies;
        }

        public StubHttpMessageHandler Handler { get; }

        public TaskSyncSession Session { get; }

        public List<string> Bodies { get; }

        public static Fixture Create(
            InMemoryTaskStore tasks,
            ITaskSyncStateStore state,
            IInboxCaptureOutbox? outbox,
            Func<HttpRequestMessage, int, HttpResponseMessage> respond)
        {
            var bodies = new List<string>();

            var handler = new StubHttpMessageHandler((request, index) =>
            {
                bodies.Add(request.Content is null
                    ? string.Empty
                    : request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult());

                return respond(request, index);
            });

            var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };

            var session = new TaskSyncSession(
                new TaskSyncClient(http),
                new TaskReplicaMerge(tasks),
                tasks,
                state,
                new InMemoryDeviceCredentialStore(new DeviceCredential(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "Workshop PC",
                    "a-registration-credential")),
                new FakeTimeProvider(Noon.AddHours(6)),
                outbox);

            return new Fixture(http, handler, session, bodies);
        }

        public void Dispose() => _http.Dispose();
    }
}
