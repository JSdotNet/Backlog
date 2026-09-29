using System.Net;
using System.Text.Json;

using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The roadmap's two documents leave the machine on the push, after the tasks,
/// whenever the stamp is later than the one this device last had accepted for it
/// (local ADR 0018, Decision §3) — and a device that never saved one sends
/// nothing.
/// </summary>
public sealed class TaskSyncSessionRoadmapTests
{
    private static readonly DateTimeOffset Morning = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string PlanJson = """{"version":1,"items":[],"milestones":[],"bands":{}}""";
    private const string PaceJson = """{"storyPointsPerWeek":12,"source":"Manual"}""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static HttpResponseMessage Accepted(int count) =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"accepted":{{count}}}""");

    [Fact]
    public async Task Each_saved_document_is_pushed_whole_after_the_tasks()
    {
        var store = new InMemoryTaskStore();
        store.Seed(TaskChanges.Task("Edited here", Morning));
        var roadmap = new RecordingRoadmapReplication();
        roadmap.Held[RoadmapReplicaDocument.Plan] = new RoadmapReplicaCopyDto(PlanJson, Morning.AddMinutes(1));
        roadmap.Held[RoadmapReplicaDocument.Pace] = new RoadmapReplicaCopyDto(PaceJson, Morning.AddMinutes(2));

        using var fixture = Fixture.Create(store, new InMemoryTaskSyncStateStore(), roadmap, (_, _) => Accepted(1));

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Pushed);
        Assert.Equal(3, fixture.Bodies.Count);
        Assert.Equal("Edited here", Assert.Single(Pushed(fixture.Bodies[0])).Task.Title);

        var plan = Assert.Single(Pushed(fixture.Bodies[1]));
        Assert.Equal(RoadmapReplicaDocuments.PlanId, plan.Id);
        Assert.Equal("roadmap-plan", plan.Task.Type);
        Assert.Equal(PlanJson, plan.Task.ContentMd);
        Assert.Equal(Morning.AddMinutes(1), plan.UpdatedAt);
        Assert.Null(plan.DeletedAt);

        var pace = Assert.Single(Pushed(fixture.Bodies[2]));
        Assert.Equal(RoadmapReplicaDocuments.PaceId, pace.Id);
        Assert.Equal("planning-pace", pace.Task.Type);
        Assert.Equal(PaceJson, pace.Task.ContentMd);
    }

    [Fact]
    public async Task A_pushed_document_is_not_sent_again_until_it_changes()
    {
        var roadmap = new RecordingRoadmapReplication();
        roadmap.Held[RoadmapReplicaDocument.Plan] = new RoadmapReplicaCopyDto(PlanJson, Morning);
        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(new InMemoryTaskStore(), state, roadmap, (_, _) => Accepted(1));

        await fixture.Session.PushAsync(Cancellation);
        Assert.Equal(Morning, state.Current.DocumentWatermark(RoadmapReplicaDocuments.PlanType));

        await fixture.Session.PushAsync(Cancellation);
        Assert.Single(fixture.Bodies);

        roadmap.Held[RoadmapReplicaDocument.Plan] = new RoadmapReplicaCopyDto(PlanJson, Morning.AddMinutes(5));
        await fixture.Session.PushAsync(Cancellation);

        Assert.Equal(2, fixture.Bodies.Count);
        Assert.Equal(Morning.AddMinutes(5), state.Current.DocumentWatermark(RoadmapReplicaDocuments.PlanType));
    }

    /// <summary>ADR 0018 Verification 5: a device with no saved plan and no velocity
    /// file pushes nothing — never an empty plan, never the default of seven.</summary>
    [Fact]
    public async Task A_device_that_never_saved_either_document_sends_nothing()
    {
        using var fixture = Fixture.Create(
            new InMemoryTaskStore(), new InMemoryTaskSyncStateStore(), new RecordingRoadmapReplication(), (_, _) => Accepted(0));

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Pushed);
        Assert.Empty(fixture.Handler.Requests);
    }

    /// <summary>ADR 0018 Verification 2: the older push is refused — a short
    /// accepted count — and said so in the summary. The mark still moves past it:
    /// the replica holds a later version, which the pull brings back.</summary>
    [Fact]
    public async Task A_refused_document_is_counted_refused_and_not_offered_again()
    {
        var roadmap = new RecordingRoadmapReplication();
        roadmap.Held[RoadmapReplicaDocument.Plan] = new RoadmapReplicaCopyDto(PlanJson, Morning);
        var state = new InMemoryTaskSyncStateStore();
        var activity = new SyncActivityLog();

        using var fixture = Fixture.Create(new InMemoryTaskStore(), state, roadmap, (_, _) => Accepted(0), activity);

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.Equal(0, result.Value.Pushed);
        Assert.Equal(1, result.Value.Refused);
        Assert.Empty(activity.Snapshot());
        Assert.Equal(Morning, state.Current.DocumentWatermark(RoadmapReplicaDocuments.PlanType));
    }

    [Fact]
    public async Task A_pushed_document_is_recorded_as_a_roadmap_document_sent()
    {
        var roadmap = new RecordingRoadmapReplication();
        roadmap.Held[RoadmapReplicaDocument.Pace] = new RoadmapReplicaCopyDto(PaceJson, Morning);
        var activity = new SyncActivityLog();

        using var fixture = Fixture.Create(
            new InMemoryTaskStore(), new InMemoryTaskSyncStateStore(), roadmap, (_, _) => Accepted(1), activity);

        await fixture.Session.PushAsync(Cancellation);

        var entry = Assert.Single(activity.Snapshot());
        Assert.Equal(SyncDirection.Sent, entry.Direction);
        Assert.Equal(SyncItemKind.Roadmap, entry.Kind);
        Assert.Equal("Planning pace", entry.Title);
    }

    [Fact]
    public async Task A_failed_push_leaves_the_mark_where_it_was()
    {
        var roadmap = new RecordingRoadmapReplication();
        roadmap.Held[RoadmapReplicaDocument.Plan] = new RoadmapReplicaCopyDto(PlanJson, Morning);
        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(new InMemoryTaskStore(), state, roadmap,
            (_, _) => StubHttpMessageHandler.Problem(HttpStatusCode.ServiceUnavailable, "sync.replica_unavailable", "later"));

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.True(result.IsFailure);
        Assert.Null(state.Current.DocumentWatermark(RoadmapReplicaDocuments.PlanType));
    }

    /// <summary>The marks are one owner's, like the task watermark: a device that
    /// becomes somebody else's sends its plan to the new owner too.</summary>
    [Fact]
    public async Task A_new_identity_sends_the_documents_again()
    {
        var roadmap = new RecordingRoadmapReplication();
        roadmap.Held[RoadmapReplicaDocument.Plan] = new RoadmapReplicaCopyDto(PlanJson, Morning);
        var state = new InMemoryTaskSyncStateStore(
            new TaskSyncState(DateTimeOffset.MinValue, null, Guid.NewGuid(), Guid.NewGuid())
                .WithDocumentWatermark(RoadmapReplicaDocuments.PlanType, Morning));

        using var fixture = Fixture.Create(new InMemoryTaskStore(), state, roadmap, (_, _) => Accepted(1));

        await fixture.Session.PushAsync(Cancellation);

        Assert.Single(fixture.Bodies);
    }

    /// <summary>ADR 0018 Verification 7: a session whose merge has no Roadmap port —
    /// the older build — counts both kinds Skipped, writes no task, and still moves
    /// the cursor past the page.</summary>
    [Fact]
    public async Task Without_the_roadmap_port_a_pull_skips_both_kinds_and_still_moves_the_cursor()
    {
        var tasks = new InMemoryTaskStore();
        var state = new InMemoryTaskSyncStateStore();
        var page = JsonSerializer.Serialize(
            new PullTasksResponse(
                [
                    new TaskChangeRecord(RoadmapReplicaChanges.Plan(PlanJson, Morning), Guid.NewGuid(), 10),
                    new TaskChangeRecord(RoadmapReplicaChanges.Pace(PaceJson, Morning), Guid.NewGuid(), 11),
                ],
                "cursor-after-the-page",
                HasMore: false),
            Web);

        using var fixture = Fixture.Create(tasks, state, roadmap: null,
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, page));

        var result = await fixture.Session.PullAsync(Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Pulled);
        Assert.Equal(0, result.Value.Applied);
        Assert.Equal(2, result.Value.Skipped);
        Assert.Empty(tasks.Writes);
        Assert.Equal("cursor-after-the-page", state.Current.PullCursor);
    }

    private static IReadOnlyList<TaskChange> Pushed(string body) =>
        JsonSerializer.Deserialize<PushTasksRequest>(body, Web)!.Tasks;

    /// <summary>The shape of <c>TaskSyncSessionOutboxTests.Fixture</c>, with the
    /// roadmap port threaded through the merge and the session alike, the way the
    /// container hands both the same singleton.</summary>
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
            IRoadmapReplication? roadmap,
            Func<HttpRequestMessage, int, HttpResponseMessage> respond,
            SyncActivityLog? activity = null)
        {
            var bodies = new List<string>();

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
                new TaskReplicaMerge(tasks, roadmap: roadmap),
                tasks,
                state,
                new InMemoryDeviceCredentialStore(new DeviceCredential(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "Workshop PC",
                    "a-registration-credential")),
                new FakeTimeProvider(Morning.AddHours(6)),
                activity: activity,
                roadmap: roadmap);

            return new Fixture(http, handler, session, bodies);
        }

        public void Dispose() => _http.Dispose();
    }
}
