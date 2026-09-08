using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.Copilot;
using Backlog.Desktop.UI.Tasks;
using Backlog.Desktop.UI.Knowledge;
using Backlog.Desktop.UI.AppUpdate;
using Backlog.Desktop.UI.Shell;
using Backlog.Modules.DevPc.Abstractions;
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
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Knowledge;
using Backlog.Infrastructure.Sync;
using Backlog.Infrastructure.Sync.Extensions;
using Backlog.Infrastructure.Sync.Sessions;
using Backlog.UI.Components.Diagrams;
using Backlog.UI.Components.Feedback;
using Backlog.Desktop.WebHarness;
using Backlog.Desktop.WebHarness.Components;
using Backlog.Aspire.ServiceDefaults;

// Names the client the branch-archive download uses, so it gets a handler of its
// own rather than sharing a general-purpose one whose timeout is set for
// request-response calls.
const string GitHubArchiveHttpClient = "github-archive";

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The workspace settings file, and the two module ports the adapters over it
// answer. The knowledge resolver is what both ports share, so neither context
// has to see the other's settings.
builder.Services.AddSingleton<WorkspaceSettingsStore>();

// Knowledge read from a repository branch, for a repository nobody has cloned.
// The download half lives in the GitHub adapter and the disk half in the file
// system one; the cache root arrives as a delegate rather than as the workspace
// store, because the GitHub adapter may not see that one.
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
// How often the list re-reads a store somebody else may have written to. Scoped
// to the content root like the harness's other settings files, so a session here
// never rewrites the real per-user choice.
builder.Services.AddSingleton<ITasksRefreshSettings>(
    _ => CreateLocalDevelopmentRefreshSettingsStore(builder.Environment.ContentRootPath));
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
builder.Services.AddSingleton<ITaskRepository>(sp =>
    new RootedSqliteTaskRepository(() => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
builder.Services.AddTasksModule();

// The same arrangement for the plan: the Roadmap module brings its use cases, and
// the host picks the adapter. One document row in the same database the tasks use,
// following the same folder.
builder.Services.AddSingleton<IRoadmapPlanRepository>(sp =>
    new RootedSqliteRoadmapPlanRepository(() => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
builder.Services.AddRoadmapModule();

// The two cross-context joins the plan takes part in, answered by adapters that may
// see both contexts: the backlog's tag picker offers the plan's tags, and a roadmap
// item rolls up the backlog entries and knowledge chapters it gathers. Both capture
// services the modules register as Scoped, so they are Scoped too — registered in
// one place both hosts share so the lifetimes cannot drift apart.
builder.Services.AddRoadmapCrossContextAdapters();
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
builder.Services.AddSessionSyncStores(
    Environment.GetEnvironmentVariable("BACKLOG_DESKTOP_SESSION_SYNC_PATH") is { Length: > 0 } sessionSyncFolder
        ? sessionSyncFolder
        : Path.Combine(builder.Environment.ContentRootPath, "obj", "local-development"));
// "https+http://sync" is resolved by Aspire service discovery, so the harness
// always talks to the sync service of this AppHost run. Task replication is the
// second call and not part of the first: it needs the ITaskRepository above, and
// a host without one composes only the pairing surface.
builder.Services.AddSyncClient(new Uri("https+http://sync"));
builder.Services.AddTaskSyncClient(new Uri("https+http://sync"));
builder.Services.AddSingleton(_ => CreateLocalDevelopmentAzureFoundrySettingsStore(builder.Environment.ContentRootPath));
builder.Services.AddHttpClient<IAzureFoundryChatClient, AzureFoundryChatClient>();
// The embedding deployment beside the chat one. Registered and never called in
// this change: local ADR 0004's semantic tier is wired and dormant, and the
// thing that would join it up - writing vectors into _meta/knowledge.db -
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
builder.Services.AddSingleton<IGitHubActivityClient>(sp => new GitHubActivityClient(
    sp.GetRequiredService<ResolvingGitHubTransport>(),
    sp.GetRequiredService<IPullRequestDetailCache>()));
// Counts only, over the search API, for the stretches of history the detailed
// client is too expensive to walk.
builder.Services.AddSingleton<IGitHubActivityBaselineClient>(sp => new GitHubActivityBaselineClient(
    sp.GetRequiredService<ResolvingGitHubTransport>()));
builder.Services.AddSingleton<IGitHubBillingClient>(sp => new GitHubBillingClient(
    sp.GetRequiredService<ResolvingGitHubTransport>(),
    sp.GetRequiredService<IGitHubIdentityClient>(),
    sp.GetRequiredService<GitHubSettingsStore>()));

// Claude usage reporting reports itself unavailable until an Admin API key is
// configured, so it is safe to register unconditionally.
builder.Services.AddSingleton(_ => CreateLocalDevelopmentClaudeSettingsStore(builder.Environment.ContentRootPath));
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
// AddTasksModule() above because it reads the GitHub settings store and that is
// only configured by this point. It is what lets an imported plan resolve a
// `repo:` name against the repositories somebody has configured — and register one
// it names that nobody has, per ADR 0007.
builder.Services.AddTasksAdapters();

builder.Services.AddSingleton<GitHubIntegration>();
builder.Services.AddSingleton<FeedbackReporter>();
builder.Services.AddSingleton<DesignKnowledgeProvider>();
builder.Services.AddSingleton<TechnologyKnowledgeService>();
builder.Services.AddSingleton<KnowledgeAtlasService>();
// Retrieval, both tiers. Adapters over the generated database rather than over
// the Markdown: search is the one capability ADR 0004's ladder does not let
// degrade to a corpus scan, so where there is no database these report that in
// words instead of answering slowly or answering nothing.
builder.Services.AddSingleton<IKnowledgeSearch>(sp =>
    new KnowledgeFullTextSearch(sp.GetRequiredService<IKnowledgeFolderSource>()));
builder.Services.AddSingleton<IKnowledgeVectorSearch>(sp =>
    new KnowledgeSemanticSearch(sp.GetRequiredService<IKnowledgeFolderSource>(), KnowledgeEmbeddingModel.Default));
builder.Services.AddSingleton<InstructionSourceDiscovery>();
builder.Services.AddSingleton<KnowledgeMenu>();
builder.Services.AddSingleton<Arc42KnowledgeStore>();
// The C4 model beside the architecture chapters. Registered next to the
// arc42 store because it answers the same scope question against the same
// clone; it reads its own feature key and hands back nothing when that key
// is off, so registering it does not turn it on.
builder.Services.AddSingleton<C4KnowledgeStore>();
builder.Services.AddSingleton<KnowledgeChapterWriter>();
builder.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
builder.Services.AddSingleton<KnowledgeFolderOpenService>();
builder.Services.AddSingleton(_ => TasksCopilotCli.Unavailable);
builder.Services.AddSingleton(_ => new KnowledgeCopilotCli(new UnavailableCopilotCliLauncher()));
// The shared diagram component asks for this optionally, so registering it is
// what switches Archify artifacts on for the harness at all. It takes the same
// unavailable launcher as its neighbour: this host cannot start a CLI, and
// pressing the offer says so rather than doing nothing.
builder.Services.AddSingleton<IDiagramArtifactSource>(sp => new ArchifyDiagramArtifacts(
    sp.GetRequiredService<IAppFeatureSettings>(),
    sp.GetRequiredService<IKnowledgeFolderSource>(),
    sp.GetRequiredService<GitHubSettingsStore>(),
    new UnavailableCopilotCliLauncher()));
builder.Services.AddSingleton<KnowledgeScope>();
builder.Services.AddSingleton<KnowledgeUpdateService>();

// Shared by the knowledge pane and the settings screen, and a singleton so the
// branch list somebody fetched in one is already there in the other.
builder.Services.AddSingleton<KnowledgeSourceSelection>();
builder.Services.AddScoped<TasksDesktopState>();
// The save-state band and the toast tray, both mounted by MainLayout under every
// route. Scoped rather than singleton, and that is forced rather than tidy: this
// host has one circuit per visitor, a singleton forwarding to a scoped
// TasksDesktopState is a captive dependency that throws on resolve, and a
// singleton channel would show one visitor's toasts to every other.
builder.Services.AddScoped<ISaveStatusSource>(sp => sp.GetRequiredService<TasksDesktopState>());
builder.Services.AddScoped<ToastChannel>();
builder.Services.AddScoped<IToastChannel>(sp => sp.GetRequiredService<ToastChannel>());
builder.Services.AddScoped(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));

// The web host never distributes or updates the desktop app, so it always
// reports updates as unsupported.
builder.Services.AddSingleton<IAppUpdateService, UnsupportedAppUpdateService>();
builder.Services.AddSingleton<IDevToolService, LocalDevelopmentDevToolService>();

// The session list reads the two agents' own folders in the profile of whoever is
// signed in, and the harness runs as that person on that machine — so unlike the
// tool service above there is nothing for a local-development variant to differ
// about, and both hosts compose the same adapter.
builder.Services.AddAgentSessionSource();

// Session replication, on top of AddSyncClient above and after the readers it
// pushes from: it reads this machine's sessions through the port that call
// registers and contributes a second source to the same port for what the other
// environments reported. Both lines are lazy factories, so the order is for
// whoever reads this file rather than for the container. Its own call and its own
// feature key, because a person can want their tasks on both machines and still
// not want a list of what their agents have been doing leaving either one.
builder.Services.AddSessionSyncClient(new Uri("https+http://sync"));

// The join between the two: the Dashboard's sessions part reports on what the Sessions
// context reads. Only an infrastructure adapter may see both, so the registration is
// there rather than in either module — and it comes after both AddDashboardModule() and
// AddAgentSessionSource(), whose ports it sits between.
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
            KnowledgeFolders = KnowledgeFolderSetting.Defaults()
        }
    ]);

    foreach (var folder in KnowledgeFolderSetting.Defaults())
    {
        settings.SetKnowledgeFolder(alias, folder.Key, enabled: true, path: null);
    }

    return settings;
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

static void SeedLocalAzureFoundrySettings(AzureFoundrySettingsStore settings)
{
    var localEndpoint = Environment.GetEnvironmentVariable("BACKLOG_AZURE_FOUNDRY_LOCAL_ENDPOINT");
    if (string.IsNullOrWhiteSpace(localEndpoint) || HasUserConfiguredAzureFoundry(settings.Current))
    {
        return;
    }

    var error = settings.SetConnection(localEndpoint, "local-ai", "local-development", AzureFoundrySettingsStore.DefaultApiVersion);
    if (error is not null)
    {
        throw new InvalidOperationException(error);
    }
}

static bool HasUserConfiguredAzureFoundry(AzureFoundrySettings settings) =>
    !string.IsNullOrWhiteSpace(settings.Endpoint)
    || !string.IsNullOrWhiteSpace(settings.Deployment)
    || !string.IsNullOrWhiteSpace(settings.ApiKey);


static AppFeatureSettingsStore CreateLocalDevelopmentFeatureSettingsStore(string contentRootPath)
{
    var settingsPath = Environment.GetEnvironmentVariable("BACKLOG_FEATURE_SETTINGS_PATH");
    if (string.IsNullOrWhiteSpace(settingsPath))
    {
        settingsPath = Path.Combine(contentRootPath, "obj", "local-development", "feature.settings.json");
    }

    return new AppFeatureSettingsStore(AppFeatures.All, settingsPath);
}

static TasksRefreshSettingsStore CreateLocalDevelopmentRefreshSettingsStore(string contentRootPath)
{
    var settingsPath = Environment.GetEnvironmentVariable("BACKLOG_REFRESH_SETTINGS_PATH");
    if (string.IsNullOrWhiteSpace(settingsPath))
    {
        settingsPath = Path.Combine(contentRootPath, "obj", "local-development", "refresh.settings.json");
    }

    return new TasksRefreshSettingsStore(settingsPath);
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
