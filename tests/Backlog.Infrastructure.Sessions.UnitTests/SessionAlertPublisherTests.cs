using Backlog.Infrastructure.Sessions;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel.Results;
using Backlog.Tests;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// The schedule and loop alerts of local ADR 0051 §5 as they reach the notification centre: each
/// cause publishes once however many sweeps see it, and closes once it clears — the run archived,
/// a missed schedule's run started, the loop checked in again — while the others stay open.
/// </summary>
public sealed class SessionAlertPublisherTests
{
    private static readonly DateTimeOffset Due = new(2026, 11, 11, 5, 30, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Due);
    private readonly InMemoryScheduleRegister _schedules = new();
    private readonly InMemoryLoopRegister _loops;
    private readonly InMemorySessionRequestStore _requests;
    private readonly HeldSessions _sessions = new();
    private readonly HeldWaitingQuestions _questions = new();
    private readonly Ledger _ledger = new();
    private bool _enabled = true;

    public SessionAlertPublisherTests()
    {
        _loops = new InMemoryLoopRegister(_time);
        _requests = new InMemorySessionRequestStore(_time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SessionAlertPublisher Publisher() =>
        new(_schedules, _loops, _sessions, () => _ledger, () => _ledger, () => _enabled, _requests, _questions, clock: _time, store: () => _ledger) { Zone = TimeZoneInfo.Utc };

    private Task ReportAsync(ScheduledRunEvent @event, string session, string? verdict = null, IReadOnlyList<string>? needsYou = null, string schedule = "weekly-update") =>
        _schedules.RecordAsync(
            new ScheduledRunReport(schedule, schedule == "weekly-update" ? "Weekly update" : "Nightly sweep", "JSdotNet/Backlog", "30 5 * * 0", session, @event, verdict, needsYou),
            _time.GetUtcNow(),
            Ct);

    private async Task FailAsync(string session, string schedule = "weekly-update")
    {
        await ReportAsync(ScheduledRunEvent.Started, session, schedule: schedule);
        await ReportAsync(ScheduledRunEvent.Failed, session, "gh: authentication required", schedule: schedule);
        _time.Advance(TimeSpan.FromMinutes(5));
    }

    private Task CheckInAsync(string session = "manager-1") =>
        _loops.CheckInAsync(LoopSessions.CheckIn(session, LoopKind.Dispatcher, LoopActivity.Listening, 30, LoopHeldWork.None, _time.GetUtcNow()), Ct);

    private Task NeedsYouAsync(string session = "run-1") =>
        ReportAsync(ScheduledRunEvent.Finished, session, "needs-you", ["Review two pull requests"]);

    [Fact]
    public async Task A_run_that_needs_you_is_published_once_however_many_sweeps_see_it()
    {
        await ReportAsync(ScheduledRunEvent.Started, "run-1");
        await NeedsYouAsync();
        var publisher = Publisher();

        Assert.Equal((1, 0), await publisher.SweepAsync(Ct));
        Assert.Equal((0, 0), await publisher.SweepAsync(Ct));
        Assert.Equal((0, 0), await Publisher().SweepAsync(Ct));

        var alert = Assert.Single(_ledger.Views);
        Assert.Equal(NotificationKind.Alert, alert.Kind);
        Assert.Equal("Weekly update needs you", alert.Title);
        // The schedule's repository leads the place, so the centre files the alert under it.
        Assert.Equal(new NotificationOrigin("Backlog", "JSdotNet/Backlog · Schedules"), alert.Origin);
        Assert.Equal(new NotificationSubject("sessions", "alert", "needs-you:schedule:weekly-update:run-1"), alert.Subject);
        Assert.Equal(
            [
                new NotificationAction("Open run", "backlog://window/sessions?session=run-1"),
                new NotificationAction("Open Schedules", "backlog://window/sessions?page=schedules&schedule=weekly-update")
            ],
            alert.Actions!);
    }

    [Fact]
    public async Task An_acknowledged_alert_is_not_raised_again_while_its_cause_holds()
    {
        await NeedsYouAsync();
        await Publisher().SweepAsync(Ct);
        _ledger.Acknowledge(Assert.Single(_ledger.Views).Id);

        Assert.Equal((0, 0), await Publisher().SweepAsync(Ct));
        Assert.Single(_ledger.Views);
    }

    [Fact]
    public async Task Archiving_the_runs_session_closes_its_alert()
    {
        await NeedsYouAsync();
        var publisher = Publisher();
        await publisher.SweepAsync(Ct);

        _sessions.Archive("run-1");

        Assert.Equal((0, 1), await publisher.SweepAsync(Ct));
        var alert = Assert.Single(_ledger.Views);
        Assert.Equal(NotificationState.Closed, alert.State);
        Assert.Equal("Run archived", alert.CloseReason);
    }

    [Fact]
    public async Task A_run_that_finishes_review_ready_closes_every_earlier_failed_alert_of_its_schedule_naming_itself()
    {
        await FailAsync("run-1");
        await FailAsync("run-2");
        var publisher = Publisher();
        Assert.Equal((2, 0), await publisher.SweepAsync(Ct));

        await ReportAsync(ScheduledRunEvent.Started, "run-3");
        await ReportAsync(ScheduledRunEvent.Finished, "run-3", "review-ready");

        Assert.Equal((0, 2), await publisher.SweepAsync(Ct));
        Assert.All(_ledger.Views, view =>
        {
            Assert.Equal(NotificationState.Closed, view.State);
            Assert.Equal("Handled: the Wed 11 Nov 05:40 run finished Review ready", view.CloseReason);
        });
        Assert.Equal((0, 0), await Publisher().SweepAsync(Ct));
    }

    [Fact]
    public async Task A_recovery_leaves_another_schedules_failed_alert_and_other_kinds_open()
    {
        await NeedsYouAsync("run-0");
        await FailAsync("run-1");
        await FailAsync("sweep-1", schedule: "nightly-sweep");
        var publisher = Publisher();
        Assert.Equal((3, 0), await publisher.SweepAsync(Ct));

        await ReportAsync(ScheduledRunEvent.Finished, "run-2", "review-ready");

        Assert.Equal((0, 1), await publisher.SweepAsync(Ct));
        Assert.Equal(
            ["Nightly sweep failed", "Weekly update needs you"],
            _ledger.Views.Where(view => view.IsOpen).Select(view => view.Title).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_later_run_that_fails_again_leaves_the_earlier_failed_alert_open_and_raises_its_own()
    {
        await FailAsync("run-1");
        var publisher = Publisher();
        Assert.Equal((1, 0), await publisher.SweepAsync(Ct));

        await FailAsync("run-2");

        Assert.Equal((1, 0), await publisher.SweepAsync(Ct));
        Assert.Equal(
            ["failed:schedule:weekly-update:run-1", "failed:schedule:weekly-update:run-2"],
            _ledger.Views.Where(view => view.IsOpen).Select(view => view.Subject!.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Archiving_a_failed_run_still_closes_its_alert_as_archived()
    {
        await FailAsync("run-1");
        var publisher = Publisher();
        await publisher.SweepAsync(Ct);

        _sessions.Archive("run-1");
        await ReportAsync(ScheduledRunEvent.Finished, "run-2", "review-ready");

        Assert.Equal((0, 1), await publisher.SweepAsync(Ct));
        Assert.Equal("Run archived", Assert.Single(_ledger.Views).CloseReason);
    }

    [Fact]
    public async Task A_held_schedules_next_skip_raises_no_second_alert()
    {
        await NeedsYouAsync();
        await ReportAsync(ScheduledRunEvent.Skipped, "run-2");
        var publisher = Publisher();
        await publisher.SweepAsync(Ct);
        var raised = _ledger.Views.Count;

        _time.Advance(TimeSpan.FromDays(7));
        await ReportAsync(ScheduledRunEvent.Skipped, "run-3");

        Assert.Equal((0, 0), await publisher.SweepAsync(Ct));
        Assert.Equal(raised, _ledger.Views.Count);
        Assert.Contains(_ledger.Views, view => view.Title == "Weekly update is held" && view.IsOpen);
    }

    [Fact]
    public async Task A_missed_schedule_alerts_once_and_closes_when_its_next_run_starts()
    {
        await ReportAsync(ScheduledRunEvent.Started, "run-1");
        await ReportAsync(ScheduledRunEvent.Finished, "run-1", "review-ready");
        var publisher = Publisher();

        Assert.Equal((0, 0), await publisher.SweepAsync(Ct));

        _time.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(31));
        Assert.Equal((1, 0), await publisher.SweepAsync(Ct));
        Assert.Equal("Weekly update missed its run", Assert.Single(_ledger.Views).Title);

        await ReportAsync(ScheduledRunEvent.Started, "run-2");

        Assert.Equal((0, 1), await publisher.SweepAsync(Ct));
        Assert.Equal("A run started", Assert.Single(_ledger.Views).CloseReason);
    }

    [Fact]
    public async Task A_stalled_loop_alerts_once_and_closes_when_it_checks_in_again()
    {
        _sessions.Add("manager-1");
        await CheckInAsync();
        var publisher = Publisher();

        _time.Advance(TimeSpan.FromMinutes(41));
        Assert.Equal((1, 0), await publisher.SweepAsync(Ct));
        Assert.Equal((0, 0), await publisher.SweepAsync(Ct));

        var stalled = Assert.Single(_ledger.Views);
        Assert.Equal("Session dispatcher stalled", stalled.Title);
        Assert.Equal(new NotificationOrigin("Backlog", "Sessions"), stalled.Origin);
        Assert.Equal([new NotificationAction("Open Sessions", "backlog://window/sessions?session=manager-1")], stalled.Actions!);

        await CheckInAsync();

        Assert.Equal((0, 1), await publisher.SweepAsync(Ct));
        Assert.Equal("Checked in again, or its process ended", Assert.Single(_ledger.Views).CloseReason);
    }

    [Fact]
    public async Task A_dispatcher_archived_while_requests_wait_raises_one_alert_that_counts_them()
    {
        _sessions.Add("manager-1");
        await CheckInAsync();
        for (var i = 0; i < 3; i++)
        {
            _requests.Add(new SessionRequest(Guid.NewGuid(), SessionRequestKind.Entry, Guid.NewGuid(), null, "JSdotNet/Backlog", @"D:\Repos\Backlog", SessionRequestState.Requested, null, null, null, Due));
        }

        _sessions.Archive("manager-1");

        Assert.Equal((1, 0), await Publisher().SweepAsync(Ct));
        var alert = Assert.Single(_ledger.Views);
        Assert.Equal("Session dispatcher stopped", alert.Title);
        Assert.StartsWith("3 session requests wait for it.", alert.Summary, StringComparison.Ordinal);
        Assert.Equal(NotificationSeverity.Critical, alert.Severity);
    }

    [Fact]
    public async Task A_dispatcher_archived_with_nothing_waiting_raises_nothing()
    {
        _sessions.Add("manager-1");
        await CheckInAsync();
        _sessions.Archive("manager-1");

        Assert.Equal((0, 0), await Publisher().SweepAsync(Ct));
        Assert.Empty(_ledger.Views);
    }

    /// <summary>A paused queue hands out no request, so what it holds waits for the person,
    /// not for a dispatcher: no Not-running alert counts it.</summary>
    [Fact]
    public async Task A_dispatcher_archived_while_the_queue_is_paused_raises_nothing()
    {
        _sessions.Add("manager-1");
        await CheckInAsync();
        _requests.Add(new SessionRequest(Guid.NewGuid(), SessionRequestKind.Entry, Guid.NewGuid(), null, "JSdotNet/Backlog", @"D:\Repos\Backlog", SessionRequestState.Requested, null, null, null, Due));
        await _requests.SetPausedAsync(true, Ct);
        _sessions.Archive("manager-1");

        Assert.Equal((0, 0), await Publisher().SweepAsync(Ct));
        Assert.Empty(_ledger.Views);
    }

    /// <summary>Deliveries flow while the queue is paused, so a pending one still waits for
    /// the dispatcher — and it alone is counted.</summary>
    [Fact]
    public async Task A_dispatcher_archived_while_the_queue_is_paused_with_a_pending_delivery_counts_only_the_delivery()
    {
        _sessions.Add("manager-1");
        await CheckInAsync();
        _requests.Add(new SessionRequest(Guid.NewGuid(), SessionRequestKind.Entry, Guid.NewGuid(), null, "JSdotNet/Backlog", @"D:\Repos\Backlog", SessionRequestState.Requested, null, null, null, Due));
        await _requests.SetPausedAsync(true, Ct);
        _questions.AddPendingDelivery("asking-session", "Which branch?", "main", Due);
        _sessions.Archive("manager-1");

        Assert.Equal((1, 0), await Publisher().SweepAsync(Ct));
        Assert.StartsWith("1 answer delivery waits for it.", Assert.Single(_ledger.Views).Summary, StringComparison.Ordinal);
    }

    // --- the session reading's window (local ADR 0051 §5) ----------------------------------

    private Task FinishCleanAsync(string session, string schedule = "weekly-update") =>
        ReportAsync(ScheduledRunEvent.Finished, session, "review-ready", schedule: schedule);

    private IEnumerable<NotificationView> RunAlerts() =>
        _ledger.Views.Where(view => view.Subject!.Id.StartsWith("needs-you:", StringComparison.Ordinal));

    /// <summary>A run that finished Review ready 90 days ago can raise nothing, whatever its
    /// archiving: the reading reaches back only to the latest run, less a day.</summary>
    [Fact]
    public async Task The_session_reading_reaches_back_only_as_far_as_an_alert_can_still_change()
    {
        await FinishCleanAsync("run-old");
        _time.Advance(TimeSpan.FromDays(89));
        await FinishCleanAsync("run-new");
        _time.Advance(TimeSpan.FromDays(1));

        await Publisher().SweepAsync(Ct);

        Assert.Equal(_time.GetUtcNow() - TimeSpan.FromDays(2), Assert.Single(_sessions.Horizons));
    }

    /// <summary>A Needs-you run seen archived once leaves the window: the next sweep reads only
    /// as far back as the latest run, and the run is not raised again though the reading no
    /// longer holds its session.</summary>
    [Fact]
    public async Task An_archived_needs_you_run_drops_out_of_the_reading_once_seen()
    {
        _sessions.HonoursHorizon = true;
        await NeedsYouAsync("run-1");
        _sessions.Add("run-1", lastActive: Due);
        _sessions.Archive("run-1");
        _time.Advance(TimeSpan.FromDays(30));
        await FinishCleanAsync("run-2");
        var publisher = Publisher();

        await publisher.SweepAsync(Ct);
        await publisher.SweepAsync(Ct);

        Assert.Equal(2, _sessions.Horizons.Count);
        Assert.True(_sessions.Horizons[0] < Due, "The first sweep must read as far back as the unseen archived run.");
        Assert.True(_sessions.Horizons[1] > _time.GetUtcNow() - TimeSpan.FromDays(2), $"The second sweep reached back to {_sessions.Horizons[1]}.");
        Assert.Empty(RunAlerts());
    }

    /// <summary>Only the loop of each kind that checked in last can raise an alert, so an
    /// older dispatcher's row does not widen the reading.</summary>
    [Fact]
    public async Task A_superseded_loop_does_not_widen_the_reading()
    {
        await CheckInAsync("manager-old");
        _time.Advance(TimeSpan.FromDays(60));
        await CheckInAsync("manager-new");

        await Publisher().SweepAsync(Ct);

        var horizon = Assert.Single(_sessions.Horizons);
        Assert.True(horizon > _time.GetUtcNow() - TimeSpan.FromDays(2), $"The reading reached back to {horizon}.");
    }

    /// <summary>A Needs-you run nobody archived keeps its alert open however old it is: the
    /// reading reaches back to it for as long as it is not seen archived.</summary>
    [Fact]
    public async Task An_unarchived_needs_you_run_keeps_its_alert_open_however_old()
    {
        _sessions.HonoursHorizon = true;
        await NeedsYouAsync("run-1");
        _sessions.Add("run-1", lastActive: Due);
        var publisher = Publisher();
        await publisher.SweepAsync(Ct);
        _time.Advance(TimeSpan.FromDays(200));
        await FinishCleanAsync("run-2");

        await publisher.SweepAsync(Ct);
        await Publisher().SweepAsync(Ct);

        Assert.True(Assert.Single(RunAlerts()).IsOpen);
        Assert.All(_sessions.Horizons, horizon => Assert.True(horizon < Due));
    }

    [Fact]
    public async Task Nothing_is_raised_while_the_sessions_flag_is_off()
    {
        await NeedsYouAsync();
        _enabled = false;

        Assert.Equal((0, 0), await Publisher().SweepAsync(Ct));
        Assert.Empty(_ledger.Views);
    }

    [Fact]
    public async Task Clearing_one_cause_leaves_the_others_open()
    {
        await NeedsYouAsync("run-1");
        await ReportAsync(ScheduledRunEvent.Started, "run-2");
        await ReportAsync(ScheduledRunEvent.Failed, "run-2", "gh: authentication required");
        var publisher = Publisher();
        Assert.Equal((2, 0), await publisher.SweepAsync(Ct));

        _sessions.Archive("run-1");

        Assert.Equal((0, 1), await publisher.SweepAsync(Ct));
        Assert.Equal("Weekly update failed", Assert.Single(_ledger.Views, view => view.IsOpen).Title);
    }

    [Fact]
    public async Task Turning_the_sessions_area_off_closes_the_alerts_still_open()
    {
        await NeedsYouAsync();
        var publisher = Publisher();
        await publisher.SweepAsync(Ct);

        _enabled = false;

        Assert.Equal((0, 1), await publisher.SweepAsync(Ct));
        Assert.Equal(SessionAlertPublisher.TurnedOffReason, Assert.Single(_ledger.Views).CloseReason);
    }

    [Fact]
    public async Task Turning_the_sessions_area_back_on_raises_again_an_alert_whose_cause_still_holds()
    {
        await NeedsYouAsync();
        var publisher = Publisher();
        await publisher.SweepAsync(Ct);
        _enabled = false;
        await publisher.SweepAsync(Ct);

        _enabled = true;

        Assert.Equal((1, 0), await publisher.SweepAsync(Ct));
        Assert.Equal((0, 0), await Publisher().SweepAsync(Ct));
        var raised = Assert.Single(_ledger.Views, view => view.IsOpen);
        Assert.Equal("Weekly update needs you", raised.Title);
        Assert.Equal(SessionAlertPublisher.IdFor("needs-you:schedule:weekly-update:run-1", 2), raised.Id);
        Assert.Equal(2, _ledger.Views.Count);
    }

    [Fact]
    public async Task Each_switch_off_and_on_raises_the_alert_again_under_the_next_generation()
    {
        await NeedsYouAsync();
        var publisher = Publisher();
        await publisher.SweepAsync(Ct);

        for (var cycle = 0; cycle < 2; cycle++)
        {
            _enabled = false;
            await publisher.SweepAsync(Ct);
            _enabled = true;
            Assert.Equal((1, 0), await publisher.SweepAsync(Ct));
        }

        Assert.Equal(SessionAlertPublisher.IdFor("needs-you:schedule:weekly-update:run-1", 3), Assert.Single(_ledger.Views, view => view.IsOpen).Id);
        Assert.Equal(3, _ledger.Views.Count);
    }

    [Fact]
    public async Task A_switch_off_closing_the_centre_no_longer_lists_is_found_in_the_store()
    {
        await NeedsYouAsync();
        var publisher = Publisher();
        await publisher.SweepAsync(Ct);
        _enabled = false;
        await publisher.SweepAsync(Ct);
        var first = Assert.Single(_ledger.Views).Id;

        _ledger.Unlist(first);
        _enabled = true;

        Assert.Equal((1, 0), await publisher.SweepAsync(Ct));
        Assert.Equal(SessionAlertPublisher.IdFor("needs-you:schedule:weekly-update:run-1", 2), Assert.Single(_ledger.Views, view => view.IsOpen).Id);
    }

    [Fact]
    public async Task An_acknowledged_alert_stays_acknowledged_across_a_switch_off_and_on()
    {
        await NeedsYouAsync();
        var publisher = Publisher();
        await publisher.SweepAsync(Ct);
        _ledger.Acknowledge(Assert.Single(_ledger.Views).Id);

        _enabled = false;
        await publisher.SweepAsync(Ct);
        _enabled = true;

        Assert.Equal((0, 0), await publisher.SweepAsync(Ct));
        Assert.Equal(NotificationState.Acknowledged, Assert.Single(_ledger.Views).State);
    }

    [Fact]
    public async Task An_alert_closed_by_its_cause_clearing_is_not_raised_again_by_turning_the_area_off_and_on()
    {
        await NeedsYouAsync();
        var publisher = Publisher();
        await publisher.SweepAsync(Ct);
        _sessions.Archive("run-1");
        await publisher.SweepAsync(Ct);

        _enabled = false;
        await publisher.SweepAsync(Ct);
        _enabled = true;

        Assert.Equal((0, 0), await publisher.SweepAsync(Ct));
        Assert.Equal("Run archived", Assert.Single(_ledger.Views).CloseReason);
    }

    [Fact]
    public async Task Open_run_names_the_session_by_the_id_the_sessions_list_knows_it_by()
    {
        _sessions.Add("3f2c-transcript-id", desktopId: "local_run-1");
        await NeedsYouAsync("local_run-1");

        await Publisher().SweepAsync(Ct);

        Assert.Equal("backlog://window/sessions?session=3f2c-transcript-id", Assert.Single(_ledger.Views).Actions![0].Link);
    }

    [Fact]
    public void The_id_is_the_same_for_the_same_key_and_another_for_another()
    {
        Assert.Equal(SessionAlertPublisher.IdFor("stalled:loop:a:1"), SessionAlertPublisher.IdFor("stalled:loop:a:1"));
        Assert.NotEqual(SessionAlertPublisher.IdFor("stalled:loop:a:1"), SessionAlertPublisher.IdFor("stalled:loop:a:2"));
    }

    /// <summary>The sessions the reading answers: added live, and archived on demand.</summary>
    /// <summary>The session list. It records every horizon it is asked for; with
    /// <see cref="HonoursHorizon"/> it also answers only the sessions active since it, as the
    /// readers over the transcripts do.</summary>
    // Differs from StubSessionSource: adds and archives sessions live, and can answer only those since a horizon.
    private sealed class HeldSessions : IAgentSessionSource
    {
        private readonly List<AgentSession> _held = [];

        public List<DateTimeOffset?> Horizons { get; } = [];

        public bool HonoursHorizon { get; set; }

        public void Add(string id, string? desktopId = null, DateTimeOffset? lastActive = null) =>
            _held.Add(new AgentSession(id, AgentSessionKind.Claude, "env", "This PC", "📡 SESSION DISPATCHER", @"D:\repo", "owner/repo", "main", Due, lastActive ?? Due, AgentSessionState.Running, null, AgentSessionOrigin.Local)
            {
                DesktopSessionId = desktopId
            });

        public void Archive(string id)
        {
            var index = _held.FindIndex(session => session.Id == id);

            if (index < 0)
            {
                Add(id);
                index = _held.Count - 1;
            }

            _held[index] = _held[index] with { Archived = true, State = AgentSessionState.Finished };
        }

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionCatalog([.. _held], [], _held.Count));

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default)
        {
            Horizons.Add(query.Horizon);

            if (!HonoursHorizon || query.Horizon is not { } horizon) return GetSessionsAsync(cancellationToken);

            List<AgentSession> since = [.. _held.Where(session => session.LastActivityAt >= horizon)];

            return Task.FromResult(new AgentSessionCatalog(since, [], since.Count));
        }
    }

    /// <summary>
    /// The owner's port and centre over one list, keeping the rules the sweep leans on: a held id
    /// publishes nothing, closing a subject closes its open notifications, and the centre lists
    /// open and settled alike — but for one <see cref="Unlist"/> has let go of, as the centre lets
    /// go of what settled more than a week ago, which the store still holds.
    /// </summary>
    // Differs from RecordingNotifications: is also the centre and replication, closing a subject's notifications.
    private sealed class Ledger : INotifications, INotificationCentre, INotificationReplication
    {
        private readonly List<NotificationView> _views = [];
        private readonly HashSet<Guid> _unlisted = [];

        public void Unlist(Guid id) => _unlisted.Add(id);

        public Task<IReadOnlyList<NotificationSnapshot>> ListOpenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NotificationSnapshot>>([.. _views.Where(view => view.IsOpen).Select(Snapshot)]);

        public Task<NotificationSnapshot?> GetAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_views.Where(view => view.Id == notificationId).Select(Snapshot).FirstOrDefault());

        private static NotificationSnapshot Snapshot(NotificationView view) =>
            new(view.Id, view.OwnerInstanceId, view.Source, view.Subject, view.Kind, view.Severity, view.Title, view.Summary, view.BodyMarkdown,
                view.AnswerSpec, view.DeepLink, view.IsPrivate, view.CreatedAt, view.State, view.SettledAt, view.CloseReason, view.Answer, view.Origin);

        public IReadOnlyList<NotificationView> Views => [.. _views];

        public DeviceIdentity ThisDevice { get; } = new(Guid.Parse("7f3a0000-0000-0000-0000-000000000001"), "WORKSTATION-01");

        public event EventHandler<NotificationChanged>? Changed;

        public Task<Result<Guid>> PublishAsync(NotificationDraft draft, CancellationToken cancellationToken = default)
        {
            var id = draft.Id ?? Guid.NewGuid();

            if (_views.All(view => view.Id != id))
            {
                var view = new NotificationView(
                    id, ThisDevice.Id, draft.Source, draft.Subject, draft.Kind, draft.Severity, draft.Title, draft.Summary, draft.BodyMarkdown,
                    draft.Answer, draft.DeepLink, draft.IsPrivate, Due, NotificationState.Open, null, null, null, draft.Origin, draft.Actions);
                _views.Add(view);
                Changed?.Invoke(this, new NotificationChanged(view, true));
            }

            return Task.FromResult(Result.Success(id));
        }

        public Task<Result<int>> CloseForSubjectAsync(NotificationSubject subject, string reason, CancellationToken cancellationToken = default)
        {
            var closed = 0;

            for (var i = 0; i < _views.Count; i++)
            {
                if (_views[i].IsOpen && _views[i].Subject == subject)
                {
                    _views[i] = _views[i] with { State = NotificationState.Closed, CloseReason = reason, SettledAt = Due };
                    closed++;
                }
            }

            return Task.FromResult(Result.Success(closed));
        }

        public void Acknowledge(Guid id)
        {
            var index = _views.FindIndex(view => view.Id == id);
            _views[index] = _views[index] with { State = NotificationState.Acknowledged, SettledAt = Due };
        }

        public Task<IReadOnlyList<NotificationView>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NotificationView>>([.. _views.Where(view => !_unlisted.Contains(view.Id))]);

        public Task<Result> CloseAsync(Guid notificationId, string? reason = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<NotificationAnswer?>> GetAnswerAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<NotificationAnswer>> AnswerAsync(Guid notificationId, IReadOnlyList<string> choiceIds, string? text, string? note = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result> AcknowledgeAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
