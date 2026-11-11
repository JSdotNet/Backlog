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
    private readonly Ledger _ledger = new();
    private bool _enabled = true;

    public SessionAlertPublisherTests()
    {
        _loops = new InMemoryLoopRegister(_time);
        _requests = new InMemorySessionRequestStore(_time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SessionAlertPublisher Publisher() =>
        new(_schedules, _loops, _sessions, () => _ledger, () => _ledger, () => _enabled, _requests, clock: _time) { Zone = TimeZoneInfo.Utc };

    private Task ReportAsync(ScheduledRunEvent @event, string session, string? verdict = null, IReadOnlyList<string>? needsYou = null) =>
        _schedules.RecordAsync(
            new ScheduledRunReport("weekly-update", "Weekly update", "JSdotNet/Backlog", "30 5 * * 0", session, @event, verdict, needsYou),
            _time.GetUtcNow(),
            Ct);

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
        Assert.Equal(new NotificationOrigin("Backlog", "Schedules"), alert.Origin);
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
    private sealed class HeldSessions : IAgentSessionSource
    {
        private readonly List<AgentSession> _held = [];

        public void Add(string id, string? desktopId = null) =>
            _held.Add(new AgentSession(id, AgentSessionKind.Claude, "env", "This PC", "📡 SESSION DISPATCHER", @"D:\repo", "owner/repo", "main", Due, Due, AgentSessionState.Running, null, AgentSessionOrigin.Local)
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

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);
    }

    /// <summary>
    /// The owner's port and centre over one list, keeping the rules the sweep leans on: a held id
    /// publishes nothing, closing a subject closes its open notifications, and the centre lists
    /// open and settled alike.
    /// </summary>
    private sealed class Ledger : INotifications, INotificationCentre
    {
        private readonly List<NotificationView> _views = [];

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
            Task.FromResult<IReadOnlyList<NotificationView>>([.. _views]);

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
