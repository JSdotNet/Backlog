using System.Net;
using System.Text.Json;

using Backlog.Infrastructure.Sync.DevbookSubjects;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// One devbook subject exchange: where the push watermark lands, when the
/// cursor is written down, what the pull does with what it gets — the questions
/// <see cref="AnnotationSyncSessionTests"/> asks of the loop this one copies —
/// and the one it does not: a service older than this client has no subject
/// route, and that is "not yet", not a failure.
/// </summary>
public sealed class DevbookSubjectSyncSessionTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid ThisOwner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ThisDevice = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AnotherOwner = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly DeviceCredential Paired = new(ThisOwner, ThisDevice, "Workshop PC", "a-registration-credential");

    private static readonly DevbookSubject Proposal = DevbookSubject.Proposal("add-subject-store");

    private static DevbookSubjectSyncState Mine(DateTimeOffset watermark, string? cursor) =>
        new(watermark, cursor, ThisOwner, ThisDevice);

    [Fact]
    public async Task Only_what_changed_since_the_watermark_is_sent_and_the_watermark_lands_on_it()
    {
        var store = new InMemoryDevbookSubjectStateStore();
        store.Seed(Subjects.Local("identified", Noon));
        store.Seed(Subjects.Local("draft", Noon.AddHours(1), Proposal));
        var state = new InMemoryDevbookSubjectSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(store, state, (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}"""));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Pushed);

        var request = Assert.Single(fixture.Handler.Requests);
        Assert.Equal(SyncRoutes.Absolute(SyncRoutes.DevbookSubjects), request.Path);

        var change = Assert.Single(Pushed(fixture.Bodies[0]));
        Assert.Equal("proposal", change.Subject.Kind);
        Assert.Equal("add-subject-store", change.Subject.Key);
        Assert.Equal("draft", change.Subject.State);

        Assert.Equal(Noon.AddHours(1), state.Current.PushWatermark);
    }

    [Fact]
    public async Task A_batch_the_replica_took_in_part_reports_the_rest_as_refused()
    {
        var store = new InMemoryDevbookSubjectStateStore();
        store.Seed(Subjects.Local("planned", Noon.AddHours(1)));
        store.Seed(Subjects.Local("draft", Noon.AddHours(2), Proposal));
        var state = new InMemoryDevbookSubjectSyncStateStore();

        using var fixture = Fixture.Create(store, state, (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}"""));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Pushed);
        Assert.Equal(1, result.Value.Refused);
        Assert.Equal(Noon.AddHours(2), state.Current.PushWatermark);
    }

    /// <summary>A state the service would refuse — here one filed under no
    /// repository — is left out of its batch and named, so it cannot take the
    /// rest down with it on every cycle; the watermark moves past it.</summary>
    [Fact]
    public async Task A_state_the_service_would_refuse_is_left_out_and_named_without_holding_back_the_rest()
    {
        var store = new InMemoryDevbookSubjectStateStore();
        var valid = store.Seed(Subjects.Local("planned", Noon.AddHours(1)));
        var unscoped = store.Seed(Subjects.Local("draft", Noon.AddHours(2), Proposal, repository: string.Empty));
        var state = new InMemoryDevbookSubjectSyncStateStore(Mine(Noon, null));
        var log = new CapturingLogger<DevbookSubjectSyncSession>();

        using var fixture = Fixture.Create(store, state, AsTheServiceAnswers, log);

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(1, result.Value.Pushed);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Equal(valid.Id, Assert.Single(Pushed(Assert.Single(fixture.Bodies))).Id);
        Assert.Equal(Noon.AddHours(2), state.Current.PushWatermark);

        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Contains(unscoped.Id.ToString(), line.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alias of the repository", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_batch_of_nothing_the_service_would_store_is_not_sent()
    {
        var store = new InMemoryDevbookSubjectStateStore();
        store.Seed(Subjects.Local("planned", Noon.AddHours(1), repository: "  "));
        var state = new InMemoryDevbookSubjectSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(store, state, AsTheServiceAnswers);

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(fixture.Handler.Requests);
        Assert.Equal(Noon.AddHours(1), state.Current.PushWatermark);
    }

    [Fact]
    public async Task A_push_that_fails_leaves_the_watermark_where_it_was()
    {
        var store = new InMemoryDevbookSubjectStateStore();
        store.Seed(Subjects.Local("planned", Noon.AddHours(1)));
        var state = new InMemoryDevbookSubjectSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(store, state,
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
        var state = new InMemoryDevbookSubjectSyncStateStore(Mine(DateTimeOffset.MinValue, null));

        using var fixture = Fixture.Create(new InMemoryDevbookSubjectStateStore(), state, (_, index) => index switch
        {
            0 => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"subjects":[],"since":"cursor-1","hasMore":true}"""),
            _ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"subjects":[],"since":"cursor-2","hasMore":false}"""),
        });

        await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["cursor-1", "cursor-2"], state.Saved.Select(saved => saved.PullCursor).ToArray());
        Assert.Contains("since=cursor-1", fixture.Queries[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_expired_cursor_is_dropped_and_the_pull_starts_over_once()
    {
        var state = new InMemoryDevbookSubjectSyncStateStore(Mine(Noon, "a-cursor-the-store-forgot"));

        using var fixture = Fixture.Create(new InMemoryDevbookSubjectStateStore(), state, (_, index) => index switch
        {
            0 => StubHttpMessageHandler.Problem(HttpStatusCode.BadRequest, SyncErrorCodes.SyncCursorExpired, "Too old."),
            _ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"subjects":[],"since":"cursor-1","hasMore":false}"""),
        });

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("since=", fixture.Queries[1], StringComparison.Ordinal);
        Assert.Equal("cursor-1", state.Current.PullCursor);
    }

    [Fact]
    public async Task A_pulled_state_is_applied_and_counted_apart_from_what_arrived()
    {
        var store = new InMemoryDevbookSubjectStateStore();
        var state = new InMemoryDevbookSubjectSyncStateStore(Mine(DateTimeOffset.MinValue, null));
        var change = Subjects.Change("planned", Noon);

        var body = JsonSerializer.Serialize(
            new { subjects = new[] { new { change, deviceId = Guid.NewGuid(), serverTimestamp = 100L } }, since = "cursor-1", hasMore = false },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var fixture = Fixture.Create(store, state, (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Pulled);
        Assert.Equal(1, result.Value.Applied);
        Assert.Equal("planned", store.Get(Subjects.Repository, Subjects.Debt)?.State);
    }

    [Fact]
    public async Task Syncing_pushes_before_it_pulls_and_a_failed_push_stops_the_exchange()
    {
        var store = new InMemoryDevbookSubjectStateStore();
        store.Seed(Subjects.Local("planned", Noon.AddHours(1)));
        var state = new InMemoryDevbookSubjectSyncStateStore(Mine(DateTimeOffset.MinValue, null));

        using var ordered = Fixture.Create(store, state, AsTheServiceAnswers);

        var result = await ordered.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([HttpMethod.Post, HttpMethod.Get], ordered.Handler.Requests.Select(request => request.Method).ToArray());
        Assert.Equal(1, result.Value.Pushed);

        store.Seed(Subjects.Local("draft", Noon.AddHours(2), Proposal));

        using var failing = Fixture.Create(store, state,
            (_, _) => StubHttpMessageHandler.Problem(HttpStatusCode.ServiceUnavailable, SyncErrorCodes.ReplicaUnavailable, "Not yet."));

        var stopped = await failing.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(stopped.IsFailure);
        Assert.Single(failing.Handler.Requests);
    }

    /// <summary>The deployed service is updated by hand and lags the desktop. A
    /// service without the route answers 404 to the push; the exchange ends
    /// there, quietly, and the watermark stays where it was so everything is sent
    /// once the service has the route.</summary>
    [Fact]
    public async Task A_service_without_the_route_is_not_yet_and_not_a_failure()
    {
        var store = new InMemoryDevbookSubjectStateStore();
        store.Seed(Subjects.Local("planned", Noon.AddHours(1)));
        var state = new InMemoryDevbookSubjectSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(store, state, (_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await fixture.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.True(result.Value.NotYetServed);
        Assert.Equal(0, result.Value.Pushed);
        Assert.Single(fixture.Handler.Requests);
        Assert.Equal(Noon, state.Current.PushWatermark);
    }

    /// <summary>With nothing to push the pull is the first request, and the same
    /// 404 there reads the same way.</summary>
    [Fact]
    public async Task A_service_without_the_route_is_not_yet_when_only_the_pull_asks()
    {
        var state = new InMemoryDevbookSubjectSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(new InMemoryDevbookSubjectStateStore(), state, (_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await fixture.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.True(result.Value.NotYetServed);
        Assert.Equal([HttpMethod.Get], fixture.Handler.Requests.Select(request => request.Method).ToArray());
        Assert.Null(state.Current.PullCursor);
    }

    /// <summary>Only the bare 404 an unmapped route produces means "no route
    /// yet". A 404 carrying a code is the service saying something specific —
    /// a fault, or a path in front of it that is wrong — and fails as it does on
    /// every other feed, so it shows instead of the feed quietly never
    /// syncing.</summary>
    [Fact]
    public async Task A_coded_404_on_the_push_is_a_failure()
    {
        var store = new InMemoryDevbookSubjectStateStore();
        store.Seed(Subjects.Local("planned", Noon.AddHours(1)));
        var state = new InMemoryDevbookSubjectSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(store, state,
            (_, _) => StubHttpMessageHandler.Problem(HttpStatusCode.NotFound, "sync.owner_not_found", "No such owner."));

        var result = await fixture.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("sync.owner_not_found", result.Error.Code);
        Assert.Equal(Noon, state.Current.PushWatermark);
    }

    [Fact]
    public async Task A_coded_404_on_the_pull_is_a_failure()
    {
        var state = new InMemoryDevbookSubjectSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(new InMemoryDevbookSubjectStateStore(), state,
            (_, _) => StubHttpMessageHandler.Problem(HttpStatusCode.NotFound, "sync.owner_not_found", "No such owner."));

        var result = await fixture.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("sync.owner_not_found", result.Error.Code);
    }

    [Fact]
    public async Task Progress_recorded_for_another_owner_is_started_over()
    {
        var state = new InMemoryDevbookSubjectSyncStateStore(
            new DevbookSubjectSyncState(Noon, "the-old-owners-cursor", AnotherOwner, ThisDevice));

        using var fixture = Fixture.Create(new InMemoryDevbookSubjectStateStore(), state,
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"subjects":[],"since":"cursor-fresh","hasMore":false}"""));

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("the-old-owners-cursor", fixture.Queries[0], StringComparison.Ordinal);
        Assert.Equal(ThisOwner, state.Current.OwnerId);
        Assert.Equal(DateTimeOffset.MinValue, state.Saved[0].PushWatermark);
    }

    private static IReadOnlyList<DevbookSubjectChange> Pushed(string body) =>
        JsonSerializer.Deserialize<PushDevbookSubjectsRequest>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Subjects;

    /// <summary>The service as it answers: a push refused whole when any change
    /// in it breaks the shared rule, otherwise taken in full; a pull reads an
    /// empty feed.</summary>
    private static HttpResponseMessage AsTheServiceAnswers(HttpRequestMessage request, int index)
    {
        if (request.Method != HttpMethod.Post)
        {
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"subjects":[],"since":"cursor-1","hasMore":false}""");
        }

        var changes = Pushed(request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult());

        if (changes.Any(change => DevbookSubjectRecordLimits.WhyRefused(change) is not null))
        {
            return StubHttpMessageHandler.Problem(HttpStatusCode.BadRequest, SyncErrorCodes.DevbookSubjectInvalid, "Refused.");
        }

        return StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"accepted":{{changes.Count}}}""");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;

        private Fixture(HttpClient http, StubHttpMessageHandler handler, DevbookSubjectSyncSession session, List<string> bodies, List<string> queries)
        {
            _http = http;
            Handler = handler;
            Session = session;
            Bodies = bodies;
            Queries = queries;
        }

        public StubHttpMessageHandler Handler { get; }

        public DevbookSubjectSyncSession Session { get; }

        public List<string> Bodies { get; }

        public List<string> Queries { get; }

        public static Fixture Create(
            InMemoryDevbookSubjectStateStore store,
            IDevbookSubjectSyncStateStore state,
            Func<HttpRequestMessage, int, HttpResponseMessage> respond,
            ILogger<DevbookSubjectSyncSession>? log = null)
        {
            var bodies = new List<string>();
            var queries = new List<string>();

            var handler = new StubHttpMessageHandler((request, index) =>
            {
                queries.Add(request.RequestUri?.Query ?? string.Empty);

                if (request.Content is not null)
                {
                    bodies.Add(request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult());
                }

                return respond(request, index);
            });

            var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };

            var session = new DevbookSubjectSyncSession(
                new DevbookSubjectSyncClient(http),
                new DevbookSubjectReplicaMerge(store),
                store,
                state,
                new InMemoryDeviceCredentialStore(Paired),
                new FakeTimeProvider(Noon.AddHours(6)),
                log);

            return new Fixture(http, handler, session, bodies, queries);
        }

        public void Dispose() => _http.Dispose();
    }
}
