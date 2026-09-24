using Backlog.Desktop.UI.Inbox;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.Extensions;
using Backlog.Infrastructure.Sync;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.DomainModels;
using Microsoft.Extensions.Time.Testing;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the shell does with the Inbox: loads it when its pane first shows, and
/// when an item is routed reloads the Tasks pane from the store and opens it
/// alongside.
/// <para>
/// Driven through the rendered shell rather than the state, because the Inbox
/// raising <c>Routed</c> and the shell answering it is what is under test: the
/// Inbox knows nothing about a backlog row, and refreshing the pane that shows
/// one is the shell's job. The module's write into the backlog is stood in for
/// by the fake's <see cref="FakeInboxItems.OnRouted"/> hook writing an entry
/// through Tasks' own port — which is what the real adapter does.
/// </para>
/// </summary>
public sealed class HomeInboxWiringTests
{
    [Fact]
    public async Task Routing_an_item_reloads_the_tasks_from_the_store_and_opens_the_tasks_pane_alongside()
    {
        using var harness = CreateHarness();
        var item = harness.Inbox.Seed("Route me");
        harness.Inbox.OnRouted = (routed, _) =>
        {
            // The adapter's job, done here: the entry exists in the store before
            // the Inbox says it routed.
            var order = harness.Entries.ListAsync().GetAwaiter().GetResult().Count;
            var saved = harness.Entries.SaveFromTextAsync(null, $"# {routed.Title}\n`task` `!draft`\n", order).GetAwaiter().GetResult();
            Assert.True(saved.IsSuccess);
        };

        var component = Render(harness);
        await OpenInboxAsync(component);

        // The Inbox alone: the Tasks pane is closed until routing opens it.
        Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
        var state = State(harness);
        Assert.Empty(state.Rows);

        await component.Find($"[data-testid='inbox-item-{item.Id:D}']").ClickAsync(new());
        await component.Find("[data-testid='inbox-move-to-backlog']").ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.Contains(state.Rows, row => row.PreviewTitle == "Route me");
        });

        // Alongside, not instead: the Inbox the item was routed from stays.
        Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane']"));
        Assert.Contains("Inbox", harness.ShellNavigation.LastEnabledPanes);
        Assert.Contains("Tasks", harness.ShellNavigation.LastEnabledPanes);
    }

    /// <summary>
    /// The other writer from outside the pane: a sync pull lands another
    /// device's task in the same database. The timestamp poll that used to
    /// notice it is gone, so the shell has to hear the worker and reload the
    /// pane — and only when a cycle applied something, since the worker raises
    /// at both ends of every cycle including the ones that changed nothing.
    /// </summary>
    [Fact]
    public async Task A_sync_pull_that_applies_a_task_reloads_the_tasks_pane()
    {
        var pulled = new TaskItem("Pulled from the other machine", string.Empty, EntryType.Task);
        using var harness = CreateHarness(inboxOpenOnStart: false, pull: pulled);
        harness.ShellNavigation.SetLastPanes(["Tasks"]);

        var component = Render(harness);
        var state = State(harness);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']")));
        Assert.Empty(state.Rows);

        var worker = harness.Context.Services.GetRequiredService<TaskSyncWorker>();
        worker.RequestSync();

        component.WaitForAssertion(
            () => Assert.Contains(state.Rows, row => row.PreviewTitle == "Pulled from the other machine"),
            TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task The_inbox_loads_when_its_pane_first_shows_and_not_before()
    {
        using var harness = CreateHarness(inboxOpenOnStart: false);

        var component = Render(harness);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane-option']")));

        Assert.Equal(0, harness.Inbox.EnsureDefaultOrganizerCalls);

        await OpenInboxAsync(component);

        Assert.Equal(1, harness.Inbox.EnsureDefaultOrganizerCalls);
        Assert.NotEmpty(component.FindAll("[data-testid='inbox-nav-ungrouped']"));
    }

    [Fact]
    public async Task A_change_in_the_inbox_re_renders_the_shell()
    {
        using var harness = CreateHarness();

        var component = Render(harness);
        await OpenInboxAsync(component);

        Assert.Empty(component.FindAll("[data-testid='inbox-pane-item']"));

        // Something arrives behind the pane's back — a pull, say — and the state
        // is told to reload; the shell hears Changed and draws the row.
        harness.Inbox.Seed("Arrived by sync", channel: "mobile");
        await component.InvokeAsync(() => harness.Context.Services.GetRequiredService<InboxDesktopState>().ReloadAsync());

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='inbox-pane-item']")));
    }

    // --- Driving it -------------------------------------------------------

    private static async Task OpenInboxAsync(IRenderedComponent<Home> component)
    {
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane-option']")));

        if (component.FindAll("[data-testid='inbox-pane']").Count == 0)
        {
            await component.Find("[data-testid='inbox-pane-option']").ClickAsync(new());
        }

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane-list'], [data-testid='inbox-pane-empty']")));
    }

    private static TasksDesktopState State(Harness harness) =>
        harness.Context.Services.GetRequiredService<TasksDesktopState>();

    private static IRenderedComponent<Home> Render(Harness harness)
    {
        harness.Context.JSInterop.Mode = JSRuntimeMode.Loose;
        return harness.Context.Render<Home>();
    }

    /// <summary>
    /// The shell with the Inbox on and the Tasks pane off, and everything that
    /// would put extra chrome or a network call in the way off too. The Inbox
    /// ships behind a Dev flag, so a test about it has to turn it on first.
    /// </summary>
    private static Harness CreateHarness(bool inboxOpenOnStart = true, TaskItem? pull = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-inbox-wiring-tests", Guid.NewGuid().ToString("n"));
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        var shellNavigation = new ShellNavigationStore(Path.Combine(root, "shell", "shell-navigation.json"));

        // Only the Inbox open on start, unless the test is about opening it:
        // the routed entry has to open the Tasks pane itself for the first test
        // to prove anything.
        if (inboxOpenOnStart) shellNavigation.SetLastPanes(["Inbox"]);

        _ = featureSettings.SetEnabled(AppFeatures.InboxPane, true);
        _ = featureSettings.SetEnabled(RoadmapFeatures.Roadmap, false);
        _ = featureSettings.SetEnabled(DashboardFeatures.Dashboard, false);
        _ = featureSettings.SetEnabled(DevPcFeatures.SystemTools, false);
        _ = featureSettings.SetEnabled(SessionFeatures.Sessions, false);
        _ = featureSettings.SetEnabled(DevbookFeatures.DevbookSections, false);
        _ = featureSettings.SetEnabled(DevbookFeatures.RepositoryDevbook, false);
        _ = featureSettings.SetEnabled(AppFeatures.AiAssistant, false);
        _ = featureSettings.SetEnabled(AppFeatures.FeedbackReporting, false);
        _ = featureSettings.SetEnabled(TasksFeatures.GitHubIntegration, false);

        var gitHub = new GitHubIntegration(gitHubSettings, new StubGitHubClient(), new StubProbe());

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(featureSettings);
        context.Services.AddSingleton(shellNavigation);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton(new FeedbackReporter(gitHub));
        context.Services.AddSingleton<IAzureFoundryChatClient, StubAzureFoundryChatClient>();
        context.Services.AddSingleton<IDevToolService, UnsupportedDevToolService>();
        context.Services.AddSingleton<IAgentSessionSource>(new EmptySessionSource());
        context.Services.AddSingleton<IAppUpdateService, UnsupportedAppUpdateService>();
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(gitHubSettings, store));
        context.Services.AddSingleton<IRoadmapPlanning>(sp =>
            TasksTestHost.PlanningFor(sp.GetRequiredService<WorkspaceSettingsStore>()));
        context.Services.AddSingleton<IRoadmapItemRollup>(sp =>
            new Backlog.Infrastructure.FileSystem.Roadmap.RoadmapItemRollupService(
                TasksTestHost.EntriesFor(sp.GetRequiredService<WorkspaceSettingsStore>()),
                () => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
        context.Services.AddSingleton<IImportedPlanSource>(sp =>
            TasksTestHost.ImportedPlansFor(sp.GetRequiredService<WorkspaceSettingsStore>()));
        context.Services.AddSingleton<IRoadmapWorkChanges>(TasksTestHost.WorkChanges());
        context.Services.AddSingleton<DesignDevbookProvider>();
        context.Services.AddSingleton<AiDevbookProvider>();
        context.Services.AddSingleton<TechnologyDevbookService>();
        context.Services.AddSingleton<InstructionSourceDiscovery>();
        context.Services.AddSingleton<DevbookMenu>();
        context.Services.AddSingleton<Arc42DevbookStore>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<DevbookFolderOpenService>();
        context.Services.AddSingleton<DevbookScope>();
        // The pane publishes its open chapter here for the Ask AI source; a pane
        // rendered without it would fail on inject, as the application hosts would.
        context.Services.AddScoped<DevbookOpenChapter>();
        context.Services.AddSingleton<DevbookUpdateService>();
        context.Services.AddSingleton<IGitHubBranchCatalog>(new StubBranchCatalog());
        context.Services.AddSingleton<DevbookSourceSelection>();
        context.Services.AddSingleton(new DevbookCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        _ = context.Services.AddUnavailableDashboard("backlog", "backlog-ide");
        context.Services.AddScoped(sp => new DomainDevbookStore(sp.GetRequiredService<IDevbookFolderSource>()));

        // Home answers the Inbox's Capture button through the module's runner and
        // injects it hard, so a host that renders Home composes the module and
        // picks where its sources are kept, the same as the application hosts do.
        context.Services.AddSingleton<ICaptureSourceSettings>(
            new CaptureSourcesSettingsStore(Path.Combine(root, "capture", "capture-sources.json")));
        context.Services.AddSingleton<ICaptureRunLog>(
            new CaptureRunLogStore(Path.Combine(root, "capture", "capture-runs.json")));
        context.Services.AddCaptureModule();
        InboxTestHost.AddCaptureDelivery(context.Services);

        TasksTestHost.AddToastChannel(context.Services);
        context.Services.AddScoped(sp => TasksTestHost.StateFor(
            sp.GetRequiredService<WorkspaceSettingsStore>(),
            sp.GetRequiredService<GitHubIntegration>(),
            TasksCopilotCli.Unavailable));
        var inbox = InboxTestHost.AddInboxState(context.Services);

        // A task sync worker over a scripted replica that hands back one task,
        // writing through the same repository the pane reads. The real session
        // and the real worker, for the reason SettingsDevicesTests composes
        // them: what is under test is the shell hearing what the exchange did.
        if (pull is not null)
        {
            _ = featureSettings.SetEnabled(SyncFeatures.Sync, true);
            var credentials = new InMemoryDeviceCredentialStore(
                new DeviceCredential(Guid.NewGuid(), Guid.NewGuid(), "Workshop PC", "a-registration-credential"));
            var http = new HttpClient(new OnePullReplica(pull)) { BaseAddress = new Uri("https://sync.test") };
            var tasks = TasksTestHost.RepositoryFor(store);
            var syncState = new SettingsDevicesTests.ForgetfulTaskSyncStateStore();

            context.Services.AddSingleton<IDeviceCredentialStore>(credentials);
            context.Services.AddSingleton<ITaskSyncStateStore>(syncState);
            context.Services.AddSingleton(_ => new TaskSyncSession(
                new TaskSyncClient(http), new TaskReplicaMerge(tasks), tasks, syncState, credentials, TimeProvider.System));
            context.Services.AddSingleton(sp => new TaskSyncWorker(
                sp, featureSettings, credentials, syncState, new FakeTimeProvider()));
        }

        return new Harness(root, context, shellNavigation, TasksTestHost.EntriesFor(store), inbox);
    }

    private sealed record Harness(
        string Root,
        BunitContext Context,
        ShellNavigationStore ShellNavigation,
        ITaskItems Entries,
        FakeInboxItems Inbox) : IDisposable
    {
        public void Dispose()
        {
            Context.Dispose();

            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>A replica with one task to give: every push is accepted and
    /// the first pull hands the task back, on a cursor that then stays put.</summary>
    private sealed class OnePullReplica(TaskItem task) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { accepted = 0 })
                });
            }

            var firstPull = !request.RequestUri!.Query.Contains("since=cursor-1", StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    tasks = firstPull
                        ? new[] { new { change = TaskReplicaMerge.ToChange(task), deviceId = Guid.NewGuid(), serverTimestamp = 100L } }
                        : [],
                    since = "cursor-1",
                    hasMore = false
                })
            });
        }
    }

    private sealed class EmptySessionSource : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AgentSessionCatalog.Empty);
    }

    private sealed class StubAzureFoundryChatClient : IAzureFoundryChatClient
    {
        public Task<AzureFoundryChatResponse> AskAsync(AzureFoundryChatRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AzureFoundryChatResponse("Not used in this test."));

        public Task<AzureFoundryPlanResponse> DraftPlanAsync(AzureFoundryPlanRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AzureFoundryPlanResponse("# Not used in this test."));
    }

    private sealed class StubGitHubClient : IGitHubClient
    {
        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<GitHubIssue> CreateIssueAsync(
            GitHubRepositoryRef repository,
            string title,
            string? body,
            IEnumerable<string>? labels = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(
            GitHubRepositoryRef repository,
            int number,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(
            GitHubRepositoryRef repository,
            string path,
            string branch,
            byte[] content,
            string commitMessage,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not connected."));

        public void Invalidate()
        {
        }
    }
}
