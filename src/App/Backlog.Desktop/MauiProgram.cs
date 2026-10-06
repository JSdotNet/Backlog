using Backlog.Desktop.Composition;
using Backlog.Desktop.Mcp;
using Backlog.Desktop.Services;
using Backlog.Desktop.UI.Shell;
using Backlog.Aspire.ServiceDefaults;
using Backlog.SharedKernel;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Features.SyncLinkedTasks;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.FileSystem.Logging;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Devbook;
using Backlog.Infrastructure.DevPc;
using Backlog.Infrastructure.SpecManager;
using Backlog.Infrastructure.Sync;
using Backlog.Infrastructure.Sync.Annotations;
using Backlog.Infrastructure.Sync.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        // Everything this head composes alike with the web harness: every module,
        // adapter, Ask AI source and settings page, in one place both call so the two
        // cannot drift. What follows the call is this head's own.
        //
        // The per-machine files go under the workspace's own app-data folder -
        // Backlog.Debug for a debug head, Backlog for the installed one - like the log
        // above, so a checkout run beside a paired installed app starts unpaired
        // rather than syncing as that device. The watermark and cursor describe how
        // far *this machine* has got, which is why they are never in the workspace
        // root (ADR 0005). The per-user settings stores take their own per-user files.
        var appData = WorkspaceSettingsStore.DefaultAppDataDirectory;
        builder.Services.AddDesktopComposition(new DesktopCompositionOptions
        {
            // Constructed here, at startup, rather than handed to the container as a
            // factory: the identity belongs to the installation, not to whichever
            // surface first asks for it, so device.json exists from the first start
            // whether or not the Dashboard or Sessions is ever opened — the pairing
            // that ADR 0005 attaches to this id will need it before any pane does.
            // Constructed rather than resolved for a second reason: the store offers
            // three public constructors, and letting the container choose between
            // them would make which file the real installation writes to depend on a
            // rule nobody reading this line can see.
            DeviceIdentity = new DeviceIdentityStore(),
            // DPAPI is the store ADR 0005 asks for on Windows, and this head is the
            // Windows one; the credential belongs to this machine and not to the
            // workspace.
            DeviceCredentialStore = _ => new DpapiDeviceCredentialStore(Path.Combine(appData, "device-credential.json")),
            // Plaintext because neither value is a secret.
            TaskSyncStateStore = _ => new FileTaskSyncStateStore(Path.Combine(appData, "task-sync-state.json")),
            SessionSyncFolder = appData,
            AnnotationSyncFolder = appData,
            BackupStatePath = Path.Combine(appData, "backup-state.json"),
            // The URL on the Settings page, per machine and never in the workspace.
            SyncServiceSettingsPath = Path.Combine(appData, "sync-service.json"),
            // The catalog is the shell's product copy; the store is the adapter that
            // remembers the choices. Composing the two is the host's job.
            FeatureSettings = _ => new AppFeatureSettingsStore(AppFeatures.All),
            WorkingHoursSettings = _ => new WorkingHoursSettingsStore(),
            UsageResetSettings = _ => new UsageResetSettingsStore(),
            // The pace document carries the device's working week (local ADR 0019), and
            // the Roadmap's Days off presses block a day in it.
            PlanningVelocitySettings = sp => new PlanningVelocitySettingsStore(sp.GetRequiredService<WorkingHoursSettingsStore>()),
            ShellNavigation = _ => new ShellNavigationStore(),
            CaptureSourceSettings = _ => new CaptureSourcesSettingsStore(),
            CaptureRunLog = _ => new CaptureRunLogStore(),
            CaptureTargetLedger = _ => new CaptureTargetLedgerStore(),
            ConnectedTargets = _ => new ConnectedTargetsSettingsStore(),
            // A refresh token is a secret, so DPAPI, beside github.json under the
            // per-user Backlog folder; the connected targets above stay plain text.
            SpecManagerTokenStore = _ => new DpapiSpecManagerTokenStore(),
            InboxRoutingRules = _ => new InboxRoutingRulesStore(),
            GitHubSettings = root => new GitHubSettingsStore(GitHubSettingsStore.DefaultLocalPath, root),
            PullRequestPins = _ => new PullRequestPinsStore(),
            ClaudeSettings = _ => new ClaudeSettingsStore(),
            AzureFoundrySettings = _ => new AzureFoundrySettingsStore(),
            // The desktop head's tools adapter runs the CLIs, and reads this head's
            // MCP endpoint - registered below - to describe the Backlog server's row.
            DevToolService = sp => new DevToolService(
                sp.GetRequiredService<ITaskStore>(),
                sp.GetRequiredService<IMcpEndpointSource>(),
                sp.GetService<ILogger<DevToolService>>()),
            FolderEditorLauncher = _ => new VsCodeFolderEditorLauncher(),
            // The MSIX head can manage its own updates when packaged; it degrades to
            // an "unsupported" report when running unpackaged (e.g. Debug), so this is
            // safe to register unconditionally.
            AppUpdateService = sp => ActivatorUtilities.CreateInstance<MsixAppUpdateService>(sp),
            CopilotCliLauncher = _ => new ProcessCopilotCliLauncher(),
            SessionRepositoryResolver = sp => new SettingsSessionRepositoryResolver(sp.GetRequiredService<GitHubSettingsStore>()),
            // One window, one user: the panes' state, the save-state band, the toast
            // channel, the Report issue channel and the Inbox's plan drafter are each
            // one object that outlives the page it is drawn on. The drafter is a
            // singleton because that is the lifetime the chain above it actually has
            // in this host: InboxDesktopState is a singleton, and everything it
            // reaches through IInboxItems is resolved once from the root and kept for
            // the window's life whatever its registration says.
            WindowStateLifetime = ServiceLifetime.Singleton
        });

        // The Devbook - its adapters and AddDevbookModule - is in the composition
        // above since issue #738: one window, so its domain store is a singleton
        // like the rest of the window's state, and its Copilot offers start the CLI
        // through the launcher handed in there.

        // The loopback MCP listener local ADR 0012 decided. TryAdd rather than
        // Add, the way AddTaskSyncClient registers its own worker: this head
        // composes it once and a second registration would be a second listener
        // fighting the first for one port. It takes the provider itself, because
        // the listener it builds has a container of its own and forwards every
        // port into this one rather than composing a second ITaskItems.
        builder.Services.TryAddSingleton<McpServerWorker>();
        // What the tools pane asks about that server: the port, the address it
        // makes, and whether the socket was taken. Beside the worker
        // because it reads it — and through a Func, so that resolving the tool
        // service is never what constructs the worker and binds the port. The
        // line below is where that is meant to happen, after Build().
        builder.Services.TryAddSingleton<IMcpEndpointSource>(sp => new DesktopMcpEndpointSource(
            sp.GetRequiredService<WorkspaceSettingsStore>(),
            sp.GetRequiredService<McpServerWorker>));

        // The bill for the Azure Foundry resource, read from Azure Cost Management
        // with the developer sign-in on this machine. Reports itself unavailable
        // until a cost scope is in Settings, so it is safe to register
        // unconditionally. This head's own because the web harness points the same
        // client at its local stand-in instead.
        builder.Services.AddAzureFoundryCostClient();

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

        // And the linked task sync's timer, on the same terms: each connected
        // repository or product syncs on its own interval only while this exists.
        _ = app.Services.GetRequiredService<LinkedTaskSyncWorker>();

        // And the MCP listener, on the same terms again - its constructor is
        // what binds the port, so a singleton nobody resolves is a server no
        // session can reach. There is no switch in front of it: the MCP server
        // is always on while the app runs.
        _ = app.Services.GetRequiredService<McpServerWorker>();

        // And the devbook database refresher, which is nothing until something
        // reads a devbook: resolving it configures where every reader looks for a
        // repository's database, so it has to exist before the first pane opens.
        // It schedules nothing at startup (local ADR 0015).
        _ = app.Services.GetRequiredService<DevbookDatabaseRefresher>();

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
