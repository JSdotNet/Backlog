using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.Extensions;
using Backlog.Modules.Notifications.Services;
using Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using static Backlog.Modules.Notifications.UnitTests.Drafts;

namespace Backlog.Modules.Notifications.UnitTests;

/// <summary>
/// The owner's ports end to end over an in-memory store: publish, close, close by subject, accept a
/// reply, read the answer — and the answer's delivery, its replay to a handler that registers
/// later, and the startup subject probes.
/// </summary>
public sealed class NotificationsTests
{
    private readonly InMemoryNotificationRepository _store = new();
    private readonly FakeTimeProvider _time = new(Now);

    private OwnerNotifications Owner_(params INotificationAnswerHandler[] handlers) =>
        new(_store, new FixedDevice(Owner), Delivery(handlers), _time);

    private AnswerDelivery Delivery(params INotificationAnswerHandler[] handlers) => new(handlers, _store, _time);

    private NotificationsStartup Startup(OwnerNotifications owner, INotificationAnswerHandler[] handlers, params INotificationSubjectProbe[] probes) =>
        new(_store, owner, new FixedDevice(Owner), Delivery(handlers), probes);

    private static NotificationReply ReplyTo(Guid id, string choice, Guid? from = null) =>
        new(Guid.NewGuid(), id, Owner, from ?? Phone, [choice], null, Now);

    // --- Publishing ------------------------------------------------------------------

    [Fact]
    public async Task Publishing_stores_an_open_notification_owned_by_this_device()
    {
        var id = (await Owner_().PublishAsync(Question(), TestContext.Current.CancellationToken)).Value;

        var stored = await _store.GetAsync(id, TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(Owner, stored.OwnerInstanceId);
        Assert.Equal(NotificationState.Open, stored.State);
    }

    [Fact]
    public async Task A_refused_draft_stores_nothing()
    {
        var result = await Owner_().PublishAsync(Question() with { Subject = null }, TestContext.Current.CancellationToken);

        Assert.Equal("notification.question_incomplete", result.Error.Code);
        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public async Task Publishing_is_refused_while_the_flag_is_off_on_this_device()
    {
        var owner = new OwnerNotifications(_store, new FixedDevice(Owner), Delivery(), _time, new Flags(enabled: false));

        var result = await owner.PublishAsync(Alert(), TestContext.Current.CancellationToken);

        Assert.Equal("notifications.not_available", result.Error.Code);
        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public async Task Publishing_goes_ahead_while_the_flag_is_on()
    {
        var owner = new OwnerNotifications(_store, new FixedDevice(Owner), Delivery(), _time, new Flags(enabled: true));

        Assert.True((await owner.PublishAsync(Alert(), TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Fact]
    public async Task A_draft_that_names_its_id_is_stored_under_it()
    {
        var named = Guid.NewGuid();

        var id = (await Owner_().PublishAsync(Question() with { Id = named }, TestContext.Current.CancellationToken)).Value;

        Assert.Equal(named, id);
        Assert.NotNull(await _store.GetAsync(named, TestContext.Current.CancellationToken));
    }

    /// <summary>A source that derives its id from what it asks about publishes the same thing on
    /// every pull: the second publish changes nothing, the first one's state included.</summary>
    [Fact]
    public async Task Publishing_a_named_id_again_changes_nothing_whatever_state_it_reached()
    {
        var owner = Owner_();
        var named = Guid.NewGuid();
        await owner.PublishAsync(Question() with { Id = named }, TestContext.Current.CancellationToken);
        await owner.CloseAsync(named, "Answered elsewhere", TestContext.Current.CancellationToken);
        var saves = _store.Saves;

        var again = await owner.PublishAsync(Question() with { Id = named, Title = "Asked again" }, TestContext.Current.CancellationToken);

        Assert.Equal(named, again.Value);
        Assert.Equal(saves, _store.Saves);
        var stored = await _store.GetAsync(named, TestContext.Current.CancellationToken);
        Assert.Equal(NotificationState.Closed, stored!.State);
        Assert.NotEqual("Asked again", stored.Title);
    }

    [Fact]
    public async Task An_empty_named_id_is_refused()
    {
        var result = await Owner_().PublishAsync(Question() with { Id = Guid.Empty }, TestContext.Current.CancellationToken);

        Assert.Equal("notification.id_required", result.Error.Code);
        Assert.Equal(0, _store.Saves);
    }

    // --- Closing ----------------------------------------------------------------------

    [Fact]
    public async Task The_publisher_closes_a_question_and_a_later_reply_is_rejected_as_closed()
    {
        var owner = Owner_();
        var id = (await owner.PublishAsync(Question(), TestContext.Current.CancellationToken)).Value;

        Assert.True((await owner.CloseAsync(id, "No longer needed", TestContext.Current.CancellationToken)).IsSuccess);

        var reply = await owner.AcceptReplyAsync(ReplyTo(id, "yes"), TestContext.Current.CancellationToken);
        Assert.Equal("reply.closed", reply.Error.Code);
    }

    [Fact]
    public async Task Closing_an_unknown_notification_is_refused()
    {
        var result = await Owner_().CloseAsync(Guid.NewGuid(), null, TestContext.Current.CancellationToken);

        Assert.Equal("notification.not_found", result.Error.Code);
    }

    [Fact]
    public async Task Closing_for_a_subject_closes_every_open_notification_about_it_and_nothing_else()
    {
        var owner = Owner_();
        var first = (await owner.PublishAsync(Question(), TestContext.Current.CancellationToken)).Value;
        var second = (await owner.PublishAsync(Alert(Session), TestContext.Current.CancellationToken)).Value;
        var other = (await owner.PublishAsync(Question(subject: new("tasks", "task", "T-123")), TestContext.Current.CancellationToken)).Value;

        var closed = await owner.CloseForSubjectAsync(Session, "Session archived", TestContext.Current.CancellationToken);

        Assert.Equal(2, closed.Value);
        Assert.Equal(NotificationState.Closed, (await _store.GetAsync(first, TestContext.Current.CancellationToken))!.State);
        Assert.Equal("Session archived", (await _store.GetAsync(second, TestContext.Current.CancellationToken))!.CloseReason);
        Assert.Equal(NotificationState.Open, (await _store.GetAsync(other, TestContext.Current.CancellationToken))!.State);
    }

    [Fact]
    public async Task Closing_for_a_subject_leaves_another_owners_notifications_alone()
    {
        var foreign = DomainModels.Notification.Publish(Guid.NewGuid(), OtherDesktop, Question(), Now).Value;
        _store.Put(foreign);

        var closed = await Owner_().CloseForSubjectAsync(Session, "Session archived", TestContext.Current.CancellationToken);

        Assert.Equal(0, closed.Value);
        Assert.Equal(NotificationState.Open, (await _store.GetAsync(foreign.Id, TestContext.Current.CancellationToken))!.State);
    }

    // --- Answering --------------------------------------------------------------------

    [Fact]
    public async Task An_accepted_answer_is_stored_and_read_back()
    {
        var owner = Owner_();
        var id = (await owner.PublishAsync(Question(), TestContext.Current.CancellationToken)).Value;

        Assert.Null((await owner.GetAnswerAsync(id, TestContext.Current.CancellationToken)).Value);

        await owner.AcceptReplyAsync(ReplyTo(id, "yes"), TestContext.Current.CancellationToken);

        var answer = (await owner.GetAnswerAsync(id, TestContext.Current.CancellationToken)).Value;
        Assert.NotNull(answer);
        Assert.Equal(["yes"], answer.ChoiceIds);
        Assert.Equal(Now, answer.AnsweredAt);
    }

    [Fact]
    public async Task Reading_the_answer_of_an_unknown_notification_is_refused()
    {
        var result = await Owner_().GetAnswerAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal("notification.not_found", result.Error.Code);
    }

    [Fact]
    public async Task Two_replies_racing_settle_on_exactly_one_answer()
    {
        var owner = Owner_();
        var id = (await owner.PublishAsync(Question(), TestContext.Current.CancellationToken)).Value;

        var replies = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(n => Task.Run(
                () => owner.AcceptReplyAsync(ReplyTo(id, n % 2 == 0 ? "yes" : "no"), TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken)));

        Assert.Single(replies, reply => reply.IsSuccess);
        Assert.All(replies.Where(reply => reply.IsFailure), reply => Assert.Equal("notification.already_answered", reply.Error.Code));
    }

    [Fact]
    public async Task Acknowledging_an_alert_settles_it()
    {
        var owner = Owner_();
        var id = (await owner.PublishAsync(Alert(), TestContext.Current.CancellationToken)).Value;

        Assert.True((await owner.AcknowledgeAsync(id, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(NotificationState.Acknowledged, (await _store.GetAsync(id, TestContext.Current.CancellationToken))!.State);
    }

    // --- Delivery and replay ----------------------------------------------------------

    [Fact]
    public async Task The_accepted_answer_is_handed_to_the_handler_registered_for_its_source()
    {
        var sessions = new RecordingAnswerHandler("sessions");
        var devbook = new RecordingAnswerHandler("devbook");
        var owner = Owner_(sessions, devbook);
        var id = (await owner.PublishAsync(Question(), TestContext.Current.CancellationToken)).Value;

        await owner.AcceptReplyAsync(ReplyTo(id, "yes"), TestContext.Current.CancellationToken);

        var received = Assert.Single(sessions.Received);
        Assert.Equal(id, received.NotificationId);
        Assert.Empty(devbook.Received);
        Assert.False((await _store.GetAsync(id, TestContext.Current.CancellationToken))!.AwaitsDelivery);
    }

    [Fact]
    public async Task An_answer_with_no_handler_yet_is_replayed_to_a_handler_that_registers_later()
    {
        var id = (await Owner_().PublishAsync(Question(), TestContext.Current.CancellationToken)).Value;
        await Owner_().AcceptReplyAsync(ReplyTo(id, "yes"), TestContext.Current.CancellationToken);

        Assert.True((await _store.GetAsync(id, TestContext.Current.CancellationToken))!.AwaitsDelivery);

        // The next start composes the handler.
        var later = new RecordingAnswerHandler();
        await Startup(Owner_(later), [later]).RunAsync(TestContext.Current.CancellationToken);

        var replayed = Assert.Single(later.Received);
        Assert.Equal(id, replayed.NotificationId);
        Assert.Equal(["yes"], replayed.ChoiceIds);
    }

    [Fact]
    public async Task The_replay_hands_an_answer_over_once_and_never_again()
    {
        var id = (await Owner_().PublishAsync(Question(), TestContext.Current.CancellationToken)).Value;
        await Owner_().AcceptReplyAsync(ReplyTo(id, "no"), TestContext.Current.CancellationToken);

        var handler = new RecordingAnswerHandler();
        var startup = Startup(Owner_(handler), [handler]);

        await startup.RunAsync(TestContext.Current.CancellationToken);
        await startup.RunAsync(TestContext.Current.CancellationToken);
        await Startup(Owner_(handler), [handler]).RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(handler.Received);
    }

    [Fact]
    public async Task An_answer_a_handler_threw_on_stays_stored_and_is_replayed_on_the_next_start()
    {
        var handler = new RecordingAnswerHandler { Throws = true };
        var owner = Owner_(handler);
        var id = (await owner.PublishAsync(Question(), TestContext.Current.CancellationToken)).Value;

        var accepted = await owner.AcceptReplyAsync(ReplyTo(id, "yes"), TestContext.Current.CancellationToken);

        Assert.True(accepted.IsSuccess);
        Assert.True((await _store.GetAsync(id, TestContext.Current.CancellationToken))!.AwaitsDelivery);

        handler.Throws = false;
        await Startup(Owner_(handler), [handler]).RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(handler.Received);
        Assert.False((await _store.GetAsync(id, TestContext.Current.CancellationToken))!.AwaitsDelivery);
    }

    [Fact]
    public async Task A_redelivery_after_a_lost_mark_hands_the_handler_the_same_answer_for_the_same_notification()
    {
        var handler = new RecordingAnswerHandler();
        var owner = Owner_(handler);
        var id = (await owner.PublishAsync(Question(), TestContext.Current.CancellationToken)).Value;
        await owner.AcceptReplyAsync(ReplyTo(id, "yes"), TestContext.Current.CancellationToken);

        // A crash between the handler returning and the mark being stored: the row still says the
        // answer waits, so the next start hands it over again — the same answer, under the same id.
        var stored = (await _store.GetAsync(id, TestContext.Current.CancellationToken))!;
        _store.Put(DomainModels.Notification.Restore(
            stored.Id, stored.OwnerInstanceId, stored.Source, stored.Subject, stored.Kind, stored.Severity, stored.Title,
            stored.Summary, stored.BodyMarkdown, stored.AnswerSpec, stored.DeepLink, stored.IsPrivate, stored.CreatedAt,
            stored.State, stored.SettledAt, stored.CloseReason, stored.Answer, answerDeliveredAt: null));

        await Startup(Owner_(handler), [handler]).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Received.Count);
        Assert.Equal(handler.Received[0], handler.Received[1]);
        Assert.Equal(id, handler.Received[1].NotificationId);
    }

    [Fact]
    public async Task The_startup_replay_and_a_reply_accepted_meanwhile_hand_each_answer_over_once()
    {
        // Answers stored while no handler was registered.
        var waiting = new List<Guid>();
        for (var n = 0; n < 10; n++)
        {
            var stale = (await Owner_().PublishAsync(Question(), TestContext.Current.CancellationToken)).Value;
            await Owner_().AcceptReplyAsync(ReplyTo(stale, "no"), TestContext.Current.CancellationToken);
            waiting.Add(stale);
        }

        var handler = new ConcurrentRecordingHandler();
        var delivery = Delivery(handler);
        var owner = new OwnerNotifications(_store, new FixedDevice(Owner), delivery, _time);
        var startup = new NotificationsStartup(_store, owner, new FixedDevice(Owner), delivery, []);
        var fresh = (await owner.PublishAsync(Question(), TestContext.Current.CancellationToken)).Value;

        await Task.WhenAll(
            startup.RunAsync(TestContext.Current.CancellationToken),
            startup.RunAsync(TestContext.Current.CancellationToken),
            owner.AcceptReplyAsync(ReplyTo(fresh, "yes"), TestContext.Current.CancellationToken));

        Assert.Equal(11, handler.Received.Count);
        Assert.Equal([.. waiting.Append(fresh).Order()], handler.Received.Select(answer => answer.NotificationId).Order());
    }

    // --- Startup subject probes ---------------------------------------------------------

    [Fact]
    public async Task At_startup_a_notification_whose_subject_ended_is_closed()
    {
        var owner = Owner_();
        var ended = (await owner.PublishAsync(Question(), TestContext.Current.CancellationToken)).Value;
        var live = (await owner.PublishAsync(Question(subject: new("sessions", "session", "sess-12")), TestContext.Current.CancellationToken)).Value;

        var probe = new SetProbe("sessions", "sess-91");
        await Startup(owner, [], probe).RunAsync(TestContext.Current.CancellationToken);

        var closed = (await _store.GetAsync(ended, TestContext.Current.CancellationToken))!;
        Assert.Equal(NotificationState.Closed, closed.State);
        Assert.Equal(NotificationsStartup.SubjectEndedReason, closed.CloseReason);
        Assert.Equal(NotificationState.Open, (await _store.GetAsync(live, TestContext.Current.CancellationToken))!.State);
    }

    [Fact]
    public async Task At_startup_a_subject_is_asked_about_once_and_only_by_its_own_contexts_probe()
    {
        var owner = Owner_();
        await owner.PublishAsync(Question(), TestContext.Current.CancellationToken);
        await owner.PublishAsync(Alert(Session), TestContext.Current.CancellationToken);
        await owner.PublishAsync(Question(subject: new("tasks", "task", "T-123")), TestContext.Current.CancellationToken);

        var sessions = new SetProbe("sessions");
        var tasks = new SetProbe("tasks");
        await Startup(owner, [], sessions, tasks).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal([Session], sessions.Asked);
        Assert.Equal([new NotificationSubject("tasks", "task", "T-123")], tasks.Asked);
    }

    [Fact]
    public async Task A_subject_with_no_probe_or_a_failing_probe_stays_open()
    {
        var owner = Owner_();
        var unprobed = (await owner.PublishAsync(Question(subject: new("devbook", "remark", "r-1")), TestContext.Current.CancellationToken)).Value;
        var failing = (await owner.PublishAsync(Question(), TestContext.Current.CancellationToken)).Value;

        await Startup(owner, [], new SetProbe("sessions", "sess-91") { Throws = true }).RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(NotificationState.Open, (await _store.GetAsync(unprobed, TestContext.Current.CancellationToken))!.State);
        Assert.Equal(NotificationState.Open, (await _store.GetAsync(failing, TestContext.Current.CancellationToken))!.State);
    }

    [Fact]
    public async Task Start_runs_the_pass_once()
    {
        var owner = Owner_();
        await owner.PublishAsync(Question(), TestContext.Current.CancellationToken);
        var probe = new SetProbe("sessions");
        var startup = Startup(owner, [], probe);

        var first = startup.Start();
        var second = startup.Start();
        await first;

        Assert.Same(first, second);
        Assert.Single(probe.Asked);
    }

    // --- Status --------------------------------------------------------------------------

    [Fact]
    public async Task The_status_reads_back_the_source_subject_state_and_close_reason()
    {
        var owner = Owner_();
        var statuses = new OwnerNotificationStatuses(_store);
        var ct = TestContext.Current.CancellationToken;
        var draft = Question();
        var id = (await owner.PublishAsync(draft, ct)).Value;

        var open = (await statuses.GetStatusAsync(id, ct)).Value;
        Assert.Equal((id, draft.Source, draft.Subject, NotificationKind.Question, NotificationState.Open), (open.Id, open.Source, open.Subject, open.Kind, open.State));
        Assert.Null(open.Answer);

        await owner.CloseAsync(id, "No longer needed", ct);

        var closed = (await statuses.GetStatusAsync(id, ct)).Value;
        Assert.Equal((NotificationState.Closed, "No longer needed"), (closed.State, closed.CloseReason));
    }

    [Fact]
    public async Task The_status_carries_the_accepted_answer()
    {
        var owner = Owner_();
        var ct = TestContext.Current.CancellationToken;
        var id = (await owner.PublishAsync(Question(), ct)).Value;
        await owner.AcceptReplyAsync(ReplyTo(id, "yes"), ct);

        var status = (await new OwnerNotificationStatuses(_store).GetStatusAsync(id, ct)).Value;

        Assert.Equal(NotificationState.Answered, status.State);
        Assert.Equal(["yes"], status.Answer!.ChoiceIds);
    }

    [Fact]
    public async Task The_status_of_an_unknown_notification_is_refused()
    {
        var result = await new OwnerNotificationStatuses(_store).GetStatusAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal("notification.not_found", result.Error.Code);
    }

    // --- Viewer --------------------------------------------------------------------------

    [Fact]
    public async Task A_viewer_never_publishes_or_changes_a_notification()
    {
        var viewer = new ViewerNotifications();
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal("notifications.viewer_only", (await viewer.PublishAsync(Alert(), ct)).Error.Code);
        Assert.Equal("notifications.viewer_only", (await viewer.CloseAsync(Guid.NewGuid(), null, ct)).Error.Code);
        Assert.Equal("notifications.viewer_only", (await viewer.CloseForSubjectAsync(Session, "gone", ct)).Error.Code);
        Assert.Equal("notifications.viewer_only", (await viewer.GetAnswerAsync(Guid.NewGuid(), ct)).Error.Code);
        Assert.Equal("notifications.viewer_only", (await viewer.AcceptReplyAsync(ReplyTo(Guid.NewGuid(), "yes"), ct)).Error.Code);
        Assert.Equal("notifications.viewer_only", (await viewer.AcknowledgeAsync(Guid.NewGuid(), ct)).Error.Code);
        Assert.Equal("notifications.viewer_only", (await new ViewerNotificationStatuses().GetStatusAsync(Guid.NewGuid(), ct)).Error.Code);
    }

    // --- Registration ----------------------------------------------------------------------

    [Fact]
    public async Task The_owner_registration_resolves_one_instance_behind_both_ports_and_hands_answers_to_registered_handlers()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INotificationRepository>(_store);
        services.AddSingleton<IDeviceIdentitySource>(new FixedDevice(Owner));
        services.AddNotificationsModule();
        services.AddNotificationAnswerHandler<StaticHandler>();
        services.AddNotificationAnswerHandler<StaticHandler>();
        services.AddNotificationSubjectProbe<NeverLiveProbe>();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var notifications = provider.GetRequiredService<INotifications>();
        Assert.Same(notifications, provider.GetRequiredService<INotificationReplies>());
        Assert.Single(provider.GetServices<INotificationAnswerHandler>());
        Assert.IsType<OwnerNotificationStatuses>(provider.GetRequiredService<INotificationStatuses>());

        var id = (await notifications.PublishAsync(Question(), TestContext.Current.CancellationToken)).Value;
        await provider.GetRequiredService<INotificationReplies>().AcceptReplyAsync(ReplyTo(id, "yes"), TestContext.Current.CancellationToken);
        Assert.Contains(id, StaticHandler.Seen);

        var other = (await notifications.PublishAsync(Question(subject: new("never", "thing", "1")), TestContext.Current.CancellationToken)).Value;
        await provider.GetRequiredService<NotificationsStartup>().Start();
        Assert.Equal(NotificationState.Closed, (await _store.GetAsync(other, TestContext.Current.CancellationToken))!.State);
    }

    /// <summary>A source whose handler refused an answer for a reason it can see end — a sign-in
    /// that lapsed — has it handed over again now, through the same delivery the startup uses.</summary>
    [Fact]
    public async Task The_owner_registration_replays_a_waiting_answer_on_request()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INotificationRepository>(_store);
        services.AddSingleton<IDeviceIdentitySource>(new FixedDevice(Owner));
        services.AddNotificationsModule();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var notifications = provider.GetRequiredService<INotifications>();
        var id = (await notifications.PublishAsync(Question(), TestContext.Current.CancellationToken)).Value;
        await provider.GetRequiredService<INotificationReplies>().AcceptReplyAsync(ReplyTo(id, "yes"), TestContext.Current.CancellationToken);
        Assert.True((await _store.GetAsync(id, TestContext.Current.CancellationToken))!.AwaitsDelivery);

        var replay = provider.GetRequiredService<INotificationAnswerReplay>();
        Assert.Equal(0, await replay.ReplayAsync(TestContext.Current.CancellationToken));
        Assert.Same(provider.GetRequiredService<AnswerDelivery>(), replay);
    }

    [Fact]
    public void The_viewer_registration_needs_no_store_and_no_device_identity()
    {
        var services = new ServiceCollection();
        services.AddNotificationsModule(NotificationsRole.Viewer);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Assert.IsType<ViewerNotifications>(provider.GetRequiredService<INotifications>());
        Assert.IsType<ViewerNotificationStatuses>(provider.GetRequiredService<INotificationStatuses>());
        Assert.Null(provider.GetService<NotificationsStartup>());
    }

    private sealed class Flags(bool enabled) : IAppFeatureSettings
    {
        public event Action? Changed { add { } remove { } }

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => string.Empty;

        public bool IsEnabled(string key) => enabled;

        public string? SetEnabled(string key, bool enabled) => null;
    }

    private sealed class StaticHandler : INotificationAnswerHandler
    {
        public static readonly System.Collections.Concurrent.ConcurrentBag<Guid> Seen = [];

        public string Source => "sessions";

        public Task HandleAnswerAsync(NotificationAnswer answer, CancellationToken cancellationToken = default)
        {
            Seen.Add(answer.NotificationId);
            return Task.CompletedTask;
        }
    }

    private sealed class NeverLiveProbe : INotificationSubjectProbe
    {
        public string Context => "never";

        public Task<bool> IsLiveAsync(NotificationSubject subject, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
