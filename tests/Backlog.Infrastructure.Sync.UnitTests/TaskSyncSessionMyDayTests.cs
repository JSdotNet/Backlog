using System.Net;
using System.Text.Json;

using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The My Day preferences leave the machine on the push as one whole document whenever
/// their stamp is later than the one this device last had accepted, under the kind
/// token's mark (local ADR 0032, §3); a device that never set the switch sends nothing.
/// </summary>
public sealed class TaskSyncSessionMyDayTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string PreferencesJson = """{"updatedAt":"2026-10-15T08:00:00Z","clearsOvernight":false}""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static HttpResponseMessage Accepted(int count) =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"accepted":{{count}}}""");

    [Fact]
    public async Task The_preferences_are_pushed_whole_and_the_mark_moves_past_them()
    {
        var myDay = new RecordingMyDayPreferencesReplication { Held = new MyDayPreferencesCopyDto(PreferencesJson, Morning) };
        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(state, myDay, (_, _) => Accepted(1));

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Pushed);

        var sent = Assert.Single(Pushed(Assert.Single(fixture.Bodies)));
        Assert.Equal(MyDayReplicaDocuments.PreferencesId, sent.Id);
        Assert.Equal("my-day-preferences", sent.Task.Type);
        Assert.Equal(PreferencesJson, sent.Task.ContentMd);
        Assert.Equal(Morning, sent.UpdatedAt);
        Assert.Null(sent.Task.InMyDayOn);
        Assert.Null(sent.DeletedAt);
        Assert.Equal(Morning, state.Current.DocumentWatermark("my-day-preferences"));
    }

    [Fact]
    public async Task Preferences_already_accepted_are_not_sent_again_until_they_change()
    {
        var myDay = new RecordingMyDayPreferencesReplication { Held = new MyDayPreferencesCopyDto(PreferencesJson, Morning) };

        using var fixture = Fixture.Create(new InMemoryTaskSyncStateStore(), myDay, (_, _) => Accepted(1));

        await fixture.Session.PushAsync(Cancellation);
        await fixture.Session.PushAsync(Cancellation);
        Assert.Single(fixture.Bodies);

        myDay.Held = new MyDayPreferencesCopyDto(PreferencesJson, Morning.AddMinutes(5));
        await fixture.Session.PushAsync(Cancellation);

        Assert.Equal(2, fixture.Bodies.Count);
    }

    /// <summary>ADR 0032 Verification 3: a newly paired desktop with no file pushes
    /// nothing, so it takes the other desktop's choice rather than replacing it with the
    /// default.</summary>
    [Fact]
    public async Task A_device_that_never_set_the_switch_sends_nothing()
    {
        using var fixture = Fixture.Create(
            new InMemoryTaskSyncStateStore(), new RecordingMyDayPreferencesReplication(), (_, _) => Accepted(0));

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Pushed);
        Assert.Empty(fixture.Handler.Requests);
    }

    /// <summary>A refused push — the replica holds a later flip — is counted refused,
    /// and the mark still moves past it: the pull brings the later one back.</summary>
    [Fact]
    public async Task Refused_preferences_are_counted_refused_and_not_offered_again()
    {
        var myDay = new RecordingMyDayPreferencesReplication { Held = new MyDayPreferencesCopyDto(PreferencesJson, Morning) };
        var state = new InMemoryTaskSyncStateStore();
        var activity = new SyncActivityLog();

        using var fixture = Fixture.Create(state, myDay, (_, _) => Accepted(0), activity);

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.Equal(0, result.Value.Pushed);
        Assert.Equal(1, result.Value.Refused);
        Assert.Empty(activity.Snapshot());
        Assert.Equal(Morning, state.Current.DocumentWatermark("my-day-preferences"));
    }

    [Fact]
    public async Task Pushed_preferences_are_recorded_as_sent()
    {
        var myDay = new RecordingMyDayPreferencesReplication { Held = new MyDayPreferencesCopyDto(PreferencesJson, Morning) };
        var activity = new SyncActivityLog();

        using var fixture = Fixture.Create(new InMemoryTaskSyncStateStore(), myDay, (_, _) => Accepted(1), activity);

        await fixture.Session.PushAsync(Cancellation);

        var entry = Assert.Single(activity.Snapshot());
        Assert.Equal(SyncDirection.Sent, entry.Direction);
        Assert.Equal(SyncItemKind.MyDayPreferences, entry.Kind);
        Assert.Equal("My Day preferences", entry.Title);
    }

    /// <summary>The pull hands the document to the port whole, at the stamp it was
    /// pushed with, and the cursor moves past the page.</summary>
    [Fact]
    public async Task Pulled_preferences_reach_the_port_whole()
    {
        var myDay = new RecordingMyDayPreferencesReplication();
        var state = new InMemoryTaskSyncStateStore();
        var sent = new MyDayPreferencesCopyDto(PreferencesJson, Morning);
        var page = JsonSerializer.Serialize(
            new PullTasksResponse(
                [new TaskChangeRecord(MyDayReplicaDocuments.ToChange(sent), Guid.NewGuid(), 10)],
                "cursor-after-the-page",
                HasMore: false),
            Web);

        using var fixture = Fixture.Create(state, myDay, (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, page));

        var result = await fixture.Session.PullAsync(Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Applied);
        Assert.Equal(sent, Assert.Single(myDay.Offered));
        Assert.Equal("cursor-after-the-page", state.Current.PullCursor);
    }

    /// <summary>Starting over for a new identity reads the new feed from the start, so
    /// the one-time replay is not owed again.</summary>
    [Fact]
    public async Task Starting_over_for_a_new_identity_keeps_the_feed_replays()
    {
        var state = new InMemoryTaskSyncStateStore(
            new TaskSyncState(Morning, "cursor-of-another-owner").WithFeedReplay(MyDayReplicaDocuments.PreferencesType));

        using var fixture = Fixture.Create(state, new RecordingMyDayPreferencesReplication(), (_, _) => Accepted(0));

        await fixture.Session.PushAsync(Cancellation);

        Assert.Null(state.Current.PullCursor);
        Assert.True(state.Current.HasReplayedFeedFor(MyDayReplicaDocuments.PreferencesType));
    }

    private static IReadOnlyList<TaskChange> Pushed(string body) =>
        JsonSerializer.Deserialize<PushTasksRequest>(body, Web)!.Tasks;

    /// <summary>A session over a scripted replica, with the preferences port threaded
    /// through the merge and the session alike.</summary>
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
            RecordingMyDayPreferencesReplication myDay,
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
                new TaskReplicaMerge(tasks, activity: activity, myDay: myDay),
                tasks,
                state,
                new InMemoryDeviceCredentialStore(new DeviceCredential(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "Workshop PC",
                    "a-registration-credential")),
                new FakeTimeProvider(Morning.AddHours(6)),
                activity: activity,
                myDay: myDay);

            return new Fixture(http, handler, session, bodies);
        }

        public void Dispose() => _http.Dispose();
    }
}
