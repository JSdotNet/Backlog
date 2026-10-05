using Backlog.Desktop.UI.Devbook.Extensions;
using Backlog.Desktop.UI.Inbox;
using Backlog.Desktop.UI.Shell;
using Backlog.Desktop.UI.Tasks;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Capture.Extensions;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.Devbook;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.FileSystem.Dashboard;
using Backlog.Infrastructure.FileSystem.Inbox;
using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sessions;
using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.Sync;
using Backlog.Infrastructure.Sync.Extensions;
using Backlog.Modules.Capture.Extensions;
using Backlog.Modules.Dashboard.Extensions;
using Backlog.Modules.Dashboard.UI.Extensions;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.DevPc.UI;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Extensions;
using Backlog.Modules.Roadmap.Extensions;
using Backlog.Modules.Roadmap.UI;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI.Extensions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Extensions;
using Backlog.SharedKernel;
using Backlog.UI.Components.Feedback;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backlog.Desktop.Composition;

/// <summary>
/// The one copy of what the MAUI desktop head and the desktop web harness
/// compose.
/// <para>
/// Each head calls <see cref="AddDesktopComposition"/> once, with
/// <see cref="DesktopCompositionOptions"/> for what it does differently, and
/// registers only what is its own beside the call: the MAUI head its WebView, MCP
/// listener, update service and debug tooling; the harness its Razor components,
/// MCP HTTP transport and local-development stand-ins. The web harness is started
/// by a test and the MAUI head cannot be, so sharing this is what puts the MAUI
/// head's registrations in front of provider validation at all.
/// </para>
/// <para>
/// The Devbook is here too since issue #738: the adapters behind its ports — the
/// snapshot cache, the database refresher, the folder source, both retrieval tiers
/// and the annotation store — and then <c>AddDevbookModule</c> for the module's own
/// stores and screens, with the Copilot launcher and the window's lifetime the heads
/// already hand in. The Devbook's Ask AI source stays in its place among the
/// others, because the order the areas are offered in is the order they are
/// registered.
/// </para>
/// <para>
/// Nothing in here asks which head it is running in. Whatever differs arrives
/// through the options, so a branch on the environment would be a second
/// composition that only one head's test ever starts.
/// </para>
/// </summary>
public static class DesktopCompositionRegistration
{
    public static IServiceCollection AddDesktopComposition(this IServiceCollection services, DesktopCompositionOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // The workspace settings file, and the two module ports the adapters over it
        // answer. The knowledge resolver is what both ports share, so neither context
        // has to see the other's settings.
        services.AddSingleton<WorkspaceSettingsStore>();
        services.AddSingleton<ITaskStore>(sp => new WorkspaceTaskStore(
            sp.GetRequiredService<WorkspaceSettingsStore>()));

        // Which hours the reader means to be working and when the assistant's weekly
        // allowance resets: kernel ports, because the settings screen writes them and
        // the dashboard reads them, and neither may reach through the other. How many
        // points the reader gets through in a week, which Roadmap reads through
        // IPlanningVelocitySettings over this store (AddRoadmapCrossContextAdapters).
        // And which surface the shell was last showing, so it reopens there.
        // The roadmap counts the same week, and its pace document carries it between
        // devices (local ADR 0019): resolving the week resolves the pace store too, so
        // a change made on the settings screen is always written into the document.
        services.AddSingleton(options.WorkingHoursSettings);
        services.AddSingleton<IWorkingHoursSettings>(sp =>
        {
            _ = sp.GetRequiredService<PlanningVelocitySettingsStore>();
            return sp.GetRequiredService<WorkingHoursSettingsStore>();
        });
        services.AddSingleton(options.UsageResetSettings);
        services.AddSingleton(options.PlanningVelocitySettings);
        services.AddSingleton(options.ShellNavigation);
        // What can change which surface is showing from outside the user interface —
        // the delivery surface's open_dashboard asks. Twice over so the shell can
        // resolve the concrete type it attaches to while every caller sees the port.
        services.AddSingleton<SessionsSurfaceActivator>();
        services.AddSingleton<ISessionsSurfaceActivator>(sp => sp.GetRequiredService<SessionsSurfaceActivator>());
        // Which machine this installation is. Sessions stamps every record with it
        // and the Dashboard filters by it, and neither owns the answer.
        services.AddSingleton(options.DeviceIdentity);

        // The Tasks module brings its use cases and the host picks the adapter behind
        // them: the task, roadmap and inbox stores in backlog.db, following the
        // storage folder rather than pinned to wherever it was at startup, because
        // somebody can move their backlog while the app is open.
        services.AddSqlite(sp => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory);
        services.AddTasksModule();

        // Linked tasks (local ADR 0020): the head decides where the connected
        // targets are kept, and the module brings the sync and its timer. No
        // connector ships yet, so the timer starts none until one is added with
        // AddTaskConnector<T>().
        services.AddSingleton(options.ConnectedTargets);
        services.AddLinkedTaskSync();

        services.AddRoadmapModule();
        // The plan behind the shell's Ask AI port, after the module so the scoped
        // planning port it holds exists.
        services.AddRoadmapAiContentSource();

        // Capture: the module brings the run, and the head decides where the
        // monitored sources and what past runs said are kept. The source adapters
        // and the delivery come further down, after the Inbox they deliver into.
        services.AddSingleton(options.CaptureSourceSettings);
        services.AddSingleton(options.CaptureRunLog);
        services.AddCaptureModule();

        // The two cross-context joins the plan takes part in, answered by adapters
        // because only an adapter may see both contexts. Scoped, like the module
        // services they capture.
        services.AddRoadmapCrossContextAdapters();

        // The reader's routing rules, which Settings writes and Classification reads.
        services.AddSingleton(options.InboxRoutingRules);
        services.AddInboxModule();

        // The Inbox's backlog target, over Tasks' published port, after
        // AddTasksModule(); and Capture's feed readers and the delivery into the
        // Inbox's intake, after AddInboxModule().
        services.AddInboxCrossContextAdapters();
        services.AddCaptureAdapters();

        // Half of a repository's configuration is workspace data and follows the
        // backlog folder: the root is read per call, and moving the workspace
        // re-reads the registry instead of leaving the old one's repositories on
        // screen.
        services.AddSingleton(sp =>
        {
            var workspace = sp.GetRequiredService<WorkspaceSettingsStore>();
            var store = options.GitHubSettings(() => workspace.RootDirectory);
            workspace.RootChanged += store.Reload;
            return store;
        });
        services.AddGitHub();
        services.AddWorkspaceCaches();
        services.AddSingleton(options.FeatureSettings);

        // The device half of cloud sync and this device's replication progress, per
        // machine and never in the workspace root: a workspace root can be a folder
        // some other product syncs, at which point another device would adopt this
        // one's watermark and skip its own unpushed work (ADR 0005). AddSyncClient
        // registers no store of its own, so a head without one would have the
        // session registered and unconstructable.
        services.AddSingleton(options.DeviceCredentialStore);
        services.AddSingleton(options.TaskSyncStateStore);
        services.AddSessionSyncStores(options.SessionSyncFolder);
        services.AddAnnotationSyncStore(options.AnnotationSyncFolder);
        // What the last backup did, per installation. The worker reads the
        // repository and the schedule off the workspace settings and uploads through
        // the same GitHub client the feedback dialog commits screenshots with.
        services.AddSingleton<IBackupStateStore>(_ => new FileBackupStateStore(options.BackupStatePath));
        services.AddSingleton<BackupWorker>();

        // Where the sync service is, asked per client rather than fixed here: the URL
        // on the Settings page, then BACKLOG_SYNC_URL, then "https+http://sync",
        // which Aspire service discovery rewrites to this run's sync resource. Task
        // replication is a second call because it needs the ITaskRepository above.
        services.AddSingleton(_ => new SyncServiceSettingsStore(options.SyncServiceSettingsPath));
        services.AddSingleton<SyncServiceEndpoint>();
        services.AddSyncClient(SyncServiceAddress);
        services.AddTaskSyncClient(SyncServiceAddress);

        services.AddSingleton(options.AzureFoundrySettings);
        // The chat client's pipeline is the adapter's own, sized for a completion
        // rather than for the service-to-service defaults — see
        // AzureFoundryRegistration. The cost client is each head's: the harness
        // points it at its local stand-in.
        services.AddAzureFoundryChatClient();
        // The Inbox's plan drafter over the same chat client, as long-lived as the
        // window's state that reaches it through IInboxItems.
        services.Add(new ServiceDescriptor(typeof(IInboxPlanDrafter), typeof(AzureFoundryInboxPlanDrafter), options.WindowStateLifetime));
        // The embedding deployment beside the chat one, registered and dormant:
        // local ADR 0004's semantic tier is wired and nothing writes vectors yet.
        services.AddHttpClient<IAzureFoundryEmbeddingsClient, AzureFoundryEmbeddingsClient>();

        // Claude usage reporting reports itself unavailable until an Admin API key
        // is configured, so it is safe to register unconditionally.
        services.AddClaude(
            options.ClaudeSettings,
            sp => sp.GetRequiredService<WorkspaceSettingsStore>().SpendCacheDirectory);

        // The Dashboard module brings its derivations; the adapters beside it decide
        // which providers are behind them. Every part reports itself unavailable
        // with a reason until the credential it needs exists.
        services.AddDashboardModule();
        services.AddGitHubDashboardAdapters();
        services.AddClaudeDashboardAdapters();
        services.AddAzureFoundryDashboardAdapters();
        services.AddDashboardUi();

        // Tasks' own adapter: an imported plan resolves a `repo:` name against the
        // repositories somebody has configured, and registers one it names that
        // nobody has (ADR 0007).
        services.AddTasksAdapters();

        services.AddSingleton<FeedbackReporter>();
        // One per window: the error screen asks the footer's dialog to open through
        // it, and in the harness a request belongs to the circuit that raised it.
        services.Add(new ServiceDescriptor(typeof(FeedbackReportChannel), typeof(FeedbackReportChannel), options.WindowStateLifetime));
        services.AddCopilot(options.CopilotCliLauncher);
        services.AddSingleton<TasksCopilotCli>();
        // The Devbook: its adapters, then the module over them. The launcher is the
        // one AddCopilot registers above, handed in rather than resolved so the
        // module says which port it starts through; the domain store lives as long
        // as one window's state does.
        AddDevbookAdapters(services);
        services.AddDevbookModule(options.CopilotCliLauncher, options.WindowStateLifetime);
        // The open-chapter mirror the Devbook pane writes and the Ask AI source that
        // pins from it, over the search and folder ports registered just above.
        services.AddDevbookAiContentSource();

        // One window's state, and what reads it. The panes' state captures the
        // modules' scoped services; see WindowStateLifetime for why that makes it a
        // singleton in one head and scoped in the other.
        services.Add(new ServiceDescriptor(typeof(TasksDesktopState), typeof(TasksDesktopState), options.WindowStateLifetime));
        services.AddTasksAiContentSource();
        services.Add(new ServiceDescriptor(typeof(InboxDesktopState), typeof(InboxDesktopState), options.WindowStateLifetime));
        services.AddInboxAiContentSource();
        // The Inbox's page on the settings screen: the routing rules. The shell draws
        // it only because it is registered here, and holds no copy of its own.
        services.AddInboxSettings();
        // The Connectors page: connect a repository or a product and set how it
        // syncs. Drawn from the connectors' descriptors, so it needs no change when
        // one is added.
        services.AddTaskConnectorSettings();
        // The band under every route reads the backlog's save state through the
        // library's interface, and the toast tray in MainLayout reads the channel a
        // screen puts a message in; concrete type and interface resolve the same one.
        services.Add(new ServiceDescriptor(
            typeof(ISaveStatusSource),
            sp => sp.GetRequiredService<TasksDesktopState>(),
            options.WindowStateLifetime));
        services.Add(new ServiceDescriptor(typeof(ToastChannel), typeof(ToastChannel), options.WindowStateLifetime));
        services.Add(new ServiceDescriptor(
            typeof(IToastChannel),
            sp => sp.GetRequiredService<ToastChannel>(),
            options.WindowStateLifetime));
        services.AddSingleton(options.FolderEditorLauncher);

        services.AddSingleton(options.AppUpdateService);
        services.AddSingleton(options.DevToolService);
        // The tool catalog behind the shell's Ask AI port, beside the port it reads.
        services.AddToolsAiContentSource();

        // The session list reads the two agents' own folders in the profile of
        // whoever is signed in, so there is nothing for a head to differ about. It
        // stamps what it finds with the device identity above.
        services.AddAgentSessionSource();
        services.AddSessionsAiContentSource();

        // Session and annotation replication, on top of AddSyncClient above and
        // after the readers they push from; both answer to the same Sync switch as
        // the task loop.
        services.AddSessionSyncClient(SyncServiceAddress);
        services.AddAnnotationSyncClient(SyncServiceAddress);

        // Which registered clone a session's working folder lies inside, read off the
        // repository list the Repositories screen writes, so a header scoped to one
        // repository can hold the Claude sessions running in its clone.
        services.AddSingleton(options.SessionRepositoryResolver);

        // When those sessions were producing, read out of the transcripts' bodies.
        // This machine's transcripts only: a replicated record's file never left the
        // machine it describes. Then the join between the Dashboard's sessions part
        // and the Sessions context, after both of the ports it sits between.
        services.AddAgentActivitySource();
        services.AddDashboardCrossContextAdapters();

        return services;
    }

    /// <summary>The base-address callback the three sync registrations share, so
    /// they cannot disagree about which service a head is talking to.</summary>
    private static Uri SyncServiceAddress(IServiceProvider services) =>
        services.GetRequiredService<SyncServiceEndpoint>().Resolve().Address;

    /// <summary>
    /// The adapters behind the Devbook module's ports, which both heads composed
    /// alike when each registered its own Devbook block. The module's own services
    /// are <c>AddDevbookModule</c>'s; these are the composition's because they are
    /// file-system, GitHub and database adapters the Devbook UI project cannot see.
    /// </summary>
    private static void AddDevbookAdapters(IServiceCollection services)
    {
        // Devbook read from a repository branch, for a repository nobody has
        // cloned. The network half — one listing per commit, one blob per file
        // somebody opens — lives in the GitHub adapter and the disk half in the
        // file-system one; the cache root arrives as a delegate rather than as the
        // workspace store, because the GitHub adapter may not see that one.
        services.AddSingleton<IDevbookSnapshotCache>(sp => new DevbookSnapshotCache(
            () => sp.GetRequiredService<WorkspaceSettingsStore>().DevbookCacheDirectory,
            sp.GetRequiredService<IGitHubTreeClient>(),
            sp.GetRequiredService<IGitHubBranchCatalog>()));

        // The devbook database: built by the app into the same cache folder as the
        // snapshots, one per repository path, in the background when a repository
        // is first read (local ADR 0015). Each head resolves it after Build(), which
        // is what points every database reader at that folder.
        services.AddSingleton(sp => DevbookDatabaseRefresher.StartForApp(
            () => sp.GetRequiredService<WorkspaceSettingsStore>().DevbookCacheDirectory,
            sp.GetService<ILogger<DevbookDatabaseRefresher>>()));

        services.AddSingleton<IDevbookFolderSource>(sp => new DevbookFolderSource(
            sp.GetRequiredService<GitHubSettingsStore>(),
            sp.GetRequiredService<WorkspaceSettingsStore>(),
            sp.GetRequiredService<IDevbookSnapshotCache>()));

        // Retrieval, both tiers. Adapters over the generated database rather than
        // over the Markdown: search is the one capability ADR 0004's ladder does
        // not let degrade to a corpus scan, so where there is no database these
        // report that in words instead of answering slowly or answering nothing.
        services.AddSingleton<IDevbookSearch>(sp =>
            new DevbookFullTextSearch(sp.GetRequiredService<IDevbookFolderSource>()));
        services.AddSingleton<IDevbookVectorSearch>(sp =>
            new DevbookSemanticSearch(sp.GetRequiredService<IDevbookFolderSource>(), DevbookEmbeddingModel.Default));
        // What a task's Devbook references point at, for the Tasks detail panel and
        // the MCP reference tools: Tasks' port, answered over the same database and
        // folders, falling back to the Markdown where there is no database.
        services.AddSingleton<IDevbookReferenceResolver>(sp =>
            new DevbookReferenceResolver(sp.GetRequiredService<IDevbookFolderSource>()));

        // A person's remarks on Devbook chapters: one JSON file per repository
        // under the storage folder, following the root the way the inbox store
        // does so a moved backlog takes its remarks along. The panels resolve this
        // by interface and fall back to a session-scoped store when it is absent,
        // which is why leaving this line out would not fail — it would only forget.
        // The folder source is what lets it name a chapter the way every other
        // device names it, whichever folder this machine has an area pointed at.
        services.AddSingleton<IDevbookAnnotationStore>(sp =>
            new DevbookAnnotationStore(
                () => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory,
                folders: sp.GetRequiredService<IDevbookFolderSource>()));
    }
}
