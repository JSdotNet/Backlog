extern alias DesktopHarness;
extern alias MobileHarness;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
