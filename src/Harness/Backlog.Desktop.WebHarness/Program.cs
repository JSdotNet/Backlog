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
using Backlog.Modules.Roadmap.Extensions;
using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Dashboard.Extensions;
using Backlog.Modules.Dashboard.UI.Extensions;
using Backlog.Modules.Sessions.UI.Extensions;
using Backlog.Infrastructure.GitHub;
using Backlog.Desktop.WebHarness;
using Backlog.Desktop.WebHarness.Components;

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

builder.Services.AddSingleton<GitHubIntegration>();
builder.Services.AddSingleton<FeedbackReporter>();
builder.Services.AddSingleton<DesignKnowledgeProvider>();
builder.Services.AddSingleton<TechnologyKnowledgeService>();
builder.Services.AddSingleton<InstructionSourceDiscovery>();
builder.Services.AddSingleton<KnowledgeMenu>();
builder.Services.AddSingleton<Arc42KnowledgeStore>();
builder.Services.AddSingleton<KnowledgeChapterWriter>();
builder.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
builder.Services.AddSingleton<KnowledgeFolderOpenService>();
builder.Services.AddSingleton(_ => BacklogCopilotCli.Unavailable);
builder.Services.AddSingleton(_ => new KnowledgeCopilotCli(new UnavailableCopilotCliLauncher()));
builder.Services.AddSingleton<KnowledgeScope>();
builder.Services.AddScoped<BacklogDesktopState>();
builder.Services.AddScoped(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));

// The web host never distributes or updates the desktop app, so it always
// reports updates as unsupported.
builder.Services.AddSingleton<IAppUpdateService, UnsupportedAppUpdateService>();
builder.Services.AddSingleton<ICopilotToolService, LocalDevelopmentCopilotToolService>();

// The session list reads the two agents' own folders in the profile of whoever is
// signed in, and the harness runs as that person on that machine — so unlike the
// tool service above there is nothing for a local-development variant to differ
// about, and both hosts compose the same adapter.
builder.Services.AddAgentSessionSource();

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
