using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.Copilot;
using Backlog.Desktop.UI.BacklogManagement;
using Backlog.Desktop.UI.Knowledge;
using Backlog.Desktop.UI.AppUpdate;
using Backlog.Desktop.UI.Shell;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.SharedKernel;
using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.Abstractions.Services;
using Backlog.Modules.Knowledge.Abstractions;
using Backlog.Modules.Backlog.Extensions;
using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Extensions;
using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Dashboard.Extensions;
using Backlog.Modules.Dashboard.UI.Extensions;
using Backlog.Modules.Sessions.UI.Extensions;
using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.Diagrams;
using Backlog.Desktop.WebHarness;
using Backlog.Desktop.WebHarness.Components;
using Backlog.Aspire.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The workspace settings file, and the two module ports the adapters over it
// answer. The knowledge resolver is what both ports share, so neither context
// has to see the other's settings.
builder.Services.AddSingleton<WorkspaceSettingsStore>();
builder.Services.AddSingleton<IKnowledgeFolderSource>(sp => new KnowledgeFolderSource(
    sp.GetRequiredService<GitHubSettingsStore>(),
    sp.GetRequiredService<WorkspaceSettingsStore>()));
builder.Services.AddSingleton<IBacklogStore>(sp => new WorkspaceBacklogStore(
    sp.GetRequiredService<WorkspaceSettingsStore>()));
// How often the list re-reads a store somebody else may have written to. Scoped
// to the content root like the harness's other settings files, so a session here
// never rewrites the real per-user choice.
builder.Services.AddSingleton<IBacklogRefreshSettings>(
    _ => CreateLocalDevelopmentRefreshSettingsStore(builder.Environment.ContentRootPath));
// Which surface the shell was last showing. Scoped to the content root like the
// harness's other settings files, so a session here never rewrites the real
// per-user choice.
builder.Services.AddSingleton(_ => CreateLocalDevelopmentShellNavigationStore(builder.Environment.ContentRootPath));

// Composition: the Backlog module brings its own use cases, and the host decides
// which adapter is behind them. The repository follows the storage folder rather
// than being pinned to wherever it was at startup.
builder.Services.AddSingleton<ITaskRepository>(sp =>
    new RootedSqliteTaskRepository(() => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
builder.Services.AddBacklogModule();

// The same arrangement for the plan: the Roadmap module brings its use cases, and
// the host picks the adapter. One JSON document under the same storage root,
// following the same folder.
builder.Services.AddSingleton<IRoadmapPlanRepository>(sp =>
    new RootedJsonRoadmapPlanRepository(() => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
builder.Services.AddRoadmapModule();

// The two cross-context joins the plan takes part in, answered by adapters that may
// see both contexts: the backlog's tag picker offers the plan's tags, and a roadmap
// item rolls up the backlog entries and knowledge chapters it gathers. Both capture
// services the modules register as Scoped, so they are Scoped too — registered in
// one place both hosts share so the lifetimes cannot drift apart.
builder.Services.AddRoadmapCrossContextAdapters();
builder.Services.AddSingleton(_ => CreateLocalDevelopmentGitHubSettingsStore(builder.Environment.ContentRootPath));
builder.Services.AddSingleton(sp => new ResolvingGitHubTransport(sp.GetRequiredService<GitHubSettingsStore>()));
builder.Services.AddSingleton<IGitHubConnectionProbe>(sp => sp.GetRequiredService<ResolvingGitHubTransport>());
builder.Services.AddSingleton<IAppFeatureSettings>(_ => CreateLocalDevelopmentFeatureSettingsStore(builder.Environment.ContentRootPath));
builder.Services.AddSingleton(_ => CreateLocalDevelopmentAzureFoundrySettingsStore(builder.Environment.ContentRootPath));
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

// Backlog Management's own adapter, registered here rather than beside
// AddBacklogModule() above because it reads the GitHub settings store and that is
// only configured by this point. It is what lets an imported plan resolve a
// `repo:` name against the repositories somebody has configured — and register one
// it names that nobody has, per ADR 0004.
builder.Services.AddBacklogAdapters();

builder.Services.AddSingleton<GitHubIntegration>();
builder.Services.AddSingleton<FeedbackReporter>();
builder.Services.AddSingleton<DesignKnowledgeProvider>();
builder.Services.AddSingleton<TechnologyKnowledgeService>();
builder.Services.AddSingleton<KnowledgeAtlasService>();
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
builder.Services.AddSingleton(_ => BacklogCopilotCli.Unavailable);
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
builder.Services.AddScoped<BacklogDesktopState>();
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

// Which worktree served this harness. It is only ever started from a checkout,
// so there is nothing to gate on beyond finding one — and when it is missing the
// header simply keeps showing the version.
if (DevelopmentWorkspace.Current is { } workspace)
{
    builder.Services.AddSingleton(new DevelopmentWorkspaceLabel(workspace));
}

var app = builder.Build();

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

static GitHubSettingsStore CreateLocalDevelopmentGitHubSettingsStore(string contentRootPath)
{
    var settingsPath = Environment.GetEnvironmentVariable("BACKLOG_GITHUB_SETTINGS_PATH");
    if (string.IsNullOrWhiteSpace(settingsPath))
    {
        settingsPath = Path.Combine(contentRootPath, "obj", "local-development", "github.settings.json");
    }

    var settings = new GitHubSettingsStore(settingsPath);
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

static BacklogRefreshSettingsStore CreateLocalDevelopmentRefreshSettingsStore(string contentRootPath)
{
    var settingsPath = Environment.GetEnvironmentVariable("BACKLOG_REFRESH_SETTINGS_PATH");
    if (string.IsNullOrWhiteSpace(settingsPath))
    {
        settingsPath = Path.Combine(contentRootPath, "obj", "local-development", "refresh.settings.json");
    }

    return new BacklogRefreshSettingsStore(settingsPath);
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
