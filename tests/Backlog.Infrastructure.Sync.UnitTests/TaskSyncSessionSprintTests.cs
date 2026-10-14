using System.Net;
using System.Text.Json;

using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// Every sprint series leaves the machine on the push as one whole document, whenever
/// its stamp is later than the one this device last had accepted for that series — a
/// mark per series, because each is a document of its own — and a device that never set
/// one up sends nothing.
/// </summary>
public sealed class TaskSyncSessionSprintTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string SeriesJson = """{"version":1,"name":"Platform","sprints":[]}""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static HttpResponseMessage Accepted(int count) =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"accepted":{{count}}}""");

    private static SprintSeriesReplicaCopyDto Copy(string name, DateTimeOffset at, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), name, SeriesJson, at);

    [Fact]
    public async Task Each_series_is_pushed_whole_under_its_own_id()
    {
        var sprints = new RecordingSprintSeriesReplication();
        var platform = Copy("Platform", Morning);
        var docs = Copy("Docs", Morning.AddMinutes(1));
        sprints.Hold(platform);
        sprints.Hold(docs);

        using var fixture = Fixture.Create(new InMemoryTaskSyncStateStore(), sprints, (_, _) => Accepted(1));

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Pushed);
        Assert.Equal(2, fixture.Bodies.Count);

        var first = Assert.Single(Pushed(fixture.Bodies[0]));
        Assert.Equal(platform.Id, first.Id);
        Assert.Equal("sprint-series", first.Task.Type);
        Assert.Equal("Platform", first.Task.Title);
        Assert.Equal(SeriesJson, first.Task.ContentMd);
        Assert.Equal(Morning, first.UpdatedAt);
        Assert.Null(first.DeletedAt);

        Assert.Equal(docs.Id, Assert.Single(Pushed(fixture.Bodies[1])).Id);
    }

    /// <summary>One series changed sends that series alone: the other's mark still
    /// covers it.</summary>
    [Fact]
    public async Task Only_the_series_that_changed_is_sent_again()
    {
        var sprints = new RecordingSprintSeriesReplication();
        var platform = Copy("Platform", Morning);
        var docs = Copy("Docs", Morning);
        sprints.Hold(platform);
        sprints.Hold(docs);
        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(state, sprints, (_, _) => Accepted(1));

        await fixture.Session.PushAsync(Cancellation);
        Assert.Equal(Morning, state.Current.DocumentWatermark(SprintReplicaDocuments.MarkOf(platform.Id)));
        Assert.Equal(Morning, state.Current.DocumentWatermark(SprintReplicaDocuments.MarkOf(docs.Id)));

        await fixture.Session.PushAsync(Cancellation);
        Assert.Equal(2, fixture.Bodies.Count);

        sprints.Hold(docs with { UpdatedAt = Morning.AddMinutes(5) });
        await fixture.Session.PushAsync(Cancellation);

        Assert.Equal(3, fixture.Bodies.Count);
        Assert.Equal(docs.Id, Assert.Single(Pushed(fixture.Bodies[2])).Id);
    }

    [Fact]
    public async Task A_device_that_never_set_up_a_series_sends_nothing()
    {
        using var fixture = Fixture.Create(
            new InMemoryTaskSyncStateStore(), new RecordingSprintSeriesReplication(), (_, _) => Accepted(0));

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Pushed);
        Assert.Empty(fixture.Handler.Requests);
    }

    /// <summary>An older push is refused — a short accepted count — and said so. The mark
    /// still moves past it: the replica holds a later version, which the pull brings
    /// back.</summary>
    [Fact]
    public async Task A_refused_series_is_counted_refused_and_not_offered_again()
    {
        var sprints = new RecordingSprintSeriesReplication();
        var platform = Copy("Platform", Morning);
        sprints.Hold(platform);
        var state = new InMemoryTaskSyncStateStore();
        var activity = new SyncActivityLog();

        using var fixture = Fixture.Create(state, sprints, (_, _) => Accepted(0), activity);

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.Equal(0, result.Value.Pushed);
        Assert.Equal(1, result.Value.Refused);
        Assert.Empty(activity.Snapshot());
        Assert.Equal(Morning, state.Current.DocumentWatermark(SprintReplicaDocuments.MarkOf(platform.Id)));
    }

    [Fact]
    public async Task A_pushed_series_is_recorded_as_a_sprint_series_sent()
    {
        var sprints = new RecordingSprintSeriesReplication();
        sprints.Hold(Copy("Platform", Morning));
        var activity = new SyncActivityLog();

        using var fixture = Fixture.Create(new InMemoryTaskSyncStateStore(), sprints, (_, _) => Accepted(1), activity);

        await fixture.Session.PushAsync(Cancellation);

        var entry = Assert.Single(activity.Snapshot());
        Assert.Equal(SyncDirection.Sent, entry.Direction);
        Assert.Equal(SyncItemKind.SprintSeries, entry.Kind);
        Assert.Equal("Platform", entry.Title);
    }

    [Fact]
    public async Task A_failed_push_leaves_the_mark_where_it_was()
    {
        var sprints = new RecordingSprintSeriesReplication();
        var platform = Copy("Platform", Morning);
        sprints.Hold(platform);
        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(state, sprints,
            (_, _) => StubHttpMessageHandler.Problem(HttpStatusCode.ServiceUnavailable, "sync.replica_unavailable", "later"));

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.True(result.IsFailure);
        Assert.Null(state.Current.DocumentWatermark(SprintReplicaDocuments.MarkOf(platform.Id)));
    }

    /// <summary>The pull hands each series to Progress whole, at the stamp it was
    /// pushed with, and the cursor moves past the page.</summary>
    [Fact]
    public async Task A_pulled_series_reaches_progress_whole()
    {
        var sprints = new RecordingSprintSeriesReplication();
        var state = new InMemoryTaskSyncStateStore();
        var sent = Copy("Platform", Morning);
        var page = JsonSerializer.Serialize(
            new PullTasksResponse(
                [new TaskChangeRecord(SprintReplicaDocuments.ToChange(sent), Guid.NewGuid(), 10)],
                "cursor-after-the-page",
                HasMore: false),
            Web);

        using var fixture = Fixture.Create(state, sprints, (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, page));

        var result = await fixture.Session.PullAsync(Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Applied);
        Assert.Equal(sent, Assert.Single(sprints.Offered));
        Assert.Equal("cursor-after-the-page", state.Current.PullCursor);
    }

    private static IReadOnlyList<TaskChange> Pushed(string body) =>
        JsonSerializer.Deserialize<PushTasksRequest>(body, Web)!.Tasks;

    /// <summary>The shape of <c>TaskSyncSessionRoadmapTests.Fixture</c>, with Progress's
    /// port threaded through the merge and the session alike, the way the container
    /// hands both the same singleton.</summary>
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
            ITaskSyncStateStore state,
            RecordingSprintSeriesReplication sprints,
            Func<HttpRequestMessage, int, HttpResponseMessage> respond,
            SyncActivityLog? activity = null)
        {
            var bodies = new List<string>();
            var tasks = new InMemoryTaskStore();

            var handler = new StubHttpMessageHandler((request, index) =>
            {
                if (request.Method == HttpMethod.Post && request.Content is not null)
                {
                    bodies.Add(request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult());
                }

                return respond(request, index);
            });

            var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };

            var session = new TaskSyncSession(
                new TaskSyncClient(http),
                new TaskReplicaMerge(tasks, activity: activity, sprints: sprints),
                tasks,
                state,
                new InMemoryDeviceCredentialStore(new DeviceCredential(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "Workshop PC",
                    "a-registration-credential")),
                new FakeTimeProvider(Morning.AddHours(6)),
                activity: activity,
                sprints: sprints);

            return new Fixture(http, handler, session, bodies);
        }

        public void Dispose() => _http.Dispose();
    }
}
