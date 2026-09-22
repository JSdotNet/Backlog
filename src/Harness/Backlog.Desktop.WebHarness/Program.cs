using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Capture.Extensions;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.Copilot;
using Backlog.Desktop.UI.Inbox;
using Backlog.Desktop.UI.Tasks;
using Backlog.Desktop.UI.Devbook;
using Backlog.Desktop.UI.AppUpdate;
using Backlog.Desktop.UI.Shell;
using Backlog.Modules.DevPc.Abstractions;
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
using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Infrastructure.Sqlite.Inbox;
using Backlog.Infrastructure.Sqlite.Roadmap;
using Backlog.Modules.Dashboard.Extensions;
using Backlog.Modules.Dashboard.UI.Extensions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI.Extensions;
using Backlog.Modules.Roadmap.UI;
using Backlog.Modules.DevPc.UI;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Devbook;
using Backlog.Infrastructure.Sync;
using Backlog.Infrastructure.Sync.Annotations;
using Backlog.Infrastructure.Sync.Extensions;
using Backlog.Infrastructure.Sync.Sessions;
using Backlog.UI.Components.Diagrams;
using Backlog.UI.Components.Feedback;
using Backlog.Desktop.WebHarness;
using Backlog.Desktop.WebHarness.Components;
using Backlog.Aspire.ServiceDefaults;

// The deployment and API key the harness seeds for the local azure-foundry-test
// service. The key is a marker, not a credential: it is how a later session
// recognises a stored configuration as its own seed rather than a person's.
const string LocalAzureFoundryDeployment = "local-ai";
const string LocalAzureFoundryApiKeyMarker = "local-development";
// The scope the seed reads spend for. The stand-in service answers any scope
// with the same canned bill, so the value only has to look like one.
const string LocalAzureFoundryCostScope = "/subscriptions/local-development/resourceGroups/local/providers/Microsoft.CognitiveServices/accounts/local-ai";

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The workspace settings file, and the two module ports the adapters over it
// answer. The knowledge resolver is what both ports share, so neither context
// has to see the other's settings.
builder.Services.AddSingleton<WorkspaceSettingsStore>();

// Devbook read from a repository branch, for a repository nobody has cloned.
// The network half — one listing per commit, one blob per file somebody opens —
// lives in the GitHub adapter and the disk half in the file system one; the
// cache root arrives as a delegate rather than as the workspace store, because
// the GitHub adapter may not see that one.
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
// Which hours the reader means to be working — written on the settings screen,
// read by the dashboard to shade a grid. Scoped to the content root like the
// harness's other settings files, so a session here never rewrites the real
// per-user choice.
builder.Services.AddSingleton<IWorkingHoursSettings>(
    _ => CreateLocalDevelopmentWorkingHoursSettingsStore(builder.Environment.ContentRootPath));
// Which surface the shell was last showing. Scoped to the content root like the
// harness's other settings files, so a session here never rewrites the real
// per-user choice.
builder.Services.AddSingleton(_ => CreateLocalDevelopmentShellNavigationStore(builder.Environment.ContentRootPath));
// Which machine this installation is. Scoped to the content root like the harness's
// other settings files — and here that is more than tidiness: several worktrees serve
// this harness at once, and one shared identity file would put the first-write race
// across processes on every parallel start. A harness is a development host, so a
// per-worktree identity is the right answer rather than a compromise. Constructed at
// startup rather than on first use, as the desktop host does: the identity belongs to
// the installation, so device.json exists from the first start whether or not a
// surface that reads it is ever opened.
builder.Services.AddSingleton<IDeviceIdentitySource>(
    CreateLocalDevelopmentDeviceIdentityStore(builder.Environment.ContentRootPath));

// Composition: the Tasks module brings its own use cases, and the host decides
// which adapter is behind them. The repository follows the storage folder rather
// than being pinned to wherever it was at startup.
// The change signal is the module's singleton, registered by AddTasksModule
// below; the repository resolves it lazily here, so line order between the
// two does not matter. It is what lets the sync loop push an edit seconds
// after it is saved rather than on its five-minute tick.
builder.Services.AddSingleton<ITaskRepository>(sp =>
    new RootedSqliteTaskRepository(
        () => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory,
        sp.GetService<ITaskChangeSignal>()));
builder.Services.AddTasksModule();

// The same arrangement for the plan: the Roadmap module brings its use cases, and
// the host picks the adapter. One document row in the same database the tasks use,
// following the same folder.
builder.Services.AddSingleton<IRoadmapPlanRepository>(sp =>
    new RootedSqliteRoadmapPlanRepository(() => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
builder.Services.AddRoadmapModule();
// The plan behind the shell's Ask AI port, after the module so the scoped
// planning port it holds exists. The other areas register theirs beside
// their own state below; the Roadmap has no state, only the port.
builder.Services.AddRoadmapAiContentSource();

// The same arrangement for capture: the module brings the run, and the host picks
// where the monitored sources are kept and where what past runs said is kept.
// Both scoped to the content root like the harness's other settings files, so a
// session here never rewrites the real per-user choice or its log.
builder.Services.AddSingleton<ICaptureSourceSettings>(
    _ => CreateLocalDevelopmentCaptureSourcesSettingsStore(builder.Environment.ContentRootPath));
builder.Services.AddSingleton<ICaptureRunLog>(
    _ => CreateLocalDevelopmentCaptureRunLogStore(builder.Environment.ContentRootPath));
builder.Services.AddCaptureModule();

// The two cross-context joins the plan takes part in, answered by adapters that may
// see both contexts: the backlog's tag picker offers the plan's tags, and a roadmap
// item rolls up the backlog entries and knowledge chapters it gathers. Both capture
// services the modules register as Scoped, so they are Scoped too — registered in
// one place both hosts share so the lifetimes cannot drift apart.
builder.Services.AddRoadmapCrossContextAdapters();

// The same arrangement for the inbox: the Inbox module brings its use cases, and
// the host picks the adapter — three tables in the same database the tasks use,
// following the same folder. One rooted store answers both of the module's
// repository ports, registered once and handed out under each.
builder.Services.AddSingleton<RootedSqliteInboxRepository>(sp =>
    new RootedSqliteInboxRepository(() => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
builder.Services.AddSingleton<IInboxItemRepository>(sp => sp.GetRequiredService<RootedSqliteInboxRepository>());
builder.Services.AddSingleton<IInboxOrganizerRepository>(sp => sp.GetRequiredService<RootedSqliteInboxRepository>());
builder.Services.AddInboxModule();

// The cross-context join routing takes part in: the Inbox's backlog target,
// answered by an adapter over Tasks' published port because only an adapter may
// see both contexts. Scoped, for the reason the roadmap adapters are, and after
// AddTasksModule() for the same reason.
builder.Services.AddInboxCrossContextAdapters();

// The other join the Inbox takes part in: Capture's feed readers and the delivery
// that hands what they found to the Inbox's intake, answered by adapters because
// neither module may see the other. After AddInboxModule() for the intake the
// delivery captures.
builder.Services.AddCaptureAdapters();
// The same arrangement the desktop host makes: the shared registry follows the
// workspace root, and moving the workspace re-reads it.
builder.Services.AddSingleton(sp =>
{
    var workspace = sp.GetRequiredService<WorkspaceSettingsStore>();
    var store = CreateLocalDevelopmentGitHubSettingsStore(
        builder.Environment.ContentRootPath,
        () => workspace.RootDirectory);
    workspace.RootChanged += store.Reload;
    return store;
});
// The same arrangement the desktop host makes: the credential a call leaves with
// is decided per call, so a repository bound to an account goes out as that
// account rather than as whoever `gh` happens to be switched to.
builder.Services.AddSingleton<IGhCliAccountSource>(_ => new GhCliAccountSource());
builder.Services.AddSingleton<IGitHubCredentialResolver>(sp => new GitHubCredentialResolver(
    sp.GetRequiredService<GitHubSettingsStore>(),
    sp.GetRequiredService<IGhCliAccountSource>()));
builder.Services.AddSingleton(sp => new ResolvingGitHubTransport(
    sp.GetRequiredService<GitHubSettingsStore>(),
    credentials: sp.GetRequiredService<IGitHubCredentialResolver>(),
    accounts: sp.GetRequiredService<IGhCliAccountSource>()));
builder.Services.AddSingleton<IGitHubConnectionProbe>(sp => sp.GetRequiredService<ResolvingGitHubTransport>());
builder.Services.AddSingleton<IGitHubAccountProbe>(sp => sp.GetRequiredService<ResolvingGitHubTransport>());
builder.Services.AddSingleton<IAppFeatureSettings>(_ => CreateLocalDevelopmentFeatureSettingsStore(builder.Environment.ContentRootPath));
// The device half of cloud sync. Scoped to the content root like the harness's
// other settings files, so a session here pairs a device of its own rather than
// rewriting the real per-user credential — and so this harness and the mobile
// one are two devices under one owner, which is what pairing is for. The
// override variable is this harness's own for the same reason: one shared name
// would let a single setting collapse the pair back into one device.
builder.Services.AddSingleton(_ => DeviceCredentialStoreFactory.CreateLocalDevelopmentStore(
    builder.Environment.ContentRootPath,
    "BACKLOG_DESKTOP_DEVICE_CREDENTIAL_PATH",
    Path.Combine("obj", "local-development", "device-credential.json")));
// How far this device has pushed and pulled, scoped the same way and with an
// override variable of its own for the same reason: a shared name would let the
// two harnesses share a watermark, and each would then skip what the other had
// pushed - silently, because nothing about that fails.
builder.Services.AddSingleton<ITaskSyncStateStore>(_ => TaskSyncStateStoreFactory.CreateLocalDevelopmentStore(
    builder.Environment.ContentRootPath,
    "BACKLOG_DESKTOP_TASK_SYNC_STATE_PATH",
    Path.Combine("obj", "local-development", "task-sync-state.json")));
// Session replication's two files, scoped to this harness's content root the same
// way and with an override variable of its own for the same reason: a shared name
// would let two harnesses share a session watermark, and each would then skip what
// the other had pushed - silently, because nothing about that fails. A folder
// rather than a path, because two stores is an implementation detail of the
// exchange and where they live is not.
// What the last backup did, scoped to this harness's content root like the sync
// state above and for the same reason: a shared file would let two harnesses
// count each other's backups as their own.
builder.Services.AddSingleton<IBackupStateStore>(_ => new FileBackupStateStore(
    Environment.GetEnvironmentVariable("BACKLOG_DESKTOP_BACKUP_STATE_PATH") is { Length: > 0 } backupStatePath
        ? backupStatePath
        : Path.Combine(builder.Environment.ContentRootPath, "obj", "local-development", "backup-state.json")));
builder.Services.AddSingleton<BackupWorker>();
builder.Services.AddSessionSyncStores(
    Environment.GetEnvironmentVariable("BACKLOG_DESKTOP_SESSION_SYNC_PATH") is { Length: > 0 } sessionSyncFolder
        ? sessionSyncFolder
        : Path.Combine(builder.Environment.ContentRootPath, "obj", "local-development"));
// Annotation replication's progress file, scoped and overridable the same way
// and for the same reason: two harnesses sharing an annotation watermark would
// each skip what the other had pushed.
builder.Services.AddAnnotationSyncStore(
    Environment.GetEnvironmentVariable("BACKLOG_DESKTOP_ANNOTATION_SYNC_PATH") is { Length: > 0 } annotationSyncFolder
        ? annotationSyncFolder
        : Path.Combine(builder.Environment.ContentRootPath, "obj", "local-development"));
// Where the sync service is, resolved the way the desktop head resolves it so the
// Settings page behaves the same here: a URL entered there, then BACKLOG_SYNC_URL,
// then "https+http://sync", which Aspire service discovery rewrites to this
// AppHost run's sync resource. Under Aspire with nothing entered that is the
// address this harness always used. The settings file is this harness's own,
// under its content root and with an override variable of its own, for the
// reason the credential above is: a shared file would point both harnesses at
// whatever one of them was told. Task replication is the second call and not
// part of the first: it needs the ITaskRepository above, and a host without one
// composes only the pairing surface.
builder.Services.AddSingleton(_ => new SyncServiceSettingsStore(
    Environment.GetEnvironmentVariable("BACKLOG_DESKTOP_SYNC_SERVICE_SETTINGS_PATH") is { Length: > 0 } syncSettingsPath
        ? syncSettingsPath
        : Path.Combine(builder.Environment.ContentRootPath, "obj", "local-development", "sync-service.json")));
builder.Services.AddSingleton<SyncServiceEndpoint>();
builder.Services.AddSyncClient(SyncServiceAddress);
builder.Services.AddTaskSyncClient(SyncServiceAddress);
var azureFoundrySettings = CreateLocalDevelopmentAzureFoundrySettingsStore(builder.Environment.ContentRootPath);
builder.Services.AddSingleton(azureFoundrySettings);
// The chat client's pipeline is the adapter's own, sized for a completion rather
// than for the service-to-service defaults AddServiceDefaults puts on every other
// client — see AzureFoundryRegistration.
builder.Services.AddAzureFoundryChatClient();
// The bill for the same resource. When the settings are this session's local
// seed, the query goes to the stand-in service beside the chat one — with a
// token nothing signed, because the stand-in checks none — so the dashboard's
// Cost section has figures without an Azure sign-in. A person's own Foundry
// configuration keeps the real client, and their real bill.
AddAzureFoundryCostClient(builder.Services, azureFoundrySettings);
// The Inbox's plan drafter over the same chat client. Scoped, like the other
// port adapters the Inbox module takes: the handler that asks for it is
// scoped, and the typed client behind it is transient either way.
builder.Services.AddScoped<IInboxPlanDrafter, AzureFoundryInboxPlanDrafter>();
// The embedding deployment beside the chat one. Registered and never called in
// this change: local ADR 0004's semantic tier is wired and dormant, and the
// thing that would join it up - writing vectors into _meta/devbook.db -
// belongs to the Node generator, which is the only writer that file has.
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
// The detail cache is what keeps a dashboard read from re-fetching every pull
// request it already read. Its folder is beside the per-user settings and never
// under the backlog root - see ActivityCacheDirectory.
builder.Services.AddSingleton<IPullRequestDetailCache>(sp => new PullRequestDetailCache(
    () => sp.GetRequiredService<WorkspaceSettingsStore>().ActivityCacheDirectory));
// And the listing cache is what keeps it from re-walking the pages that found
// those pull requests. Same folder, so forgetting a repository is one gesture
// that drops both.
builder.Services.AddSingleton<IActivityListingCache>(sp => new ActivityListingCache(
    () => sp.GetRequiredService<WorkspaceSettingsStore>().ActivityCacheDirectory));
builder.Services.AddSingleton<IGitHubActivityClient>(sp => new GitHubActivityClient(
    sp.GetRequiredService<ResolvingGitHubTransport>(),
    sp.GetRequiredService<IPullRequestDetailCache>(),
    sp.GetRequiredService<IActivityListingCache>()));
// Counts only, over the search API, for the stretches of history the detailed
// client is too expensive to walk.
builder.Services.AddSingleton<IGitHubActivityBaselineClient>(sp => new GitHubActivityBaselineClient(
    sp.GetRequiredService<ResolvingGitHubTransport>()));
// Settled Copilot months and settled Claude days are kept beside each other
// under the spend cache - see SpendCacheDirectory for why it is a folder of its
// own - so a seven-month trend costs the running month and nothing else.
builder.Services.AddSingleton<IAiCreditUsageCache>(sp => new AiCreditUsageCache(
    () => sp.GetRequiredService<WorkspaceSettingsStore>().SpendCacheDirectory));
builder.Services.AddSingleton<IGitHubBillingClient>(sp => new GitHubBillingClient(
    sp.GetRequiredService<ResolvingGitHubTransport>(),
    sp.GetRequiredService<IGitHubIdentityClient>(),
    sp.GetRequiredService<GitHubSettingsStore>(),
    sp.GetRequiredService<IAiCreditUsageCache>()));

// Claude usage reporting reports itself unavailable until an Admin API key is
// configured, so it is safe to register unconditionally.
builder.Services.AddSingleton(_ => CreateLocalDevelopmentClaudeSettingsStore(builder.Environment.ContentRootPath));
builder.Services.AddHttpClient<IClaudeTransport, ClaudeAdminTransport>();
builder.Services.AddSingleton<IClaudeAccountProbe>(sp => new ClaudeAccountProbe(sp.GetRequiredService<IClaudeTransport>()));
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
// AddTasksModule() above because it reads the GitHub settings store and that is
// only configured by this point. It is what lets an imported plan resolve a
// `repo:` name against the repositories somebody has configured — and register one
// it names that nobody has, per ADR 0007.
builder.Services.AddTasksAdapters();

builder.Services.AddSingleton<GitHubIntegration>();
builder.Services.AddSingleton<FeedbackReporter>();
// Scoped, unlike the reporter above it: a request to open the Report issue
// dialog belongs to the circuit that raised it, not to every tab on the harness.
builder.Services.AddScoped<FeedbackReportChannel>();
// This assembly's own pages, under Components/Pages: they exist only to be
// driven — the shipped app has no route that throws on request.
builder.Services.AddSingleton(new AdditionalRouteAssemblies([typeof(Program).Assembly]));
builder.Services.AddSingleton<DesignDevbookProvider>();
builder.Services.AddSingleton<AiDevbookProvider>();
builder.Services.AddSingleton<TechnologyDevbookService>();
builder.Services.AddSingleton<DevbookAtlasService>();
// Retrieval, both tiers. Adapters over the generated database rather than over
// the Markdown: search is the one capability ADR 0004's ladder does not let
// degrade to a corpus scan, so where there is no database these report that in
// words instead of answering slowly or answering nothing.
builder.Services.AddSingleton<IDevbookSearch>(sp =>
    new DevbookFullTextSearch(sp.GetRequiredService<IDevbookFolderSource>()));
builder.Services.AddSingleton<IDevbookVectorSearch>(sp =>
    new DevbookSemanticSearch(sp.GetRequiredService<IDevbookFolderSource>(), DevbookEmbeddingModel.Default));
builder.Services.AddSingleton<InstructionSourceDiscovery>();
builder.Services.AddSingleton<DevbookMenu>();
builder.Services.AddSingleton<Arc42DevbookStore>();
// The C4 model beside the architecture chapters. Registered next to the
// arc42 store because it answers the same scope question against the same
// clone; it reads its own feature key and hands back nothing when that key
// is off, so registering it does not turn it on.
builder.Services.AddSingleton<C4DevbookStore>();
builder.Services.AddSingleton<DevbookChapterWriter>();
// A person's remarks on Devbook chapters, under the storage folder with the rest
// of the person's data and following the root the way the inbox store does.
// Composed the same way in src/App/Backlog.Desktop/MauiProgram.cs.
builder.Services.AddSingleton<IDevbookAnnotationStore>(sp =>
    new DevbookAnnotationStore(() => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
builder.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
builder.Services.AddSingleton<DevbookFolderOpenService>();
builder.Services.AddSingleton(_ => TasksCopilotCli.Unavailable);
builder.Services.AddSingleton(_ => new DevbookCopilotCli(new UnavailableCopilotCliLauncher()));
// The shared diagram component asks for this optionally, so registering it is
// what switches Archify artifacts on for the harness at all. It takes the same
// unavailable launcher as its neighbour: this host cannot start a CLI, and
// pressing the offer says so rather than doing nothing.
builder.Services.AddSingleton<IDiagramArtifactSource>(sp => new ArchifyDiagramArtifacts(
    sp.GetRequiredService<IAppFeatureSettings>(),
    sp.GetRequiredService<IDevbookFolderSource>(),
    sp.GetRequiredService<GitHubSettingsStore>(),
    new UnavailableCopilotCliLauncher()));
builder.Services.AddSingleton<DevbookScope>();
// The open-chapter mirror the pane writes and the Ask AI source that pins
// from it, after the search and folder ports above that the source holds.
builder.Services.AddDevbookAiContentSource();
builder.Services.AddSingleton<DevbookUpdateService>();

// Shared by the Devbook pane and the settings screen, and a singleton so the
// branch list somebody fetched in one is already there in the other.
builder.Services.AddSingleton<DevbookSourceSelection>();
builder.Services.AddScoped<TasksDesktopState>();
// The backlog behind the shell's Ask AI port, beside the state it reads.
builder.Services.AddTasksAiContentSource();
// The Inbox pane's state, on the same terms as TasksDesktopState and for the
// same reason: it captures the module's scoped IInboxItems, and a singleton over
// a scoped service is a captive dependency validate-on-build refuses.
builder.Services.AddScoped<InboxDesktopState>();
// The Inbox behind the shell's Ask AI port, beside the state it reads.
builder.Services.AddInboxAiContentSource();
// The save-state band and the toast tray, both mounted by MainLayout under every
// route. Scoped rather than singleton, and that is forced rather than tidy: this
// host has one circuit per visitor, a singleton forwarding to a scoped
// TasksDesktopState is a captive dependency that throws on resolve, and a
// singleton channel would show one visitor's toasts to every other.
builder.Services.AddScoped<ISaveStatusSource>(sp => sp.GetRequiredService<TasksDesktopState>());
builder.Services.AddScoped<ToastChannel>();
builder.Services.AddScoped<IToastChannel>(sp => sp.GetRequiredService<ToastChannel>());
builder.Services.AddScoped(sp => new DomainDevbookStore(sp.GetRequiredService<IDevbookFolderSource>()));

// The web host never distributes or updates the desktop app, so it always
// reports updates as unsupported.
builder.Services.AddSingleton<IAppUpdateService, UnsupportedAppUpdateService>();
builder.Services.AddSingleton<IDevToolService, LocalDevelopmentDevToolService>();
// The tool catalog behind the shell's Ask AI port, beside the port it reads.
builder.Services.AddToolsAiContentSource();

// The session list reads the two agents' own folders in the profile of whoever is
// signed in, and the harness runs as that person on that machine — so unlike the
// tool service above there is nothing for a local-development variant to differ
// about, and both hosts compose the same adapter.
builder.Services.AddAgentSessionSource();

// Session replication, on top of AddSyncClient above and after the readers it
// pushes from: it reads this machine's sessions through the port that call
// registers and contributes a second source to the same port for what the other
// environments reported. Both lines are lazy factories, so the order is for
// whoever reads this file rather than for the container. Its own call because a
// head can have a task database and no session readers; it answers to the same
// Sync switch as the task loop.
builder.Services.AddSessionSyncClient(SyncServiceAddress);

// Annotation replication, the third exchange over the same token pipeline, on
// the same terms as the session one above and composed the same way in
// src/App/Backlog.Desktop/MauiProgram.cs.
builder.Services.AddAnnotationSyncClient(SyncServiceAddress);

// What a transcript's parsed runs are kept in, so an activity read parses only the
// transcripts that have changed. Beside the per-user settings and never under the
// backlog root - see ActivityCacheDirectory: ADR 0005 syncs the workspace, and a
// per-machine parse cache travelling to another device is exactly the hazard.
builder.Services.AddSingleton<IAgentActivityCache>(sp => new AgentActivityCache(
    () => sp.GetRequiredService<WorkspaceSettingsStore>().SessionActivityCacheDirectory));
// The other pass over the same transcripts: the folder, branch and turn count the
// session list reads. Same folder, same reasons, forgotten together.
builder.Services.AddSingleton<ITranscriptFactsCache>(sp => new TranscriptFactsCache(
    () => sp.GetRequiredService<WorkspaceSettingsStore>().SessionActivityCacheDirectory));
// Which registered clone a session's working folder lies inside, read off the
// same repository list the Repositories screen writes. The session readers
// stamp the answer on each local session so a header scoped to one repository
// can hold the Claude sessions running in its clone — Claude records none itself.
builder.Services.AddSingleton<ISessionRepositoryResolver>(sp =>
    new SettingsSessionRepositoryResolver(sp.GetRequiredService<GitHubSettingsStore>()));

// When those sessions were actually producing, read out of the bodies of the
// transcripts the call above only stats. A separate call because it is a separate
// port: asking for the session list must not be the same thing as asking for hundreds
// of megabytes to be parsed. It picks up the cache registered above through
// GetService, so a host that composed none would still be correct and only slower.
//
// This machine's transcripts and no others, unlike the session list beside it: a
// replicated session record says what another environment did, and the file its runs
// would have to be parsed out of never left that machine.
builder.Services.AddAgentActivitySource();

// The join between the two contexts: the Dashboard's sessions part reports on what the
// Sessions context reads. Only an infrastructure adapter may see both, so the
// registration is there rather than in either module — and it comes after
// AddDashboardModule(), AddAgentSessionSource() and AddAgentActivitySource(), whose
// ports it sits between.
builder.Services.AddDashboardCrossContextAdapters();

// Which worktree served this harness. It is only ever started from a checkout,
// so there is nothing to gate on beyond finding one — and when it is missing the
// header simply keeps showing the version.
if (DevelopmentWorkspace.Current is { } workspace)
{
    builder.Services.AddSingleton(new DevelopmentWorkspaceLabel(workspace));
}

var app = builder.Build();

// The background sync loop, asked for once and then left alone. A singleton
// nobody resolves is a singleton that never runs, and this one is nothing but a
// constructor that starts a timer - so without this line the harness would only
// replicate while somebody had the Devices settings panel open. Resolved the
// same way in src/App/Backlog.Desktop/MauiProgram.cs, and for the same reason
// there is no IHostedService here to do it instead: that head has no generic
// host to start one, and a loop only one of the two heads runs is a loop nobody
// can test against the harness.
_ = app.Services.GetRequiredService<TaskSyncWorker>();

// And session replication's own loop, for the same reason and with the same
// failure if it is left out. A sibling rather than a second exchange inside the
// worker above: see SessionSyncWorker for why one loop over two independently
// switchable features would have to run whenever either was on, and would give the
// two one shared error to report.
_ = app.Services.GetRequiredService<SessionSyncWorker>();

// And annotation replication's loop, the third sibling, on the same terms.
_ = app.Services.GetRequiredService<AnnotationSyncWorker>();

// And the backup loop, on the same terms: a timer that only existed while the
// Storage tab was open would miss every slot it was set for.
_ = app.Services.GetRequiredService<BackupWorker>();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Routes).Assembly);

app.Run();

static GitHubSettingsStore CreateLocalDevelopmentGitHubSettingsStore(string contentRootPath, Func<string> rootDirectory)
{
    var settingsPath = Environment.GetEnvironmentVariable("BACKLOG_GITHUB_SETTINGS_PATH");
    if (string.IsNullOrWhiteSpace(settingsPath))
    {
        settingsPath = Path.Combine(contentRootPath, "obj", "local-development", "github.settings.json");
    }

    // The machine half goes in the harness's own build output, so a development
    // session never touches a real per-user file; the shared registry goes where
    // the workspace root says, because that is what the app under test reads.
    var settings = new GitHubSettingsStore(settingsPath, rootDirectory);
    var repositoryRoot = ResolveRepositoryRoot(contentRootPath);
    if (repositoryRoot is null)
    {
        return settings;
    }

    const string alias = "backlog";
    settings.SetRepositories(
    [
        new GitHubRepositoryRef(alias, "JSdotNet", "Backlog")
        {
            CloneDirectory = repositoryRoot,
            DevbookFolders = DevbookFolderSetting.Defaults()
        }
    ]);

    foreach (var folder in DevbookFolderSetting.Defaults())
    {
        settings.SetDevbookFolder(alias, folder.Key, enabled: true, path: null);
    }

    return settings;
}

// The base-address callback the three sync registrations share, so they cannot
// disagree about which service this harness is talking to.
static Uri SyncServiceAddress(IServiceProvider services) =>
    services.GetRequiredService<SyncServiceEndpoint>().Resolve().Address;

static void AddAzureFoundryCostClient(IServiceCollection services, AzureFoundrySettingsStore settings)
{
    var localEndpoint = Environment.GetEnvironmentVariable("BACKLOG_AZURE_FOUNDRY_LOCAL_ENDPOINT");

    if (string.IsNullOrWhiteSpace(localEndpoint) || !IsLocalAzureFoundrySeed(settings.Current))
    {
        services.AddAzureFoundryCostClient();
        return;
    }

    services.AddSingleton<IAzureManagementTokenSource, LocalAzureManagementTokenSource>();
    services.AddAzureFoundryCostClient(new Uri(localEndpoint));
}

static AzureFoundrySettingsStore CreateLocalDevelopmentAzureFoundrySettingsStore(string contentRootPath)
{
    var settingsPath = Environment.GetEnvironmentVariable("BACKLOG_AZURE_FOUNDRY_SETTINGS_PATH");
    if (string.IsNullOrWhiteSpace(settingsPath))
    {
        settingsPath = Path.Combine(contentRootPath, "obj", "local-development", "azure-foundry.settings.json");
    }

    var settings = new AzureFoundrySettingsStore(settingsPath);
    SeedLocalAzureFoundrySettings(settings);
    return settings;
}

// Every git worktree shares one Backlog.Debug workspace and therefore one
// Azure Foundry settings file, while the azure-foundry-test service binds a
// fresh dynamic port per Aspire session. A seed left behind by an earlier
// session (any worktree) therefore points at a port nobody listens on any
// more, and "Create plan" fails with a connection refused. The seed is told
// apart from a person's own configuration by its API key: the marker below
// is never a real key, so a stored configuration carrying it is ours to
// overwrite with this session's endpoint, and anything else is left alone.
static void SeedLocalAzureFoundrySettings(AzureFoundrySettingsStore settings)
{
    var localEndpoint = Environment.GetEnvironmentVariable("BACKLOG_AZURE_FOUNDRY_LOCAL_ENDPOINT");
    if (string.IsNullOrWhiteSpace(localEndpoint) || HasUserConfiguredAzureFoundry(settings.Current))
    {
        return;
    }

    var error = settings.SetConnection(localEndpoint, LocalAzureFoundryDeployment, LocalAzureFoundryApiKeyMarker, AzureFoundrySettingsStore.DefaultApiVersion)
        ?? settings.SetCostScope(LocalAzureFoundryCostScope);
    if (error is not null)
    {
        throw new InvalidOperationException(error);
    }
}

// A configuration is the user's own unless its API key is the local seed's
// marker. An endpoint or deployment with no key at all is still the user's:
// a half-entered form is not something the harness gets to finish for them.
static bool HasUserConfiguredAzureFoundry(AzureFoundrySettings settings) =>
    !IsLocalAzureFoundrySeed(settings)
    && (!string.IsNullOrWhiteSpace(settings.Endpoint)
        || !string.IsNullOrWhiteSpace(settings.Deployment)
        || !string.IsNullOrWhiteSpace(settings.ApiKey));

static bool IsLocalAzureFoundrySeed(AzureFoundrySettings settings) =>
    string.Equals(settings.ApiKey, LocalAzureFoundryApiKeyMarker, StringComparison.Ordinal);


static AppFeatureSettingsStore CreateLocalDevelopmentFeatureSettingsStore(string contentRootPath)
{
    var settingsPath = Environment.GetEnvironmentVariable("BACKLOG_FEATURE_SETTINGS_PATH");
    if (string.IsNullOrWhiteSpace(settingsPath))
    {
        settingsPath = Path.Combine(contentRootPath, "obj", "local-development", "feature.settings.json");
    }

    return new AppFeatureSettingsStore(AppFeatures.All, settingsPath);
}

static WorkingHoursSettingsStore CreateLocalDevelopmentWorkingHoursSettingsStore(string contentRootPath)
{
    var settingsPath = Environment.GetEnvironmentVariable("BACKLOG_WORKING_HOURS_SETTINGS_PATH");
    if (string.IsNullOrWhiteSpace(settingsPath))
    {
        settingsPath = Path.Combine(contentRootPath, "obj", "local-development", "working-hours.settings.json");
    }

    return new WorkingHoursSettingsStore(settingsPath);
}

static CaptureSourcesSettingsStore CreateLocalDevelopmentCaptureSourcesSettingsStore(string contentRootPath)
{
    var settingsPath = Environment.GetEnvironmentVariable("BACKLOG_CAPTURE_SOURCES_SETTINGS_PATH");
    if (string.IsNullOrWhiteSpace(settingsPath))
    {
        settingsPath = Path.Combine(contentRootPath, "obj", "local-development", "capture-sources.settings.json");
    }

    return new CaptureSourcesSettingsStore(settingsPath);
}

static CaptureRunLogStore CreateLocalDevelopmentCaptureRunLogStore(string contentRootPath)
{
    var logPath = Environment.GetEnvironmentVariable("BACKLOG_CAPTURE_RUN_LOG_PATH");
    if (string.IsNullOrWhiteSpace(logPath))
    {
        logPath = Path.Combine(contentRootPath, "obj", "local-development", "capture-runs.json");
    }

    return new CaptureRunLogStore(logPath);
}

static DeviceIdentityStore CreateLocalDevelopmentDeviceIdentityStore(string contentRootPath)
{
    var settingsPath = Environment.GetEnvironmentVariable("BACKLOG_DEVICE_IDENTITY_PATH");
    if (string.IsNullOrWhiteSpace(settingsPath))
    {
        settingsPath = Path.Combine(contentRootPath, "obj", "local-development", "device.json");
    }

    return new DeviceIdentityStore(settingsPath);
}

static ShellNavigationStore CreateLocalDevelopmentShellNavigationStore(string contentRootPath)
{
    var settingsPath = Environment.GetEnvironmentVariable("BACKLOG_SHELL_NAVIGATION_SETTINGS_PATH");
    if (string.IsNullOrWhiteSpace(settingsPath))
    {
        settingsPath = Path.Combine(contentRootPath, "obj", "local-development", "shell-navigation.settings.json");
    }

    return new ShellNavigationStore(settingsPath);
}

static ClaudeSettingsStore CreateLocalDevelopmentClaudeSettingsStore(string contentRootPath)
{
    var settingsPath = Environment.GetEnvironmentVariable("BACKLOG_CLAUDE_SETTINGS_PATH");
    if (string.IsNullOrWhiteSpace(settingsPath))
    {
        settingsPath = Path.Combine(contentRootPath, "obj", "local-development", "claude.settings.json");
    }

    return new ClaudeSettingsStore(settingsPath);
}

static string? ResolveRepositoryRoot(string contentRootPath)
{
    var configured = Environment.GetEnvironmentVariable("BACKLOG_REPOSITORY_ROOT");
    if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
    {
        return Path.GetFullPath(configured);
    }

    var current = new DirectoryInfo(contentRootPath);
    while (current is not null)
    {
        if (Directory.Exists(Path.Combine(current.FullName, ".github")) &&
            (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, ".git"))))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    return null;
}
