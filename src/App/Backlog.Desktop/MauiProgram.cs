using Backlog.Desktop.Services;
using Backlog.Desktop.UI.BacklogManagement;
using Backlog.Desktop.UI.Knowledge;
using Backlog.Desktop.UI.Shell;
using Backlog.Desktop.UI.Workspace;
using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.Extensions;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;
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
        builder.Services.AddSingleton<BacklogStore>();

        // Composition: the Backlog module brings its own use cases, and the host
        // decides which adapter is behind them. The repository follows the
        // storage folder rather than being pinned to wherever it was at startup,
        // because somebody can move their backlog while the app is open.
        builder.Services.AddSingleton<IBacklogRepository>(sp =>
            new RootedFileBacklogRepository(() => sp.GetRequiredService<BacklogStore>().RootDirectory));
        builder.Services.AddBacklogModule();
        builder.Services.AddSingleton<GitHubSettingsStore>();
        builder.Services.AddSingleton(sp => new ResolvingGitHubTransport(sp.GetRequiredService<GitHubSettingsStore>()));
        builder.Services.AddSingleton<IGitHubConnectionProbe>(sp => sp.GetRequiredService<ResolvingGitHubTransport>());
        builder.Services.AddSingleton<AppFeatureSettingsStore>();
        builder.Services.AddSingleton<AzureFoundrySettingsStore>();
        builder.Services.AddHttpClient<IAzureFoundryChatClient, AzureFoundryChatClient>();
        builder.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        builder.Services.AddSingleton<IGitHubClient>(sp => new GitHubClient(sp.GetRequiredService<ResolvingGitHubTransport>()));
        builder.Services.AddSingleton<ICopilotUsageClient>(sp => new CopilotUsageClient(sp.GetRequiredService<ResolvingGitHubTransport>()));

        // Claude usage reporting is registered unconditionally; it reports
        // itself unavailable until an Admin API key is configured, and the
        // "usage-metrics" feature decides whether anything asks it.
        builder.Services.AddSingleton<ClaudeSettingsStore>();
        builder.Services.AddHttpClient<IClaudeTransport, ClaudeAdminTransport>();
        builder.Services.AddSingleton<IClaudeUsageClient>(sp => new ClaudeUsageClient(
            sp.GetRequiredService<IClaudeTransport>(),
            sp.GetRequiredService<ClaudeSettingsStore>()));
        builder.Services.AddSingleton<GitHubIntegration>();
        builder.Services.AddSingleton<DesignKnowledgeProvider>();
        builder.Services.AddSingleton<KnowledgeFolderSource>();
        builder.Services.AddSingleton<RepositoryBacklogSource>();
        builder.Services.AddSingleton<TechnologyKnowledgeService>();
        builder.Services.AddSingleton<InstructionSourceDiscovery>();
        builder.Services.AddSingleton<KnowledgeMenu>();
        builder.Services.AddSingleton<ICopilotCliLauncher, ProcessCopilotCliLauncher>();
        builder.Services.AddSingleton<BacklogCopilotCli>();
        builder.Services.AddSingleton<KnowledgeCopilotCli>();
        builder.Services.AddSingleton<KnowledgeScope>();
        builder.Services.AddSingleton<BacklogDesktopState>();
        builder.Services.AddSingleton<IFolderEditorLauncher, VsCodeFolderEditorLauncher>();
        builder.Services.AddSingleton<KnowledgeFolderOpenService>();
        builder.Services.AddSingleton<Arc42KnowledgeStore>();
        builder.Services.AddSingleton(sp => new DomainKnowledgeStore(sp.GetRequiredService<KnowledgeFolderSource>()));

        // The MSIX head can manage its own updates when packaged; it degrades to
        // an "unsupported" report when running unpackaged (e.g. Debug), so this is
        // safe to register unconditionally.
        builder.Services.AddSingleton<IAppUpdateService, MsixAppUpdateService>();
        builder.Services.AddSingleton<ICopilotToolService>(sp => new CopilotToolService(
            sp.GetRequiredService<BacklogStore>(),
            sp.GetService<ILogger<CopilotToolService>>()));

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
