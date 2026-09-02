using Backlog.Desktop.Services;
using Backlog.Desktop.UI.Tasks;
using Backlog.Desktop.UI.Knowledge;
using Backlog.Desktop.UI.AppUpdate;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Desktop.UI.Shell;
using Backlog.SharedKernel;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Knowledge.Abstractions;
using Backlog.Modules.Tasks.Extensions;
using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Extensions;
using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Dashboard.Extensions;
using Backlog.Modules.Dashboard.UI.Extensions;
using Backlog.Modules.Sessions.UI.Extensions;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.GitHub;
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
        builder.Services.AddSingleton<IKnowledgeFolderSource>(sp => new KnowledgeFolderSource(
            sp.GetRequiredService<GitHubSettingsStore>(),
            sp.GetRequiredService<WorkspaceSettingsStore>()));
        builder.Services.AddSingleton<ITaskStore>(sp => new WorkspaceTaskStore(
            sp.GetRequiredService<WorkspaceSettingsStore>()));
        // How often the list re-reads a store somebody else may have written to.
        // Its own per-user file beside the feature choices, for the same reason
        // theirs is not in settings.json.
        builder.Services.AddSingleton<ITasksRefreshSettings, TasksRefreshSettingsStore>();
        // Which surface the shell was last showing, so it reopens there instead
        // of always defaulting to the workspace panes.
        builder.Services.AddSingleton<ShellNavigationStore>();

        // Composition: the Tasks module brings its own use cases, and the host
        // decides which adapter is behind them. The repository follows the
        // storage folder rather than being pinned to wherever it was at startup,
        // because somebody can move their backlog while the app is open.
        builder.Services.AddSingleton<ITaskRepository>(sp =>
            new RootedSqliteTaskRepository(() => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
        builder.Services.AddTasksModule();

        // The same arrangement for the plan: the Roadmap module brings its use
        // cases, and the host picks the adapter. One JSON document under the same
        // storage root, following the same folder, so moving the storage folder
        // moves the plan with the backlog rather than leaving it behind.
        builder.Services.AddSingleton<IRoadmapPlanRepository>(sp =>
            new RootedJsonRoadmapPlanRepository(() => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
        builder.Services.AddRoadmapModule();

        // The two cross-context joins the plan takes part in, each a port a screen
        // owns and an adapter here answers because only an adapter may see both
        // contexts: the backlog's tag picker offers the plan's tags, and a roadmap
        // item rolls up the backlog entries and knowledge chapters it gathers. Both
        // capture services the modules register as Scoped, so they are Scoped too —
        // registered in one place both hosts share so the lifetimes cannot drift.
        builder.Services.AddRoadmapCrossContextAdapters();
        builder.Services.AddSingleton<GitHubSettingsStore>();
        builder.Services.AddSingleton(sp => new ResolvingGitHubTransport(sp.GetRequiredService<GitHubSettingsStore>()));
        builder.Services.AddSingleton<IGitHubConnectionProbe>(sp => sp.GetRequiredService<ResolvingGitHubTransport>());
        // The catalog is the shell's product copy; the store is the adapter that
        // remembers the choices. Composing the two is the host's job.
        builder.Services.AddSingleton<IAppFeatureSettings>(_ => new AppFeatureSettingsStore(AppFeatures.All));
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
        // — and register one it names that nobody has, per ADR 0004.
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
        builder.Services.AddSingleton<TasksDesktopState>();
        builder.Services.AddSingleton<IFolderEditorLauncher, VsCodeFolderEditorLauncher>();
        builder.Services.AddSingleton<KnowledgeFolderOpenService>();
        builder.Services.AddSingleton<Arc42KnowledgeStore>();
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
        // for a host to differ about and both hosts compose the same adapter.
        builder.Services.AddAgentSessionSource();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
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
