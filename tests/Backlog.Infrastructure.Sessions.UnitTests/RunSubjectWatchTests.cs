using Backlog.Infrastructure.Sessions;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// The open Personal Validation questions closing themselves while the app runs, once the
/// run they wait on ended — its pull request merged or closed, or every session that drove it
/// archived — rather than only at the next start
/// (<c>.devbook/domain/notifications/requirements.md#requirement-a-notification-closes-when-its-subject-ends</c>).
/// </summary>
public sealed class RunSubjectWatchTests : IDisposable
{
    private const string Worktree = @"D:\Repos\Backlog\.claude\worktrees\run-subject-watch-1c2d3e";
    private const string PullRequest = "https://github.com/JSdotNet/Backlog/pull/1652";
    private static readonly DateTimeOffset Noon = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] Stages = ["Implement", "Personal Validation", "Create Pull Request"];

    private readonly string _home = Path.Combine(Path.GetTempPath(), "backlog-run-subject-watch-tests", Guid.NewGuid().ToString("n"));
    private readonly RecordingNotifications _notifications = new();
    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly SwitchablePullRequests _pulls = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_question_whose_run_pull_request_merged_closes_on_the_next_tick_once()
    {
        var run = await RunWithPullRequestAsync();
        var open = new OpenQuestions(_notifications, Question(run));
        using var watch = Watch(open);

        _clock.Advance(RunSubjectWatch.Interval);
        await watch.Idle;
        Assert.Empty(_notifications.Closed);

        _pulls.State = SessionPullRequestState.Merged;
        _clock.Advance(RunSubjectWatch.Interval);
        await watch.Idle;
        _clock.Advance(RunSubjectWatch.Interval);
        await watch.Idle;

        var closed = Assert.Single(_notifications.Closed);
        Assert.Equal(new NotificationSubject("sessions", "run", run), closed.Subject);
        Assert.Equal("Pull request merged", closed.Reason);
    }

    [Fact]
    public async Task It_looks_no_more_often_than_every_five_minutes_and_reads_nothing_while_no_validation_question_is_open()
    {
        var run = await RunWithPullRequestAsync();
        _pulls.State = SessionPullRequestState.Closed;
        var open = new OpenQuestions(
            _notifications,
            // An alert about the run, and a relayed session question: neither is the gate's.
            Question(run) with { Kind = NotificationKind.Alert, AnswerSpec = null },
            Question(run) with { Source = new NotificationSource("sessions", Guid.NewGuid().ToString("D")) });
        using var watch = Watch(open);

        Assert.True(RunSubjectWatch.Interval >= TimeSpan.FromMinutes(5));

        _clock.Advance(RunSubjectWatch.Interval - TimeSpan.FromSeconds(1));
        await watch.Idle;
        Assert.Equal(0, open.Listings);

        _clock.Advance(TimeSpan.FromSeconds(1));
        await watch.Idle;

        Assert.Equal(1, open.Listings);
        Assert.Empty(_pulls.Asked);
        Assert.Empty(_notifications.Closed);
    }

    [Fact]
    public async Task A_read_that_fails_leaves_the_question_open_and_the_next_tick_tries_again()
    {
        var run = await RunWithPullRequestAsync();
        _pulls.State = SessionPullRequestState.Closed;
        _pulls.Fails = new HttpRequestException("GitHub did not answer");
        using var watch = Watch(new OpenQuestions(_notifications, Question(run)));

        _clock.Advance(RunSubjectWatch.Interval);
        await watch.Idle;
        Assert.Empty(_notifications.Closed);

        _pulls.Fails = null;
        _clock.Advance(RunSubjectWatch.Interval);
        await watch.Idle;

        Assert.Equal("Pull request closed", Assert.Single(_notifications.Closed).Reason);
    }

    [Fact]
    public async Task A_read_that_never_answers_is_abandoned_within_the_tick_and_later_ticks_still_run()
    {
        var run = await RunWithPullRequestAsync();
        _pulls.Hang = true;
        using var watch = Watch(new OpenQuestions(_notifications, Question(run)));

        _clock.Advance(RunSubjectWatch.Interval);
        await _pulls.Entered.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        _clock.Advance(RunSubjectWatch.ReadBound);
        await watch.Idle.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        Assert.Empty(_notifications.Closed);

        _pulls.Hang = false;
        _pulls.State = SessionPullRequestState.Merged;
        _clock.Advance(RunSubjectWatch.Interval);
        await watch.Idle.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        Assert.Equal("Pull request merged", Assert.Single(_notifications.Closed).Reason);
    }

    [Fact]
    public async Task A_tick_that_finds_the_previous_one_still_running_is_skipped()
    {
        var run = await RunWithPullRequestAsync();
        var open = new OpenQuestions(_notifications, Question(run)) { Hold = new TaskCompletionSource() };
        using var watch = Watch(open);

        _clock.Advance(RunSubjectWatch.Interval);
        _clock.Advance(RunSubjectWatch.Interval);
        open.Hold.SetResult();
        await watch.Idle;

        Assert.Equal(1, open.Listings);
    }

    [Fact]
    public async Task Without_notifications_or_their_listing_a_tick_does_nothing()
    {
        await RunWithPullRequestAsync();
        using var watch = new RunSubjectWatch(() => null, Probe(), () => _notifications, _clock);

        _clock.Advance(RunSubjectWatch.Interval);
        await watch.Idle;

        Assert.Empty(_pulls.Asked);
        Assert.Empty(_notifications.Closed);
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }

    private RunSubjectWatch Watch(OpenQuestions open) =>
        new(() => open, Probe(), () => _notifications, _clock);

    private SessionSubjectProbe Probe() =>
        new(new NoSessions(), new LocalDeliveryRunSource(_home, "machine", "Workshop PC"), _pulls, _clock);

    /// <summary>A run at Personal Validation that recorded its pull request on the stage
    /// before.</summary>
    private async Task<string> RunWithPullRequestAsync()
    {
        var surface = new LocalDeliverySurfaceLifecycle(_home, "machine", "Workshop PC");
        var run = (await surface.StartRunAsync(Worktree, "flow-code", "Approval card", Stages, cancellationToken: Cancellation)).RunId;
        await surface.UpdateStageAsync(Worktree, run, 0, "done", links: [new("PR", PullRequest, null)], cancellationToken: Cancellation);
        await surface.UpdateStageAsync(Worktree, run, 1, "in_progress", cancellationToken: Cancellation);

        return run;
    }

    private static NotificationSnapshot Question(string runId) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new NotificationSource("sessions", SessionNotificationSubjects.ValidationSourceKey(runId, Worktree)),
            new NotificationSubject("sessions", "run", runId),
            NotificationKind.Question,
            NotificationSeverity.Info,
            "Backlog · Approval card · Approve or revise",
            "The run reached Personal Validation.",
            "**Approval card** reached Personal Validation.",
            new AnswerSpec([new AnswerChoice("approve", "Approve")]),
            DeepLink: null,
            IsPrivate: false,
            Noon,
            NotificationState.Open,
            SettledAt: null,
            CloseReason: null,
            Answer: null);

    /// <summary>The owner's open notifications at their boundary: what it was given, less
    /// every one whose subject has been closed since, and a listing that can be held.</summary>
    private sealed class OpenQuestions(RecordingNotifications closes, params NotificationSnapshot[] open) : INotificationReplication
    {
        public int Listings { get; private set; }

        public TaskCompletionSource? Hold { get; init; }

        public async Task<IReadOnlyList<NotificationSnapshot>> ListOpenAsync(CancellationToken cancellationToken = default)
        {
            Listings++;
            if (Hold is { } hold) await hold.Task.ConfigureAwait(false);

            var closed = closes.Closed.Select(close => close.Subject).ToHashSet();
            return [.. open.Where(notification => notification.Subject is null || !closed.Contains(notification.Subject))];
        }

        public Task<NotificationSnapshot?> GetAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(open.FirstOrDefault(notification => notification.Id == notificationId));
    }

    /// <summary>GitHub at its boundary: one state for every pull request, or a failure.</summary>
    private sealed class SwitchablePullRequests : ISessionPullRequestStates
    {
        private readonly List<(string, int)> _asked = [];
        private readonly Lock _gate = new();

        public SessionPullRequestState? State { get; set; } = SessionPullRequestState.Draft;

        public Exception? Fails { get; set; }

        /// <summary>While set, a read never answers, whatever its token says.</summary>
        public bool Hang { get; set; }

        public SemaphoreSlim Entered { get; } = new(0);

        public IReadOnlyList<(string Repository, int Number)> Asked
        {
            get
            {
                lock (_gate) return [.. _asked];
            }
        }

        public Task<SessionPullRequestState?> StateAsync(string repository, int number, CancellationToken cancellationToken = default)
        {
            lock (_gate) _asked.Add((repository, number));
            Entered.Release();

            if (Hang) return new TaskCompletionSource<SessionPullRequestState?>().Task;

            return Fails is { } failure ? Task.FromException<SessionPullRequestState?>(failure) : Task.FromResult(State);
        }
    }

    private sealed class NoSessions : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            GetSessionsAsync(AgentSessionQuery.Newest, cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionCatalog([], [], 0));
    }
}
