using System.Net;
using System.Text.Json;

using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// Every goal leaves the machine on the push as one whole <c>goal</c> document whenever its
/// stamp is later than the one this device last had accepted for it — a mark per goal —
/// and comes back on the pull to Progress whole
/// (<c>.devbook/domain/progress/requirements.md#requirement-goals-and-impediments-travel-as-one-document-each</c>).
/// </summary>
public sealed class TaskSyncSessionGoalTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string GoalJson = """{"version":1,"title":"Customers get paid faster","dueDate":"2026-10-31","attachments":[]}""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static HttpResponseMessage Accepted(int count) =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"accepted":{{count}}}""");

    private static GoalReplicaCopyDto Copy(string title, DateTimeOffset at) => new(Guid.NewGuid(), title, GoalJson, at);

    [Fact]
    public async Task Each_goal_is_pushed_whole_under_its_own_id_and_only_again_once_it_changed()
    {
        var goals = new RecordingGoalReplication();
        var paid = Copy("Customers get paid faster", Morning);
        var search = Copy("Devbook search ships", Morning);
        goals.Hold(paid);
        goals.Hold(search);
        var state = new InMemoryTaskSyncStateStore();
        var activity = new SyncActivityLog();

        using var fixture = Fixture.Create(state, goals, (_, _) => Accepted(1), activity);

        var first = await fixture.Session.PushAsync(Cancellation);

        Assert.Equal(2, first.Value.Pushed);
        var sent = Assert.Single(Pushed(fixture.Bodies[0]));
        Assert.Equal(paid.Id, sent.Id);
        Assert.Equal("goal", sent.Task.Type);
        Assert.Equal(GoalJson, sent.Task.ContentMd);
        Assert.Equal(Morning, sent.UpdatedAt);
        Assert.Equal(Morning, state.Current.DocumentWatermark(GoalReplicaDocuments.MarkOf(paid.Id)));
        Assert.All(activity.Snapshot(), entry => Assert.Equal(SyncItemKind.Goal, entry.Kind));

        await fixture.Session.PushAsync(Cancellation);
        Assert.Equal(2, fixture.Bodies.Count);

        goals.Hold(search with { UpdatedAt = Morning.AddMinutes(5) });
        await fixture.Session.PushAsync(Cancellation);

        Assert.Equal(3, fixture.Bodies.Count);
        Assert.Equal(search.Id, Assert.Single(Pushed(fixture.Bodies[2])).Id);
    }

    [Fact]
    public async Task A_device_that_never_set_a_goal_sends_nothing()
    {
        using var fixture = Fixture.Create(new InMemoryTaskSyncStateStore(), new RecordingGoalReplication(), (_, _) => Accepted(0));

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.Equal(0, result.Value.Pushed);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task A_pulled_goal_reaches_progress_whole()
    {
        var goals = new RecordingGoalReplication();
        var sent = Copy("Customers get paid faster", Morning);
        var page = JsonSerializer.Serialize(
            new PullTasksResponse(
                [new TaskChangeRecord(GoalReplicaDocuments.ToChange(sent), Guid.NewGuid(), 10)],
                "cursor-after-the-page",
                HasMore: false),
            Web);

        using var fixture = Fixture.Create(new InMemoryTaskSyncStateStore(), goals, (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, page));

        var result = await fixture.Session.PullAsync(Cancellation);

        Assert.Equal(1, result.Value.Applied);
        Assert.Equal(sent, Assert.Single(goals.Offered));
    }

    private static IReadOnlyList<TaskChange> Pushed(string body) =>
        JsonSerializer.Deserialize<PushTasksRequest>(body, Web)!.Tasks;

    /// <summary>The shape of <c>TaskSyncSessionSprintTests.Fixture</c>, with the goal port
    /// threaded through the merge and the session alike.</summary>
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
            RecordingGoalReplication goals,
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
                new TaskReplicaMerge(tasks, activity: activity, goals: goals),
                tasks,
                state,
                new InMemoryDeviceCredentialStore(new DeviceCredential(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "Workshop PC",
                    "a-registration-credential")),
                new FakeTimeProvider(Morning.AddHours(6)),
                activity: activity,
                goals: goals);

            return new Fixture(http, handler, session, bodies);
        }

        public void Dispose() => _http.Dispose();
    }
}
