using Backlog.Desktop.Services;
using Backlog.Desktop.UI.Tasks;
using Backlog.Desktop.UI.Knowledge;
using Backlog.Desktop.UI.AppUpdate;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Desktop.UI.Shell;
using Backlog.Aspire.ServiceDefaults;
using Backlog.SharedKernel;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Knowledge.Abstractions;
using Backlog.Modules.Tasks.Extensions;
using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Extensions;
using Backlog.Infrastructure.FileSystem.Dashboard;
using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Infrastructure.Sqlite.Roadmap;
using Backlog.Modules.Dashboard.Extensions;
using Backlog.Modules.Dashboard.UI.Extensions;
using Backlog.Modules.Sessions.UI.Extensions;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sync;
using Backlog.Infrastructure.Sync.Extensions;
using Backlog.Infrastructure.Sync.Sessions;
using Backlog.UI.Components.Feedback;
using Backlog.UI.Components.Diagrams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Backlog.Desktop;

public static class MauiProgram
{
    /// <summary>Names the client the branch-archive download uses, so it gets a
    /// handler of its own rather than sharing a general-purpose one whose
    /// timeout is set for request-response calls.</summary>
    private const string GitHubArchiveHttpClient = "github-archive";

    public static MauiApp CreateMauiApp()
    {
        ConfigureWebView2RemoteDebugging();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.AddServiceDefaults();
        // The workspace settings file, and the two module ports the adapters
        // over it answer. The knowledge resolver is what both ports share, so
        // neither context has to see the other's settings.
        builder.Services.AddSingleton<WorkspaceSettingsStore>();

        // Knowledge read from a repository branch, for a repository nobody has
        // cloned. The download half lives in the GitHub adapter and the disk
        // half here; the cache root arrives as a delegate rather than as the
        // workspace store, because the GitHub adapter may not see this one.
        builder.Services.AddHttpClient(GitHubArchiveHttpClient);
        builder.Services.AddSingleton<IGitHubBranchCatalog>(sp => new GitHubBranchCatalog(
            sp.GetRequiredService<ResolvingGitHubTransport>()));
        builder.Services.AddSingleton<IGitHubArchiveClient>(sp => new GitHubArchiveClient(
            sp.GetRequiredService<IGitHubCredentialResolver>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(GitHubArchiveHttpClient),
            () => sp.GetRequiredService<GitHubSettingsStore>().Current.ApiEndpoint));
        builder.Services.AddSingleton<IKnowledgeSnapshotCache>(sp => new KnowledgeSnapshotCache(
            () => sp.GetRequiredService<WorkspaceSettingsStore>().KnowledgeCacheDirectory,
            sp.GetRequiredService<IGitHubArchiveClient>(),
            sp.GetRequiredService<IGitHubBranchCatalog>()));

        builder.Services.AddSingleton<IKnowledgeFolderSource>(sp => new KnowledgeFolderSource(
            sp.GetRequiredService<GitHubSettingsStore>(),
            sp.GetRequiredService<WorkspaceSettingsStore>(),
            sp.GetRequiredService<IKnowledgeSnapshotCache>()));
        builder.Services.AddSingleton<ITaskStore>(sp => new WorkspaceTaskStore(
            sp.GetRequiredService<WorkspaceSettingsStore>()));
        // How often the list re-reads a store somebody else may have written to.
        // Its own per-user file beside the feature choices, for the same reason
        // theirs is not in settings.json.
        builder.Services.AddSingleton<ITasksRefreshSettings, TasksRefreshSettingsStore>();
        // Which surface the shell was last showing, so it reopens there instead
        // of always defaulting to the workspace panes.
        builder.Services.AddSingleton<ShellNavigationStore>();
        // Which machine this installation is: minted once into device.json beside the
        // settings above, and stable across restarts and renames. Two contexts read it —
        // Sessions stamps every record it finds with it and the Dashboard offers it as a
        // filter — and neither of them owns the answer, which is why it is a kernel port.
        // Constructed here, at startup, rather than handed to the container as a factory:
        // the identity belongs to the installation, not to whichever surface first asks
        // for it, so device.json exists from the first start whether or not the Dashboard
        // or Sessions is ever opened — the pairing that ADR 0005 attaches to this id will
        // need it before any pane does. Constructed rather than resolved for a second
        // reason: the store offers three public constructors — the per-user location, an
        // explicit path, and an explicit path and machine name — and letting the container
        // choose between them would make which file the real installation writes to
        // depend on a rule nobody reading this line can see.
        builder.Services.AddSingleton<IDeviceIdentitySource>(new DeviceIdentityStore());

        // Composition: the Tasks module brings its own use cases, and the host
        // decides which adapter is behind them. The repository follows the
        // storage folder rather than being pinned to wherever it was at startup,
        // because somebody can move their backlog while the app is open.
        builder.Services.AddSingleton<ITaskRepository>(sp =>
            new RootedSqliteTaskRepository(() => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
        builder.Services.AddTasksModule();

        // The same arrangement for the plan: the Roadmap module brings its use
        // cases, and the host picks the adapter. One document row in the same
        // database the tasks use, following the same folder, so moving the storage
        // folder moves the plan with the backlog rather than leaving it behind.
        builder.Services.AddSingleton<IRoadmapPlanRepository>(sp =>
            new RootedSqliteRoadmapPlanRepository(() => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
        builder.Services.AddRoadmapModule();

        // The two cross-context joins the plan takes part in, each a port a screen
        // owns and an adapter here answers because only an adapter may see both
        // contexts: the backlog's tag picker offers the plan's tags, and a roadmap
        // item rolls up the backlog entries and knowledge chapters it gathers. Both
        // capture services the modules register as Scoped, so they are Scoped too —
        // registered in one place both hosts share so the lifetimes cannot drift.
        builder.Services.AddRoadmapCrossContextAdapters();
        // Half of a repository's configuration is workspace data and lives under
        // the backlog folder, so it follows that folder the way the task database
        // and the roadmap plan already do: the root is read per call rather than
        // pinned at startup, and moving the workspace re-reads the registry
        // instead of leaving the old one's repositories on screen.
        builder.Services.AddSingleton(sp =>
        {
            var workspace = sp.GetRequiredService<WorkspaceSettingsStore>();
            var store = new GitHubSettingsStore(GitHubSettingsStore.DefaultLocalPath, () => workspace.RootDirectory);
            workspace.RootChanged += store.Reload;
            return store;
        });
        // Which credential a call leaves with is decided per call, against the
        // settings as they are at that moment: a repository bound to an account goes
        // out as that account, and never as whoever `gh` happens to be switched to.
        // The gh CLI is a source of credentials here as well as a way of sending, so
        // the account source is registered once and shared — its token cache and its
        // account list are the things "Check the connection" invalidates.
        builder.Services.AddSingleton<IGhCliAccountSource>(_ => new GhCliAccountSource());
        builder.Services.AddSingleton<IGitHubCredentialResolver>(sp => new GitHubCredentialResolver(
            sp.GetRequiredService<GitHubSettingsStore>(),
            sp.GetRequiredService<IGhCliAccountSource>()));
        builder.Services.AddSingleton(sp => new ResolvingGitHubTransport(
            sp.GetRequiredService<GitHubSettingsStore>(),
            credentials: sp.GetRequiredService<IGitHubCredentialResolver>(),
            accounts: sp.GetRequiredService<IGhCliAccountSource>()));
        builder.Services.AddSingleton<IGitHubConnectionProbe>(sp => sp.GetRequiredService<ResolvingGitHubTransport>());
        // The catalog is the shell's product copy; the store is the adapter that
        // remembers the choices. Composing the two is the host's job.
        builder.Services.AddSingleton<IAppFeatureSettings>(_ => new AppFeatureSettingsStore(AppFeatures.All));
        // The device half of cloud sync. DPAPI is the store ADR 0005 asks for on
        // Windows, and this head is the Windows one; the credential lands beside the
        // app's other per-user state rather than in the backlog folder, because it
        // belongs to this machine and not to the workspace.
        builder.Services.AddSingleton<IDeviceCredentialStore>(_ => new DpapiDeviceCredentialStore());
        // And this device's replication progress, beside that credential and for
        // the same reason: LocalApplicationData, never the workspace root. The
        // watermark and the cursor describe how far *this machine* has got, and a
        // workspace root can be pointed at a folder some other product syncs — at
        // which point the other device would adopt this one's watermark and skip
        // its own unpushed work, silently. Removing that failure is why ADR 0005
        // exists. Plaintext because neither value is a secret; AddSyncClient
        // registers no store of its own, so a head that skips this line has the
        // session registered and unconstructable.
        builder.Services.AddSingleton<ITaskSyncStateStore>(_ => new FileTaskSyncStateStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog",
            "task-sync-state.json")));
        // Session replication's two files, in that same folder and for the same
        // reasons - per-user, per-installation, never the workspace root. Two
        // stores rather than one because a session save that corrupted a shared
        // file would reset the task watermark above and re-push the whole machine;
        // one call because where they go is the only part the host knows.
        builder.Services.AddSessionSyncStores(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog"));
        // "https+http://sync" is resolved by Aspire service discovery, which
        // AddServiceDefaults above wired up, so the desktop always talks to the sync
        // service of this AppHost run. Ports are dynamic; a literal one would be
        // wrong by the next launch. Task replication is the second call and not
        // part of the first: it needs the ITaskRepository this head registers,
        // and a head without one composes only the pairing surface.
        builder.Services.AddSyncClient(new Uri("https+http://sync"));
        builder.Services.AddTaskSyncClient(new Uri("https+http://sync"));
        builder.Services.AddSingleton<AzureFoundrySettingsStore>();
        builder.Services.AddHttpClient<IAzureFoundryChatClient, AzureFoundryChatClient>();
        builder.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        builder.Services.AddSingleton<IGitFileHistoryService, GitFileHistoryService>();
        builder.Services.AddSingleton<IGitHubClient>(sp => new GitHubClient(sp.GetRequiredService<ResolvingGitHubTransport>()));
        builder.Services.AddSingleton<ICopilotUsageClient>(sp => new CopilotUsageClient(sp.GetRequiredService<ResolvingGitHubTransport>()));

        // The three GitHub clients the dashboard reads. Identity is shared by the other
        // two: the activity client filters to the signed-in author, and the billing client
        // chooses between the user and organization endpoints by the same login, so
        // neither needs a setting for it.
        builder.Services.AddSingleton<IGitHubIdentityClient>(sp => new GitHubIdentityClient(sp.GetRequiredService<ResolvingGitHubTransport>()));
        builder.Services.AddSingleton<IGitHubActivityClient>(sp => new GitHubActivityClient(sp.GetRequiredService<ResolvingGitHubTransport>()));
        builder.Services.AddSingleton<IGitHubBillingClient>(sp => new GitHubBillingClient(
            sp.GetRequiredService<ResolvingGitHubTransport>(),
            sp.GetRequiredService<IGitHubIdentityClient>(),
            sp.GetRequiredService<GitHubSettingsStore>()));

        // Claude usage reporting is registered unconditionally; it reports
        // itself unavailable until an Admin API key is configured, and the
        // "usage-metrics" feature decides whether anything asks it.
        builder.Services.AddSingleton<ClaudeSettingsStore>();
        builder.Services.AddHttpClient<IClaudeTransport, ClaudeAdminTransport>();
        builder.Services.AddSingleton<IClaudeUsageClient>(sp => new ClaudeUsageClient(
            sp.GetRequiredService<IClaudeTransport>(),
            sp.GetRequiredService<ClaudeSettingsStore>()));

        // The Dashboard module brings its derivations; the adapters beside it decide which
        // providers are behind them. Registered after the provider clients above, which is
        // all the adapters hold. Every part reports itself unavailable with a reason until
        // the credential it needs exists, so this is safe to register unconditionally.
        builder.Services.AddDashboardModule();
        builder.Services.AddDashboardAdapters();

        // Tasks' own adapter, registered here rather than beside
        // AddTasksModule() above because it reads the GitHub settings store and
        // that is only configured by this point. It is what lets an imported plan
        // resolve a `repo:` name against the repositories somebody has configured
        // — and register one it names that nobody has, per ADR 0007.
        builder.Services.AddTasksAdapters();

        builder.Services.AddSingleton<GitHubIntegration>();
        builder.Services.AddSingleton<FeedbackReporter>();
        builder.Services.AddSingleton<DesignKnowledgeProvider>();
        builder.Services.AddSingleton<TechnologyKnowledgeService>();
        builder.Services.AddSingleton<KnowledgeAtlasService>();
        builder.Services.AddSingleton<InstructionSourceDiscovery>();
        builder.Services.AddSingleton<KnowledgeMenu>();
        builder.Services.AddSingleton<ICopilotCliLauncher, ProcessCopilotCliLauncher>();
        builder.Services.AddSingleton<TasksCopilotCli>();
        builder.Services.AddSingleton<KnowledgeCopilotCli>();
        // The shared diagram component asks for this optionally, so registering it
        // is what switches Archify artifacts on for the app at all. Everything it
        // answers — the flag, which clone the chapters came from, whether a CLI is
        // installed — is the host's to know, which is why the library only asks.
        builder.Services.AddSingleton<IDiagramArtifactSource, ArchifyDiagramArtifacts>();
        builder.Services.AddSingleton<KnowledgeScope>();
        builder.Services.AddSingleton<KnowledgeUpdateService>();

        // Shared by the knowledge pane and the settings screen, and a singleton so
        // the branch list somebody fetched in one is already there in the other.
        builder.Services.AddSingleton<KnowledgeSourceSelection>();
        builder.Services.AddSingleton<TasksDesktopState>();
        // The band under every route reads the backlog's save state through the
        // library's own interface rather than reaching for the state class, so the
        // shell's footer never learns which module is the interesting one. Same
        // lifetime as the state itself: in MAUI there is one window and one user.
        builder.Services.AddSingleton<ISaveStatusSource>(sp => sp.GetRequiredService<TasksDesktopState>());
        // Where a screen puts a message it needs a reader to see, and where the
        // tray in MainLayout reads it back. Registered as the concrete type with
        // the interface forwarded to it so both sides resolve the same queue.
        builder.Services.AddSingleton<ToastChannel>();
        builder.Services.AddSingleton<IToastChannel>(sp => sp.GetRequiredService<ToastChannel>());
        builder.Services.AddSingleton<IFolderEditorLauncher, VsCodeFolderEditorLauncher>();
        builder.Services.AddSingleton<KnowledgeFolderOpenService>();
        builder.Services.AddSingleton<Arc42KnowledgeStore>();
        // The C4 model beside the architecture chapters. Registered next to the
        // arc42 store because it answers the same scope question against the same
        // clone; it reads its own feature key and hands back nothing when that key
        // is off, so registering it does not turn it on.
        builder.Services.AddSingleton<C4KnowledgeStore>();
        builder.Services.AddSingleton<KnowledgeChapterWriter>();
        builder.Services.AddSingleton(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));

        // The MSIX head can manage its own updates when packaged; it degrades to
        // an "unsupported" report when running unpackaged (e.g. Debug), so this is
        // safe to register unconditionally.
        builder.Services.AddSingleton<IAppUpdateService, MsixAppUpdateService>();
        builder.Services.AddSingleton<IDevToolService>(sp => new DevToolService(
            sp.GetRequiredService<ITaskStore>(),
            sp.GetService<ILogger<DevToolService>>()));

        // The session list reads the two agents' own folders in the profile of
        // whoever is signed in, so unlike the tool service above there is nothing
        // for a host to differ about and both hosts compose the same adapter. It
        // stamps what it finds with the device identity registered above.
        builder.Services.AddAgentSessionSource();

        // Session replication, on top of AddSyncClient above and after the readers
        // it pushes from: it reads this machine's sessions through the port that
        // call registers and contributes a second source to the same port for what
        // the other environments reported. Both lines are lazy factories, so the
        // order is for whoever reads this file rather than for the container. It is
        // its own call and its own feature key, because a person can want their
        // tasks on both machines and still not want a list of what their agents
        // have been doing leaving either one.
        builder.Services.AddSessionSyncClient(new Uri("https+http://sync"));

        // The join between the two: the Dashboard's sessions part reports on what the
        // Sessions context reads. Only an infrastructure adapter may see both, so the
        // registration is there rather than in either module — and it comes after both
        // AddDashboardModule() and AddAgentSessionSource(), whose ports it sits
        // between.
        builder.Services.AddDashboardCrossContextAdapters();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();

        // Which worktree this window came from, for the header. Debug builds are
        // the ones run out of a checkout, often several at once; an installed
        // build registers nothing and the header shows the version.
        if (DevelopmentWorkspace.Current is { } workspace)
        {
            builder.Services.AddSingleton(new DevelopmentWorkspaceLabel(workspace));
        }
#endif

        var app = builder.Build();

        // The background sync loop, asked for once and then left alone. A
        // singleton nobody resolves is a singleton that never runs, and this one
        // is nothing but a constructor that starts a timer - so without this line
        // task replication would happen only while somebody had the Devices
        // settings panel open and was pressing the button. It is deliberately not
        // an IHostedService: this head has no generic host to start one. See
        // TaskSyncWorker for the whole of that reasoning.
        _ = app.Services.GetRequiredService<TaskSyncWorker>();

        // And session replication's own loop, for the same reason and with the
        // same failure if it is left out. A sibling rather than a second exchange
        // inside the worker above: see SessionSyncWorker for why one loop over two
        // independently switchable features would have to run whenever either was
        // on, and would give the two one shared error to report.
        _ = app.Services.GetRequiredService<SessionSyncWorker>();

        return app;
    }

    private static void ConfigureWebView2RemoteDebugging()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string argument = "--remote-debugging-port=9222";
        var current = Environment.GetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", EnvironmentVariableTarget.Process);

        if (string.IsNullOrWhiteSpace(current))
        {
            Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", argument, EnvironmentVariableTarget.Process);
            return;
        }

        if (!current.Contains("--remote-debugging-port", StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable(
                "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
                $"{current} {argument}",
                EnvironmentVariableTarget.Process);
        }
    }
}
