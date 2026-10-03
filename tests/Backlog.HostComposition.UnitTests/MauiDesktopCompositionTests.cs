using System.Runtime.Versioning;
using Backlog.Aspire.ServiceDefaults;
using Backlog.Desktop.Composition;
using Backlog.Desktop.UI.AppUpdate;
using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.Devbook;
using Backlog.Infrastructure.DevPc;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sync;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Desktop.UI.Inbox;
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
/// credential store, the full tools adapter, and one window's state held as
/// singletons — together with what the head registers beside the call.
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
    }

    /// <summary>
    /// With scopes validated too, the head is refused for exactly one reason, and it
    /// is one <c>MauiProgram</c> chose and says so: the Tasks and Inbox panes' state
    /// is a singleton over the modules' scoped <c>ITaskItems</c> and
    /// <c>IInboxItems</c>, because this head has one window and the state outlives
    /// every page drawn on it. The harness registers the same state Scoped and passes
    /// with scopes validated (see <see cref="WebHarnessHostTests"/>).
    /// <para>
    /// Pinned rather than switched off, so a second captive dependency — a new
    /// singleton over a scoped service nobody meant to hold — fails here instead of
    /// hiding behind the two that are deliberate.
    /// </para>
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void With_scopes_validated_the_maui_head_is_refused_only_for_its_window_state()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head and its DPAPI credential store are Windows-only.");
            return;
        }

        var refused = Assert.Throws<AggregateException>(() => BuildMauiShaped(validateScopes: true).Dispose());

        string[] deliberate =
        [
            $"from singleton '{typeof(TasksDesktopState).FullName}'",
            $"from singleton '{typeof(InboxDesktopState).FullName}'"
        ];

        Assert.All(refused.InnerExceptions, error => Assert.True(
            error.Message.Contains("Cannot consume scoped service", StringComparison.Ordinal)
            && deliberate.Any(singleton => error.Message.Contains(singleton, StringComparison.Ordinal)),
            "A captive dependency other than the window state MauiProgram holds on purpose: " + error.Message));
    }

    /// <summary>
    /// One window, so the window's state is one object from the root — where
    /// every pane of that window, and the shell's footer and toast tray, ask for it.
    /// </summary>
    [Fact]
    public void The_maui_head_holds_one_windows_state_as_singletons()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head and its DPAPI credential store are Windows-only.");
            return;
        }

        using var provider = BuildMauiShaped(validateScopes: false);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var tasks = provider.GetRequiredService<TasksDesktopState>();
        Assert.Same(tasks, first.ServiceProvider.GetRequiredService<TasksDesktopState>());
        Assert.Same(tasks, second.ServiceProvider.GetRequiredService<TasksDesktopState>());
        Assert.Same(tasks, provider.GetRequiredService<ISaveStatusSource>());
        Assert.Same(provider.GetRequiredService<InboxDesktopState>(), second.ServiceProvider.GetRequiredService<InboxDesktopState>());
        Assert.Same(provider.GetRequiredService<ToastChannel>(), provider.GetRequiredService<IToastChannel>());

        Assert.IsType<DpapiDeviceCredentialStore>(provider.GetRequiredService<IDeviceCredentialStore>());
        Assert.IsType<DevToolService>(provider.GetRequiredService<IDevToolService>());
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

    [SupportedOSPlatform("windows")]
    private ServiceProvider BuildMauiShaped(bool validateScopes)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.AddServiceDefaults();

        builder.Services.AddDesktopComposition(new DesktopCompositionOptions
        {
            DeviceIdentity = new DeviceIdentityStore(Path.Combine(_appData, "device.json")),
            DeviceCredentialStore = _ => new DpapiDeviceCredentialStore(Path.Combine(_appData, "device-credential.json")),
            TaskSyncStateStore = _ => new FileTaskSyncStateStore(Path.Combine(_appData, "task-sync-state.json")),
            SessionSyncFolder = _appData,
            AnnotationSyncFolder = _appData,
            BackupStatePath = Path.Combine(_appData, "backup-state.json"),
            SyncServiceSettingsPath = Path.Combine(_appData, "sync-service.json"),
            FeatureSettings = _ => new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(_appData, "features.json")),
            WorkingHoursSettings = _ => new WorkingHoursSettingsStore(Path.Combine(_appData, "working-hours.json")),
            UsageResetSettings = _ => new UsageResetSettingsStore(Path.Combine(_appData, "usage-reset.json")),
            PlanningVelocitySettings = sp => new PlanningVelocitySettingsStore(
                Path.Combine(_appData, "planning-velocity.json"),
                time: null,
                sp.GetRequiredService<WorkingHoursSettingsStore>()),
            ShellNavigation = _ => new ShellNavigationStore(Path.Combine(_appData, "shell-navigation.json")),
            CaptureSourceSettings = _ => new CaptureSourcesSettingsStore(Path.Combine(_appData, "capture-sources.json")),
            CaptureRunLog = _ => new CaptureRunLogStore(Path.Combine(_appData, "capture-runs.json")),
            InboxRoutingRules = _ => new InboxRoutingRulesStore(Path.Combine(_appData, "inbox-routing-rules.json")),
            GitHubSettings = root => new GitHubSettingsStore(Path.Combine(_appData, "github.settings.json"), root),
            ClaudeSettings = _ => new ClaudeSettingsStore(Path.Combine(_appData, "claude.settings.json")),
            AzureFoundrySettings = _ => new Backlog.Infrastructure.AzureFoundry.AzureFoundrySettingsStore(Path.Combine(_appData, "azure-foundry.json")),
            FolderEditorLauncher = _ => new VsCodeFolderEditorLauncher(),
            // MsixAppUpdateService is the head's and compiled for Windows only.
            AppUpdateService = _ => new UnsupportedAppUpdateService(),
            CopilotCliLauncher = _ => new ProcessCopilotCliLauncher(),
            DevToolService = sp => new DevToolService(
                sp.GetRequiredService<ITaskStore>(),
                sp.GetRequiredService<IMcpEndpointSource>(),
                sp.GetService<ILogger<DevToolService>>()),
            SessionRepositoryResolver = sp => new SettingsSessionRepositoryResolver(sp.GetRequiredService<GitHubSettingsStore>()),
            WindowStateLifetime = ServiceLifetime.Singleton
        });

        // The workspace store the composition registers reads the real per-user
        // settings; the container is the subject here, not the person's workspace,
        // so a later registration points it at a folder of the test's own.
        builder.Services.AddSingleton(new WorkspaceSettingsStore(_appData, Path.Combine(_appData, "settings.json"), _ => null));

        AddMauiHeadRegistrations(builder.Services);

        return builder.Services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = validateScopes
        });
    }

    /// <summary>What <c>MauiProgram</c> registers beside the shared composition:
    /// the Devbook block issue #738 will move, the MCP endpoint the tools adapter
    /// reads, and the cost client.</summary>
    private static void AddMauiHeadRegistrations(IServiceCollection services)
    {
        services.AddSingleton<IDevbookSnapshotCache>(sp => new DevbookSnapshotCache(
            () => sp.GetRequiredService<WorkspaceSettingsStore>().DevbookCacheDirectory,
            sp.GetRequiredService<IGitHubTreeClient>(),
            sp.GetRequiredService<IGitHubBranchCatalog>()));
        services.AddSingleton(sp => DevbookDatabaseRefresher.StartForApp(
            () => sp.GetRequiredService<WorkspaceSettingsStore>().DevbookCacheDirectory,
            sp.GetService<ILogger<DevbookDatabaseRefresher>>()));
        services.AddSingleton<IDevbookFolderSource>(sp => new DevbookFolderSource(
            sp.GetRequiredService<GitHubSettingsStore>(),
            sp.GetRequiredService<WorkspaceSettingsStore>(),
            sp.GetRequiredService<IDevbookSnapshotCache>()));
        services.AddSingleton<Backlog.Desktop.UI.Devbook.DesignDevbookProvider>();
        services.AddSingleton<Backlog.Desktop.UI.Devbook.AiDevbookProvider>();
        services.AddSingleton<Backlog.Desktop.UI.Devbook.TechnologyDevbookService>();
        services.AddSingleton<Backlog.Desktop.UI.Devbook.DevbookAtlasService>();
        services.AddSingleton<IDevbookSearch>(sp =>
            new DevbookFullTextSearch(sp.GetRequiredService<IDevbookFolderSource>()));
        services.AddSingleton<IDevbookVectorSearch>(sp =>
            new DevbookSemanticSearch(sp.GetRequiredService<IDevbookFolderSource>(), DevbookEmbeddingModel.Default));
        services.AddSingleton<Backlog.Desktop.UI.Devbook.InstructionSourceDiscovery>();
        services.AddSingleton<Backlog.Desktop.UI.Devbook.DevbookMenu>();
        services.AddSingleton<Backlog.Desktop.UI.Devbook.DevbookCopilotCli>();
        services.AddSingleton<IDiagramArtifactSource, Backlog.Desktop.UI.Devbook.ArchifyDiagramArtifacts>();
        services.AddSingleton<Backlog.Desktop.UI.Devbook.DevbookScope>();
        services.AddSingleton<Backlog.Desktop.UI.Devbook.DevbookUpdateService>();
        services.AddSingleton<Backlog.Desktop.UI.Devbook.DevbookSourceSelection>();
        services.AddSingleton<Backlog.Desktop.UI.Devbook.DevbookFolderOpenService>();
        services.AddSingleton<Backlog.Desktop.UI.Devbook.Arc42DevbookStore>();
        services.AddSingleton<Backlog.Desktop.UI.Devbook.C4DevbookStore>();
        services.AddSingleton<Backlog.Desktop.UI.Devbook.DevbookChapterWriter>();
        services.AddSingleton(sp => new Backlog.Desktop.UI.Devbook.DomainDevbookStore(sp.GetRequiredService<IDevbookFolderSource>()));
        services.AddSingleton<IDevbookAnnotationStore>(sp =>
            new DevbookAnnotationStore(
                () => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory,
                folders: sp.GetRequiredService<IDevbookFolderSource>()));

        // DesktopMcpEndpointSource over McpServerWorker is the head's own; the
        // stand-in answers the same port so the tools adapter can be composed.
        services.TryAddSingleton<IMcpEndpointSource>(new SwitchedOffMcpEndpoint());

        Backlog.Infrastructure.AzureFoundry.AzureFoundryRegistration.AddAzureFoundryCostClient(services);
    }

    private sealed class SwitchedOffMcpEndpoint : IMcpEndpointSource
    {
        public bool Enabled => false;

        public int Port => 0;

        public string? Unavailable => null;

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public string EnsureToken() => string.Empty;
    }
}
