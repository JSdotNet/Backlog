extern alias DesktopHarness;
extern alias MobileHarness;

using Microsoft.AspNetCore.Hosting;
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
    /// actually do.
    /// </summary>
    private sealed class Harness<TEntryPoint> : WebApplicationFactory<TEntryPoint>
        where TEntryPoint : class
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment("Development");
    }

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
    /// <para>
    /// The diagram port is looked up by name in the module's published surface so that
    /// the assertion that it lives there fails as an assertion rather than as a build.
    /// </para>
    /// </summary>
    [Fact]
    public void The_desktop_harness_composes_the_sessions_adapters_from_their_infrastructure_project()
    {
        const string adapter = "Backlog.Infrastructure.Sessions";

        using var harness = new Harness<DesktopHarness::Program>();

        var diagramPort = typeof(Backlog.Modules.Sessions.Abstractions.IAgentSessionSource).Assembly
            .GetType("Backlog.Modules.Sessions.Abstractions.IDeliveryRunDiagrams");

        Assert.NotNull(diagramPort);
        Assert.True(diagramPort.IsPublic, "IDeliveryRunDiagrams is the module's published port.");

        object[] adapters =
        [
            harness.Services.GetRequiredService<Backlog.Modules.Sessions.Abstractions.IAgentSessionSource>(),
            harness.Services.GetRequiredService<Backlog.Modules.Sessions.Abstractions.IAgentActivitySource>(),
            harness.Services.GetRequiredService<Backlog.Modules.Sessions.Abstractions.IDeliveryRunSource>(),
            harness.Services.GetRequiredService(diagramPort),
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
}
