using System.Net;
using System.Text.Json;

using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The two single settings documents — the iteration settings (local ADR 0036, §4) and
/// the My Day preferences (local ADR 0032, §3) — leave the machine on the push as one whole
/// document whenever their stamp is later than the one this device last had accepted,
/// under the kind token's mark; a device that never set one sends nothing.
/// </summary>
public sealed class TaskSyncSessionSettingsTests
{
    private const string Iteration = "iteration-settings";
    private const string MyDay = "my-day-preferences";

    private static readonly DateTimeOffset Morning = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>One settings document as the session meets it: its kind token (also its
    /// mark), its constant id, what the activity log calls it, a JSON it may hold, and the
    /// copy that JSON travels as.</summary>
    private sealed record Setting(
        string Type,
        Guid Id,
        SyncItemKind Kind,
        string Title,
        string Json,
        Func<string, DateTimeOffset, object> Copy,
        Func<object, TaskChange> ToChange);

    private static readonly Dictionary<string, Setting> Settings = new()
    {
        [Iteration] = new(
            Iteration,
            IterationReplicaDocuments.SettingsId,
            SyncItemKind.IterationSettings,
            "Iteration settings",
            """{"updatedAt":"2026-10-20T08:00:00Z","length":"two-weeks","twoWeekStart":"2026-10-19"}""",
            (json, at) => new IterationSettingsCopyDto(json, at),
            copy => IterationReplicaDocuments.ToChange((IterationSettingsCopyDto)copy)),
        [MyDay] = new(
            MyDay,
            MyDayReplicaDocuments.PreferencesId,
            SyncItemKind.MyDayPreferences,
            "My Day preferences",
            """{"updatedAt":"2026-10-15T08:00:00Z","clearsOvernight":false}""",
            (json, at) => new MyDayPreferencesCopyDto(json, at),
            copy => MyDayReplicaDocuments.ToChange((MyDayPreferencesCopyDto)copy)),
    };

    public static TheoryData<string> Kinds => [Iteration, MyDay];

    private static HttpResponseMessage Accepted(int count) =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"accepted":{{count}}}""");

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task The_document_is_pushed_whole_and_the_mark_moves_past_it(string kind)
    {
        var setting = Settings[kind];
        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(kind, state, (_, _) => Accepted(1));
        fixture.Hold(setting.Copy(setting.Json, Morning));

        var result = await fixture.Session.PushAsync(Cancellation);

        var summary = Assert.IsType<Ok<TaskSyncSummary>>(result.Value).Value;
        Assert.Equal(1, summary.Pushed);

        var sent = Assert.Single(Pushed(Assert.Single(fixture.Bodies)));
        Assert.Equal(setting.Id, sent.Id);
        Assert.Equal(setting.Type, sent.Task.Type);
        Assert.Equal(setting.Json, sent.Task.ContentMd);
        Assert.Equal(Morning, sent.UpdatedAt);
        Assert.Null(sent.Task.InMyDayOn);
        Assert.Null(sent.DeletedAt);
        Assert.Equal(Morning, state.Current.DocumentWatermark(setting.Type));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_document_already_accepted_is_not_sent_again_until_it_changes(string kind)
    {
        var setting = Settings[kind];

        using var fixture = Fixture.Create(kind, new InMemoryTaskSyncStateStore(), (_, _) => Accepted(1));
        fixture.Hold(setting.Copy(setting.Json, Morning));

        await fixture.Session.PushAsync(Cancellation);
        await fixture.Session.PushAsync(Cancellation);
        Assert.Single(fixture.Bodies);

        fixture.Hold(setting.Copy(setting.Json, Morning.AddMinutes(5)));
        await fixture.Session.PushAsync(Cancellation);

        Assert.Equal(2, fixture.Bodies.Count);
    }

    /// <summary>ADR 0036 Verification 5: a seeded copy, at the floor stamp, is pushed when
    /// the device has never sent one — so with none on the feed, it becomes the person's
    /// value everywhere.</summary>
    [Fact]
    public async Task A_seeded_iteration_copy_at_the_floor_stamp_is_pushed()
    {
        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(Iteration, state, (_, _) => Accepted(1));
        fixture.Hold(new IterationSettingsCopyDto("""{"length":"two-weeks"}""", DateTimeOffset.UnixEpoch));

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.Equal(1, Assert.IsType<Ok<TaskSyncSummary>>(result.Value).Value.Pushed);
        Assert.Equal(DateTimeOffset.UnixEpoch, Assert.Single(Pushed(Assert.Single(fixture.Bodies))).UpdatedAt);
        Assert.Equal(DateTimeOffset.UnixEpoch, state.Current.DocumentWatermark(Iteration));
    }

    /// <summary>ADR 0036 Verification 4 and ADR 0032 Verification 3: a newly paired desktop
    /// with no file pushes nothing, so it takes the other desktop's choice rather than
    /// replacing it with the default.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_device_that_never_set_the_document_sends_nothing(string kind)
    {
        using var fixture = Fixture.Create(kind, new InMemoryTaskSyncStateStore(), (_, _) => Accepted(0));

        var result = await fixture.Session.PushAsync(Cancellation);

        var summary = Assert.IsType<Ok<TaskSyncSummary>>(result.Value).Value;
        Assert.Equal(0, summary.Pushed);
        Assert.Empty(fixture.Handler.Requests);
    }

    /// <summary>A refused push — the replica holds a later change — is counted refused,
    /// and the mark still moves past it: the pull brings the later one back.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_refused_document_is_counted_refused_and_not_offered_again(string kind)
    {
        var setting = Settings[kind];
        var state = new InMemoryTaskSyncStateStore();
        var activity = new SyncActivityLog();

        using var fixture = Fixture.Create(kind, state, (_, _) => Accepted(0), activity);
        fixture.Hold(setting.Copy(setting.Json, Morning));

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.Equal(0, Assert.IsType<Ok<TaskSyncSummary>>(result.Value).Value.Pushed);
        Assert.Equal(1, Assert.IsType<Ok<TaskSyncSummary>>(result.Value).Value.Refused);
        Assert.Empty(activity.Snapshot());
        Assert.Equal(Morning, state.Current.DocumentWatermark(setting.Type));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_pushed_document_is_recorded_as_sent(string kind)
    {
        var setting = Settings[kind];
        var activity = new SyncActivityLog();

        using var fixture = Fixture.Create(kind, new InMemoryTaskSyncStateStore(), (_, _) => Accepted(1), activity);
        fixture.Hold(setting.Copy(setting.Json, Morning));

        await fixture.Session.PushAsync(Cancellation);

        var entry = Assert.Single(activity.Snapshot());
        Assert.Equal(SyncDirection.Sent, entry.Direction);
        Assert.Equal(setting.Kind, entry.Kind);
        Assert.Equal(setting.Title, entry.Title);
    }

    /// <summary>The pull hands the document to the port whole, at the stamp it was pushed
    /// with, and the cursor moves past the page.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_pulled_document_reaches_the_port_whole(string kind)
    {
        var setting = Settings[kind];
        var state = new InMemoryTaskSyncStateStore();
        var sent = setting.Copy(setting.Json, Morning);
        var page = JsonSerializer.Serialize(
            new PullTasksResponse(
                [new TaskChangeRecord(setting.ToChange(sent), Guid.NewGuid(), 10)],
                "cursor-after-the-page",
                HasMore: false),
            Web);

        using var fixture = Fixture.Create(kind, state, (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, page));

        var result = await fixture.Session.PullAsync(Cancellation);

        var summary = Assert.IsType<Ok<TaskSyncSummary>>(result.Value).Value;
        Assert.Equal(1, summary.Applied);
        Assert.Equal(sent, Assert.Single(fixture.Offered));
        Assert.Equal("cursor-after-the-page", state.Current.PullCursor);
    }

    /// <summary>Starting over for a new identity reads the new feed from the start, so
    /// the one-time replay is not owed again.</summary>
    [Fact]
    public async Task Starting_over_for_a_new_identity_keeps_the_feed_replays()
    {
        var state = new InMemoryTaskSyncStateStore(
            new TaskSyncState(Morning, "cursor-of-another-owner").WithFeedReplay(MyDayReplicaDocuments.PreferencesType));

        using var fixture = Fixture.Create(MyDay, state, (_, _) => Accepted(0));

        await fixture.Session.PushAsync(Cancellation);

        Assert.Null(state.Current.PullCursor);
        Assert.True(state.Current.HasReplayedFeedFor(MyDayReplicaDocuments.PreferencesType));
    }

    private static IReadOnlyList<TaskChange> Pushed(string body) =>
        JsonSerializer.Deserialize<PushTasksRequest>(body, Web)!.Tasks;

    /// <summary>A session over a scripted replica, with one settings document's recording
    /// port threaded through the merge and the session alike.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;
        private readonly Action<object?> _hold;

        private Fixture(
            HttpClient http,
            StubHttpMessageHandler handler,
            TaskSyncSession session,
            List<string> bodies,
            Action<object?> hold,
            IReadOnlyList<object> offered)
        {
            _http = http;
            _hold = hold;
            Handler = handler;
            Session = session;
            Bodies = bodies;
            Offered = offered;
        }

        public StubHttpMessageHandler Handler { get; }

        public TaskSyncSession Session { get; }

        public List<string> Bodies { get; }

        /// <summary>Every copy the pull offered the port, in order.</summary>
        public IReadOnlyList<object> Offered { get; }

        /// <summary>Sets the copy the port answers a push with.</summary>
        public void Hold(object? copy) => _hold(copy);

        public static Fixture Create(
            string kind,
            ITaskSyncStateStore state,
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

            var iteration = kind == Iteration ? new RecordingIterationSettingsReplication() : null;
            var myDay = kind == MyDay ? new RecordingMyDayPreferencesReplication() : null;

            var session = new TaskSyncSession(
                new TaskSyncClient(http),
                new TaskReplicaMerge(tasks, activity: activity, myDay: myDay, iteration: iteration),
                tasks,
                state,
                new InMemoryDeviceCredentialStore(new DeviceCredential(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "Workshop PC",
                    "a-registration-credential")),
                new FakeTimeProvider(Morning.AddHours(6)),
                activity: activity,
                myDay: myDay,
                iteration: iteration);

            return iteration is not null
                ? new Fixture(http, handler, session, bodies, copy => iteration.Held = (IterationSettingsCopyDto?)copy, iteration.Offered)
                : new Fixture(http, handler, session, bodies, copy => myDay!.Held = (MyDayPreferencesCopyDto?)copy, myDay!.Offered);
        }

        public void Dispose() => _http.Dispose();
    }
}
