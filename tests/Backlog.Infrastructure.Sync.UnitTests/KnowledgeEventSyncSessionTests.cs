using System.Net;
using System.Text.Json;

using Backlog.Infrastructure.Sync.KnowledgeEvents;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// One knowledge event exchange (local ADR 0050 §2): what the push sends and where its
/// watermark lands, what the pull does with what it gets, and — as for devbook subjects — a
/// service older than this client, which has no route yet and is "not yet", not a failure.
/// </summary>
public sealed class KnowledgeEventSyncSessionTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid ThisOwner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ThisDevice = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AnotherOwner = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly DeviceCredential Paired = new(ThisOwner, ThisDevice, "Workshop PC", "a-registration-credential");

    private static KnowledgeEventSyncState Mine(DateTimeOffset watermark, string? cursor) =>
        new(watermark, cursor, ThisOwner, ThisDevice);

    [Fact]
    public async Task Events_and_series_changed_since_the_watermark_go_in_one_push_in_stamp_order()
    {
        var replication = new InMemoryKnowledgeEventReplication();
        replication.Seed(KnowledgeEventCopies.Event("Already sent", Noon));
        var meetup = replication.Seed(KnowledgeEventCopies.Event("dotNET Friday", Noon.AddHours(2)));
        var series = replication.Seed(KnowledgeEventCopies.Series("Techorama", Noon.AddHours(1)));
        var state = new InMemoryKnowledgeEventSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(replication, state, AsTheServiceAnswers);

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(2, result.Value.Pushed);

        var request = Assert.Single(fixture.Handler.Requests);
        Assert.Equal(SyncRoutes.Absolute(SyncRoutes.KnowledgeEvents), request.Path);

        var changes = Pushed(fixture.Bodies[0]);
        Assert.Equal(
            [(series.Id, KnowledgeEventKinds.EventSeries, Noon.AddHours(1)), (meetup.Id, KnowledgeEventKinds.KnowledgeEvent, Noon.AddHours(2))],
            changes.Select(change => (change.Id, change.Kind, change.UpdatedAt)));

        Assert.Equal(Noon.AddHours(2), state.Current.PushWatermark);
    }

    /// <summary>The document is the shape the phone reads: every field it shows, under the names
    /// it reads them by, and the series as its own document beside it.</summary>
    [Fact]
    public async Task The_pushed_documents_carry_what_the_phone_shows_under_their_wire_names()
    {
        var replication = new InMemoryKnowledgeEventReplication();
        var series = replication.Seed(KnowledgeEventCopies.Series("Techorama", Noon.AddHours(1)));
        var techorama = replication.Seed(KnowledgeEventCopies.Techorama(Noon.AddHours(2), series.Id));

        using var fixture = Fixture.Create(replication, new InMemoryKnowledgeEventSyncStateStore(Mine(Noon, null)), AsTheServiceAnswers);

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        var changes = Pushed(fixture.Bodies[0]);
        var seriesDocument = changes[0].Document;
        Assert.Equal("Techorama", seriesDocument.GetProperty("name").GetString());
        Assert.Equal("Techorama BV", seriesDocument.GetProperty("organiser").GetString());
        Assert.Equal("https://techorama.nl", seriesDocument.GetProperty("link").GetString());

        var document = changes[1].Document;
        Assert.Equal(techorama.Id, document.GetProperty("id").GetGuid());
        Assert.Equal("Techorama 2026", document.GetProperty("title").GetString());
        Assert.Equal("going", document.GetProperty("state").GetString());
        Assert.Equal("2026-10-26", document.GetProperty("start").GetProperty("date").GetString());
        Assert.Equal("08:30:00", document.GetProperty("start").GetProperty("time").GetString());
        Assert.Equal("01:00:00", document.GetProperty("start").GetProperty("utcOffset").GetString());
        Assert.Equal("2026-10-28", document.GetProperty("end").GetProperty("date").GetString());
        Assert.Equal("event-page", document.GetProperty("dateSource").GetString());
        Assert.Equal("Kinepolis Utrecht", document.GetProperty("venue").GetString());
        Assert.False(document.GetProperty("online").GetBoolean());
        Assert.Equal("https://techorama.nl/2026", document.GetProperty("link").GetString());
        Assert.Equal("Early bird ends", document.GetProperty("keyDates")[0].GetProperty("label").GetString());
        Assert.Equal("2026-06-30", document.GetProperty("keyDates")[0].GetProperty("date").GetString());
        Assert.Equal("Aspire in production", document.GetProperty("whatToLearn").GetString());
        Assert.Equal(series.Id, document.GetProperty("seriesId").GetGuid());

        var note = document.GetProperty("notes")[0];
        Assert.Equal("talk", note.GetProperty("kind").GetString());
        Assert.Equal("6f1c2d3e-0000-0000-0000-000000000042", note.GetProperty("reference").GetString());
        Assert.Equal("Keynote", note.GetProperty("title").GetString());

        Assert.Equal(Noon.AddHours(2), document.GetProperty("updatedAt").GetDateTimeOffset());
    }

    /// <summary>The service takes at most fifty changes in one push, so more go in batches, the
    /// watermark advancing with each.</summary>
    [Fact]
    public async Task More_changes_than_one_push_takes_go_in_batches_of_fifty()
    {
        var replication = new InMemoryKnowledgeEventReplication();
        for (var minute = 1; minute <= 51; minute++)
        {
            replication.Seed(KnowledgeEventCopies.Event($"Meetup {minute}", Noon.AddMinutes(minute)));
        }

        var state = new InMemoryKnowledgeEventSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(replication, state, AsTheServiceAnswers);

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(51, result.Value.Pushed);
        Assert.Equal([50, 1], fixture.Bodies.Select(body => Pushed(body).Count).ToArray());
        Assert.Equal([Noon.AddMinutes(49), Noon.AddMinutes(51)], state.Saved.Select(saved => saved.PushWatermark).ToArray());
    }

    [Fact]
    public async Task An_event_the_service_would_refuse_is_left_out_and_named_without_holding_back_the_rest()
    {
        var replication = new InMemoryKnowledgeEventReplication();
        var kept = replication.Seed(KnowledgeEventCopies.Event("Kept", Noon.AddHours(1)));
        var oversized = replication.Seed(KnowledgeEventCopies.Event("Oversized", Noon.AddHours(2)) with
        {
            WhatToLearn = new string('x', KnowledgeEventRecordLimits.DocumentBytes),
        });
        var state = new InMemoryKnowledgeEventSyncStateStore(Mine(Noon, null));
        var log = new CapturingLogger<KnowledgeEventSyncSession>();

        using var fixture = Fixture.Create(replication, state, AsTheServiceAnswers, log);

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(1, result.Value.Pushed);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Equal(kept.Id, Assert.Single(Pushed(Assert.Single(fixture.Bodies))).Id);
        Assert.Equal(Noon.AddHours(2), state.Current.PushWatermark);

        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Contains(oversized.Id.ToString(), line.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_push_that_fails_leaves_the_watermark_where_it_was()
    {
        var replication = new InMemoryKnowledgeEventReplication();
        replication.Seed(KnowledgeEventCopies.Event("dotNET Friday", Noon.AddHours(1)));
        var state = new InMemoryKnowledgeEventSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(replication, state,
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
        var state = new InMemoryKnowledgeEventSyncStateStore(Mine(DateTimeOffset.MinValue, null));

        using var fixture = Fixture.Create(new InMemoryKnowledgeEventReplication(), state, (_, index) => index switch
        {
            0 => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"events":[],"since":"cursor-1","hasMore":true}"""),
            _ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"events":[],"since":"cursor-2","hasMore":false}"""),
        });

        await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["cursor-1", "cursor-2"], state.Saved.Select(saved => saved.PullCursor).ToArray());
        Assert.Contains("since=cursor-1", fixture.Queries[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_expired_cursor_is_dropped_and_the_pull_starts_over_once()
    {
        var state = new InMemoryKnowledgeEventSyncStateStore(Mine(Noon, "a-cursor-the-store-forgot"));

        using var fixture = Fixture.Create(new InMemoryKnowledgeEventReplication(), state, (_, index) => index switch
        {
            0 => StubHttpMessageHandler.Problem(HttpStatusCode.BadRequest, SyncErrorCodes.SyncCursorExpired, "Too old."),
            _ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"events":[],"since":"cursor-1","hasMore":false}"""),
        });

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("since=", fixture.Queries[1], StringComparison.Ordinal);
        Assert.Equal("cursor-1", state.Current.PullCursor);
    }

    /// <summary>What another desktop pushed lands here whole, at the stamp it was pushed with —
    /// so this device's next push offers it as the version the replica already holds, not as a
    /// newer change of its own.</summary>
    [Fact]
    public async Task A_pulled_event_is_applied_at_its_own_stamp_and_counted_apart_from_what_arrived()
    {
        var replication = new InMemoryKnowledgeEventReplication();
        var heldNewer = replication.Seed(KnowledgeEventCopies.Event("Edited here later", Noon.AddHours(3)));
        var state = new InMemoryKnowledgeEventSyncStateStore(Mine(DateTimeOffset.MinValue, null));

        var arriving = KnowledgeEventDocuments.ToChange(KnowledgeEventCopies.Techorama(Noon, Guid.NewGuid()));
        var stale = KnowledgeEventDocuments.ToChange(KnowledgeEventCopies.Event("Older elsewhere", Noon, heldNewer.Id));

        var body = PageOf("cursor-1", hasMore: false, (arriving, 100L), (stale, 101L));

        using var fixture = Fixture.Create(replication, state, (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, body));

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Pulled);
        Assert.Equal(1, result.Value.Applied);
        Assert.Equal(Noon, replication.Event(arriving.Id)!.UpdatedAt);
        Assert.Equal("Techorama 2026", replication.Event(arriving.Id)!.Title);
        Assert.Equal("Edited here later", replication.Event(heldNewer.Id)!.Title);
    }

    [Fact]
    public async Task Syncing_pushes_before_it_pulls_and_a_failed_push_stops_the_exchange()
    {
        var replication = new InMemoryKnowledgeEventReplication();
        replication.Seed(KnowledgeEventCopies.Event("dotNET Friday", Noon.AddHours(1)));
        var state = new InMemoryKnowledgeEventSyncStateStore(Mine(DateTimeOffset.MinValue, null));

        using var ordered = Fixture.Create(replication, state, AsTheServiceAnswers);

        var result = await ordered.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([HttpMethod.Post, HttpMethod.Get], ordered.Handler.Requests.Select(request => request.Method).ToArray());
        Assert.Equal(1, result.Value.Pushed);

        replication.Seed(KnowledgeEventCopies.Event("Build", Noon.AddHours(2)));

        using var failing = Fixture.Create(replication, state,
            (_, _) => StubHttpMessageHandler.Problem(HttpStatusCode.ServiceUnavailable, SyncErrorCodes.ReplicaUnavailable, "Not yet."));

        var stopped = await failing.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(stopped.IsFailure);
        Assert.Single(failing.Handler.Requests);
    }

    /// <summary>The deployed sync service is updated by hand and lags the desktop. A service
    /// without the route answers a bare 404; the exchange ends there, quietly, and the watermark
    /// stays where it was so everything is sent once the service has the route.</summary>
    [Fact]
    public async Task A_service_without_the_route_is_not_yet_and_not_a_failure()
    {
        var replication = new InMemoryKnowledgeEventReplication();
        replication.Seed(KnowledgeEventCopies.Event("dotNET Friday", Noon.AddHours(1)));
        var state = new InMemoryKnowledgeEventSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(replication, state, (_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await fixture.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.True(result.Value.NotYetServed);
        Assert.Equal(0, result.Value.Pushed);
        Assert.Single(fixture.Handler.Requests);
        Assert.Equal(Noon, state.Current.PushWatermark);
    }

    [Fact]
    public async Task A_service_without_the_route_is_not_yet_when_only_the_pull_asks()
    {
        var state = new InMemoryKnowledgeEventSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(new InMemoryKnowledgeEventReplication(), state, (_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await fixture.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.True(result.Value.NotYetServed);
        Assert.Equal([HttpMethod.Get], fixture.Handler.Requests.Select(request => request.Method).ToArray());
        Assert.Null(state.Current.PullCursor);
    }

    /// <summary>Only the bare 404 an unmapped route produces means "no route yet". A 404 that
    /// carries a code fails as on every other feed.</summary>
    [Fact]
    public async Task A_coded_404_is_a_failure()
    {
        var replication = new InMemoryKnowledgeEventReplication();
        replication.Seed(KnowledgeEventCopies.Event("dotNET Friday", Noon.AddHours(1)));
        var state = new InMemoryKnowledgeEventSyncStateStore(Mine(Noon, null));

        using var fixture = Fixture.Create(replication, state,
            (_, _) => StubHttpMessageHandler.Problem(HttpStatusCode.NotFound, "sync.owner_not_found", "No such owner."));

        var result = await fixture.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("sync.owner_not_found", result.Error.Code);
        Assert.Equal(Noon, state.Current.PushWatermark);
    }

    [Fact]
    public async Task Progress_recorded_for_another_owner_is_started_over()
    {
        var state = new InMemoryKnowledgeEventSyncStateStore(
            new KnowledgeEventSyncState(Noon, "the-old-owners-cursor", AnotherOwner, ThisDevice));

        using var fixture = Fixture.Create(new InMemoryKnowledgeEventReplication(), state,
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"events":[],"since":"cursor-fresh","hasMore":false}"""));

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("the-old-owners-cursor", fixture.Queries[0], StringComparison.Ordinal);
        Assert.Equal(ThisOwner, state.Current.OwnerId);
        Assert.Equal(DateTimeOffset.MinValue, state.Saved[0].PushWatermark);
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static IReadOnlyList<KnowledgeEventChange> Pushed(string body) =>
        JsonSerializer.Deserialize<PushKnowledgeEventsRequest>(body, Web)!.Events;

    private static string PageOf(string since, bool hasMore, params (KnowledgeEventChange Change, long ServerTimestamp)[] records) =>
        JsonSerializer.Serialize(
            new PullKnowledgeEventsResponse(
                [.. records.Select(record => new KnowledgeEventChangeRecord(record.Change, Guid.NewGuid(), record.ServerTimestamp))],
                since,
                hasMore),
            Web);

    /// <summary>The service as it answers: a push refused whole when any change in it breaks the
    /// shared rule or the batch is over fifty, otherwise taken in full; a pull reads an empty
    /// feed.</summary>
    private static HttpResponseMessage AsTheServiceAnswers(HttpRequestMessage request, int index)
    {
        if (request.Method != HttpMethod.Post)
        {
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"events":[],"since":"cursor-1","hasMore":false}""");
        }

        var changes = Pushed(request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult());

        if (changes.Count > 50)
        {
            return StubHttpMessageHandler.Problem(HttpStatusCode.BadRequest, SyncErrorCodes.KnowledgeEventBatchTooLarge, "Too many.");
        }

        if (changes.Any(change => KnowledgeEventRecordLimits.WhyRefused(change) is not null))
        {
            return StubHttpMessageHandler.Problem(HttpStatusCode.BadRequest, SyncErrorCodes.KnowledgeEventInvalid, "Refused.");
        }

        return StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"accepted":{{changes.Count}}}""");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;

        private Fixture(HttpClient http, StubHttpMessageHandler handler, KnowledgeEventSyncSession session, List<string> bodies, List<string> queries)
        {
            _http = http;
            Handler = handler;
            Session = session;
            Bodies = bodies;
            Queries = queries;
        }

        public StubHttpMessageHandler Handler { get; }

        public KnowledgeEventSyncSession Session { get; }

        public List<string> Bodies { get; }

        public List<string> Queries { get; }

        public static Fixture Create(
            InMemoryKnowledgeEventReplication replication,
            IKnowledgeEventSyncStateStore state,
            Func<HttpRequestMessage, int, HttpResponseMessage> respond,
            ILogger<KnowledgeEventSyncSession>? log = null)
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

            var session = new KnowledgeEventSyncSession(
                new KnowledgeEventSyncClient(http),
                new KnowledgeEventReplicaMerge(replication),
                replication,
                state,
                new InMemoryDeviceCredentialStore(Paired),
                new FakeTimeProvider(Noon.AddHours(6)),
                log);

            return new Fixture(http, handler, session, bodies, queries);
        }

        public void Dispose() => _http.Dispose();
    }
}
