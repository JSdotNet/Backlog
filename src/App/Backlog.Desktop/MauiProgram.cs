using Backlog.Desktop.Services;
using Backlog.Desktop.UI.Inbox;
using Backlog.Desktop.UI.Tasks;
using Backlog.Desktop.UI.Devbook;
using Backlog.Desktop.UI.AppUpdate;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Desktop.UI.Shell;
using Backlog.Aspire.ServiceDefaults;
using Backlog.SharedKernel;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Extensions;
using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Extensions;
using Backlog.Modules.Inbox;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Extensions;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.Extensions;
using Backlog.Infrastructure.FileSystem.Dashboard;
using Backlog.Infrastructure.FileSystem.Inbox;
using Backlog.Infrastructure.FileSystem.Logging;
using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Infrastructure.Sqlite.Inbox;
using Backlog.Infrastructure.Sqlite.Roadmap;
using Backlog.Modules.Dashboard.Extensions;
using Backlog.Modules.Dashboard.UI.Extensions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI.Extensions;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Capture.Extensions;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Devbook;
using Backlog.Infrastructure.Sync;
using Backlog.Infrastructure.Sync.Annotations;
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
    public static MauiApp CreateMauiApp()
    {
        ConfigureWebView2RemoteDebugging();

        // Before any store exists over the AppData folder: a packaged install
        // that was writing into its redirected LocalCache until the manifest
        // opted out finds its state in the folder the app names, not an empty one.
        var adoption = PackagedAppDataAdoption.Run();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.AddServiceDefaults();
        // The one log sink an installed build has. The sync and backup workers
        // catch whatever a cycle throws, put one sentence on screen and write the
        // exception to the log - and until this line the installed app had no
        // log, only the Debug provider below, which is compiled out of it. A
        // second PC reporting "could not be reached, try again in a moment" with
        // nothing anywhere to say why is what this fixes. Under the workspace's
        // own app-data folder - Backlog.Debug for a debug head, Backlog for the
        // installed one - so a checkout run beside the installed app never writes
        // into its file; the Settings page says where.
        builder.Logging.AddFileLogging(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            WorkspaceSettingsStore.DefaultAppDataFolderName,
            "logs"));
        // The workspace settings file, and the two module ports the adapters
        // over it answer. The knowledge resolver is what both ports share, so
        // neither context has to see the other's settings.
        builder.Services.AddSingleton<WorkspaceSettingsStore>();

        // Devbook read from a repository branch, for a repository nobody has
        // cloned. The network half — one listing per commit, one blob per file
        // somebody opens — lives in the GitHub adapter and the disk half here;
        // the cache root arrives as a delegate rather than as the workspace
        // store, because the GitHub adapter may not see this one.
        builder.Services.AddSingleton<IGitHubBranchCatalog>(sp => new GitHubBranchCatalog(
            sp.GetRequiredService<ResolvingGitHubTransport>()));
        builder.Services.AddSingleton<IGitHubTreeClient>(sp => new GitHubTreeClient(
            sp.GetRequiredService<ResolvingGitHubTransport>()));
        builder.Services.AddSingleton<IDevbookSnapshotCache>(sp => new DevbookSnapshotCache(
            () => sp.GetRequiredService<WorkspaceSettingsStore>().DevbookCacheDirectory,
            sp.GetRequiredService<IGitHubTreeClient>(),
            sp.GetRequiredService<IGitHubBranchCatalog>()));

        builder.Services.AddSingleton<IDevbookFolderSource>(sp => new DevbookFolderSource(
            sp.GetRequiredService<GitHubSettingsStore>(),
            sp.GetRequiredService<WorkspaceSettingsStore>(),
            sp.GetRequiredService<IDevbookSnapshotCache>()));
        builder.Services.AddSingleton<ITaskStore>(sp => new WorkspaceTaskStore(
            sp.GetRequiredService<WorkspaceSettingsStore>()));
        // Which hours the reader means to be working. A kernel port rather than a
        // dashboard one: the settings screen writes it and the dashboard shades a
        // grid with it, and neither may reach through the other. Its own per-user
        // file beside the two above, for the same reason theirs are not in
        // settings.json.
        builder.Services.AddSingleton<IWorkingHoursSettings, WorkingHoursSettingsStore>();
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
        // The change signal is the module's singleton, registered by AddTasksModule
        // below; the repository resolves it lazily here, so line order between the
        // two does not matter. It is what lets the sync loop push an edit seconds
        // after it is saved rather than on its five-minute tick.
        builder.Services.AddSingleton<ITaskRepository>(sp =>
            new RootedSqliteTaskRepository(
                () => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory,
                sp.GetService<ITaskChangeSignal>()));
        builder.Services.AddTasksModule();

        // The same arrangement for the plan: the Roadmap module brings its use
        // cases, and the host picks the adapter. One document row in the same
        // database the tasks use, following the same folder, so moving the storage
        // folder moves the plan with the backlog rather than leaving it behind.
        builder.Services.AddSingleton<IRoadmapPlanRepository>(sp =>
            new RootedSqliteRoadmapPlanRepository(() => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
        builder.Services.AddRoadmapModule();

        // The same arrangement for capture: the module brings the run, and the host
        // decides where the monitored sources are kept — its own per-user file
        // beside the choices above, for the same reason theirs are not in
        // settings.json — and where what past runs said is kept, a second file
        // beside it. The source adapters and the delivery come further down,
        // after the Inbox they deliver into.
        builder.Services.AddSingleton<ICaptureSourceSettings, CaptureSourcesSettingsStore>();
        builder.Services.AddSingleton<ICaptureRunLog, CaptureRunLogStore>();
        builder.Services.AddCaptureModule();

        // The two cross-context joins the plan takes part in, each a port a screen
        // owns and an adapter here answers because only an adapter may see both
        // contexts: the backlog's tag picker offers the plan's tags, and a roadmap
        // item rolls up the backlog entries and knowledge chapters it gathers. Both
        // capture services the modules register as Scoped, so they are Scoped too —
        // registered in one place both hosts share so the lifetimes cannot drift.
        builder.Services.AddRoadmapCrossContextAdapters();

        // The same arrangement for the inbox: the Inbox module brings its use
        // cases, and the host picks the adapter — three tables in the same
        // database the tasks use, following the same folder. One rooted store
        // answers both of the module's repository ports, registered once and
        // handed out under each, so the two cannot follow different roots.
        builder.Services.AddSingleton<RootedSqliteInboxRepository>(sp =>
            new RootedSqliteInboxRepository(() => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
        builder.Services.AddSingleton<IInboxItemRepository>(sp => sp.GetRequiredService<RootedSqliteInboxRepository>());
        builder.Services.AddSingleton<IInboxOrganizerRepository>(sp => sp.GetRequiredService<RootedSqliteInboxRepository>());
        builder.Services.AddInboxModule();

        // The cross-context join routing takes part in: the Inbox's backlog
        // target, answered by an adapter over Tasks' published port because only
        // an adapter may see both contexts. Scoped, for the reason the roadmap
        // adapters are, and after AddTasksModule() for the same reason.
        builder.Services.AddInboxCrossContextAdapters();

        // The other join the Inbox takes part in: Capture's feed readers and the
        // delivery that hands what they found to the Inbox's intake, answered by
        // adapters because neither module may see the other. After
        // AddInboxModule() for the intake the delivery captures.
        builder.Services.AddCaptureAdapters();
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
        // And annotation replication's progress file, in that same folder for the
        // same reasons. The remarks themselves are the Devbook annotation store's,
        // under the storage folder with the rest of the person's data.
        builder.Services.AddAnnotationSyncStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog"));
        // What the last backup did, in that same folder and for the same reason:
        // per-installation bookkeeping, never the workspace root. The worker
        // reads the repository and the schedule off the workspace settings and
        // uploads through the same GitHub client the feedback dialog commits
        // screenshots with.
        builder.Services.AddSingleton<IBackupStateStore>(_ => new FileBackupStateStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog",
            "backup-state.json")));
        builder.Services.AddSingleton<BackupWorker>();
        // Where the sync service is, asked per client rather than fixed here.
        // Under the AppHost it is "https+http://sync", which the service discovery
        // AddServiceDefaults wired up rewrites to this run's sync resource - ports
        // are dynamic and a literal one would be wrong by the next launch. The
        // installed app is never launched that way, and for it that name is a DNS
        // lookup that fails; so the URL on the Settings page comes first, then
        // BACKLOG_SYNC_URL, and the discovery name is what is left. The settings
        // file sits beside the credential above, per machine and never in the
        // workspace. Task replication is the second call and not part of the
        // first: it needs the ITaskRepository this head registers, and a head
        // without one composes only the pairing surface.
        builder.Services.AddSingleton<SyncServiceSettingsStore>();
        builder.Services.AddSingleton<SyncServiceEndpoint>();
        builder.Services.AddSyncClient(SyncServiceAddress);
        builder.Services.AddTaskSyncClient(SyncServiceAddress);
        builder.Services.AddSingleton<AzureFoundrySettingsStore>();
        // The chat client's pipeline is the adapter's own, sized for a completion
        // rather than for the service-to-service defaults AddServiceDefaults
        // puts on every other client — see AzureFoundryRegistration.
        builder.Services.AddAzureFoundryChatClient();
        // The bill for the same resource, read from Azure Cost Management with
        // the developer sign-in on this machine. Reports itself unavailable until
        // a cost scope is in Settings, so it is safe to register unconditionally.
        builder.Services.AddAzureFoundryCostClient();
        // The Inbox's plan drafter over the same chat client. Singleton here, where
        // the web harness registers it Scoped, because that is the lifetime the
        // chain above it actually has in this host: InboxDesktopState is a
        // singleton, and everything it reaches through IInboxItems - the handlers,
        // and this - is resolved once from the root and kept for the window's
        // life whatever its registration says. A Scoped registration would only
        // hide that; naming it keeps the one HttpClient the drafter holds the same
        // captive it already was, and no more so than the one Home.razor injects
        // for the window's own AI question.
        builder.Services.AddSingleton<IInboxPlanDrafter, AzureFoundryInboxPlanDrafter>();
        // The embedding deployment beside the chat one. Registered and never
        // called in this change: local ADR 0004's semantic tier is wired and
        // dormant, and the thing that would join it up - writing vectors into
        // _meta/devbook.db - belongs to the Node generator, which is the only
        // writer that file has.
        builder.Services.AddHttpClient<IAzureFoundryEmbeddingsClient, AzureFoundryEmbeddingsClient>();
        builder.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        builder.Services.AddSingleton<IGitFileHistoryService, GitFileHistoryService>();
        builder.Services.AddSingleton<IGitHubClient>(sp => new GitHubClient(sp.GetRequiredService<ResolvingGitHubTransport>()));
        builder.Services.AddSingleton<ICopilotUsageClient>(sp => new CopilotUsageClient(sp.GetRequiredService<ResolvingGitHubTransport>()));

        // The three GitHub clients the dashboard reads. Identity is shared by the other
        // two: the activity client filters to the signed-in author, and the billing client
        // chooses between the user and organization endpoints by the same login, so
        // neither needs a setting for it.
        builder.Services.AddSingleton<IGitHubIdentityClient>(sp => new GitHubIdentityClient(sp.GetRequiredService<ResolvingGitHubTransport>()));
        // The detail cache is what keeps a dashboard read from re-fetching every
        // pull request it already read. Its folder is beside the per-user
        // settings and never under the backlog root - see ActivityCacheDirectory.
        builder.Services.AddSingleton<IPullRequestDetailCache>(sp => new PullRequestDetailCache(
            () => sp.GetRequiredService<WorkspaceSettingsStore>().ActivityCacheDirectory));
        // And the listing cache is what keeps it from re-walking the pages that
        // found those pull requests. Same folder, so forgetting a repository is
        // one gesture that drops both.
        builder.Services.AddSingleton<IActivityListingCache>(sp => new ActivityListingCache(
            () => sp.GetRequiredService<WorkspaceSettingsStore>().ActivityCacheDirectory));
        builder.Services.AddSingleton<IGitHubActivityClient>(sp => new GitHubActivityClient(
            sp.GetRequiredService<ResolvingGitHubTransport>(),
            sp.GetRequiredService<IPullRequestDetailCache>(),
            sp.GetRequiredService<IActivityListingCache>()));
        // Counts only, over the search API, for the stretches of history the
        // detailed client is too expensive to walk.
        builder.Services.AddSingleton<IGitHubActivityBaselineClient>(sp => new GitHubActivityBaselineClient(
            sp.GetRequiredService<ResolvingGitHubTransport>()));
        // Settled Copilot months and settled Claude days are kept beside each other
        // under the spend cache - see SpendCacheDirectory for why it is a folder of
        // its own - so a seven-month trend costs the running month and nothing else.
        builder.Services.AddSingleton<IAiCreditUsageCache>(sp => new AiCreditUsageCache(
            () => sp.GetRequiredService<WorkspaceSettingsStore>().SpendCacheDirectory));
        builder.Services.AddSingleton<IGitHubBillingClient>(sp => new GitHubBillingClient(
            sp.GetRequiredService<ResolvingGitHubTransport>(),
            sp.GetRequiredService<IGitHubIdentityClient>(),
            sp.GetRequiredService<GitHubSettingsStore>(),
            sp.GetRequiredService<IAiCreditUsageCache>()));

        // Claude usage reporting is registered unconditionally; it reports
        // itself unavailable until an Admin API key is configured, and the
        // "usage-metrics" feature decides whether anything asks it.
        builder.Services.AddSingleton<ClaudeSettingsStore>();
        builder.Services.AddHttpClient<IClaudeTransport, ClaudeAdminTransport>();
        builder.Services.AddSingleton<IClaudeUsageClient>(sp => new ClaudeUsageClient(
            sp.GetRequiredService<IClaudeTransport>(),
            sp.GetRequiredService<ClaudeSettingsStore>()));
        builder.Services.AddSingleton<IClaudeCodeUsageCache>(sp => new ClaudeCodeUsageCache(
            () => sp.GetRequiredService<WorkspaceSettingsStore>().SpendCacheDirectory));

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
        // One per window, and this head has one window: the error screen asks
        // the footer's dialog to open through it.
        builder.Services.AddSingleton<FeedbackReportChannel>();
        builder.Services.AddSingleton<DesignDevbookProvider>();
        builder.Services.AddSingleton<AiDevbookProvider>();
        builder.Services.AddSingleton<TechnologyDevbookService>();
        builder.Services.AddSingleton<DevbookAtlasService>();
        // Retrieval, both tiers. Adapters over the generated database rather than
        // over the Markdown: search is the one capability ADR 0004's ladder does
        // not let degrade to a corpus scan, so where there is no database these
        // report that in words instead of answering slowly or answering nothing.
        builder.Services.AddSingleton<IDevbookSearch>(sp =>
            new DevbookFullTextSearch(sp.GetRequiredService<IDevbookFolderSource>()));
        builder.Services.AddSingleton<IDevbookVectorSearch>(sp =>
            new DevbookSemanticSearch(sp.GetRequiredService<IDevbookFolderSource>(), DevbookEmbeddingModel.Default));
        builder.Services.AddSingleton<InstructionSourceDiscovery>();
        builder.Services.AddSingleton<DevbookMenu>();
        builder.Services.AddSingleton<ICopilotCliLauncher, ProcessCopilotCliLauncher>();
        builder.Services.AddSingleton<TasksCopilotCli>();
        builder.Services.AddSingleton<DevbookCopilotCli>();
        // The shared diagram component asks for this optionally, so registering it
        // is what switches Archify artifacts on for the app at all. Everything it
        // answers — the flag, which clone the chapters came from, whether a CLI is
        // installed — is the host's to know, which is why the library only asks.
        builder.Services.AddSingleton<IDiagramArtifactSource, ArchifyDiagramArtifacts>();
        builder.Services.AddSingleton<DevbookScope>();
        builder.Services.AddSingleton<DevbookUpdateService>();

        // Shared by the Devbook pane and the settings screen, and a singleton so
        // the branch list somebody fetched in one is already there in the other.
        builder.Services.AddSingleton<DevbookSourceSelection>();
        builder.Services.AddSingleton<TasksDesktopState>();
        // The Inbox pane's state, on the same terms as TasksDesktopState: one
        // window, one user, one object that outlives the page it is drawn on.
        builder.Services.AddSingleton<InboxDesktopState>();
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
        builder.Services.AddSingleton<DevbookFolderOpenService>();
        builder.Services.AddSingleton<Arc42DevbookStore>();
        // The C4 model beside the architecture chapters. Registered next to the
        // arc42 store because it answers the same scope question against the same
        // clone; it reads its own feature key and hands back nothing when that key
        // is off, so registering it does not turn it on.
        builder.Services.AddSingleton<C4DevbookStore>();
        builder.Services.AddSingleton<DevbookChapterWriter>();
        builder.Services.AddSingleton(sp => new DomainDevbookStore(sp.GetRequiredService<IDevbookFolderSource>()));
        // A person's remarks on Devbook chapters: one JSON file per repository
        // under the storage folder, following the root the way the inbox store
        // does so a moved backlog takes its remarks along. The panels resolve this
        // by interface and fall back to a session-scoped store when it is absent,
        // which is why leaving this line out would not fail — it would only forget.
        builder.Services.AddSingleton<IDevbookAnnotationStore>(sp =>
            new DevbookAnnotationStore(() => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));

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
        // its own call because a head can have a task database and no session
        // readers; it answers to the same Sync switch as the task loop.
        builder.Services.AddSessionSyncClient(SyncServiceAddress);

        // Annotation replication, the third exchange over the same token pipeline:
        // it pushes what the annotation store above changed and applies what the
        // other desktops did. Its own call for the reason the session one is —
        // a head can have a task database and no Devbook — and it answers to the
        // same Sync switch as the other two loops.
        builder.Services.AddAnnotationSyncClient(SyncServiceAddress);

        // What a transcript's parsed runs are kept in, so an activity read parses only
        // the transcripts that have changed. Beside the per-user settings and never
        // under the backlog root - see ActivityCacheDirectory: ADR 0005 syncs the
        // workspace, and a per-machine parse cache travelling to another device is
        // exactly the hazard.
        builder.Services.AddSingleton<IAgentActivityCache>(sp => new AgentActivityCache(
            () => sp.GetRequiredService<WorkspaceSettingsStore>().SessionActivityCacheDirectory));
        // The other pass over the same transcripts: the folder, branch and turn count
        // the session list reads. Same folder, same reasons, forgotten together.
        builder.Services.AddSingleton<ITranscriptFactsCache>(sp => new TranscriptFactsCache(
            () => sp.GetRequiredService<WorkspaceSettingsStore>().SessionActivityCacheDirectory));
        // Which registered clone a session's working folder lies inside, read off
        // the same repository list the Repositories screen writes. The session
        // readers stamp the answer on each local session so a header scoped to one
        // repository can hold the Claude sessions running in its clone — Claude
        // records none itself.
        builder.Services.AddSingleton<ISessionRepositoryResolver>(sp =>
            new SettingsSessionRepositoryResolver(sp.GetRequiredService<GitHubSettingsStore>()));

        // When those sessions were actually producing, read out of the bodies of the
        // transcripts the call above only stats. A separate call because it is a
        // separate port: asking for the session list must not be the same thing as
        // asking for hundreds of megabytes to be parsed. It picks up the cache
        // registered above through GetService, so a host that composed none would
        // still be correct and only slower.
        //
        // This machine's transcripts and no others, unlike the session list beside it:
        // a replicated session record says what another environment did, and the file
        // its runs would have to be parsed out of never left that machine.
        builder.Services.AddAgentActivitySource();

        // The join between the two contexts: the Dashboard's sessions part reports on
        // what the Sessions context reads. Only an infrastructure adapter may see both,
        // so the registration is there rather than in either module — and it comes after
        // AddDashboardModule(), AddAgentSessionSource() and AddAgentActivitySource(),
        // whose ports it sits between.
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

        adoption.Log(app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PackagedAppDataAdoption)));

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

        // And annotation replication's loop, the third sibling, on the same terms.
        _ = app.Services.GetRequiredService<AnnotationSyncWorker>();

        // And the backup loop, on the same terms: a timer that only existed
        // while the Storage tab was open would miss every slot it was set for.
        _ = app.Services.GetRequiredService<BackupWorker>();

        return app;
    }

    /// <summary>The base-address callback the three sync registrations share, so
    /// they cannot disagree about which service this head is talking to.</summary>
    private static Uri SyncServiceAddress(IServiceProvider services) =>
        services.GetRequiredService<SyncServiceEndpoint>().Resolve().Address;

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
