using System.Net;
using System.Text.Json;

using Backlog.Infrastructure.Sync.Annotations;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// One annotation exchange, and the bookkeeping that decides whether a device
/// ever catches up: where the push watermark lands, when the cursor is written
/// down, and what the pull does with what it gets. The same questions
/// <see cref="TaskSyncSessionTests"/> asks, because the loop is the same loop.
/// </summary>
public sealed class AnnotationSyncSessionTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Only_what_changed_since_the_watermark_is_sent_and_the_watermark_lands_on_it()
    {
        var store = new InMemoryDevbookAnnotationStore();
        store.Seed(Annotations.Local("Already sent", Noon));
        store.Seed(Annotations.Local("Edited since", Noon.AddHours(1)));
        var state = new InMemoryAnnotationSyncStateStore(new AnnotationSyncState(Noon, null));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)),
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}"""));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Pushed);

        var request = Assert.Single(fixture.Handler.Requests);
        Assert.Equal(SyncRoutes.Absolute(SyncRoutes.Annotations), request.Path);

        var change = Assert.Single(Pushed(fixture.Bodies[0]));
        Assert.Equal("Edited since", change.Annotation.Body);
        Assert.Equal(Annotations.Repository, change.Annotation.RepositoryAlias);
        Assert.Equal(Annotations.Chapter, change.Annotation.ChapterPath);

        // Onto the stamp that was accepted, never onto the clock.
        Assert.Equal(Noon.AddHours(1), state.Current.PushWatermark);
    }

    [Fact]
    public async Task A_remark_deleted_here_is_pushed_as_a_tombstone()
    {
        var store = new InMemoryDevbookAnnotationStore();
        store.Seed(Annotations.Local("Deleted here", Noon.AddHours(1), deletedAt: Noon.AddHours(1)));
        var state = new InMemoryAnnotationSyncStateStore();

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)),
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}"""));

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        var change = Assert.Single(Pushed(fixture.Bodies[0]));
        Assert.Equal(Noon.AddHours(1), change.DeletedAt);
    }

    [Fact]
    public async Task A_draft_never_leaves_the_machine()
    {
        var store = new InMemoryDevbookAnnotationStore();
        store.Seed(Annotations.Local(string.Empty, Noon.AddHours(1)));
        var state = new InMemoryAnnotationSyncStateStore();

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)),
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":0}"""));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task A_push_that_fails_leaves_the_watermark_where_it_was()
    {
        var store = new InMemoryDevbookAnnotationStore();
        store.Seed(Annotations.Local("Unsent", Noon.AddHours(1)));
        var state = new InMemoryAnnotationSyncStateStore(new AnnotationSyncState(Noon, null));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon.AddHours(6)),
            (_, _) => StubHttpMessageHandler.Problem(HttpStatusCode.ServiceUnavailable, SyncErrorCodes.ReplicaUnavailable, "Not yet."));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SyncErrorCodes.ReplicaUnavailable, result.Error.Code);
        Assert.Equal(Noon, state.Current.PushWatermark);
        Assert.Empty(state.Saved);
    }

    [Fact]
    public async Task Every_page_saves_its_cursor_and_the_loop_ends_on_the_services_word()
    {
        var store = new InMemoryDevbookAnnotationStore();
        var state = new InMemoryAnnotationSyncStateStore();

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon), (_, index) => index switch
        {
            0 => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"annotations":[],"since":"cursor-1","hasMore":true}"""),
            1 => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"annotations":[],"since":"cursor-2","hasMore":true}"""),
            _ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"annotations":[],"since":"cursor-3","hasMore":false}"""),
        });

        await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["cursor-1", "cursor-2", "cursor-3"], state.Saved.Select(saved => saved.PullCursor).ToArray());
        Assert.Contains("since=cursor-2", fixture.Queries[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_expired_cursor_is_dropped_and_the_pull_starts_over_once()
    {
        var store = new InMemoryDevbookAnnotationStore();
        var state = new InMemoryAnnotationSyncStateStore(new AnnotationSyncState(Noon, "a-cursor-the-store-forgot"));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon), (_, index) => index switch
        {
            0 => StubHttpMessageHandler.Problem(HttpStatusCode.BadRequest, SyncErrorCodes.SyncCursorExpired, "Too old."),
            _ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"annotations":[],"since":"cursor-1","hasMore":false}"""),
        });

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("since=", fixture.Queries[1], StringComparison.Ordinal);
        Assert.Null(state.Saved[0].PullCursor);
        Assert.Equal("cursor-1", state.Current.PullCursor);
    }

    [Fact]
    public async Task A_cursor_belonging_to_somebody_else_is_not_swallowed()
    {
        var store = new InMemoryDevbookAnnotationStore();
        var state = new InMemoryAnnotationSyncStateStore(new AnnotationSyncState(Noon, "somebody-elses-cursor"));

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon),
            (_, _) => StubHttpMessageHandler.Problem(HttpStatusCode.Forbidden, SyncErrorCodes.SyncCursorNotYours, "Not yours."));

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SyncErrorCodes.SyncCursorNotYours, result.Error.Code);
        Assert.Equal("somebody-elses-cursor", state.Current.PullCursor);
    }

    [Fact]
    public async Task A_pulled_remark_is_applied_and_counted_apart_from_what_arrived()
    {
        var store = new InMemoryDevbookAnnotationStore();
        var state = new InMemoryAnnotationSyncStateStore();
        var change = Annotations.Change(Guid.NewGuid(), "From the other machine", Noon);

        var body = JsonSerializer.Serialize(new
        {
            annotations = new[] { new { change, deviceId = Guid.NewGuid(), serverTimestamp = 100L } },
            since = "cursor-1",
            hasMore = false
        });

        using var fixture = Fixture.Create(store, state, new FakeTimeProvider(Noon),
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Pulled);
        Assert.Equal(1, result.Value.Applied);

        var listed = Assert.Single(store.List(Annotations.Repository, Annotations.Chapter));
        Assert.Equal("From the other machine", listed.Body);
        Assert.Equal("DEV-LAPTOP", listed.Author);
    }

    [Fact]
    public async Task Syncing_pushes_before_it_pulls_and_a_failed_push_stops_the_exchange()
    {
        var store = new InMemoryDevbookAnnotationStore();
        store.Seed(Annotations.Local("Unsent", Noon.AddHours(1)));
        var state = new InMemoryAnnotationSyncStateStore();

        using var ordered = Fixture.Create(store, state, new FakeTimeProvider(Noon), (request, _) =>
            request.Method == HttpMethod.Post
                ? StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}""")
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"annotations":[],"since":"cursor-1","hasMore":false}"""));

        var result = await ordered.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([HttpMethod.Post, HttpMethod.Get], ordered.Handler.Requests.Select(request => request.Method).ToArray());
        Assert.Equal(1, result.Value.Pushed);

        store.Seed(Annotations.Local("Another", Noon.AddHours(2)));

        using var failing = Fixture.Create(store, state, new FakeTimeProvider(Noon),
            (_, _) => StubHttpMessageHandler.Problem(HttpStatusCode.ServiceUnavailable, SyncErrorCodes.ReplicaUnavailable, "Not yet."));

        var stopped = await failing.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(stopped.IsFailure);
        Assert.Single(failing.Handler.Requests);
    }

    private static IReadOnlyList<AnnotationChange> Pushed(string body) =>
        JsonSerializer.Deserialize<PushAnnotationsRequest>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Annotations;

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;

        private Fixture(HttpClient http, StubHttpMessageHandler handler, AnnotationSyncSession session, List<string> bodies, List<string> queries)
        {
            _http = http;
            Handler = handler;
            Session = session;
            Bodies = bodies;
            Queries = queries;
        }

        public StubHttpMessageHandler Handler { get; }

        public AnnotationSyncSession Session { get; }

        public List<string> Bodies { get; }

        public List<string> Queries { get; }

        public static Fixture Create(
            InMemoryDevbookAnnotationStore store,
            IAnnotationSyncStateStore state,
            TimeProvider time,
            Func<HttpRequestMessage, int, HttpResponseMessage> respond)
        {
            var bodies = new List<string>();
            var queries = new List<string>();

            var handler = new StubHttpMessageHandler((request, index) =>
            {
                queries.Add(request.RequestUri?.Query ?? string.Empty);

                bodies.Add(request.Content is null
                    ? string.Empty
                    : request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult());

                return respond(request, index);
            });

            var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };

            var session = new AnnotationSyncSession(
                new AnnotationSyncClient(http),
                new AnnotationReplicaMerge(store),
                store,
                state,
                time);

            return new Fixture(http, handler, session, bodies, queries);
        }

        public void Dispose() => _http.Dispose();
    }
}
