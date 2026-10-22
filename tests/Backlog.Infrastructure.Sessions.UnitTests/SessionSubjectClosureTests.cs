using Backlog.Infrastructure.Sessions;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// The Sessions context closing the notifications about its subjects when they end, and
/// answering the startup probe about them (<c>.devbook/domain/notifications/domain.md#subject</c>):
/// a session archived or deleted, a delivery run finished or abandoned, a pull request
/// merged or closed.
/// </summary>
public sealed class SessionSubjectClosureTests : IDisposable
{
    private const string Worktree = @"D:\Repos\Backlog\.claude\worktrees\subject-closure-7c44d9";
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] Stages = ["Update Base", "Implement", "Summary"];

    private readonly string _home = Path.Combine(Path.GetTempPath(), "backlog-subject-closure-tests", Guid.NewGuid().ToString("n"));
    private readonly RecordingNotifications _notifications = new RecordingNotifications();

    [Theory]
    [InlineData("done")]
    [InlineData("blocked")]
    [InlineData("parked")]
    [InlineData("cancelled")]
    public async Task Finishing_a_run_closes_the_notifications_about_it_whatever_it_finished_on(string status)
    {
        var surface = Surface();
        var started = await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, cancellationToken: TestContext.Current.CancellationToken);

        await surface.FinishRunAsync(Worktree, started.RunId, status, cancellationToken: TestContext.Current.CancellationToken);

        var closed = Assert.Single(_notifications.Closed);
        Assert.Equal(new NotificationSubject("sessions", "run", started.RunId), closed.Subject);
        Assert.Equal("Run finished", closed.Reason);
    }

    [Fact]
    public async Task A_run_still_finishes_when_closing_its_notifications_fails()
    {
        _notifications.Fails = new InvalidOperationException("The notifications store is locked.");
        var surface = Surface();
        var started = await surface.StartRunAsync(Worktree, "flow-code", "Run", Stages, cancellationToken: TestContext.Current.CancellationToken);

        await surface.FinishRunAsync(Worktree, started.RunId, "done", cancellationToken: TestContext.Current.CancellationToken);

        var run = await surface.GetRunAsync(Worktree, started.RunId, TestContext.Current.CancellationToken);
        Assert.Equal("done", run!.Status);
    }

    [Fact]
    public async Task A_session_seen_open_and_then_archived_closes_its_notifications_once()
    {
        var source = new SwitchingSource(Session("sess-91", archived: false));
        var watched = new SessionEndWatchingSource(source, () => _notifications);

        await watched.GetSessionsAsync(TestContext.Current.CancellationToken);
        Assert.Empty(_notifications.Closed);

        source.Answer = [Session("sess-91", archived: true)];
        await watched.GetSessionsAsync(TestContext.Current.CancellationToken);
        await watched.GetSessionsAsync(TestContext.Current.CancellationToken);

        var closed = Assert.Single(_notifications.Closed);
        Assert.Equal(new NotificationSubject("sessions", "session", "sess-91"), closed.Subject);
        Assert.Equal("Session archived", closed.Reason);
    }

    [Fact]
    public async Task A_session_whose_desktop_record_is_gone_in_two_readings_ends_like_an_archived_one()
    {
        var source = new SwitchingSource(Session("sess-92", archived: false));
        var watched = new SessionEndWatchingSource(source, () => _notifications);
        await watched.GetSessionsAsync(TestContext.Current.CancellationToken);

        source.Answer = [Session("sess-92", archived: null)];
        await watched.GetSessionsAsync(TestContext.Current.CancellationToken);
        Assert.Empty(_notifications.Closed);

        await watched.GetSessionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("sess-92", Assert.Single(_notifications.Closed).Subject.Id);
    }

    [Fact]
    public async Task A_desktop_record_missing_for_one_reading_or_unread_closes_nothing()
    {
        // A record being rewritten reads as none for a moment; a reading that could not
        // open a folder, or an answer from the session record rather than the files, says
        // nothing about the desktop app at all.
        var source = new SwitchingSource(Session("blip", archived: false), Session("unread", archived: false), Session("recorded", archived: false));
        var watched = new SessionEndWatchingSource(source, () => _notifications);
        await watched.GetSessionsAsync(TestContext.Current.CancellationToken);

        source.Answer = [Session("blip", archived: null), Session("unread", archived: false), Session("recorded", archived: null) with { Origin = AgentSessionOrigin.Recorded }];
        await watched.GetSessionsAsync(TestContext.Current.CancellationToken);
        source.Answer = [Session("blip", archived: false), Session("unread", archived: null), Session("recorded", archived: null) with { Origin = AgentSessionOrigin.Recorded }];
        source.Unreadable = ["claude-desktop"];
        await watched.GetSessionsAsync(TestContext.Current.CancellationToken);
        await watched.GetSessionsAsync(TestContext.Current.CancellationToken);
        source.Unreadable = [];
        source.Answer = [Session("blip", archived: null), Session("unread", archived: false), Session("recorded", archived: null) with { Origin = AgentSessionOrigin.Recorded }];
        await watched.GetSessionsAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_notifications.Closed);
    }

    [Fact]
    public async Task A_session_never_seen_open_or_missing_from_a_reading_closes_nothing()
    {
        // Archived before this process read it: the startup probe's to catch. Missing:
        // a capped reading leaves sessions out on purpose.
        var source = new SwitchingSource(Session("old", archived: true), Session("cli", archived: null), Session("open", archived: false));
        var watched = new SessionEndWatchingSource(source, () => _notifications);
        await watched.GetSessionsAsync(TestContext.Current.CancellationToken);

        source.Answer = [];
        await watched.GetSessionsAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_notifications.Closed);
    }

    [Fact]
    public async Task A_session_reading_still_answers_when_closing_fails_or_no_notifications_are_composed()
    {
        _notifications.Fails = new InvalidOperationException("locked");
        var source = new SwitchingSource(Session("sess-93", archived: false));
        var failing = new SessionEndWatchingSource(source, () => _notifications);
        var absent = new SessionEndWatchingSource(source, () => null);
        await failing.GetSessionsAsync(TestContext.Current.CancellationToken);
        await absent.GetSessionsAsync(TestContext.Current.CancellationToken);

        source.Answer = [Session("sess-93", archived: true)];

        Assert.Single((await failing.GetSessionsAsync(TestContext.Current.CancellationToken)).Sessions);
        Assert.Single((await absent.GetSessionsAsync(TestContext.Current.CancellationToken)).Sessions);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(null, true)]
    [InlineData(true, false)]
    public async Task The_probe_calls_a_session_ended_only_once_archived(bool? archived, bool live)
    {
        var probe = Probe(sessions: new SwitchingSource(Session("sess-91", archived)));

        Assert.Equal(live, await probe.IsLiveAsync(new NotificationSubject("sessions", "session", "sess-91"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_probe_calls_a_session_no_source_knows_ended_unless_a_folder_was_unreadable()
    {
        var gone = Probe(sessions: new SwitchingSource());
        var unreadable = Probe(sessions: new SwitchingSource { Unreadable = ["claude"] });
        var subject = new NotificationSubject("sessions", "session", "sess-404");

        Assert.False(await gone.IsLiveAsync(subject, TestContext.Current.CancellationToken));
        Assert.True(await unreadable.IsLiveAsync(subject, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_probe_reads_sessions_with_no_cap_and_once_per_pass()
    {
        var source = new SwitchingSource(Session("a", archived: false));
        var probe = Probe(sessions: source);

        await probe.IsLiveAsync(new NotificationSubject("sessions", "session", "a"), TestContext.Current.CancellationToken);
        await probe.IsLiveAsync(new NotificationSubject("sessions", "session", "b"), TestContext.Current.CancellationToken);

        var query = Assert.Single(source.Queries);
        Assert.NotNull(query.Horizon);
    }

    [Fact]
    public async Task The_probe_calls_a_run_live_only_while_it_is_in_progress()
    {
        var surface = Surface();
        var running = await surface.StartRunAsync(Worktree, "flow-code", "Running", Stages, cancellationToken: TestContext.Current.CancellationToken);
        var finished = await surface.StartRunAsync(Worktree, "flow-spec", "Finished", Stages, cancellationToken: TestContext.Current.CancellationToken);
        await surface.FinishRunAsync(Worktree, finished.RunId, "done", cancellationToken: TestContext.Current.CancellationToken);
        var probe = Probe(runs: new LocalDeliveryRunSource(_home, "machine", "Workshop PC"));

        Assert.True(await probe.IsLiveAsync(new NotificationSubject("sessions", "run", running.RunId), TestContext.Current.CancellationToken));
        Assert.False(await probe.IsLiveAsync(new NotificationSubject("sessions", "run", finished.RunId), TestContext.Current.CancellationToken));
        // No run file holds it: abandoned.
        Assert.False(await probe.IsLiveAsync(new NotificationSubject("sessions", "run", "run-gone"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_run_missing_from_one_reading_is_read_again_before_it_is_called_abandoned()
    {
        var surface = Surface();
        var running = await surface.StartRunAsync(Worktree, "flow-code", "Running", Stages, cancellationToken: TestContext.Current.CancellationToken);
        var runs = new FirstReadingEmpty(new LocalDeliveryRunSource(_home, "machine", "Workshop PC"));
        var probe = Probe(runs: runs);

        Assert.True(await probe.IsLiveAsync(new NotificationSubject("sessions", "run", running.RunId), TestContext.Current.CancellationToken));
        Assert.Equal(2, runs.Readings);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, true)]
    public async Task The_probe_asks_the_pull_request_port_and_reads_no_answer_as_live(bool? open, bool live)
    {
        var states = new StubPullRequestStates(open);
        var probe = Probe(pullRequests: states);

        Assert.Equal(live, await probe.IsLiveAsync(
            new NotificationSubject("sessions", "pull-request", SessionNotificationSubjects.PullRequestId("JSdotNet/Backlog", 1480)),
            TestContext.Current.CancellationToken));
        Assert.Equal(("jsdotnet/backlog", 1480), states.Asked);
    }

    [Fact]
    public async Task Without_a_pull_request_port_or_for_a_type_it_does_not_name_the_probe_answers_live()
    {
        var probe = Probe();

        Assert.True(await probe.IsLiveAsync(new NotificationSubject("sessions", "pull-request", "jsdotnet/backlog#1"), TestContext.Current.CancellationToken));
        Assert.True(await probe.IsLiveAsync(new NotificationSubject("sessions", "worktree", "x"), TestContext.Current.CancellationToken));
        Assert.Equal("sessions", probe.Context);
    }

    [Fact]
    public void A_pull_request_id_round_trips_and_one_repository_spelled_twice_is_one_subject()
    {
        Assert.Equal("jsdotnet/backlog#12", SessionNotificationSubjects.PullRequestId(" JSdotNet/Backlog ", 12));
        Assert.Equal(("jsdotnet/backlog", 12), SessionNotificationSubjects.ParsePullRequestId("jsdotnet/backlog#12"));
        Assert.Null(SessionNotificationSubjects.ParsePullRequestId("backlog#12"));
        Assert.Null(SessionNotificationSubjects.ParsePullRequestId("jsdotnet/backlog#x"));
    }

    private LocalDeliverySurfaceLifecycle Surface() =>
        new(_home, "machine", "Workshop PC", notifications: () => _notifications);

    private SessionSubjectProbe Probe(
        IAgentSessionSource? sessions = null,
        IDeliveryRunSource? runs = null,
        ISessionPullRequestStates? pullRequests = null) =>
        new(
            sessions ?? new SwitchingSource(),
            runs ?? new LocalDeliveryRunSource(_home, "machine", "Workshop PC"),
            pullRequests,
            new FakeTimeProvider(Noon));

    private static AgentSession Session(string id, bool? archived) =>
        new(
            id,
            AgentSessionKind.Claude,
            "11111111-1111-1111-1111-111111111111",
            "Workshop PC",
            "A session",
            string.Empty,
            null,
            null,
            null,
            Noon,
            AgentSessionState.Running,
            null,
            AgentSessionOrigin.Local)
        {
            Archived = archived
        };

    private sealed class SwitchingSource(params AgentSession[] sessions) : IAgentSessionSource
    {
        public IReadOnlyList<AgentSession> Answer { get; set; } = sessions;

        public IReadOnlyList<string> Unreadable { get; set; } = [];

        public List<AgentSessionQuery> Queries { get; } = [];

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            GetSessionsAsync(AgentSessionQuery.Newest, cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(new AgentSessionCatalog(Answer, Unreadable, Answer.Count));
        }
    }

    /// <summary>A run reading that skipped every file the first time — the reader drops a
    /// file it cannot open that moment — and reads normally after.</summary>
    private sealed class FirstReadingEmpty(IDeliveryRunSource inner) : IDeliveryRunSource
    {
        public int Readings { get; private set; }

        public Task<DeliveryRunCatalog> GetRunsAsync(CancellationToken cancellationToken = default) =>
            ++Readings == 1 ? Task.FromResult(DeliveryRunCatalog.Empty) : inner.GetRunsAsync(cancellationToken);
    }

    private sealed class StubPullRequestStates(bool? open) : ISessionPullRequestStates
    {
        public (string Repository, int Number)? Asked { get; private set; }

        public Task<bool?> IsOpenAsync(string repository, int number, CancellationToken cancellationToken = default)
        {
            Asked = (repository, number);
            return Task.FromResult(open);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }
}
