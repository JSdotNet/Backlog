using System.Runtime.Versioning;
using Backlog.Aspire.ServiceDefaults;
using Backlog.Desktop.Composition;
using Backlog.UI.Components.AppUpdate;
using Backlog.Desktop.UI.Devbook;
using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.DevPc;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Infrastructure.FileSystem.Dashboard;
using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.SpecManager;
using Backlog.Infrastructure.Sync;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Inbox.UI;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Desktop.UI.Tasks;
using Backlog.UI.Components.Diagrams;
using Backlog.UI.Components.Feedback;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// The MAUI desktop head's container, validated the way the web harness's is.
/// <para>
/// A MAUI head cannot be started in a test process, and <c>MauiAppBuilder</c>
/// never validates its provider, so a registration the head could not satisfy
/// used to be found by somebody launching the app. Both heads now compose through
/// <c>AddDesktopComposition</c>, and <see cref="WebHarnessHostTests"/> proves the
/// harness's options; this validates the MAUI head's shape of them — the DPAPI
/// credential store, the full tools adapter, and each window's state scoped
/// to that window — together with what the head registers beside the call.
/// </para>
/// <para>
/// The options and the head-only lines are restated here rather than read from
/// <c>MauiProgram</c>, because no test project can reference that head. What cannot
/// be restated is replaced by the nearest stand-in and says so: the MCP listener
/// and its endpoint source are the head's own types, and the MSIX update service
/// is compiled for Windows alone.
/// </para>
/// </summary>
public sealed class MauiDesktopCompositionTests : IDisposable
{
    private readonly string _appData = Path.Combine(
        Path.GetTempPath(), "backlog-maui-composition", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_appData, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Every registration the head makes can be constructed. This is the check that
    /// would have caught a registration missing from the MAUI head before somebody
    /// launched it, and the session and the background loop are resolved from the
    /// root, where the loop asks for them.
    /// </summary>
    [Fact]
    public void The_maui_head_composition_validates_on_build()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head and its DPAPI credential store are Windows-only.");
            return;
        }

        using var provider = BuildMauiShaped(validateScopes: false);

        Assert.NotNull(provider.GetRequiredService<TaskSyncSession>());
        Assert.NotNull(provider.GetRequiredService<TaskSyncWorker>());
        Assert.NotNull(provider.GetRequiredService<Backlog.Modules.Tasks.Features.SyncLinkedTasks.LinkedTaskSyncWorker>());
        Assert.Contains(
            provider.GetServices<Backlog.Modules.Tasks.Abstractions.Connectors.ITaskConnector>(),
            connector => connector is Backlog.Infrastructure.GitHub.GitHubConnector);
    }

    /// <summary>
    /// With scopes validated too, as a Debug build of the head now builds its provider,
    /// the head is accepted: window state is scoped (local ADR 0043, §5), and nothing a
    /// singleton is constructed from reaches it.
    /// <para>
    /// And every worker and service <c>MauiProgram</c> resolves from the root after
    /// <c>Build()</c> resolves there under validated scopes. A factory registration is
    /// opaque to validate-on-build; resolving it is what proves it asks the root for no
    /// window's state. <c>McpServerWorker</c> and <c>WindowsAppNotifications</c> are the
    /// head's own types, which no test project can reference; what they take is the
    /// provider and singletons, and the Debug head's own validated start covers them.
    /// </para>
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void With_scopes_validated_the_maui_head_builds_and_its_root_workers_resolve()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head and its DPAPI credential store are Windows-only.");
            return;
        }

        using var provider = BuildMauiShaped(validateScopes: true);

        Type[] rootResolved =
        [
            typeof(TaskSyncWorker),
            typeof(Backlog.Infrastructure.Sync.Sessions.SessionSyncWorker),
            typeof(Backlog.Infrastructure.Sync.Annotations.AnnotationSyncWorker),
            typeof(Backlog.Infrastructure.Sync.Notifications.NotificationSyncWorker),
            typeof(Backlog.Infrastructure.Sync.Notifications.NotificationNudgeListener),
            typeof(BackupWorker),
            typeof(Backlog.Modules.Tasks.Features.SyncLinkedTasks.LinkedTaskSyncWorker),
            typeof(BlockedTaskWatcher),
            typeof(Backlog.Modules.Notifications.Services.NotificationsStartup),
            typeof(Backlog.Infrastructure.Devbook.DevbookDatabaseRefresher),
            typeof(BrainFolderWatch),
            typeof(Backlog.Modules.Notifications.UI.NotificationCentreRequests),
            typeof(Backlog.Modules.Notifications.Abstractions.INotificationCentre),
            typeof(Backlog.Modules.Sessions.Abstractions.ISessionsSurfaceActivator),
            typeof(IMcpEndpointSource)
        ];

        foreach (var service in rootResolved)
        {
            Assert.NotNull(provider.GetRequiredService(service));
        }
    }

    /// <summary>
    /// Every factory-registered singleton of the product resolves from the root under
    /// validated scopes. Validate-on-build cannot see inside a factory, so a singleton
    /// built as <c>sp =&gt; new X(sp.GetRequiredService&lt;…&gt;())</c> that reached a
    /// window's state would pass the build and keep whichever window's instance it was
    /// first handed: the captive dependency scoped window state makes possible. The
    /// scope check refusing a resolution is exactly that; anything else a factory
    /// throws in a test process is not this test's business.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void No_singleton_factory_reaches_a_windows_state_from_the_root()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head and its DPAPI credential store are Windows-only.");
            return;
        }

        var services = BuildMauiShapedServices();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var captive = new List<string>();
        var singletonFactories = services
            .Where(descriptor => descriptor.Lifetime == ServiceLifetime.Singleton
                && !descriptor.IsKeyedService
                && descriptor.ImplementationFactory is not null
                && !descriptor.ServiceType.ContainsGenericParameters
                && descriptor.ServiceType.Namespace?.StartsWith("Backlog", StringComparison.Ordinal) == true)
            .Select(descriptor => descriptor.ServiceType)
            .Distinct()
            .ToList();

        Assert.NotEmpty(singletonFactories);

        foreach (var service in singletonFactories)
        {
            try
            {
                _ = provider.GetService(service);
            }
            catch (InvalidOperationException error) when (error.Message.Contains("from root provider", StringComparison.Ordinal))
            {
                captive.Add($"{service.FullName}: {error.Message}");
            }
#pragma warning disable CA1031 // Only the scope check's refusal is under test here.
            catch (Exception)
            {
            }
#pragma warning restore CA1031
        }

        Assert.True(captive.Count == 0, "A singleton reaches a window's state from the root:\n" + string.Join("\n", captive));
    }

    /// <summary>
    /// One set of window state per window (local ADR 0043, §5): each scope, as each
    /// window's BlazorWebView opens one, gets its own panes' state, and inside a window
    /// every pane, the footer and the toast tray ask for the same one. The data under
    /// them is the process's: the task change signal is one object for every window.
    /// </summary>
    [Fact]
    public void The_maui_head_holds_each_windows_state_in_its_scope()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head and its DPAPI credential store are Windows-only.");
            return;
        }

        using var provider = BuildMauiShaped(validateScopes: true);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var tasks = first.ServiceProvider.GetRequiredService<TasksDesktopState>();
        Assert.Same(tasks, first.ServiceProvider.GetRequiredService<TasksDesktopState>());
        Assert.Same(tasks, first.ServiceProvider.GetRequiredService<ISaveStatusSource>());
        Assert.NotSame(tasks, second.ServiceProvider.GetRequiredService<TasksDesktopState>());
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<InboxDesktopState>(),
            second.ServiceProvider.GetRequiredService<InboxDesktopState>());
        Assert.Same(
            first.ServiceProvider.GetRequiredService<ToastChannel>(),
            first.ServiceProvider.GetRequiredService<IToastChannel>());
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<ToastChannel>(),
            second.ServiceProvider.GetRequiredService<ToastChannel>());
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<FeedbackReportChannel>(),
            second.ServiceProvider.GetRequiredService<FeedbackReportChannel>());
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<AppUpdateWindowRequests>(),
            second.ServiceProvider.GetRequiredService<AppUpdateWindowRequests>());
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<Backlog.Modules.Inbox.Abstractions.Services.IInboxPlanDrafter>(),
            second.ServiceProvider.GetRequiredService<Backlog.Modules.Inbox.Abstractions.Services.IInboxPlanDrafter>());
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<Backlog.Modules.Inbox.Abstractions.Services.IInboxTriageAdvisor>(),
            second.ServiceProvider.GetRequiredService<Backlog.Modules.Inbox.Abstractions.Services.IInboxTriageAdvisor>());
        Assert.Same(
            first.ServiceProvider.GetRequiredService<ITaskChangeSignal>(),
            second.ServiceProvider.GetRequiredService<ITaskChangeSignal>());

        Assert.IsType<DpapiDeviceCredentialStore>(provider.GetRequiredService<IDeviceCredentialStore>());
        Assert.IsType<DevToolService>(provider.GetRequiredService<IDevToolService>());
    }

    /// <summary>
    /// The sync merge the head composes reads a <c>sprint-series</c> document an older
    /// desktop left on the replica and drops it (local ADR 0035): nothing is applied,
    /// nothing is counted as unreadable, and it never lands as a draft task.
    /// </summary>
    [Fact]
    public async Task The_maui_head_drops_a_retired_sprint_series_document()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head and its DPAPI credential store are Windows-only.");
            return;
        }

        using var provider = BuildMauiShaped(validateScopes: false);
        var id = Guid.NewGuid();
        var document = $$"""{"version":1,"id":"{{id:D}}","name":"Platform","repositories":["backlog"]}""";
        var change = new Backlog.Modules.Sync.Abstractions.DataTransferObjects.TaskChange(
            id,
            new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero),
            DeletedAt: null,
            new Backlog.Modules.Sync.Abstractions.DataTransferObjects.TaskPayload(
                "Platform", document, "sprint-series", "draft", "medium", 0, null,
                new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero),
                null, null, null, null, null, null, null, null, null, null, null,
                [], [], [], [], [], []));

        var merged = await provider.GetRequiredService<TaskReplicaMerge>().ApplyAsync(
            [new Backlog.Modules.Sync.Abstractions.DataTransferObjects.TaskChangeRecord(change, Guid.NewGuid(), 1)],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, merged.Applied);
        Assert.Equal(0, merged.Skipped);
        Assert.Null(await provider
            .GetRequiredService<Backlog.Modules.Tasks.ITaskRepository>()
            .GetIncludingDeletedAsync(id, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The head keeps one My Day preferences store behind both ports (local ADR 0032,
    /// §2), and the sync merge it composes hands a <c>my-day-preferences</c> document to
    /// it: the switch another desktop turned off reads off here, on the screen's port.
    /// </summary>
    [Fact]
    public async Task The_maui_head_takes_synced_my_day_preferences_into_its_one_store()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head and its DPAPI credential store are Windows-only.");
            return;
        }

        using var provider = BuildMauiShaped(validateScopes: false);
        var preferences = provider.GetRequiredService<IMyDayPreferences>();
        Assert.Same(preferences, provider.GetRequiredService<IMyDayPreferencesReplication>());
        Assert.True(preferences.ClearsOvernight);

        var stamp = new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
        var change = MyDayReplicaDocuments.ToChange(
            new Backlog.Modules.Tasks.Abstractions.DataTransferObjects.MyDayPreferencesCopyDto(
                """{"updatedAt":"2026-10-15T08:00:00Z","clearsOvernight":false}""", stamp));

        var merged = await provider.GetRequiredService<TaskReplicaMerge>().ApplyAsync(
            [new Backlog.Modules.Sync.Abstractions.DataTransferObjects.TaskChangeRecord(change, Guid.NewGuid(), 1)],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, merged.Applied);
        Assert.False(preferences.ClearsOvernight);
    }

    /// <summary>
    /// The head keeps one iteration settings store behind the screens' port and the
    /// replication port (local ADR 0036, §3), so the length the Goals view sets is the
    /// document the sync sends — and it is seeded from the shell navigation file an
    /// earlier build kept the choice in (§5).
    /// </summary>
    [Fact]
    public async Task The_maui_head_keeps_the_iteration_in_its_one_synced_store()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head and its DPAPI credential store are Windows-only.");
            return;
        }

        using var provider = BuildMauiShaped(validateScopes: false);
        var store = provider.GetRequiredService<IterationSettingsStore>();
        Assert.Same(store, provider.GetRequiredService<Backlog.Modules.Progress.Abstractions.Services.IIterationSettingsReplication>());
        Assert.Null(await ((Backlog.Modules.Progress.Abstractions.Services.IIterationSettingsReplication)store).ReadAsync(TestContext.Current.CancellationToken));

        provider.GetRequiredService<Backlog.Modules.Progress.Abstractions.Services.IIterationSettings>()
            .SetLength(Backlog.Modules.Progress.Abstractions.IterationLength.TwoWeeks, new DateOnly(2026, 10, 21));

        Assert.Equal(new IterationChoice("two-weeks", new DateOnly(2026, 10, 19)), store.Choice);
        Assert.Equal(Path.Combine(_appData, IterationSettingsStore.FileName), store.SettingsPath);
    }

    /// <summary>
    /// The sync merge the head composes hands an <c>iteration-settings</c> document to
    /// its one store (local ADR 0036, §4): two weeks chosen on another desktop reads as
    /// two weeks here, on the screens' port.
    /// </summary>
    [Fact]
    public async Task The_maui_head_takes_synced_iteration_settings_into_its_one_store()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head and its DPAPI credential store are Windows-only.");
            return;
        }

        using var provider = BuildMauiShaped(validateScopes: false);
        var settings = provider.GetRequiredService<Backlog.Modules.Progress.Abstractions.Services.IIterationSettings>();
        Assert.Equal(Backlog.Modules.Progress.Abstractions.IterationLength.Week, settings.Length);

        var stamp = new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
        var change = IterationReplicaDocuments.ToChange(
            new Backlog.Modules.Progress.Abstractions.DataTransferObjects.IterationSettingsCopyDto(
                """{"updatedAt":"2026-10-20T08:00:00Z","length":"two-weeks","twoWeekStart":"2026-10-19"}""", stamp));

        var merged = await provider.GetRequiredService<TaskReplicaMerge>().ApplyAsync(
            [new Backlog.Modules.Sync.Abstractions.DataTransferObjects.TaskChangeRecord(change, Guid.NewGuid(), 1)],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, merged.Applied);
        Assert.Equal(Backlog.Modules.Progress.Abstractions.IterationLength.TwoWeeks, settings.Length);
        Assert.Equal(new DateOnly(2026, 10, 19), settings.TwoWeekStart);
    }

    /// <summary>
    /// The roadmap's actual hours are answered by the adapter over the agent activity,
    /// and one instance serves every window, as the activity source it reads does.
    /// </summary>
    [Fact]
    public void The_maui_head_answers_the_roadmaps_actual_hours_from_agent_activity()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head and its DPAPI credential store are Windows-only.");
            return;
        }

        using var provider = BuildMauiShaped(validateScopes: false);
        using var scope = provider.CreateScope();

        var hours = provider.GetRequiredService<IRoadmapActualHours>();

        Assert.IsType<RoadmapActualHours>(hours);
        Assert.Same(hours, scope.ServiceProvider.GetRequiredService<IRoadmapActualHours>());
    }

    /// <summary>
    /// The Dashboard's hours worked are answered by the adapter over the same agent
    /// activity (local ADR 0019, §7), one instance for every window, and the module's
    /// derivation over it resolves in a window's scope.
    /// </summary>
    [Fact]
    public void The_maui_head_answers_the_dashboards_hours_worked_from_agent_activity()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head and its DPAPI credential store are Windows-only.");
            return;
        }

        using var provider = BuildMauiShaped(validateScopes: false);
        using var scope = provider.CreateScope();

        var hours = provider.GetRequiredService<IHoursWorkedSource>();

        Assert.IsType<AgentActivityHoursWorkedSource>(hours);
        Assert.Same(hours, scope.ServiceProvider.GetRequiredService<IHoursWorkedSource>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IHoursWorkedInsights>());
    }

    /// <summary>
    /// Every service the head's inline Devbook block used to register still resolves,
    /// now that <c>AddDevbookModule</c> registers them through the shared composition —
    /// and this head's choices survive the move: the domain store is one object per
    /// window, and the Copilot launcher behind the Devbook offers is the one that
    /// starts the CLI.
    /// </summary>
    [Fact]
    public void The_maui_head_resolves_every_devbook_service()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head and its DPAPI credential store are Windows-only.");
            return;
        }

        using var provider = BuildMauiShaped(validateScopes: false);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        foreach (var service in DevbookModuleServices.All)
        {
            Assert.NotNull(first.ServiceProvider.GetRequiredService(service));
        }

        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<DomainDevbookStore>(),
            second.ServiceProvider.GetRequiredService<DomainDevbookStore>());
        Assert.IsType<ArchifyDiagramArtifacts>(provider.GetRequiredService<IDiagramArtifactSource>());
        Assert.IsType<ProcessCopilotCliLauncher>(provider.GetRequiredService<ICopilotCliLauncher>());
    }

    /// <summary>
    /// The Brain beside the repositories (local ADR 0026, ADR 0027): its source is a
    /// port of its own over the Brain setting, the one repository folder source is
    /// still the repository's, and the watch that ties the setting to the refresher
    /// resolves and follows the setting once started.
    /// </summary>
    [Fact]
    public void The_maui_head_composes_the_brain_beside_the_repository_devbook()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head and its DPAPI credential store are Windows-only.");
            return;
        }

        using var provider = BuildMauiShaped(validateScopes: false);

        var brain = provider.GetRequiredService<IBrainFolderSource>();
        Assert.IsType<BrainFolderSource>(brain);
        Assert.IsType<DevbookFolderSource>(provider.GetRequiredService<IDevbookFolderSource>());
        Assert.Single(provider.GetServices<IDevbookFolderSource>());

        var folder = Path.Combine(_appData, "Brain");
        Assert.Null(provider.GetRequiredService<IBrainSettings>().TrySetBrainFolder(folder));
        Assert.Equal(folder, brain.BrainRoot);

        var watch = provider.GetRequiredService<BrainFolderWatch>();
        Assert.Same(watch, provider.GetRequiredService<BrainFolderWatch>());
        watch.Start();
        Assert.Equal(folder, watch.WatchedRoot);

        // Show hidden is the device's, kept beside show archived in the shell's
        // navigation file, never in the Brain folder or its setting.
        var showHidden = provider.GetRequiredService<IBrainShowHidden>();
        Assert.IsType<BrainShowHidden>(showHidden);
        Assert.Same(showHidden, provider.GetRequiredService<IBrainShowHidden>());
        showHidden.SetShown(true);
        Assert.True(provider.GetRequiredService<ShellNavigationStore>().BrainHiddenShown);
    }

    [SupportedOSPlatform("windows")]
    private ServiceProvider BuildMauiShaped(bool validateScopes) =>
        BuildMauiShapedServices().BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = validateScopes
        });

    [SupportedOSPlatform("windows")]
    private IServiceCollection BuildMauiShapedServices() => MauiShapedServices(_appData);

    /// <summary>The MAUI head's container as <c>MauiProgram</c> composes it, every
    /// per-user file under <paramref name="appData"/>; shared with
    /// <see cref="SurfaceWindowPagesTests"/>, which serves pages over it.</summary>
    [SupportedOSPlatform("windows")]
    internal static IServiceCollection MauiShapedServices(string appData)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.AddServiceDefaults();

        builder.Services.AddDesktopComposition(new DesktopCompositionOptions
        {
            DeviceIdentity = new DeviceIdentityStore(Path.Combine(appData, "device.json")),
            DeviceCredentialStore = _ => new DpapiDeviceCredentialStore(Path.Combine(appData, "device-credential.json")),
            TaskSyncStateStore = _ => new FileTaskSyncStateStore(Path.Combine(appData, "task-sync-state.json")),
            SessionSyncFolder = appData,
            AnnotationSyncFolder = appData,
            BackupStatePath = Path.Combine(appData, "backup-state.json"),
            SyncServiceSettingsPath = Path.Combine(appData, "sync-service.json"),
            FeatureSettings = _ => new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(appData, "features.json")),
            WorkingHoursSettings = _ => new WorkingHoursSettingsStore(Path.Combine(appData, "working-hours.json")),
            UsageResetSettings = _ => new UsageResetSettingsStore(Path.Combine(appData, "usage-reset.json")),
            SpendBudgetSettings = _ => new SpendBudgetSettingsStore(Path.Combine(appData, "spend-budgets.json")),
            PlanningVelocitySettings = sp => new PlanningVelocitySettingsStore(
                Path.Combine(appData, "planning-velocity.json"),
                time: null,
                sp.GetRequiredService<WorkingHoursSettingsStore>()),
            MyDayPreferences = _ => new MyDayPreferencesStore(Path.Combine(appData, MyDayPreferencesStore.FileName)),
            IterationSettings = sp => new IterationSettingsStore(
                Path.Combine(appData, IterationSettingsStore.FileName), sp.GetRequiredService<ShellNavigationStore>()),
            ShellNavigation = _ => new ShellNavigationStore(Path.Combine(appData, ShellNavigationStore.FileName)),
            CaptureSourceSettings = _ => new CaptureSourcesSettingsStore(Path.Combine(appData, "capture-sources.json")),
            CaptureRunLog = _ => new CaptureRunLogStore(Path.Combine(appData, "capture-runs.json")),
            CaptureTargetLedger = _ => new CaptureTargetLedgerStore(Path.Combine(appData, "capture-targets.json")),
            ConnectedTargets = _ => new ConnectedTargetsSettingsStore(Path.Combine(appData, "connected-targets.json")),
            SpecManagerTokenStore = _ => new DpapiSpecManagerTokenStore(Path.Combine(appData, "spec-manager-credentials.json")),
            InboxRoutingRules = _ => new InboxRoutingRulesStore(Path.Combine(appData, "inbox-routing-rules.json")),
            GitHubSettings = root => new GitHubSettingsStore(Path.Combine(appData, "github.settings.json"), root),
            PullRequestPins = _ => new PullRequestPinsStore(Path.Combine(appData, "pull-request-pins.json")),
            ClaudeSettings = _ => new ClaudeSettingsStore(Path.Combine(appData, "claude.settings.json")),
            AzureFoundrySettings = _ => new Backlog.Infrastructure.AzureFoundry.AzureFoundrySettingsStore(Path.Combine(appData, "azure-foundry.json")),
            BrainSettings = _ => new BrainSettingsStore(Path.Combine(appData, BrainSettingsStore.FileName)),
            FolderEditorLauncher = _ => new VsCodeFolderEditorLauncher(),
            // MsixAppUpdateService is the head's and compiled for Windows only.
            AppUpdateService = _ => new UnsupportedAppUpdateService(),
            CopilotCliLauncher = _ => new ProcessCopilotCliLauncher(),
            DevToolService = sp => new DevToolService(
                sp.GetRequiredService<ITaskStore>(),
                sp.GetRequiredService<IMcpEndpointSource>(),
                sp.GetService<ILogger<DevToolService>>()),
            SessionRepositoryResolver = sp => new SettingsSessionRepositoryResolver(sp.GetRequiredService<GitHubSettingsStore>()),
            WindowStateLifetime = ServiceLifetime.Scoped
        });

        // The workspace store the composition registers reads the real per-user
        // settings; the container is the subject here, not the person's workspace,
        // so a later registration points it at a folder of the test's own.
        builder.Services.AddSingleton(new WorkspaceSettingsStore(appData, Path.Combine(appData, "settings.json"), _ => null));

        AddMauiHeadRegistrations(builder.Services);

        return builder.Services;
    }

    /// <summary>What <c>MauiProgram</c> registers beside the shared composition: the
    /// MCP endpoint the tools adapter reads, the notification centre's request channel,
    /// the pop-out pages' link tokens, and the cost client. There is no Devbook
    /// block here any more: <c>AddDesktopComposition</c> calls <c>AddDevbookModule</c>
    /// (issue #738), so neither the head nor this restatement of it registers one.</summary>
    private static void AddMauiHeadRegistrations(IServiceCollection services)
    {
        // DesktopMcpEndpointSource over McpServerWorker is the head's own; the
        // stand-in answers the same port so the tools adapter can be composed.
        services.TryAddSingleton<IMcpEndpointSource>(new SwitchedOffMcpEndpoint());

        // The channel a press on a Windows notification opens the centre through,
        // registered by the head beside WindowsAppNotifications.
        services.TryAddSingleton<Backlog.Modules.Notifications.UI.NotificationCentreRequests>();

        // The link tokens the MCP listener admits a browser to its pop-out pages by,
        // registered by the head beside McpServerWorker (local ADR 0043 §7).
        services.TryAddSingleton<Backlog.Desktop.UI.Mcp.WindowLinkTokens>();

        // What open_window opens and links to, registered by the head beside them: no
        // OS window can open in a test, so its windows are a stand-in; the link names
        // the port the listener is configured on.
        services.TryAddSingleton<Backlog.Infrastructure.Mcp.ISurfaceWindowHost>(sp => new Backlog.Desktop.UI.Mcp.DesktopSurfaceWindowHost(
            new NoShellWindows(),
            sp.GetRequiredService<Backlog.Desktop.UI.Mcp.WindowLinkTokens>(),
            () => sp.GetRequiredService<WorkspaceSettingsStore>().McpServerPort));

        Backlog.Infrastructure.AzureFoundry.AzureFoundryRegistration.AddAzureFoundryCostClient(services);
    }

    /// <summary>The head's windows, which a test cannot open.</summary>
    private sealed class NoShellWindows : Backlog.Desktop.UI.Shell.IShellWindows
    {
        public void Open(string surfaceId, IReadOnlyDictionary<string, string>? query = null)
        {
        }

        public Task<bool> OpenAsync(string surfaceId, IReadOnlyDictionary<string, string>? query = null) => Task.FromResult(false);

        public void Close(string windowKey)
        {
        }

        public void SetTitle(string windowKey, string title)
        {
        }

        public void ActivateMainWindow()
        {
        }
    }

    private sealed class SwitchedOffMcpEndpoint : IMcpEndpointSource
    {
        public bool Enabled => false;

        public int Port => 0;

        public string? Unavailable => null;

        public bool IsListening => false;

        public Uri? Endpoint => null;

        public string? SetPort(int port) => null;

        public void Retry()
        {
        }

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public string EnsureToken() => string.Empty;

        public string? CurrentToken => null;
    }
}
