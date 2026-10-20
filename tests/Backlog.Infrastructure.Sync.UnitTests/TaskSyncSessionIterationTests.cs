using System.Net;
using System.Text.Json;

using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The iteration settings leave the machine on the push as one whole document whenever
/// their stamp is later than the one this device last had accepted, under the kind
/// token's mark (local ADR 0036, §4); a device that never chose an iteration sends nothing.
/// </summary>
public sealed class TaskSyncSessionIterationTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string SettingsJson = """{"updatedAt":"2026-10-20T08:00:00Z","length":"two-weeks","twoWeekStart":"2026-10-19"}""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static HttpResponseMessage Accepted(int count) =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"accepted":{{count}}}""");

    [Fact]
    public async Task The_settings_are_pushed_whole_and_the_mark_moves_past_them()
    {
        var iteration = new RecordingIterationSettingsReplication { Held = new IterationSettingsCopyDto(SettingsJson, Morning) };
        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(state, iteration, (_, _) => Accepted(1));

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Pushed);

        var sent = Assert.Single(Pushed(Assert.Single(fixture.Bodies)));
        Assert.Equal(IterationReplicaDocuments.SettingsId, sent.Id);
        Assert.Equal("iteration-settings", sent.Task.Type);
        Assert.Equal(SettingsJson, sent.Task.ContentMd);
        Assert.Equal(Morning, sent.UpdatedAt);
        Assert.Null(sent.Task.InMyDayOn);
        Assert.Null(sent.DeletedAt);
        Assert.Equal(Morning, state.Current.DocumentWatermark("iteration-settings"));
    }

    [Fact]
    public async Task Settings_already_accepted_are_not_sent_again_until_they_change()
    {
        var iteration = new RecordingIterationSettingsReplication { Held = new IterationSettingsCopyDto(SettingsJson, Morning) };

        using var fixture = Fixture.Create(new InMemoryTaskSyncStateStore(), iteration, (_, _) => Accepted(1));

        await fixture.Session.PushAsync(Cancellation);
        await fixture.Session.PushAsync(Cancellation);
        Assert.Single(fixture.Bodies);

        iteration.Held = new IterationSettingsCopyDto(SettingsJson, Morning.AddMinutes(5));
        await fixture.Session.PushAsync(Cancellation);

        Assert.Equal(2, fixture.Bodies.Count);
    }

    /// <summary>ADR 0036 Verification 5: a seeded copy, at the floor stamp, is pushed when
    /// the device has never sent one — so with none on the feed, it becomes the person's
    /// value everywhere.</summary>
    [Fact]
    public async Task A_seeded_copy_at_the_floor_stamp_is_pushed()
    {
        var iteration = new RecordingIterationSettingsReplication
        {
            Held = new IterationSettingsCopyDto("""{"length":"two-weeks"}""", DateTimeOffset.UnixEpoch)
        };
        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(state, iteration, (_, _) => Accepted(1));

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.Equal(1, result.Value.Pushed);
        Assert.Equal(DateTimeOffset.UnixEpoch, Assert.Single(Pushed(Assert.Single(fixture.Bodies))).UpdatedAt);
        Assert.Equal(DateTimeOffset.UnixEpoch, state.Current.DocumentWatermark("iteration-settings"));
    }

    /// <summary>ADR 0036 Verification 4: a newly paired desktop with no file pushes
    /// nothing, so it takes the other desktop's choice rather than replacing it with
    /// Week.</summary>
    [Fact]
    public async Task A_device_that_never_chose_sends_nothing()
    {
        using var fixture = Fixture.Create(
            new InMemoryTaskSyncStateStore(), new RecordingIterationSettingsReplication(), (_, _) => Accepted(0));

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Pushed);
        Assert.Empty(fixture.Handler.Requests);
    }

    /// <summary>A refused push — the replica holds a later change — is counted refused,
    /// and the mark still moves past it: the pull brings the later one back.</summary>
    [Fact]
    public async Task Refused_settings_are_counted_refused_and_not_offered_again()
    {
        var iteration = new RecordingIterationSettingsReplication { Held = new IterationSettingsCopyDto(SettingsJson, Morning) };
        var state = new InMemoryTaskSyncStateStore();
        var activity = new SyncActivityLog();

        using var fixture = Fixture.Create(state, iteration, (_, _) => Accepted(0), activity);

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.Equal(0, result.Value.Pushed);
        Assert.Equal(1, result.Value.Refused);
        Assert.Empty(activity.Snapshot());
        Assert.Equal(Morning, state.Current.DocumentWatermark("iteration-settings"));
    }

    [Fact]
    public async Task Pushed_settings_are_recorded_as_sent()
    {
        var iteration = new RecordingIterationSettingsReplication { Held = new IterationSettingsCopyDto(SettingsJson, Morning) };
        var activity = new SyncActivityLog();

        using var fixture = Fixture.Create(new InMemoryTaskSyncStateStore(), iteration, (_, _) => Accepted(1), activity);

        await fixture.Session.PushAsync(Cancellation);

        var entry = Assert.Single(activity.Snapshot());
        Assert.Equal(SyncDirection.Sent, entry.Direction);
        Assert.Equal(SyncItemKind.IterationSettings, entry.Kind);
        Assert.Equal("Iteration settings", entry.Title);
    }

    /// <summary>The pull hands the document to the port whole, at the stamp it was pushed
    /// with, and the cursor moves past the page.</summary>
    [Fact]
    public async Task Pulled_settings_reach_the_port_whole()
    {
        var iteration = new RecordingIterationSettingsReplication();
        var state = new InMemoryTaskSyncStateStore();
        var sent = new IterationSettingsCopyDto(SettingsJson, Morning);
        var page = JsonSerializer.Serialize(
            new PullTasksResponse(
                [new TaskChangeRecord(IterationReplicaDocuments.ToChange(sent), Guid.NewGuid(), 10)],
                "cursor-after-the-page",
                HasMore: false),
            Web);

        using var fixture = Fixture.Create(state, iteration, (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, page));

        var result = await fixture.Session.PullAsync(Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Applied);
        Assert.Equal(sent, Assert.Single(iteration.Offered));
        Assert.Equal("cursor-after-the-page", state.Current.PullCursor);
    }

    private static IReadOnlyList<TaskChange> Pushed(string body) =>
        JsonSerializer.Deserialize<PushTasksRequest>(body, Web)!.Tasks;

    /// <summary>A session over a scripted replica, with the iteration settings port
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
            RecordingIterationSettingsReplication iteration,
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
                new TaskReplicaMerge(tasks, activity: activity, iteration: iteration),
                tasks,
                state,
                new InMemoryDeviceCredentialStore(new DeviceCredential(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "Workshop PC",
                    "a-registration-credential")),
                new FakeTimeProvider(Morning.AddHours(6)),
                activity: activity,
                iteration: iteration);

            return new Fixture(http, handler, session, bodies);
        }

        public void Dispose() => _http.Dispose();
    }
}
