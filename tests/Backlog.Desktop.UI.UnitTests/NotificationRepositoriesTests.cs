using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Which repository a notification is about, worked out at read time from what each producer
/// leaves on it (the scope's producer table): the alias the header scope is written in, null for
/// one filed against no repository — the Brain's — and no answer at all for one whose source could
/// not be read, so the centre shows it rather than dropping it.
/// </summary>
public sealed class NotificationRepositoriesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private static readonly GitHubSettings Settings = new()
    {
        Repositories =
        [
            new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog"),
            new GitHubRepositoryRef("fincent", "JSdotNet", "Fincent"),
            new GitHubRepositoryRef("old", "JSdotNet", "Old") { Archived = true },
        ],
    };

    private static string? AliasOf(string? repository) => Settings.Find(repository)?.Alias;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static NotificationView View(
        NotificationSource source,
        NotificationSubject? subject = null,
        NotificationOrigin? origin = null,
        string? deepLink = null) => new(
            Guid.NewGuid(), Guid.NewGuid(), source, subject, NotificationKind.Question, NotificationSeverity.Info,
            "A question", string.Empty, string.Empty, null, deepLink, false, Now, NotificationState.Open,
            null, null, null, origin);

    private static NotificationView Session(string sessionId) => View(
        new NotificationSource("sessions", Guid.NewGuid().ToString("D")),
        new NotificationSubject("sessions", "session", sessionId));

    private static AgentSession AgentSession(string id, string? recorded = null, string? resolved = null) =>
        new(id, AgentSessionKind.Claude, "pc", "PC", "Fix the toast", @"D:\Repos\X", recorded, null, Now, Now,
            AgentSessionState.Running, 3, AgentSessionOrigin.Local)
        {
            ResolvedRepository = resolved,
        };

    private static async Task<string?> ResolveOne(NotificationRepositories resolver, NotificationView view)
    {
        var resolved = await resolver.ResolveAsync([view], Ct);
        Assert.True(resolved.ContainsKey(view.Id), "The notification was left unresolved.");
        return resolved[view.Id];
    }

    [Fact]
    public async Task A_runs_validation_question_resolves_through_the_worktree_its_source_key_names()
    {
        var resolver = new NotificationRepositories(AliasOf, worktrees: new Worktrees(@"D:\Repos\Backlog\.claude\worktrees\x", "JSdotNet/Backlog"));
        var view = View(
            new NotificationSource("sessions", SessionNotificationSubjects.ValidationSourceKey("run-58", @"D:\Repos\Backlog\.claude\worktrees\x")),
            new NotificationSubject("sessions", "run", "run-58"),
            new NotificationOrigin("Claude Code", "somewhere-else · The run", Via: "Agent session"));

        Assert.Equal("backlog", await ResolveOne(resolver, view));
    }

    [Fact]
    public async Task A_runs_validation_question_outside_every_clone_falls_back_to_the_repository_its_place_names()
    {
        var resolver = new NotificationRepositories(AliasOf, worktrees: new Worktrees(@"D:\elsewhere", "JSdotNet/Backlog"));
        var view = View(
            new NotificationSource("sessions", SessionNotificationSubjects.ValidationSourceKey("run-58", @"D:\Repos\Fincent")),
            new NotificationSubject("sessions", "run", "run-58"),
            new NotificationOrigin("Claude Code", "JSdotNet/Fincent · The run", Via: "Agent session"));

        Assert.Equal("fincent", await ResolveOne(resolver, view));
    }

    [Fact]
    public async Task A_relayed_session_question_resolves_through_the_sessions_resolved_repository()
    {
        var resolver = new NotificationRepositories(AliasOf, sessions: new Sessions(AgentSession("sess-1", resolved: "JSdotNet/Fincent")));

        Assert.Equal("fincent", await ResolveOne(resolver, Session("sess-1")));
    }

    [Fact]
    public async Task A_session_question_falls_back_to_the_repository_the_session_recorded()
    {
        var resolver = new NotificationRepositories(AliasOf, sessions: new Sessions(AgentSession("sess-1", recorded: "JSdotNet/Backlog")));

        Assert.Equal("backlog", await ResolveOne(resolver, Session("sess-1")));
    }

    [Fact]
    public async Task A_session_worked_against_no_repository_is_the_brains()
    {
        var resolver = new NotificationRepositories(AliasOf, sessions: new Sessions(AgentSession("sess-1")));

        Assert.Null(await ResolveOne(resolver, Session("sess-1")));
    }

    [Fact]
    public async Task A_schedule_alert_resolves_through_the_repository_its_place_leads_with()
    {
        var resolver = new NotificationRepositories(AliasOf);
        var key = "needs-you:schedule:issue-sweep:run-1";
        var view = View(
            new NotificationSource(SessionNotificationSubjects.Context, key),
            new NotificationSubject(SessionNotificationSubjects.Context, SessionNotificationSubjects.Alert, key),
            NotificationOrigin.Fit(NotificationOrigins.Backlog, SessionAlerts.SchedulePlace("JSdotNet/Fincent")));

        Assert.Equal("fincent", await ResolveOne(resolver, view));
    }

    [Theory]
    [InlineData(SessionAlerts.SchedulesPlace)]
    [InlineData(SessionAlerts.SessionsPlace)]
    public async Task A_schedule_alert_with_no_repository_and_a_loops_alert_are_the_brains(string place)
    {
        // An alias that spells the bare place must not claim it: only a place with a repository
        // ahead of it names one.
        var resolver = new NotificationRepositories(repository => repository is "Schedules" or "Sessions" ? "decoy" : AliasOf(repository));
        var key = "stalled:loop:loop-1:2026-11-04T09:00:00.0000000Z";
        var view = View(
            new NotificationSource(SessionNotificationSubjects.Context, key),
            new NotificationSubject(SessionNotificationSubjects.Context, SessionNotificationSubjects.Alert, key),
            NotificationOrigin.Fit(NotificationOrigins.Backlog, place));

        Assert.Null(await ResolveOne(resolver, view));
    }

    [Fact]
    public async Task A_pull_request_notification_resolves_through_the_repository_its_subject_names()
    {
        var resolver = new NotificationRepositories(AliasOf);
        var view = View(
            new NotificationSource("sessions", "pr"),
            new NotificationSubject("sessions", "pull-request", SessionNotificationSubjects.PullRequestId("JSdotNet/Backlog", 42)));

        Assert.Equal("backlog", await ResolveOne(resolver, view));
    }

    [Fact]
    public async Task An_mcp_ask_from_a_session_resolves_as_the_session_does()
    {
        var resolver = new NotificationRepositories(AliasOf, sessions: new Sessions(AgentSession("sess-2", resolved: "JSdotNet/Backlog")));
        var view = View(
            new NotificationSource("mcp", "k"),
            new NotificationSubject("sessions", "session", "sess-2"),
            new NotificationOrigin("Claude Code", Via: "MCP ask"));

        Assert.Equal("backlog", await ResolveOne(resolver, view));
    }

    [Fact]
    public async Task An_mcp_ask_from_no_session_is_the_brains()
    {
        var resolver = new NotificationRepositories(AliasOf, sessions: new Sessions());
        var view = View(new NotificationSource("mcp", "k"), new NotificationSubject("mcp", "ask", "k"), new NotificationOrigin("An MCP client", Via: "MCP ask"));

        Assert.Null(await ResolveOne(resolver, view));
    }

    [Fact]
    public async Task A_spec_manager_question_on_a_linked_task_resolves_through_the_tasks_repository()
    {
        var task = Guid.NewGuid();
        var resolver = new NotificationRepositories(AliasOf, taskRepository: id => id == task ? TaskFiling.Under("JSdotNet/Backlog") : TaskFiling.NotHeld);
        var view = View(
            new NotificationSource("spec-manager", "fincent/q-1"),
            new NotificationSubject(TaskNotificationSubjects.Context, TaskNotificationSubjects.Task, TaskNotificationSubjects.Id(task)),
            new NotificationOrigin("spec-manager", "fincent · #7 Rounding", "Job", "typed in spec-manager"));

        Assert.Equal("backlog", await ResolveOne(resolver, view));
    }

    [Fact]
    public async Task A_task_notification_read_before_the_task_list_is_left_unresolved_so_the_next_load_asks_again()
    {
        var resolver = new NotificationRepositories(
            AliasOf,
            taskRepository: _ => TaskFiling.NotRead,
            targets: new LinkedTaskPaneTests.InMemoryTargets(new ConnectedTarget("spec-manager", "fincent") { RepositoryId = "JSdotNet/Fincent" }));
        var view = View(
            new NotificationSource("spec-manager", "fincent/q-1"),
            new NotificationSubject(TaskNotificationSubjects.Context, TaskNotificationSubjects.Task, TaskNotificationSubjects.Id(Guid.NewGuid())));

        Assert.False((await resolver.ResolveAsync([view], Ct)).ContainsKey(view.Id));
    }

    [Fact]
    public async Task A_task_filed_under_no_repository_is_the_brains_even_on_a_connected_product()
    {
        var resolver = new NotificationRepositories(
            AliasOf,
            taskRepository: _ => TaskFiling.Under(null),
            targets: new LinkedTaskPaneTests.InMemoryTargets(new ConnectedTarget("spec-manager", "fincent") { RepositoryId = "JSdotNet/Fincent" }));
        var view = View(
            new NotificationSource("spec-manager", "fincent/q-1"),
            new NotificationSubject(TaskNotificationSubjects.Context, TaskNotificationSubjects.Task, TaskNotificationSubjects.Id(Guid.NewGuid())));

        Assert.Null(await ResolveOne(resolver, view));
    }

    [Fact]
    public async Task A_task_the_read_list_does_not_hold_falls_back_to_its_products_connected_repository()
    {
        var resolver = new NotificationRepositories(
            AliasOf,
            taskRepository: _ => TaskFiling.NotHeld,
            targets: new LinkedTaskPaneTests.InMemoryTargets(new ConnectedTarget("spec-manager", "fincent") { RepositoryId = "JSdotNet/Fincent" }));
        var view = View(
            new NotificationSource("spec-manager", "fincent/q-1"),
            new NotificationSubject(TaskNotificationSubjects.Context, TaskNotificationSubjects.Task, TaskNotificationSubjects.Id(Guid.NewGuid())));

        Assert.Equal("fincent", await ResolveOne(resolver, view));
    }

    [Fact]
    public async Task The_task_list_is_read_on_the_calling_thread_before_the_session_catalog_is_awaited()
    {
        var catalog = new HeldSessions();
        var looked = 0;
        var resolver = new NotificationRepositories(
            AliasOf,
            sessions: catalog,
            taskRepository: _ => { looked++; return TaskFiling.Under("JSdotNet/Backlog"); });
        var task = View(
            new NotificationSource("spec-manager", "fincent/q-1"),
            new NotificationSubject(TaskNotificationSubjects.Context, TaskNotificationSubjects.Task, TaskNotificationSubjects.Id(Guid.NewGuid())));
        var session = Session("sess-1");

        var resolving = resolver.ResolveAsync([session, task], Ct);

        Assert.False(resolving.IsCompleted);
        Assert.Equal(1, looked);
        catalog.Release(AgentSession("sess-1", recorded: "JSdotNet/Fincent"));
        var resolved = await resolving;
        Assert.Equal(1, looked);
        Assert.Equal("backlog", resolved[task.Id]);
        Assert.Equal("fincent", resolved[session.Id]);
    }

    [Fact]
    public async Task A_spec_manager_question_on_no_task_resolves_through_its_products_connected_repository()
    {
        var resolver = new NotificationRepositories(
            AliasOf,
            targets: new LinkedTaskPaneTests.InMemoryTargets(new ConnectedTarget("spec-manager", "fincent-specs") { RepositoryId = "JSdotNet/Fincent" }));
        var view = View(
            new NotificationSource("spec-manager", "fincent-specs/q-1"),
            new NotificationSubject("spec-manager", "question", "01a187e5-0000-0000-0000-000000000000"),
            new NotificationOrigin("spec-manager", "fincent-specs · #7 Rounding", "Job", "Agent session"));

        Assert.Equal("fincent", await ResolveOne(resolver, view));
    }

    [Fact]
    public async Task A_spec_manager_product_with_no_connected_repository_is_the_brains()
    {
        var resolver = new NotificationRepositories(
            AliasOf,
            targets: new LinkedTaskPaneTests.InMemoryTargets(new ConnectedTarget("spec-manager", "fincent-specs")));
        var view = View(
            new NotificationSource("spec-manager", "fincent-specs/q-1"),
            new NotificationSubject("spec-manager", "question", "q-1"));

        Assert.Null(await ResolveOne(resolver, view));
    }

    [Fact]
    public async Task Another_spec_manager_notification_resolves_through_the_product_its_place_names()
    {
        var resolver = new NotificationRepositories(
            AliasOf,
            targets: new LinkedTaskPaneTests.InMemoryTargets(new ConnectedTarget("spec-manager", "fincent") { RepositoryId = "JSdotNet/Fincent" }));
        var view = View(
            new NotificationSource("spec-manager", "notification/" + Guid.NewGuid().ToString("D")),
            origin: new NotificationOrigin("spec-manager", "Fincent", Via: "Mention"));

        Assert.Equal("fincent", await ResolveOne(resolver, view));
    }

    [Fact]
    public async Task A_spec_manager_refusal_alert_resolves_through_the_product_its_source_key_names()
    {
        var resolver = new NotificationRepositories(
            AliasOf,
            targets: new LinkedTaskPaneTests.InMemoryTargets(new ConnectedTarget("spec-manager", "fincent-specs") { RepositoryId = "JSdotNet/Fincent" }));
        var view = View(new NotificationSource("spec-manager", "fincent-specs/q-9"), origin: new NotificationOrigin("spec-manager", "fincent-specs"));

        Assert.Equal("fincent", await ResolveOne(resolver, view));
    }

    [Fact]
    public async Task A_devbook_remark_resolves_through_the_repository_its_chapter_link_names()
    {
        var resolver = new NotificationRepositories(AliasOf);
        var view = View(
            new NotificationSource(DevbookNotificationSubjects.Context, Guid.NewGuid().ToString("D")),
            new NotificationSubject(DevbookNotificationSubjects.Context, DevbookNotificationSubjects.Remark, Guid.NewGuid().ToString("D")),
            deepLink: DevbookNotificationSubjects.ChapterLink("backlog", ".devbook/arc42/05.md"));

        Assert.Equal("backlog", await ResolveOne(resolver, view));
    }

    [Fact]
    public async Task A_devbook_remark_on_no_repository_is_the_brains()
    {
        var resolver = new NotificationRepositories(AliasOf);
        var view = View(
            new NotificationSource(DevbookNotificationSubjects.Context, "r"),
            new NotificationSubject(DevbookNotificationSubjects.Context, DevbookNotificationSubjects.Remark, "r"),
            deepLink: DevbookNotificationSubjects.ChapterLink(null, "notes/today.md"));

        Assert.Null(await ResolveOne(resolver, view));
    }

    [Fact]
    public async Task Pairing_and_the_settings_test_are_the_brains()
    {
        var resolver = new NotificationRepositories(AliasOf);

        Assert.Null(await ResolveOne(resolver, View(new NotificationSource("sync", "pairing"), origin: new NotificationOrigin("Backlog", "Pairing"))));
        Assert.Null(await ResolveOne(resolver, View(new NotificationSource("notifications", "test"))));
    }

    [Fact]
    public async Task A_repository_this_workspace_does_not_know_is_the_brains()
    {
        var resolver = new NotificationRepositories(AliasOf);
        var view = View(
            new NotificationSource("sessions", "pr"),
            new NotificationSubject("sessions", "pull-request", SessionNotificationSubjects.PullRequestId("Someone/Else", 1)));

        Assert.Null(await ResolveOne(resolver, view));
    }

    [Fact]
    public async Task An_archived_repository_still_resolves()
    {
        var resolver = new NotificationRepositories(AliasOf);
        var view = View(
            new NotificationSource("sessions", "pr"),
            new NotificationSubject("sessions", "pull-request", SessionNotificationSubjects.PullRequestId("JSdotNet/Old", 3)));

        Assert.Equal("old", await ResolveOne(resolver, view));
    }

    [Fact]
    public async Task A_session_catalog_that_cannot_be_read_leaves_its_notifications_unresolved_and_the_rest_resolved()
    {
        var resolver = new NotificationRepositories(AliasOf, sessions: new UnreadableSessions());
        var session = Session("sess-1");
        var pairing = View(new NotificationSource("sync", "pairing"), origin: new NotificationOrigin("Backlog", "Pairing"));

        var resolved = await resolver.ResolveAsync([session, pairing], Ct);

        Assert.False(resolved.ContainsKey(session.Id));
        Assert.True(resolved.ContainsKey(pairing.Id));
    }

    [Fact]
    public async Task A_session_the_catalog_does_not_hold_is_left_unresolved()
    {
        var resolver = new NotificationRepositories(AliasOf, sessions: new Sessions());
        var session = Session("sess-gone");

        Assert.False((await resolver.ResolveAsync([session], Ct)).ContainsKey(session.Id));
    }

    private sealed class Worktrees(string folder, string repository) : ISessionRepositoryResolver
    {
        public string? RepositoryOf(string workingFolder) =>
            string.Equals(workingFolder, folder, StringComparison.OrdinalIgnoreCase) ? repository : null;
    }

    private sealed class Sessions(params AgentSession[] sessions) : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionCatalog(sessions, [], sessions.Length));

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);
    }

    private sealed class HeldSessions : IAgentSessionSource
    {
        private readonly TaskCompletionSource<AgentSessionCatalog> _catalog = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release(params AgentSession[] sessions) =>
            _catalog.SetResult(new AgentSessionCatalog(sessions, [], sessions.Length));

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) => _catalog.Task;

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);
    }

    private sealed class UnreadableSessions : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            throw new IOException("The session records could not be read.");

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);
    }
}
