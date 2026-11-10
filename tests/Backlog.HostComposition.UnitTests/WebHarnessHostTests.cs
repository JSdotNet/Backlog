extern alias DesktopHarness;
extern alias MobileHarness;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// Each browser harness, started the way the AppHost starts it.
/// <para>
/// The assertion is that the host comes up at all. That sounds like nothing to
/// assert until it fails: a service registered for every caller but
/// constructable only in some of them takes the host down inside
/// <c>WebApplicationBuilder.Build()</c>, before a request, a page or a log line —
/// the mobile harness died with exit code -532462766 and a green build and a
/// green unit suite either side of it. Provider validation is what says so, and
/// only a real host turns it on.
/// </para>
/// <para>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> rather than a service
/// collection assembled here, because a collection assembled here is a copy of
/// the host's composition and a copy is what drifts. These run the harness's own
/// <c>Program</c>.
/// </para>
/// </summary>
public class WebHarnessHostTests
{
    /// <summary>
    /// Development on purpose, and it is the whole point of these tests: the
    /// generic host only turns <see cref="ServiceProviderOptions.ValidateOnBuild"/>
    /// and <c>ValidateScopes</c> on in Development, and that validation is what
    /// turns an unsatisfiable registration into a startup failure. Every AppHost
    /// run of these harnesses is a Development run, so this is also what they
    /// actually do. Over a workspace of the test's own: see
    /// <see cref="IsolatedHarnessFactory{TEntryPoint}"/>.
    /// </summary>
    private sealed class Harness<TEntryPoint> : IsolatedHarnessFactory<TEntryPoint>
        where TEntryPoint : class;

    /// <summary>
    /// The desktop harness, which has an <c>ITaskRepository</c> and opts into
    /// task replication. Resolving the session is the second half of the
    /// assertion: a host that opted in and left out its sync-state store would
    /// build and then hide the sync section at runtime, which is the quiet
    /// failure the loud one used to mask.
    /// </summary>
    [Fact]
    public void The_desktop_harness_host_starts()
    {
        using var harness = new Harness<DesktopHarness::Program>();

        using var scope = harness.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<Backlog.Infrastructure.Sync.TaskSyncSession>());
    }

    /// <summary>
    /// And the background loop is constructable in it, which is a second thing
    /// from the session being constructable: the worker takes the sync-state
    /// store directly, so a harness that composed a session but no store would
    /// pass the assertion above and fail here.
    /// <para>
    /// That the harness's own <c>Program</c> asks for it after <c>Build()</c> is
    /// checked by <c>TaskSyncClientRegistrationTests</c> rather than here. A
    /// resolve in this test would satisfy itself: it would construct the very
    /// singleton whose absence it is meant to detect.
    /// </para>
    /// </summary>
    [Fact]
    public void The_desktop_harness_can_compose_the_background_sync_loop()
    {
        using var harness = new Harness<DesktopHarness::Program>();

        using var scope = harness.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<Backlog.Infrastructure.Sync.TaskSyncWorker>());
    }

    /// <summary>
    /// The session is resolvable from the <em>root</em> provider, which is where
    /// the background loop actually asks for it — outside any scope. The session
    /// now takes the Inbox's intake and outbox, and in Development the provider
    /// validates scopes: a scoped registration on either would not fail
    /// <c>Build()</c> (a transient may take a scoped dependency) and would not
    /// fail the scoped resolve above, but it would throw here, and the loop
    /// reads that throw as "this host does not replicate". Captures would then
    /// stop arriving with nothing on screen to say so.
    /// </summary>
    [Fact]
    public void The_desktop_harness_composes_the_sync_session_where_the_loop_resolves_it()
    {
        using var harness = new Harness<DesktopHarness::Program>();

        Assert.NotNull(harness.Services.GetRequiredService<Backlog.Infrastructure.Sync.TaskSyncSession>());
        Assert.NotNull(harness.Services.GetRequiredService<Backlog.Modules.Inbox.Abstractions.Services.IInboxIntake>());
        Assert.NotNull(harness.Services.GetRequiredService<Backlog.Modules.Inbox.Abstractions.Services.IInboxCaptureOutbox>());
    }

    /// <summary>
    /// The delivery surface is constructable, and so is the shell activator it asks
    /// for.
    /// <para>
    /// Worth its own resolve for a reason <c>ValidateOnBuild</c> does not cover: the
    /// port is registered through a factory, which provider validation cannot look
    /// inside, and <em>nothing in the application resolves it</em> — no pane injects
    /// it and no worker asks for it, because its only caller is a tool surface that
    /// is not built yet. A registration that could not be satisfied would therefore
    /// sit there silently through every start of both hosts, and surface as a failure
    /// on the first call a session ever made.
    /// </para>
    /// </summary>
    [Fact]
    public void The_desktop_harness_composes_the_delivery_surface()
    {
        using var harness = new Harness<DesktopHarness::Program>();

        Assert.NotNull(harness.Services.GetRequiredService<Backlog.Modules.Sessions.Abstractions.IDeliverySurfaceLifecycle>());

        // And the shell's half of open_dashboard, which the surface takes optionally:
        // this host has a window, so it is here. A host without one composes the
        // surface alone and answers that there is nothing to bring forward.
        Assert.NotNull(harness.Services.GetRequiredService<Backlog.Modules.Sessions.Abstractions.ISessionsSurfaceActivator>());

        // The same instance behind both registrations. The shell attaches to the
        // concrete type and the surface asks for the port, and two objects here
        // would mean a window attaching to one registry while open_dashboard asked
        // the other — which looks exactly like no window being open.
        Assert.Same(
            harness.Services.GetRequiredService<Backlog.Desktop.UI.Shell.SessionsSurfaceActivator>(),
            harness.Services.GetRequiredService<Backlog.Modules.Sessions.Abstractions.ISessionsSurfaceActivator>());
    }

    /// <summary>
    /// Every service the harness's inline Devbook block used to register still resolves,
    /// with scopes validated, now that <c>AddDevbookModule</c> registers them through the
    /// shared composition — and the two choices that were this harness's own survive the
    /// move. The domain store is one per circuit, so two visitors never share a reader's
    /// state; and the Devbook offers and the Archify artifacts sit over a launcher that
    /// says it cannot start a CLI here rather than starting one.
    /// </summary>
    [Fact]
    public async Task The_desktop_harness_resolves_every_devbook_service_with_its_own_lifetimes_and_launcher()
    {
        using var harness = new Harness<DesktopHarness::Program>();
        using var first = harness.Services.CreateScope();
        using var second = harness.Services.CreateScope();

        foreach (var service in DevbookModuleServices.All)
        {
            Assert.NotNull(first.ServiceProvider.GetRequiredService(service));
        }

        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<Backlog.Desktop.UI.Devbook.DomainDevbookStore>(),
            second.ServiceProvider.GetRequiredService<Backlog.Desktop.UI.Devbook.DomainDevbookStore>());

        Assert.IsType<Backlog.Desktop.UI.Devbook.ArchifyDiagramArtifacts>(
            harness.Services.GetRequiredService<Backlog.UI.Components.Diagrams.IDiagramArtifactSource>());
        Assert.IsType<Backlog.Infrastructure.Copilot.UnavailableCopilotCliLauncher>(
            harness.Services.GetRequiredService<Backlog.Infrastructure.Copilot.ICopilotCliLauncher>());

        var copilot = harness.Services.GetRequiredService<Backlog.Desktop.UI.Devbook.DevbookCopilotCli>();
        var item = new Backlog.Desktop.UI.Devbook.DevbookActionItem(
            "Decision", "adr", ".arc42/adr/0001-decision.md", new Dictionary<string, string>(), Summary: null);

        await Assert.ThrowsAsync<Backlog.Infrastructure.Copilot.CopilotCliException>(
            () => copilot.StartAsync(item, workingDirectory: null, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The GitHub token route asks <see cref="IHttpClientFactory"/> for its client,
    /// by name, on every send — not once, when the singleton transport is built,
    /// which would hold one client for the life of the process where the
    /// factory's handler rotation never reaches it.
    /// <para>
    /// The factory is swapped for one that writes down what it was asked for, and
    /// the credential resolver for one that binds every path to an account, so
    /// the call leaves over the token route without a <c>gh</c> CLI or a
    /// configured token. The transport itself is the harness's own registration;
    /// nothing here restates it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_desktop_harness_github_transport_asks_the_factory_for_a_client_on_send()
    {
        var factory = new RecordingHttpClientFactory();
        using var harness = new Harness<DesktopHarness::Program>();
        using var recorded = harness.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IHttpClientFactory>(factory);
                services.AddSingleton<Backlog.Infrastructure.GitHub.IGitHubCredentialResolver>(new BoundCredentialResolver());
            }));

        var transport = recorded.Services.GetRequiredService<Backlog.Infrastructure.GitHub.ResolvingGitHubTransport>();

        Assert.DoesNotContain(Backlog.Infrastructure.GitHub.TokenTransport.HttpClientName, factory.Names);

        await transport.SendAsync(HttpMethod.Get, "repos/octo/demo", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, factory.Names.Count(name => name == Backlog.Infrastructure.GitHub.TokenTransport.HttpClientName));
        Assert.Equal(1, factory.Handler.RequestCount);
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _names = new();

        public IReadOnlyCollection<string> Names => _names;

        public AnsweringHandler Handler { get; } = new();

        public HttpClient CreateClient(string name)
        {
            _names.Enqueue(name);
            return new HttpClient(Handler, disposeHandler: false);
        }
    }

    /// <summary>Answers every request with an empty JSON object, so nothing leaves
    /// the test.</summary>
    private sealed class AnsweringHandler : HttpMessageHandler
    {
        private int _count;

        public int RequestCount => _count;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>Every path is bound to an account, which is the route that always
    /// goes out over HTTP rather than the <c>gh</c> CLI.</summary>
    private sealed class BoundCredentialResolver : Backlog.Infrastructure.GitHub.IGitHubCredentialResolver
    {
        public bool HasAnyCredential => true;

        public Task<Backlog.Infrastructure.GitHub.GitHubCredential?> ResolveAsync(string? path, CancellationToken cancellationToken = default) =>
            Task.FromResult<Backlog.Infrastructure.GitHub.GitHubCredential?>(
                new Backlog.Infrastructure.GitHub.GitHubCredential("ghp_test", null, "octocat"));
    }

    /// <summary>
    /// Every Dashboard port the pane and Ask AI read is still answered, with the
    /// lifetime it had when one UI-side call registered them all: the provider
    /// adapters and the machine directory once per app, the scope mirror and the
    /// Ask AI source once per reader. The adapters now come from the infrastructure
    /// they wrap, so this is what says the swap in the composition root left none
    /// of them behind.
    /// </summary>
    [Fact]
    public void The_desktop_harness_answers_every_dashboard_port_with_its_lifetime()
    {
        using var harness = new Harness<DesktopHarness::Program>();

        AssertSingleton<Backlog.Modules.Dashboard.Abstractions.Services.IRepositoryDirectory>(harness.Services);
        AssertSingleton<Backlog.Modules.Dashboard.Abstractions.Services.IMachineDirectory>(harness.Services);
        AssertSingleton<Backlog.Modules.Dashboard.Abstractions.Services.IActivitySource>(harness.Services);
        AssertSingleton<Backlog.Modules.Dashboard.Abstractions.Services.IActivityBaselineSource>(harness.Services);
        AssertSingleton<Backlog.Modules.Dashboard.Abstractions.Services.IClaudeSpendSource>(harness.Services);
        AssertSingleton<Backlog.Modules.Dashboard.Abstractions.Services.ICopilotSpendSource>(harness.Services);
        AssertSingleton<Backlog.Modules.Dashboard.Abstractions.Services.IAzureFoundrySpendSource>(harness.Services);
        AssertSingleton<Backlog.Modules.Dashboard.Abstractions.Services.IHoursWorkedSource>(harness.Services);
        Assert.IsType<Backlog.Infrastructure.FileSystem.Dashboard.AgentActivityHoursWorkedSource>(
            harness.Services.GetRequiredService<Backlog.Modules.Dashboard.Abstractions.Services.IHoursWorkedSource>());
        AssertSingleton<Backlog.Modules.Dashboard.Abstractions.Services.IDriftIssueSource>(harness.Services);
        AssertSingleton<Backlog.Modules.Dashboard.Abstractions.Services.IDriftUnitSource>(harness.Services);

        // The verdict store is composed here, so the drift part reads the sweeps' verdicts
        // rather than answering that this host keeps none.
        Assert.True(harness.Services.GetRequiredService<Backlog.Modules.Dashboard.Abstractions.Services.IDriftUnitSource>().IsAvailable);

        using var first = harness.Services.CreateScope();
        using var second = harness.Services.CreateScope();

        var scope = first.ServiceProvider.GetRequiredService<Backlog.Modules.Dashboard.UI.DashboardScopeInView>();
        Assert.Same(scope, first.ServiceProvider.GetRequiredService<Backlog.Modules.Dashboard.UI.DashboardScopeInView>());
        Assert.NotSame(scope, second.ServiceProvider.GetRequiredService<Backlog.Modules.Dashboard.UI.DashboardScopeInView>());

        var askAi = first.ServiceProvider.GetServices<Backlog.SharedKernel.Ai.IAiContentSource>()
            .Single(source => source.GetType().Name == "DashboardAiContentSource");
        Assert.DoesNotContain(askAi, second.ServiceProvider.GetServices<Backlog.SharedKernel.Ai.IAiContentSource>());

        static void AssertSingleton<TPort>(IServiceProvider services) where TPort : class
        {
            using var one = services.CreateScope();
            using var other = services.CreateScope();
            Assert.Same(
                one.ServiceProvider.GetRequiredService<TPort>(),
                other.ServiceProvider.GetRequiredService<TPort>());
        }
    }

    /// <summary>
    /// Every port the Sessions adapters answer is resolvable, and answered from
    /// <c>Backlog.Infrastructure.Sessions</c> rather than from the module's screen
    /// project; the Ask AI source the screen project keeps is still composed beside
    /// them.
    /// </summary>
    [Fact]
    public void The_desktop_harness_composes_the_sessions_adapters_from_their_infrastructure_project()
    {
        const string adapter = "Backlog.Infrastructure.Sessions";

        using var harness = new Harness<DesktopHarness::Program>();

        object[] adapters =
        [
            harness.Services.GetRequiredService<Backlog.Modules.Sessions.Abstractions.IAgentSessionSource>(),
            harness.Services.GetRequiredService<Backlog.Modules.Sessions.Abstractions.IAgentActivitySource>(),
            harness.Services.GetRequiredService<Backlog.Modules.Sessions.Abstractions.IDeliveryRunSource>(),
            harness.Services.GetRequiredService<Backlog.Modules.Sessions.Abstractions.IDeliverySurfaceLifecycle>(),
            harness.Services.GetRequiredService<Backlog.Modules.Sessions.Abstractions.IDeliveryRunTelemetry>(),
            harness.Services.GetRequiredService<Backlog.Modules.Sessions.Abstractions.ISessionRecordKeeper>(),
        ];

        Assert.All(adapters, instance => Assert.Equal(adapter, instance.GetType().Assembly.GetName().Name));

        using var scope = harness.Services.CreateScope();

        Assert.Contains(
            scope.ServiceProvider.GetServices<Backlog.SharedKernel.Ai.IAiContentSource>(),
            source => source.AreaKey == "sessions");
    }

    /// <summary>
    /// Each context that owns subjects answers the notifications startup probe about
    /// them, one probe per subject context, and the places that notice an end are the
    /// ones the hosts resolve: the remark store every caller gets is the watched one,
    /// and the pull request port the Sessions probe asks is composed.
    /// </summary>
    [Fact]
    public void The_desktop_harness_composes_a_subject_probe_for_every_context_that_owns_subjects()
    {
        using var harness = new Harness<DesktopHarness::Program>();

        var probes = harness.Services.GetServices<Backlog.Modules.Notifications.Abstractions.INotificationSubjectProbe>().ToList();

        Assert.Equal(["devbook", "sessions", "spec-manager", "sync", "tasks"], probes.Select(probe => probe.Context).Order(StringComparer.Ordinal));
        Assert.IsType<Backlog.Infrastructure.Devbook.RemarkNotificationStore>(
            harness.Services.GetRequiredService<Backlog.Modules.Devbook.Abstractions.IDevbookAnnotationStore>());
        Assert.IsType<Backlog.Desktop.UI.PullRequests.PullRequestSubjects>(
            harness.Services.GetRequiredService<Backlog.Modules.Sessions.Abstractions.ISessionPullRequestStates>());
        Assert.NotNull(harness.Services.GetRequiredService<Backlog.Infrastructure.Sync.PairingSubjects>());
    }

    /// <summary>
    /// Every reader of a pull request goes through Repository Management's one contract
    /// (local ADR 0042): the harness composes the module, one instance for the process,
    /// with GitHub behind it rather than the module's not-configured fallbacks, and the
    /// settled-fact memory on disk rather than in memory.
    /// </summary>
    [Fact]
    public void The_desktop_harness_reads_pull_requests_through_repository_managements_one_contract()
    {
        using var harness = new Harness<DesktopHarness::Program>();

        var pullRequests = harness.Services.GetRequiredService<Backlog.Modules.RepositoryManagement.Abstractions.Services.IPullRequests>();

        Assert.Equal("Backlog.Modules.RepositoryManagement", pullRequests.GetType().Assembly.GetName().Name);
        Assert.Same(pullRequests, harness.Services.GetRequiredService<Backlog.Modules.RepositoryManagement.Abstractions.Services.IPullRequests>());
        Assert.IsType<Backlog.Infrastructure.GitHub.RepositoryManagement.GitHubPullRequestSource>(
            harness.Services.GetRequiredService<Backlog.Modules.RepositoryManagement.Abstractions.Services.IPullRequestSource>());
        Assert.IsType<Backlog.Infrastructure.GitHub.RepositoryManagement.GitHubRegisteredRepositories>(
            harness.Services.GetRequiredService<Backlog.Modules.RepositoryManagement.Abstractions.Services.IRegisteredRepositories>());
        Assert.IsType<Backlog.Infrastructure.FileSystem.SettledPullRequestStore>(
            harness.Services.GetRequiredService<Backlog.Modules.RepositoryManagement.Abstractions.Services.ISettledPullRequestStore>());
        // Progress reads pull requests through the same contract (V6).
        Assert.IsType<Backlog.Infrastructure.FileSystem.Progress.RepositoryManagementGoalPullRequests>(
            harness.Services.GetRequiredService<Backlog.Modules.Progress.Abstractions.Services.IGoalPullRequestReader>());
        // And so does the task row, through Tasks' own port (V5, V10); which entries a
        // merge's finish is settled for stays Tasks' fact, in the folder it always had.
        Assert.IsType<Backlog.Infrastructure.FileSystem.Tasks.LinkedPullRequestStates>(
            harness.Services.GetRequiredService<Backlog.Modules.Tasks.Abstractions.Services.ILinkedPullRequestStates>());
        Assert.IsType<Backlog.Infrastructure.FileSystem.PullRequestFinishMemory>(
            harness.Services.GetRequiredService<Backlog.Modules.Tasks.Abstractions.Services.IPullRequestFinishMemory>());
    }

    /// <summary>
    /// Which task a session or a pull request belongs to is Tasks' answer over the
    /// circuit's own Tasks state (local ADR 0042, V8): one lookup per circuit, over the
    /// state that circuit draws, so two visitors never match over each other's rows.
    /// </summary>
    [Fact]
    public void The_desktop_harness_asks_tasks_for_a_task_over_each_circuits_own_rows()
    {
        using var harness = new Harness<DesktopHarness::Program>();
        using var first = harness.Services.CreateScope();
        using var second = harness.Services.CreateScope();

        Assert.Same(
            first.ServiceProvider.GetRequiredService<Backlog.Desktop.UI.Tasks.TasksDesktopState>(),
            first.ServiceProvider.GetRequiredService<Backlog.Modules.Tasks.Abstractions.Services.ITaskLookupCandidates>());

        var lookup = first.ServiceProvider.GetRequiredService<Backlog.Modules.Tasks.Abstractions.Services.ITaskLookup>();
        Assert.Equal("Backlog.Modules.Tasks", lookup.GetType().Assembly.GetName().Name);
        Assert.Same(lookup, first.ServiceProvider.GetRequiredService<Backlog.Modules.Tasks.Abstractions.Services.ITaskLookup>());
        Assert.NotSame(lookup, second.ServiceProvider.GetRequiredService<Backlog.Modules.Tasks.Abstractions.Services.ITaskLookup>());
    }

    /// <summary>
    /// Every context that asks questions registers an answer handler for its source, and
    /// the port resolves with them: the MCP tools, the Settings page's test question
    /// (Notifications), a run's Personal Validation (Sessions), a spec-manager question
    /// (the spec-manager adapter), and the device that
    /// redeemed a pairing code (Sync).
    /// </summary>
    [Fact]
    public void The_desktop_harness_composes_an_answer_handler_for_every_context_that_asks()
    {
        using var harness = new Harness<DesktopHarness::Program>();

        var handlers = harness.Services.GetServices<Backlog.Modules.Notifications.Abstractions.INotificationAnswerHandler>().ToList();

        Assert.Equal(["mcp", "notifications", "sessions", "spec-manager", "sync"], handlers.Select(handler => handler.Source).Order(StringComparer.Ordinal));
        Assert.NotNull(harness.Services.GetRequiredService<Backlog.Modules.Notifications.Abstractions.INotifications>());
    }

    /// <summary>
    /// Events is composed but shows nothing (local ADR 0050): the shared composition adds the
    /// module and <c>AddSqlite</c> answers its two store ports with the rooted SQLite adapters,
    /// while no shell surface, settings section or Ask AI source of it is registered — there is
    /// no pane yet — and with the default feature file the <c>events</c> switch reads as off.
    /// The events read contract and the Calendar's port over it (§7) are composed too, and
    /// with the switch off the Calendar is offered no events.
    /// </summary>
    [Fact]
    public async Task The_desktop_harness_composes_the_events_store_and_shows_nothing_of_it()
    {
        const string module = "Backlog.Modules.Events";

        using var harness = new Harness<DesktopHarness::Program>();

        Assert.IsType<Backlog.Infrastructure.Sqlite.Events.RootedSqliteKnowledgeEventRepository>(
            harness.Services.GetRequiredService<Backlog.Modules.Events.IKnowledgeEventRepository>());
        Assert.IsType<Backlog.Infrastructure.Sqlite.Events.RootedSqliteEventSeriesRepository>(
            harness.Services.GetRequiredService<Backlog.Modules.Events.IEventSeriesRepository>());

        using var scope = harness.Services.CreateScope();

        static bool OfEvents(Type type) => type.Assembly.GetName().Name!.StartsWith(module, StringComparison.Ordinal);

        Assert.DoesNotContain(
            scope.ServiceProvider.GetServices<Backlog.SharedKernel.ShellSurface>(),
            surface => surface.FeatureKey == Backlog.Modules.Events.Abstractions.EventsFeatures.Events || OfEvents(surface.Component));
        Assert.DoesNotContain(
            scope.ServiceProvider.GetServices<Backlog.SharedKernel.SettingsSection>(),
            section => section.FeatureKey == Backlog.Modules.Events.Abstractions.EventsFeatures.Events || OfEvents(section.Component));
        Assert.DoesNotContain(
            scope.ServiceProvider.GetServices<Backlog.SharedKernel.Ai.IAiContentSource>(),
            source => source.AreaKey == Backlog.Modules.Events.Abstractions.EventsFeatures.Events || OfEvents(source.GetType()));
        Assert.False(harness.Services.GetRequiredService<Backlog.SharedKernel.IAppFeatureSettings>()
            .IsEnabled(Backlog.Modules.Events.Abstractions.EventsFeatures.Events));

        Assert.Equal(module, harness.Services
            .GetRequiredService<Backlog.Modules.Events.Abstractions.Services.IKnowledgeEventReader>()
            .GetType().Assembly.GetName().Name);
        var calendarEvents = harness.Services.GetRequiredService<Backlog.Modules.Tasks.Abstractions.Services.ICalendarEvents>();
        Assert.IsType<Backlog.Infrastructure.FileSystem.Events.EventsCalendarEvents>(calendarEvents);
        Assert.False((await calendarEvents.ReadAsync(TestContext.Current.CancellationToken)).Available);
    }

    /// <summary>
    /// The mobile harness, which has no <c>ITaskRepository</c> and never will:
    /// the phone carries the Inbox, not a local task database. It composes the
    /// pairing surface and nothing of replication, and it has to start.
    /// </summary>
    [Fact]
    public void The_mobile_harness_host_starts()
    {
        using var harness = new Harness<MobileHarness::Program>();

        using var scope = harness.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<Backlog.Infrastructure.Sync.DevicePairingClient>());
        Assert.Null(scope.ServiceProvider.GetService<Backlog.Infrastructure.Sync.TaskSyncSession>());

        // And no loop either, which is the same statement: the worker comes with
        // the session, and a phone with no local task database has nothing for
        // either of them to replicate.
        Assert.Null(scope.ServiceProvider.GetService<Backlog.Infrastructure.Sync.TaskSyncWorker>());
    }

    /// <summary>
    /// Cloud dictation's upload is sent once (local ADR 0045 §6). The harness's
    /// <c>AddServiceDefaults()</c> puts the standard pipeline on every client, and on
    /// this one its ten seconds an attempt and three retries would cut a recording off
    /// and send it again; the host takes it off again before adding the sync pipeline.
    /// The data client beside it keeps it, which is what shows the walk can see one.
    /// </summary>
    [Fact]
    public void The_mobile_harness_sends_a_dictation_recording_with_no_retry()
    {
        using var harness = new Harness<MobileHarness::Program>();

        var handlers = harness.Services.GetRequiredService<IHttpMessageHandlerFactory>();
        var upload = HandlerChain(handlers.CreateHandler(nameof(Backlog.Mobile.UI.Services.CloudTranscriptionClient)));
        var data = HandlerChain(handlers.CreateHandler(nameof(Backlog.Mobile.UI.Services.CloudSyncClient)));

        Assert.DoesNotContain(upload, handler => handler is Microsoft.Extensions.Http.Resilience.ResilienceHandler);
        Assert.Contains(upload, handler => handler is Backlog.Infrastructure.Sync.SyncAuthenticationHandler);
        Assert.Contains(data, handler => handler is Microsoft.Extensions.Http.Resilience.ResilienceHandler);
    }

    /// <summary>The factory's handler, the additional handlers in order, and the primary
    /// handler last — walked the way a request travels.</summary>
    private static List<HttpMessageHandler> HandlerChain(HttpMessageHandler handler)
    {
        var chain = new List<HttpMessageHandler>();

        for (var current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
        {
            chain.Add(current);
        }

        return chain;
    }

    /// <summary>
    /// The desktop harness registers the Inbox's settings section, so its settings
    /// screen carries the routing rules the way the desktop app's does. The shell
    /// holds no copy of its own: without the registration the page is simply gone.
    /// </summary>
    [Fact]
    public void The_desktop_harness_registers_the_inbox_settings_section()
    {
        using var harness = new Harness<DesktopHarness::Program>();

        using var scope = harness.Services.CreateScope();

        var sections = scope.ServiceProvider.GetServices<Backlog.SharedKernel.SettingsSection>();
        Assert.Contains(sections, section => section.Id == "inbox" && section.Title == "Inbox");
    }

    /// <summary>
    /// The desktop harness carries the Tasks page — Clear My Day overnight — through
    /// the shared composition, and the switch it draws is the one store both sync
    /// ports read (local ADR 0032).
    /// </summary>
    [Fact]
    public void The_desktop_harness_registers_the_tasks_settings_section_over_the_my_day_preferences()
    {
        using var harness = new Harness<DesktopHarness::Program>();

        using var scope = harness.Services.CreateScope();

        var sections = scope.ServiceProvider.GetServices<Backlog.SharedKernel.SettingsSection>();
        var tasks = Assert.Single(sections, section => section.Id == "tasks");
        Assert.Equal("Tasks", tasks.Title);
        Assert.Equal(["My Day"], tasks.CardTitles!);
        Assert.Same(
            scope.ServiceProvider.GetRequiredService<Backlog.Modules.Tasks.Abstractions.Services.IMyDayPreferences>(),
            scope.ServiceProvider.GetRequiredService<Backlog.Modules.Tasks.Abstractions.Services.IMyDayPreferencesReplication>());
    }

    /// <summary>
    /// The desktop harness reads the iteration from the one store the sync's replication
    /// port reads too (local ADR 0036, §3), kept under its isolated root rather than the
    /// per-user folder the installed app shares.
    /// </summary>
    [Fact]
    public void The_desktop_harness_reads_the_iteration_from_the_store_the_sync_reads()
    {
        using var harness = new Harness<DesktopHarness::Program>();

        var store = harness.Services.GetRequiredService<Backlog.Infrastructure.FileSystem.Progress.IterationSettingsStore>();

        Assert.Same(
            store,
            harness.Services.GetRequiredService<Backlog.Modules.Progress.Abstractions.Services.IIterationSettingsReplication>());
        Assert.IsType<Backlog.Infrastructure.FileSystem.Progress.IterationSettings>(
            harness.Services.GetRequiredService<Backlog.Modules.Progress.Abstractions.Services.IIterationSettings>());
        Assert.StartsWith(HarnessIsolation.Root, Path.GetFullPath(store.SettingsPath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The desktop harness carries the Dashboard's settings section too, without a
    /// line of its own: the shared composition's <c>AddDashboardUi()</c> registers it.
    /// </summary>
    [Fact]
    public void The_desktop_harness_registers_the_dashboard_settings_section_through_the_shared_composition()
    {
        using var harness = new Harness<DesktopHarness::Program>();

        using var scope = harness.Services.CreateScope();

        var sections = scope.ServiceProvider.GetServices<Backlog.SharedKernel.SettingsSection>();
        Assert.Single(sections, section => section.Id == "dashboard" && section.Title == "Dashboard");
    }

    /// <summary>
    /// And the working week's page, which the Dashboard registers beside its own
    /// with no switch, so the desktop harness offers it whatever is turned on.
    /// </summary>
    [Fact]
    public void The_desktop_harness_registers_the_working_week_settings_section_through_the_shared_composition()
    {
        using var harness = new Harness<DesktopHarness::Program>();

        using var scope = harness.Services.CreateScope();

        var sections = scope.ServiceProvider.GetServices<Backlog.SharedKernel.SettingsSection>();
        Assert.Single(sections, section => section.Id == "working-week" && section.Title == "Working week" && section.FeatureKey is null);
    }

    /// <summary>
    /// The Devices page is the Sync module's section now, and the desktop harness
    /// carries it through the shared composition's <c>AddSyncSettings()</c>, behind
    /// the sync switch the shell used to check for itself.
    /// </summary>
    [Fact]
    public void The_desktop_harness_registers_the_devices_settings_section_through_the_shared_composition()
    {
        using var harness = new Harness<DesktopHarness::Program>();

        using var scope = harness.Services.CreateScope();

        var sections = scope.ServiceProvider.GetServices<Backlog.SharedKernel.SettingsSection>();
        Assert.Single(sections, section => section.Id == "devices"
            && section.Title == "Devices"
            && section.FeatureKey == Backlog.Modules.Sync.Abstractions.SyncFeatures.Sync);
    }

    /// <summary>
    /// The desktop harness opens a popped-out surface in a browser tab of its own
    /// (local ADR 0043 §7): it composes <see cref="Backlog.Desktop.UI.Shell.IShellWindows"/>
    /// per circuit, so <c>Home</c> draws the ⧉, and the address a ⧉ opens renders the
    /// Devbook under the popped-out window's slim header rather than "not available".
    /// </summary>
    [Fact]
    public async Task The_desktop_harness_renders_the_devbook_at_its_popped_out_window_route()
    {
        using var harness = new Harness<DesktopHarness::Program>();

        using (var scope = harness.Services.CreateScope())
        {
            Assert.IsType<Backlog.Desktop.UI.Shell.BrowserTabShellWindows>(
                scope.ServiceProvider.GetRequiredService<Backlog.Desktop.UI.Shell.IShellWindows>());
        }

        using var client = harness.CreateClient();
        using var response = await client.GetAsync(
            Backlog.Desktop.UI.Shell.SurfaceWindowRoute.PathFor("devbook"),
            TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("data-testid=\"surface-window\"", page, StringComparison.Ordinal);
        Assert.Contains("data-surface-id=\"devbook\"", page, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"devbook-surface\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"surface-window-unavailable\"", page, StringComparison.Ordinal);
    }
}
