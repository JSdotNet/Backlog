using System.Net;
using System.Text.Json;

using Backlog.Infrastructure.Sync.Notifications;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The owner desktop's notification loop and exchange against a scripted sync service: the
/// three gates and the two-minute schedule, staying idle when nothing is open, and the owner's
/// second account check (<c>reply.foreign_owner</c>). The race and the other rejections run end
/// to end against a real service in <c>NotificationExchangeTests</c>.
/// </summary>
public sealed class NotificationSyncTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Instance = Guid.Parse("7f3a0000-0000-0000-0000-0000000000aa");

    private static readonly DeviceCredential Paired = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "Workshop PC",
        "a-registration-credential");

    private static readonly Guid Phone = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // --- The loop --------------------------------------------------------------------

    [Fact]
    public void The_loop_polls_every_two_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), NotificationSyncWorker.CyclePeriod);
        Assert.NotEqual(TaskSyncWorker.FirstCycleDelay, NotificationSyncWorker.FirstCycleDelay);
        Assert.NotEqual(Backlog.Infrastructure.Sync.Annotations.AnnotationSyncWorker.FirstCycleDelay, NotificationSyncWorker.FirstCycleDelay);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void No_cycle_runs_while_a_gate_is_closed(bool syncOn, bool notificationsOn, bool paired)
    {
        using var fixture = Fixture.Create(syncOn, notificationsOn, paired);
        fixture.Replication.Open.Add(Question(Guid.CreateVersion7()));

        fixture.Clock.Advance(TimeSpan.FromMinutes(30));

        Assert.Equal(0, fixture.SessionsResolved);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task Switching_notifications_on_starts_the_loop_and_it_runs_again_two_minutes_later()
    {
        using var fixture = Fixture.Create(syncOn: true, notificationsOn: false, paired: true);

        var first = fixture.NextCycle();
        fixture.Features.Set(NotificationsFeatures.Notifications, true);
        fixture.Clock.Advance(NotificationSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, Cancellation);

        Assert.Equal(1, fixture.SessionsResolved);
        Assert.Null(fixture.Worker.LastError);

        var second = fixture.NextCycle();
        fixture.Clock.Advance(NotificationSyncWorker.CyclePeriod);
        await second.WaitAsync(Fixture.Patience, Cancellation);

        Assert.Equal(2, fixture.SessionsResolved);
    }

    /// <summary>
    /// The realtime nudge's fallback (local ADR 0038 §4): opening the socket runs a full cycle
    /// at once; while it is open the two-minute tick leaves the replies to it and lists nothing;
    /// once it closes the tick polls the replies again.
    /// </summary>
    [Fact]
    public async Task While_the_nudge_socket_is_open_the_tick_leaves_the_replies_to_it_and_polls_again_once_it_closes()
    {
        using var fixture = Fixture.Create(syncOn: true, notificationsOn: true, paired: true);
        fixture.Replication.Open.Add(Question(Guid.CreateVersion7()));

        var opened = fixture.NextCycle();
        fixture.Worker.SetNudgeOpen(true);
        await opened.WaitAsync(Fixture.Patience, Cancellation);

        Assert.True(fixture.Worker.IsNudgeOpen);
        Assert.Equal(1, fixture.ReplyListings);

        var tick = fixture.NextCycle();
        fixture.Clock.Advance(NotificationSyncWorker.FirstCycleDelay);
        await tick.WaitAsync(Fixture.Patience, Cancellation);

        Assert.False(fixture.Worker.LastSummary!.PolledReplies);
        Assert.Equal(1, fixture.ReplyListings);

        fixture.Worker.SetNudgeOpen(false);
        var fallback = fixture.NextCycle();
        fixture.Clock.Advance(NotificationSyncWorker.CyclePeriod);
        await fallback.WaitAsync(Fixture.Patience, Cancellation);

        Assert.True(fixture.Worker.LastSummary!.PolledReplies);
        Assert.Equal(2, fixture.ReplyListings);
    }

    /// <summary>A nudge that lands while a cycle is in flight is not dropped: that cycle may have
    /// listed the replies before the reply arrived, so one more full cycle runs after it.</summary>
    [Fact]
    public async Task A_nudge_during_a_cycle_runs_one_more_full_cycle_after_it()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = Fixture.Create(syncOn: true, notificationsOn: true, paired: true, before: () =>
        {
            held.TrySetResult();
            return release.Task;
        });
        fixture.Replication.Open.Add(Question(Guid.CreateVersion7()));

        fixture.Clock.Advance(NotificationSyncWorker.FirstCycleDelay);
        await held.Task.WaitAsync(Fixture.Patience, Cancellation);

        fixture.Worker.RequestSync();
        await Task.Delay(TimeSpan.FromMilliseconds(50), Cancellation);
        Assert.Equal(1, fixture.SessionsResolved);

        release.SetResult();

        var deadline = DateTime.UtcNow + Fixture.Patience;
        while (fixture.SessionsResolved < 2 || fixture.Worker.IsSyncing)
        {
            Assert.True(DateTime.UtcNow < deadline, "The owed cycle never ran.");
            await Task.Delay(10, Cancellation);
        }

        Assert.Equal(2, fixture.SessionsResolved);
    }

    /// <summary>A nudge runs a full cycle — the replies listed — even while the socket is open.</summary>
    [Fact]
    public async Task A_nudge_runs_a_full_cycle_while_the_socket_is_open()
    {
        using var fixture = Fixture.Create(syncOn: true, notificationsOn: true, paired: true);
        fixture.Replication.Open.Add(Question(Guid.CreateVersion7()));

        var opened = fixture.NextCycle();
        fixture.Worker.SetNudgeOpen(true);
        await opened.WaitAsync(Fixture.Patience, Cancellation);

        var nudged = fixture.NextCycle();
        fixture.Worker.RequestSync();
        await nudged.WaitAsync(Fixture.Patience, Cancellation);

        Assert.True(fixture.Worker.LastSummary!.PolledReplies);
        Assert.Equal(2, fixture.ReplyListings);
    }

    /// <summary>Idle otherwise: no open question and nothing new means not one request.</summary>
    [Fact]
    public async Task A_cycle_with_nothing_open_sends_no_request()
    {
        using var fixture = Fixture.Create(syncOn: true, notificationsOn: true, paired: true);

        var first = fixture.NextCycle();
        fixture.Clock.Advance(NotificationSyncWorker.FirstCycleDelay);
        await first.WaitAsync(Fixture.Patience, Cancellation);

        Assert.NotNull(fixture.Worker.LastSummary);
        Assert.False(fixture.Worker.LastSummary.PolledReplies);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task An_open_alert_is_pushed_once_and_the_replies_are_never_listed()
    {
        using var fixture = Fixture.Create(syncOn: true, notificationsOn: true, paired: true);
        fixture.Replication.Open.Add(Question(Guid.CreateVersion7()) with { Kind = NotificationKind.Alert, AnswerSpec = null });

        var first = await fixture.Session().SyncAsync(Cancellation);
        var second = await fixture.Session().SyncAsync(Cancellation);

        Assert.True(first.IsSuccess);
        Assert.Equal(1, first.Value.Pushed);
        Assert.Equal(0, second.Value.Pushed);
        Assert.All(fixture.Handler.Requests, request => Assert.Equal(HttpMethod.Post, request.Method));
        Assert.Single(fixture.Handler.Requests);
    }

    /// <summary>The cycle that pushes the last question as settled lists the replies once more,
    /// so one that reached the service before the question closed is rejected as closed rather
    /// than left waiting for the backstop expiry.</summary>
    [Fact]
    public async Task The_cycle_that_settles_the_last_question_lists_the_replies_once_more()
    {
        using var fixture = Fixture.Create(syncOn: true, notificationsOn: true, paired: true);
        var question = Question(Guid.CreateVersion7());
        fixture.Replication.Open.Add(question);
        await fixture.Session().SyncAsync(Cancellation);

        fixture.Replication.Open.Clear();
        fixture.Replication.Held[question.Id] = question with { State = NotificationState.Closed, SettledAt = Noon };
        var settling = await fixture.Session().SyncAsync(Cancellation);
        var quiet = await fixture.Session().SyncAsync(Cancellation);

        Assert.True(settling.Value.PolledReplies);
        Assert.Equal(1, settling.Value.Pushed);
        Assert.False(quiet.Value.PolledReplies);
        Assert.Equal(0, quiet.Value.Pushed);
        Assert.Contains("\"state\":\"closed\"", fixture.PushedBodies.Last(), StringComparison.Ordinal);
    }

    /// <summary>A stamp never goes back: the replica drops a change older than the one it
    /// holds, so a clock that stepped back would otherwise lose the settled state.</summary>
    [Fact]
    public async Task A_push_after_the_clock_stepped_back_is_still_stamped_later_than_the_last()
    {
        using var fixture = Fixture.Create(syncOn: true, notificationsOn: true, paired: true);
        var question = Question(Guid.CreateVersion7());
        fixture.Replication.Open.Add(question);
        await fixture.Session().SyncAsync(Cancellation);
        var first = fixture.State.Current.Tracked[question.Id].PushedAt;

        fixture.Replication.Open.Clear();
        fixture.Replication.Held[question.Id] = question with { State = NotificationState.Closed, SettledAt = Noon };
        await fixture.Session(new SteppedBackClock(Noon.AddHours(-1))).SyncAsync(Cancellation);

        Assert.True(fixture.State.Current.Tracked[question.Id].PushedAt > first);
    }

    [Fact]
    public async Task An_open_notification_is_pushed_again_a_week_later_to_keep_its_replica_copy()
    {
        // The loop stays off, so advancing the clock runs no cycle of its own beside these.
        using var fixture = Fixture.Create(syncOn: true, notificationsOn: false, paired: true);
        fixture.Replication.Open.Add(Question(Guid.CreateVersion7()) with { Kind = NotificationKind.Alert, AnswerSpec = null });

        await fixture.Session().SyncAsync(Cancellation);
        fixture.Clock.Advance(TimeSpan.FromDays(6));
        var withinTheWeek = await fixture.Session().SyncAsync(Cancellation);
        fixture.Clock.Advance(TimeSpan.FromDays(2));
        var pastIt = await fixture.Session().SyncAsync(Cancellation);

        Assert.Equal(0, withinTheWeek.Value.Pushed);
        Assert.Equal(1, pastIt.Value.Pushed);
    }

    /// <summary>Paired again under the same owner, the instance keeps the rejections it
    /// recorded and pushes everything again under its new device.</summary>
    [Fact]
    public async Task Pairing_again_under_the_same_owner_keeps_the_rejections_and_pushes_everything_again()
    {
        using var fixture = Fixture.Create(syncOn: true, notificationsOn: false, paired: true);
        var question = Question(Guid.CreateVersion7());
        fixture.Replication.Open.Add(question);
        var rejection = new NotificationReplyRejection(Guid.CreateVersion7(), Phone, NotificationReplyReasons.Invalid, "reply.invalid", "No.", Noon);
        fixture.State.Save(NotificationSyncState.Nowhere with
        {
            OwnerId = Paired.OwnerId,
            DeviceId = Paired.DeviceId,
            Tracked = new Dictionary<Guid, TrackedNotification>
            {
                [question.Id] = new(NotificationStates.Open, Dirty: false, [rejection], PushedAt: Noon),
            },
        });

        fixture.Credentials.Save(Paired with { DeviceId = Guid.CreateVersion7() });
        var synced = await fixture.Session().SyncAsync(Cancellation);

        Assert.Equal(1, synced.Value.Pushed);
        Assert.Equal(rejection, Assert.Single(fixture.State.Current.Tracked[question.Id].RejectedReplies));
        Assert.Contains(rejection.ReplyId.ToString(), fixture.PushedBodies.Last(), StringComparison.OrdinalIgnoreCase);
    }

    // --- The owner's second account check --------------------------------------------

    /// <summary>
    /// ADR 0038 §2: the sync service stores a reply only in its poster's own partition, and the
    /// owner checks again. A reply the replica stamped with another account is rejected as
    /// invalid with <c>reply.foreign_owner</c>, never handed to the owner's gate, and deleted.
    /// </summary>
    [Fact]
    public async Task A_reply_stamped_with_another_owner_is_rejected_and_never_reaches_the_gate()
    {
        var question = Question(Guid.CreateVersion7());
        var reply = new NotificationReplyRecord(
            new NotificationReplyPost(Guid.CreateVersion7(), question.Id, Instance, ["yes"], null, Noon),
            OwnerId: Guid.Parse("99999999-9999-9999-9999-999999999999"),
            FromDeviceId: Phone,
            ServerTimestamp: 1);

        using var fixture = Fixture.Create(syncOn: true, notificationsOn: true, paired: true, replies: [reply]);
        fixture.Replication.Open.Add(question);

        var synced = await fixture.Session().SyncAsync(Cancellation);

        Assert.True(synced.IsSuccess, synced.IsFailure ? synced.Error.Message : null);
        Assert.Equal(1, synced.Value.Rejected);
        Assert.Empty(fixture.Replies.Accepted);

        var rejection = Assert.Single(fixture.State.Current.Tracked[question.Id].RejectedReplies);
        Assert.Equal(reply.Reply.Id, rejection.ReplyId);
        Assert.Equal(NotificationReplyReasons.Invalid, rejection.Reason);
        Assert.Equal("reply.foreign_owner", rejection.Code);

        Assert.Contains(fixture.Handler.Requests, request =>
            request.Method == HttpMethod.Delete && request.Path == SyncRoutes.NotificationReplyFor(reply.Reply.Id));

        // The rejection travels on the notification the next push carries.
        var pushed = fixture.PushedBodies.Last();
        Assert.Contains("reply.foreign_owner", pushed, StringComparison.Ordinal);
    }

    /// <summary>A reply whose delete did not land comes back next time; one that is already
    /// the answer is only deleted, never offered to the gate a second time.</summary>
    [Fact]
    public async Task A_reply_that_is_already_the_answer_is_only_deleted()
    {
        var replyId = Guid.CreateVersion7();
        var question = Question(Guid.CreateVersion7());
        var answered = question with
        {
            State = NotificationState.Answered,
            Answer = new NotificationAnswer(question.Id, question.Source, replyId, Phone, ["yes"], null, Noon),
        };
        var reply = new NotificationReplyRecord(
            new NotificationReplyPost(replyId, question.Id, Instance, ["yes"], null, Noon),
            Paired.OwnerId,
            Phone,
            ServerTimestamp: 1);

        using var fixture = Fixture.Create(syncOn: true, notificationsOn: true, paired: true, replies: [reply]);
        fixture.Replication.Open.Add(Question(Guid.CreateVersion7()));
        fixture.Replication.Held[question.Id] = answered;

        var synced = await fixture.Session().SyncAsync(Cancellation);

        Assert.True(synced.IsSuccess);
        Assert.Equal(0, synced.Value.Accepted);
        Assert.Equal(0, synced.Value.Rejected);
        Assert.Empty(fixture.Replies.Accepted);
        Assert.Contains(fixture.Handler.Requests, request => request.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task The_owners_own_reply_is_named_by_its_installation_so_an_owner_only_question_takes_it()
    {
        var question = Question(Guid.CreateVersion7());
        var reply = new NotificationReplyRecord(
            new NotificationReplyPost(Guid.CreateVersion7(), question.Id, Instance, ["yes"], null, Noon),
            Paired.OwnerId,
            FromDeviceId: Paired.DeviceId,
            ServerTimestamp: 1);

        using var fixture = Fixture.Create(syncOn: true, notificationsOn: true, paired: true, replies: [reply]);
        fixture.Replication.Open.Add(question);

        await fixture.Session().SyncAsync(Cancellation);

        Assert.Equal(Instance, Assert.Single(fixture.Replies.Accepted).FromDeviceId);
    }

    [Theory]
    [InlineData("notification.already_answered", NotificationReplyReasons.AlreadyAnswered)]
    [InlineData("reply.closed", NotificationReplyReasons.Closed)]
    [InlineData("reply.invalid", NotificationReplyReasons.Invalid)]
    [InlineData("reply.owner_only", NotificationReplyReasons.Invalid)]
    [InlineData("reply.wrong_instance", NotificationReplyReasons.Invalid)]
    public void Every_refusal_maps_to_one_of_the_three_reasons(string code, string reason) =>
        Assert.Equal(reason, NotificationReplyReasons.For(code));

    [Fact]
    public void A_payload_round_trips_through_the_wire()
    {
        var question = Question(Guid.CreateVersion7());
        var rejection = new NotificationReplyRejection(Guid.CreateVersion7(), Phone, NotificationReplyReasons.Closed, "reply.closed", "Closed.", Noon);

        var change = NotificationPayloads.ToChange(question, [rejection], Noon);
        var read = NotificationPayloads.Read(change);

        Assert.Equal(NotificationStates.Open, change.State);
        Assert.Equal(Instance, change.OwnerInstanceId);
        Assert.NotNull(read);
        Assert.Equal(question.Title, read.Notification.Title);
        Assert.Equal(question.AnswerSpec!.Choices.Select(choice => choice.Id), read.Notification.AnswerSpec!.Choices.Select(choice => choice.Id));
        Assert.Equal(rejection, Assert.Single(read.RejectedReplies));
        Assert.Null(NotificationRecordLimits.WhyRefused(change));
    }

    // --- Doubles ---------------------------------------------------------------------

    private static NotificationSnapshot Question(Guid id) =>
        new(
            id,
            Instance,
            new NotificationSource("mcp", "ask-17"),
            new NotificationSubject("sessions", "session", "sess-91"),
            NotificationKind.Question,
            NotificationSeverity.Warning,
            "Run the migration against staging?",
            "0042_add_device_kind",
            "3 tables changed",
            new AnswerSpec([new AnswerChoice("yes", "Yes"), new AnswerChoice("no", "No")]),
            DeepLink: null,
            IsPrivate: false,
            CreatedAt: Noon,
            State: NotificationState.Open,
            SettledAt: null,
            CloseReason: null,
            Answer: null);

    private sealed class FakeReplication : INotificationReplication
    {
        public List<NotificationSnapshot> Open { get; } = [];

        public Dictionary<Guid, NotificationSnapshot> Held { get; } = [];

        public Task<IReadOnlyList<NotificationSnapshot>> ListOpenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NotificationSnapshot>>([.. Open]);

        public Task<NotificationSnapshot?> GetAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Held.TryGetValue(notificationId, out var held) ? held : Open.FirstOrDefault(snapshot => snapshot.Id == notificationId));
    }

    private sealed class FakeReplies : INotificationReplies
    {
        public List<NotificationReply> Accepted { get; } = [];

        public Task<Result<NotificationAnswer>> AcceptReplyAsync(NotificationReply reply, CancellationToken cancellationToken = default)
        {
            Accepted.Add(reply);
            return Task.FromResult(Result.Success(new NotificationAnswer(
                reply.NotificationId, new NotificationSource("mcp", "ask-17"), reply.Id, reply.FromDeviceId, reply.ChoiceIds, reply.Text, Noon)));
        }

        public Task<Result> AcknowledgeAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class InMemoryNotificationSyncStateStore : INotificationSyncStateStore
    {
        public NotificationSyncState Current { get; private set; } = NotificationSyncState.Nowhere;

        public string StorePath => "in memory";

        public void Save(NotificationSyncState state) => Current = state;
    }

    private sealed class KeyedFeatures(bool syncOn, bool notificationsOn) : IAppFeatureSettings
    {
        private readonly Dictionary<string, bool> _on = new(StringComparer.Ordinal)
        {
            [SyncFeatures.Sync] = syncOn,
            [NotificationsFeatures.Notifications] = notificationsOn,
        };

        public event Action? Changed;

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "in memory";

        public bool IsEnabled(string key) => _on.TryGetValue(key, out var on) && on;

        public string? SetEnabled(string key, bool enabled)
        {
            Set(key, enabled);
            return null;
        }

        public void Set(string key, bool enabled)
        {
            _on[key] = enabled;
            Changed?.Invoke();
        }
    }

    private sealed class FixedIdentity : IDeviceIdentitySource
    {
        public DeviceIdentity Current { get; } = new(Instance, "WORKSHOP-PC");
    }

    private sealed class Fixture : IDisposable
    {
        public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        private readonly HttpClient _http;
        private int _sessionsResolved;

        private Fixture(HttpClient http, StubHttpMessageHandler handler, KeyedFeatures features, InMemoryDeviceCredentialStore credentials, FakeTimeProvider clock)
        {
            _http = http;
            Handler = handler;
            Features = features;
            Credentials = credentials;
            Clock = clock;
            Worker = new NotificationSyncWorker(new SessionProvider(() =>
            {
                Interlocked.Increment(ref _sessionsResolved);
                return Session();
            }), features, credentials, clock);
        }

        public NotificationSyncWorker Worker { get; }

        public StubHttpMessageHandler Handler { get; }

        public KeyedFeatures Features { get; }

        public InMemoryDeviceCredentialStore Credentials { get; }

        public FakeTimeProvider Clock { get; }

        public FakeReplication Replication { get; } = new();

        public FakeReplies Replies { get; } = new();

        public InMemoryNotificationSyncStateStore State { get; } = new();

        public List<string> PushedBodies { get; } = [];

        public int SessionsResolved => Volatile.Read(ref _sessionsResolved);

        /// <summary>How many times the replies addressed to this instance were listed.</summary>
        public int ReplyListings => Handler.Requests.Count(request => request.Method == HttpMethod.Get);

        public static Fixture Create(bool syncOn, bool notificationsOn, bool paired, IReadOnlyList<NotificationReplyRecord>? replies = null, Func<Task>? before = null)
        {
            Fixture? fixture = null;
            var listed = false;

            var handler = new StubHttpMessageHandler((request, _) =>
            {
                if (request.Method == HttpMethod.Post)
                {
                    fixture!.PushedBodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                    var count = JsonDocument.Parse(fixture.PushedBodies[^1]).RootElement.GetProperty("notifications").GetArrayLength();
                    return StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"accepted":{{count}}}""");
                }

                if (request.Method == HttpMethod.Delete) return new HttpResponseMessage(HttpStatusCode.NoContent);

                // The replies are listed once; after that they have been deleted.
                var waiting = listed ? [] : replies ?? [];
                listed = true;
                return StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    JsonSerializer.Serialize(new PullNotificationRepliesResponse(waiting), JsonSerializerOptions.Web));
            }, before);

            var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };
            var credentials = paired ? new InMemoryDeviceCredentialStore(Paired) : new InMemoryDeviceCredentialStore();

            fixture = new Fixture(http, handler, new KeyedFeatures(syncOn, notificationsOn), credentials, new FakeTimeProvider(Noon));
            return fixture;
        }

        public NotificationSyncSession Session(TimeProvider? clock = null) =>
            new(new NotificationSyncClient(_http), Replication, Replies, State, Credentials, new FixedIdentity(), clock ?? Clock);

        public Task NextCycle()
        {
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnChanged()
            {
                if (Worker.IsSyncing) return;

                Worker.Changed -= OnChanged;
                finished.TrySetResult();
            }

            Worker.Changed += OnChanged;

            return finished.Task;
        }

        public void Dispose()
        {
            Worker.Dispose();
            _http.Dispose();
        }
    }

    /// <summary>A clock that reads earlier than the one the last push was stamped by.</summary>
    private sealed class SteppedBackClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SessionProvider(Func<NotificationSyncSession?> session) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(NotificationSyncSession) ? session() : null;
    }
}
