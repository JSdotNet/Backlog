using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.FileSystem;
using Backlog.Desktop.UI.Components;
using Backlog.Desktop.UI.Services;
using Backlog.Infrastructure.GitHub;
using Backlog.Desktop.WebHarness;
using Backlog.Desktop.WebHarness.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<BacklogStore>();
builder.Services.AddSingleton(_ => CreateLocalDevelopmentGitHubSettingsStore(builder.Environment.ContentRootPath));
builder.Services.AddSingleton(sp => new ResolvingGitHubTransport(sp.GetRequiredService<GitHubSettingsStore>()));
builder.Services.AddSingleton<IGitHubConnectionProbe>(sp => sp.GetRequiredService<ResolvingGitHubTransport>());
builder.Services.AddSingleton(_ => CreateLocalDevelopmentFeatureSettingsStore(builder.Environment.ContentRootPath));
builder.Services.AddSingleton(_ => CreateLocalDevelopmentAzureFoundrySettingsStore(builder.Environment.ContentRootPath));
builder.Services.AddHttpClient<IAzureFoundryChatClient, AzureFoundryChatClient>();
builder.Services.AddSingleton<IGitHubClient>(sp => new GitHubClient(sp.GetRequiredService<ResolvingGitHubTransport>()));
builder.Services.AddSingleton<GitHubIntegration>();
builder.Services.AddSingleton<DesignKnowledgeProvider>();
builder.Services.AddSingleton<KnowledgeFolderSource>();
builder.Services.AddSingleton<RepositoryBacklogSource>();
builder.Services.AddSingleton<TechnologyKnowledgeService>();
builder.Services.AddSingleton<InstructionSourceDiscovery>();
builder.Services.AddSingleton<Arc42KnowledgeStore>();
builder.Services.AddScoped<BacklogDesktopState>();
builder.Services.AddScoped<DomainKnowledgeStore>();

// The web host never distributes or updates the desktop app, so it always
// reports updates as unsupported.
builder.Services.AddSingleton<IAppUpdateService, UnsupportedAppUpdateService>();
builder.Services.AddSingleton<ICopilotToolService, LocalDevelopmentCopilotToolService>();

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
            IsPrimary = true,
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

    return new AppFeatureSettingsStore(settingsPath);
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

