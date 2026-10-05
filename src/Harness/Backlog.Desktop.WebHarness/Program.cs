using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.Copilot;
using Backlog.Desktop.Composition;
using Backlog.Desktop.UI.AppUpdate;
using Backlog.Desktop.UI.Shell;
using Backlog.SharedKernel;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Features.SyncLinkedTasks;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Devbook;
using Backlog.Infrastructure.DevPc;
using Backlog.Infrastructure.SpecManager;
using Backlog.Infrastructure.Sync;
using Backlog.Infrastructure.Sync.Annotations;
using Backlog.Infrastructure.Sync.Sessions;
using Backlog.Desktop.UI.Extensions;
using Backlog.Desktop.UI.Mcp;
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

// Everything this harness composes alike with the desktop head: every module,
// adapter, Ask AI source and settings page, in one place both call so the two
// cannot drift — and since this harness is the one a test starts, this is what puts
// the desktop head's registrations in front of provider validation. What follows
// the call is this harness's own.
//
// Every per-user and per-machine file is scoped to this harness's content root,
// each with an override variable of its own, so a session here never rewrites the
// real per-user choice, and this harness and the mobile one are two devices under
// one owner — which is what pairing is for. One shared name would let a single
// setting collapse the pair back into one device, or let two harnesses share a
// watermark and each skip what the other had pushed, silently, because nothing
// about that fails.
var localDevelopment = Path.Combine(builder.Environment.ContentRootPath, "obj", "local-development");
var azureFoundrySettings = CreateLocalDevelopmentAzureFoundrySettingsStore(builder.Environment.ContentRootPath);
builder.Services.AddDesktopComposition(new DesktopCompositionOptions
{
    // Constructed at startup rather than on first use, as the desktop host does:
    // the identity belongs to the installation. Per worktree, because several
    // worktrees serve this harness at once, and one shared identity file would put
    // the first-write race across processes on every parallel start.
    DeviceIdentity = CreateLocalDevelopmentDeviceIdentityStore(builder.Environment.ContentRootPath),
    DeviceCredentialStore = _ => DeviceCredentialStoreFactory.CreateLocalDevelopmentStore(
        builder.Environment.ContentRootPath,
        "BACKLOG_DESKTOP_DEVICE_CREDENTIAL_PATH",
        Path.Combine("obj", "local-development", "device-credential.json")),
    TaskSyncStateStore = _ => TaskSyncStateStoreFactory.CreateLocalDevelopmentStore(
        builder.Environment.ContentRootPath,
        "BACKLOG_DESKTOP_TASK_SYNC_STATE_PATH",
        Path.Combine("obj", "local-development", "task-sync-state.json")),
    // A folder rather than a path, because two stores is an implementation detail
    // of the exchange and where they live is not.
    SessionSyncFolder = Environment.GetEnvironmentVariable("BACKLOG_DESKTOP_SESSION_SYNC_PATH") is { Length: > 0 } sessionSyncFolder
        ? sessionSyncFolder
        : localDevelopment,
    AnnotationSyncFolder = Environment.GetEnvironmentVariable("BACKLOG_DESKTOP_ANNOTATION_SYNC_PATH") is { Length: > 0 } annotationSyncFolder
        ? annotationSyncFolder
        : localDevelopment,
    // A shared file would let two harnesses count each other's backups as their own.
    BackupStatePath = Environment.GetEnvironmentVariable("BACKLOG_DESKTOP_BACKUP_STATE_PATH") is { Length: > 0 } backupStatePath
        ? backupStatePath
        : Path.Combine(localDevelopment, "backup-state.json"),
    // Resolved the way the desktop head resolves it, so the Settings page behaves the
    // same here; under Aspire with nothing entered that is this AppHost run's sync
    // resource. A shared file would point both harnesses at whatever one was told.
    SyncServiceSettingsPath = Environment.GetEnvironmentVariable("BACKLOG_DESKTOP_SYNC_SERVICE_SETTINGS_PATH") is { Length: > 0 } syncSettingsPath
        ? syncSettingsPath
        : Path.Combine(localDevelopment, "sync-service.json"),
    FeatureSettings = _ => CreateLocalDevelopmentFeatureSettingsStore(builder.Environment.ContentRootPath),
    WorkingHoursSettings = _ => CreateLocalDevelopmentWorkingHoursSettingsStore(builder.Environment.ContentRootPath),
    UsageResetSettings = _ => new UsageResetSettingsStore(Path.Combine(localDevelopment, "usage-reset.settings.json")),
    // Given the working week IWorkingHoursSettings answers, so the Roadmap's Days off
    // presses have a week to block a day in (local ADR 0019).
    PlanningVelocitySettings = sp => new PlanningVelocitySettingsStore(
        Path.Combine(localDevelopment, "planning-velocity.settings.json"),
        time: null,
        sp.GetRequiredService<WorkingHoursSettingsStore>()),
    ShellNavigation = _ => CreateLocalDevelopmentShellNavigationStore(builder.Environment.ContentRootPath),
    CaptureSourceSettings = _ => CreateLocalDevelopmentCaptureSourcesSettingsStore(builder.Environment.ContentRootPath),
    CaptureRunLog = _ => CreateLocalDevelopmentCaptureRunLogStore(builder.Environment.ContentRootPath),
    ConnectedTargets = _ => CreateLocalDevelopmentConnectedTargetsSettingsStore(builder.Environment.ContentRootPath),
    // Per worktree, like the device credential: a sign-in here is not the installed
    // app's, and DPAPI still keeps the refresh token out of the clear.
    SpecManagerTokenStore = _ => CreateLocalDevelopmentSpecManagerTokenStore(builder.Environment.ContentRootPath),
    InboxRoutingRules = _ => CreateLocalDevelopmentInboxRoutingRulesStore(builder.Environment.ContentRootPath),
    GitHubSettings = root => CreateLocalDevelopmentGitHubSettingsStore(builder.Environment.ContentRootPath, root),
    ClaudeSettings = _ => CreateLocalDevelopmentClaudeSettingsStore(builder.Environment.ContentRootPath),
    AzureFoundrySettings = _ => azureFoundrySettings,
    // The desktop head's own tools adapter, configured to read the catalog and run
    // nothing: a browser session operates the pane without touching the machine.
    DevToolService = sp => DevToolService.CatalogOnly(sp.GetRequiredService<ITaskStore>()),
    FolderEditorLauncher = _ => new UnsupportedFolderEditorLauncher(),
    // The web host never distributes or updates the desktop app, so it always
    // reports updates as unsupported.
    AppUpdateService = _ => new UnsupportedAppUpdateService(),
    // This host cannot start a CLI, and pressing an offer says so rather than doing
    // nothing.
    CopilotCliLauncher = _ => new UnavailableCopilotCliLauncher(),
    // Run from a linked worktree, the seeded clone is that worktree alone, so its
    // main checkout is asked as well; see MainCheckoutSessionRepositoryResolver.
    SessionRepositoryResolver = sp =>
    {
        var settings = sp.GetRequiredService<GitHubSettingsStore>();
        var registered = new SettingsSessionRepositoryResolver(settings);
        var checkout = ResolveRepositoryRoot(builder.Environment.ContentRootPath);
        var main = DevelopmentWorkspace.MainCheckoutOf(checkout);
        var seeded = settings.Current.Repositories.FirstOrDefault(repository =>
            string.Equals(repository.CloneDirectory, checkout, StringComparison.OrdinalIgnoreCase));

        return main is null || seeded is null
            ? registered
            : new MainCheckoutSessionRepositoryResolver(registered, new RegisteredClone(seeded.FullName, main));
    },
    // Scoped rather than singleton, and that is forced rather than tidy: this host has
    // one circuit per visitor, a singleton over the modules' scoped services is a
    // captive dependency validate-on-build refuses, and a singleton channel would
    // show one visitor's toasts to every other.
    WindowStateLifetime = ServiceLifetime.Scoped
});

// The Devbook - its adapters and AddDevbookModule - is in the composition above
// since issue #738. The two things this harness did differently arrive through
// its options there: the domain store is scoped with the rest of a circuit's
// state, and the Copilot offers and Archify artifacts take the unavailable
// launcher, so pressing one says this host cannot start a CLI.

// The same MCP server the desktop head serves, on the Kestrel pipeline this
// harness already has rather than a second listener of its own — which is what
// local ADR 0012 §2 asks for, and what makes the tools reachable under Aspire
// where QA drives them. One registration shared with the MAUI head, so the tools
// and their feature gates cannot differ between the two.
//
// The Origin check comes with them; the bearer token does not. The two guards
// answer different questions and only one of them is the installed app's.
//
// Origin is here because the tools are the same tools and the workspace under
// them is the real one: this harness composes %LOCALAPPDATA%\Backlog.Debug, and
// a repository configured there points its devbook folders at a clone on this
// machine. QA has to turn the feature on to test it, and a harness left running
// afterwards is reachable from any browser page on the machine by DNS rebinding
// — Aspire's port being dynamic is not a secret, it is a number a page can find
// by trying. The check costs QA nothing: a test client is not a browser and
// sends no Origin at all.
//
// The token is not, and that is the difference. It is a secret a person copies
// into a registration; here it would be one QA had to fetch out of a container
// to drive a test, protecting a host that holds nothing the Origin check is not
// already closing.
builder.Services.AddBacklogMcpServer().WithHttpTransport();
// The bill for the Azure Foundry resource. When the settings are this session's
// local seed, the query goes to the stand-in service beside the chat one — with a
// token nothing signed, because the stand-in checks none — so the dashboard's
// Cost section has figures without an Azure sign-in. A person's own Foundry
// configuration keeps the real client, and their real bill.
AddAzureFoundryCostClient(builder.Services, azureFoundrySettings);
// This assembly's own pages, under Components/Pages: they exist only to be
// driven — the shipped app has no route that throws on request.
builder.Services.AddSingleton(new AdditionalRouteAssemblies([typeof(Program).Assembly]));

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

// And the linked task sync's timer, on the same terms and for the same reason the
// desktop head resolves it.
_ = app.Services.GetRequiredService<LinkedTaskSyncWorker>();

// And the devbook database refresher: resolving it points every database reader
// at the app's storage, so it has to exist before the first pane opens.
_ = app.Services.GetRequiredService<DevbookDatabaseRefresher>();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

// Revalidated on every request, as the storybook's are, and for its reason: with an
// ETag and no Cache-Control a browser invents a freshness window and keeps serving the
// stylesheet it already had. New markup then meets old CSS: the roadmap's day heads,
// once they became <button>s, drew as the platform's white buttons under a cached
// components.css from before the rule that takes a button's own face away. A miss
// costs a 304.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
        context.Context.Response.Headers.CacheControl = "no-cache, must-revalidate"
});
app.UseAntiforgery();

// The MCP endpoint is always on, so the only thing in front of it is the
// loopback Origin check the desktop listener makes too.
app.UseWhen(
    // The hook telemetry route beside /mcp is the same surface, so it goes
    // behind the same Origin check.
    context => context.Request.Path.StartsWithSegments(BacklogMcpServerRegistration.EndpointPath)
        || context.Request.Path.StartsWithSegments(BacklogTelemetryEndpoint.RoutePath),
    branch => branch.Use(async (context, next) =>
    {
        // The same predicate the desktop listener refuses on, and the same
        // handling of a header sent twice — two Origins is not something a
        // browser produces, so honouring either would be choosing which caller to
        // believe. Forbidden rather than Unauthorized: there is no credential the
        // caller could supply and retry with.
        var origin = context.Request.Headers.Origin;

        if (origin.Count > 1 || !McpLoopbackGuard.IsLoopbackOrigin(origin.Count == 0 ? null : origin[0]))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Routes).Assembly);

// The read-only tools, on the pipeline above. Aspire binds localhost:0 for every
// endpoint in this repository, so this harness's port is whatever this run was
// given — read it off the Aspire dashboard, never from a previous session.
app.MapMcp(BacklogMcpServerRegistration.EndpointPath);

// And the hook telemetry a session forwards, attributed to the run it is driving —
// into this harness's profile folders, which are the real ones.
app.MapPost(BacklogTelemetryEndpoint.RoutePath, async context =>
{
    context.Response.StatusCode = await BacklogTelemetryEndpoint.AcceptAsync(
        context.Request.Body,
        context.Request.ContentLength,
        context.RequestServices.GetRequiredService<IDeliveryRunTelemetry>(),
        context.RequestAborted);
});

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

    // A seed, not somebody removing what the shared workspace held before, so
    // nothing it replaces is recorded as removed.
    const string alias = "backlog";
    settings.SetRepositories(
    [
        new GitHubRepositoryRef(alias, "JSdotNet", "Backlog")
        {
            CloneDirectory = repositoryRoot,
            DevbookFolders = DevbookFolderSetting.Defaults()
        }
    ],
    rememberRemovals: false);

    foreach (var folder in DevbookFolderSetting.Defaults())
    {
        settings.SetDevbookFolder(alias, folder.Key, enabled: true, path: null);
    }

    return settings;
}

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

static InboxRoutingRulesStore CreateLocalDevelopmentInboxRoutingRulesStore(string contentRootPath)
{
    var settingsPath = Environment.GetEnvironmentVariable("BACKLOG_INBOX_ROUTING_RULES_PATH");
    if (string.IsNullOrWhiteSpace(settingsPath))
    {
        settingsPath = Path.Combine(contentRootPath, "obj", "local-development", "inbox-routing-rules.settings.json");
    }

    return new InboxRoutingRulesStore(settingsPath);
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

static ConnectedTargetsSettingsStore CreateLocalDevelopmentConnectedTargetsSettingsStore(string contentRootPath)
{
    var settingsPath = Environment.GetEnvironmentVariable("BACKLOG_CONNECTED_TARGETS_SETTINGS_PATH");
    if (string.IsNullOrWhiteSpace(settingsPath))
    {
        settingsPath = Path.Combine(contentRootPath, "obj", "local-development", "connected-targets.settings.json");
    }

    return new ConnectedTargetsSettingsStore(settingsPath);
}

static ISpecManagerTokenStore CreateLocalDevelopmentSpecManagerTokenStore(string contentRootPath)
{
    var credentialsPath = Environment.GetEnvironmentVariable("BACKLOG_SPEC_MANAGER_CREDENTIALS_PATH");
    if (string.IsNullOrWhiteSpace(credentialsPath))
    {
        credentialsPath = Path.Combine(contentRootPath, "obj", "local-development", "spec-manager-credentials.json");
    }

    return SpecManagerTokenStoreFactory.Create(credentialsPath);
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
