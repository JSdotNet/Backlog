using Backlog.Desktop.UI.Inbox;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.Extensions;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Infrastructure.Sync;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Services;
using Microsoft.Extensions.Time.Testing;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the shell does with the Inbox: loads it when it first takes the screen,
/// and when an item is routed reloads the task list from the store while the Inbox
/// keeps the screen.
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
    public async Task Routing_an_item_reloads_the_tasks_from_the_store_and_keeps_the_inbox_on_screen()
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

        // The Inbox has the whole screen: the Tasks view is underneath, not beside it.
        Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
        var state = State(harness);
        Assert.Empty(state.Rows);

        await component.Find($"[data-testid='inbox-item-{item.Id:D}']").ClickAsync(new());
        await component.Find("[data-testid='inbox-move-to-backlog']").ClickAsync(new());

        component.WaitForAssertion(() => Assert.Contains(state.Rows, row => row.PreviewTitle == "Route me"));

        // The Inbox the item was routed from keeps the screen.
        Assert.NotEmpty(component.FindAll("[data-testid='inbox-surface'] [data-testid='inbox-pane']"));
        Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
        Assert.Equal("Inbox", harness.ShellNavigation.LastSurface);
        Assert.Equal("Tasks", harness.ShellNavigation.LastView);

        // The Inbox option returns to the list, which already holds the entry.
        await component.Find("[data-testid='inbox-pane-option']").ClickAsync(new());
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.Empty(component.FindAll("[data-testid='inbox-pane']"));
        });
    }

    /// <summary>Routed while the Roadmap view is under the Inbox, the view is left as
    /// the reader chose it: only the data moves, and the Inbox option returns to the
    /// roadmap.</summary>
    [Fact]
    public async Task Routing_an_item_over_the_roadmap_leaves_the_view_and_keeps_the_inbox()
    {
        using var harness = CreateHarness(startOnRoadmap: true);
        var item = harness.Inbox.Seed("Route me from the roadmap");
        harness.Inbox.OnRouted = (routed, _) =>
        {
            var order = harness.Entries.ListAsync().GetAwaiter().GetResult().Count;
            var saved = harness.Entries.SaveFromTextAsync(null, $"# {routed.Title}\n`task` `!draft`\n", order).GetAwaiter().GetResult();
            Assert.True(saved.IsSuccess);
        };

        var component = Render(harness);
        await OpenInboxAsync(component);

        await component.Find($"[data-testid='inbox-item-{item.Id:D}']").ClickAsync(new());
        await component.Find("[data-testid='inbox-move-to-backlog']").ClickAsync(new());

        component.WaitForAssertion(() => Assert.Contains(State(harness).Rows, row => row.PreviewTitle == "Route me from the roadmap"));

        Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane']"));
        Assert.Equal("Roadmap", harness.ShellNavigation.LastView);

        await component.Find("[data-testid='inbox-pane-option']").ClickAsync(new());
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='roadmap-view']")));
    }

    /// <summary>A batch is one import, so the shell hears of it once — through
    /// <c>BatchRouted</c>, not a <c>Routed</c> per item — and one reload shows
    /// every entry it made.</summary>
    [Fact]
    public async Task Routing_a_batch_reloads_the_tasks_once_and_shows_every_entry()
    {
        using var harness = CreateHarness();
        harness.Inbox.Seed("First of the batch");
        harness.Inbox.Seed("Second of the batch");
        harness.Inbox.OnRouted = (routed, _) =>
        {
            var order = harness.Entries.ListAsync().GetAwaiter().GetResult().Count;
            var saved = harness.Entries.SaveFromTextAsync(null, $"# {routed.Title}\n`task` `!draft`\n", order).GetAwaiter().GetResult();
            Assert.True(saved.IsSuccess);
        };

        var component = Render(harness);
        await OpenInboxAsync(component);
        var state = State(harness);
        var inbox = harness.Context.Services.GetRequiredService<InboxDesktopState>();
        var singles = 0;
        inbox.Routed += _ => singles++;

        await component.Find("[data-testid='inbox-select-toggle']").ClickAsync(new());
        await component.InvokeAsync(() => inbox.SetSelectAllVisible(true));
        await component.Find("[data-testid='inbox-bulk-backlog']").ClickAsync(new());
        await component.Find("[data-testid='inbox-route-panel-confirm']").ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            Assert.Contains(state.Rows, row => row.PreviewTitle == "First of the batch");
            Assert.Contains(state.Rows, row => row.PreviewTitle == "Second of the batch");
        });
        Assert.Equal(0, singles);
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

    /// <summary>
    /// The Inbox is re-read after a sync only for a cycle that brought something:
    /// one that applied a change, read once however many times the worker raised
    /// for it. A cycle that applied nothing — the worker runs one every five
    /// minutes, and raises at both ends of each — reads nothing and draws nothing.
    /// </summary>
    [Fact]
    public async Task A_sync_cycle_rereads_the_inbox_once_when_it_applied_something_and_not_when_it_applied_nothing()
    {
        var pulled = new TaskItem("Pulled while the inbox was open", string.Empty, EntryType.Task);
        using var harness = CreateHarness(pull: pulled);
        var component = Render(harness);
        await OpenInboxAsync(component);

        var reads = 0;
        harness.Inbox.BeforeSnapshot = () =>
        {
            Interlocked.Increment(ref reads);
            return Task.CompletedTask;
        };
        var worker = harness.Context.Services.GetRequiredService<TaskSyncWorker>();

        await RunCycleAsync(component, worker);

        component.WaitForAssertion(
            () => Assert.Contains(State(harness).Rows, row => row.PreviewTitle == "Pulled while the inbox was open"),
            TimeSpan.FromSeconds(10));
        component.WaitForAssertion(() => Assert.Equal(1, Volatile.Read(ref reads)));
        Assert.Equal(1, worker.LastSummary?.Applied);

        var shellBefore = ShellRenderCount.Shell(component);

        await RunCycleAsync(component, worker);

        Assert.Equal(0, worker.LastSummary?.Applied);
        Assert.Equal(1, Volatile.Read(ref reads));
        Assert.Equal(shellBefore, ShellRenderCount.Shell(component));
    }

    /// <summary>A cycle that failed may have applied pages before it did, and says
    /// nothing about how many, so the Inbox is re-read after it.</summary>
    [Fact]
    public async Task A_sync_cycle_that_failed_rereads_the_inbox()
    {
        var pulled = new TaskItem("Pulled before the replica failed", string.Empty, EntryType.Task);
        using var harness = CreateHarness(pull: pulled);
        var component = Render(harness);
        await OpenInboxAsync(component);

        var worker = harness.Context.Services.GetRequiredService<TaskSyncWorker>();
        await RunCycleAsync(component, worker);
        component.WaitForAssertion(
            () => Assert.Contains(State(harness).Rows, row => row.PreviewTitle == "Pulled before the replica failed"),
            TimeSpan.FromSeconds(10));

        var reads = 0;
        harness.Inbox.BeforeSnapshot = () =>
        {
            Interlocked.Increment(ref reads);
            return Task.CompletedTask;
        };
        harness.Replica!.Failing = true;

        await RunCycleAsync(component, worker);

        Assert.NotNull(worker.LastError);
        component.WaitForAssertion(() => Assert.True(Volatile.Read(ref reads) >= 1));
    }

    /// <summary>
    /// The third writer from outside the pane: the app's own MCP server, which an
    /// agent uses to move an entry along while the person watches the list. It
    /// writes through the same use cases the pane does, on a request thread, and
    /// the pane used to go on showing the old status until the app restarted —
    /// nothing told it, because only the sync worker listened to the signal.
    /// </summary>
    [Fact]
    public async Task A_task_written_outside_the_pane_reloads_the_tasks_pane()
    {
        using var harness = CreateHarness(inboxOpenOnStart: false);
        var saved = await harness.Entries.SaveFromTextAsync(null, "# Moved along by an agent\n`task` `!ready`\n", 0, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(saved.IsSuccess);

        var component = Render(harness);
        var state = State(harness);
        component.WaitForAssertion(() => Assert.Contains(
            state.Rows, row => row.PreviewTitle == "Moved along by an agent" && row.Status == EntryStatus.Ready));

        // Off the renderer's thread, as a request to the MCP endpoint is.
        var moved = await Task.Run(() => harness.Entries.SaveFromTextAsync(
            saved.Value.Entry.Id, "# Moved along by an agent\n`task` `!done`\n", 0));
        Assert.True(moved.IsSuccess);

        component.WaitForAssertion(
            () => Assert.Contains(
                state.Rows, row => row.PreviewTitle == "Moved along by an agent" && row.Status == EntryStatus.Done),
            TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task The_inbox_loads_when_it_first_shows_and_not_before()
    {
        using var harness = CreateHarness(inboxOpenOnStart: false);

        var component = Render(harness);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane-option']")));

        Assert.Equal(0, harness.Inbox.EnsureDefaultOrganizerCalls);

        await OpenInboxAsync(component);

        Assert.Equal(1, harness.Inbox.EnsureDefaultOrganizerCalls);
        Assert.NotEmpty(component.FindAll("[data-testid='inbox-nav-ungrouped']"));
    }

    /// <summary>The resurface sweep runs on every opening of the Inbox, not just
    /// the first: a deferred item that came due while it was closed is back in the
    /// queue when it opens again.</summary>
    [Fact]
    public async Task Every_opening_of_the_inbox_runs_the_resurface_sweep()
    {
        using var harness = CreateHarness(inboxOpenOnStart: false);

        var component = Render(harness);
        await OpenInboxAsync(component);
        Assert.Equal(1, harness.Inbox.ResurfaceDueCalls);

        harness.Inbox.Seed("Came due while closed", status: InboxStatus.Deferred, deferredUntil: DateOnly.FromDateTime(harness.Inbox.Now.DateTime));

        // A press on its own option leaves the Inbox for the view under it.
        await component.Find("[data-testid='inbox-pane-option']").ClickAsync(new());
        component.WaitForAssertion(() => Assert.Equal("false", component.Find("[data-testid='inbox-pane-option']").GetAttribute("aria-pressed")));
        Assert.Equal(1, harness.Inbox.ResurfaceDueCalls);

        await component.Find("[data-testid='inbox-pane-option']").ClickAsync(new());

        component.WaitForAssertion(() => Assert.Equal(2, harness.Inbox.ResurfaceDueCalls));
        component.WaitForAssertion(() => Assert.Contains(
            component.FindAll("[data-testid='inbox-pane-item']"), row => row.TextContent.Contains("Came due while closed", StringComparison.Ordinal)));
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

    /// <summary>Escape is the Inbox's own key — it leaves triage for the list — so
    /// the shell's document-level Escape, which closes every other takeover, leaves
    /// the Inbox on screen.</summary>
    [Fact]
    public async Task Escape_heard_at_the_document_does_not_leave_the_inbox()
    {
        using var harness = CreateHarness();

        var component = Render(harness);
        await OpenInboxAsync(component);

        await component.InvokeAsync(() => component.Instance.CloseSurfaceOnEscapeAsync());

        Assert.NotEmpty(component.FindAll("[data-testid='inbox-surface']"));
        Assert.Equal("Inbox", harness.ShellNavigation.LastSurface);
    }

    /// <summary>Opening the Inbox puts focus inside its pane, not on the surface
    /// around it: the Inbox's keys are heard only with focus in the pane, so j, k,
    /// t and Escape out of triage work without a click first.</summary>
    [Fact]
    public async Task Opening_the_inbox_moves_focus_into_its_pane()
    {
        using var harness = CreateHarness(inboxOpenOnStart: false);

        var component = Render(harness);
        await OpenInboxAsync(component);

        component.WaitForAssertion(() => Assert.Contains(
            harness.Context.JSInterop.Invocations["backlogFocus"],
            invocation => Equals(invocation.Arguments[0], "inbox-pane")));
        Assert.Empty(harness.Context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"]);
        Assert.Equal("-1", component.Find("[data-testid='inbox-pane']").GetAttribute("tabindex"));
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

    /// <summary>
    /// One whole sync cycle, and everything the shell queued on hearing it. A cycle
    /// that runs raises the worker's Changed twice, at its start and at its end;
    /// the shell subscribed first, so by the second raise here it has queued its
    /// answer to both, and the empty dispatch after it runs behind them.
    /// </summary>
    private static async Task RunCycleAsync(IRenderedComponent<Home> component, TaskSyncWorker worker)
    {
        var raises = 0;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnChanged()
        {
            if (Interlocked.Increment(ref raises) == 2) finished.TrySetResult();
        }

        worker.Changed += OnChanged;
        try
        {
            worker.RequestSync();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            worker.Changed -= OnChanged;
        }

        await component.InvokeAsync(() => { });
    }

    private static TasksDesktopState State(Harness harness) =>
        harness.Context.Services.GetRequiredService<TasksDesktopState>();

    private static IRenderedComponent<Home> Render(Harness harness)
    {
        harness.Context.JSInterop.Mode = JSRuntimeMode.Loose;
        return harness.Context.Render<Home>();
    }

    /// <summary>
    /// The shell with the Inbox on, and everything that
    /// would put extra chrome or a network call in the way off too. The Inbox
    /// ships behind a Dev flag, so a test about it has to turn it on first.
    /// </summary>
    private static Harness CreateHarness(bool inboxOpenOnStart = true, TaskItem? pull = null, bool startOnRoadmap = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-inbox-wiring-tests", Guid.NewGuid().ToString("n"));
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        var shellNavigation = new ShellNavigationStore(Path.Combine(root, "shell", "shell-navigation.json"));

        // The Inbox on screen on start, over the Tasks view, unless the test is
        // about opening it — or over the Roadmap view, when the test is about
        // routing leaving the view alone.
        if (inboxOpenOnStart) shellNavigation.SetLastSurface("Inbox");
        if (startOnRoadmap) shellNavigation.SetLastView("Roadmap");

        _ = featureSettings.SetEnabled(AppFeatures.InboxPane, true);
        _ = featureSettings.SetEnabled(RoadmapFeatures.Roadmap, startOnRoadmap);
        _ = featureSettings.SetEnabled(DashboardFeatures.Dashboard, false);
        _ = featureSettings.SetEnabled(DevPcFeatures.SystemTools, false);
        _ = featureSettings.SetEnabled(SessionFeatures.Sessions, false);
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
        context.Services.AddSingleton(TasksTestHost.UntouchedPace());
        context.Services.AddSingleton<IRoadmapActualHours>(new ScriptedActualHours());
        context.Services.AddSingleton<IRoadmapViewPreferences>(new Backlog.Infrastructure.FileSystem.Roadmap.RoadmapViewPreferences(null));
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
        context.Services.AddSingleton<ICaptureTargetLedger>(
            new CaptureTargetLedgerStore(Path.Combine(root, "capture", "capture-targets.json")));
        context.Services.AddCaptureModule();
        InboxTestHost.AddCaptureDelivery(context.Services);

        // One change signal and one set of use cases for the pane and for the
        // test's writes outside it, which is how the hosts compose them: the
        // MCP server's tools and the pane both hold the singleton ITaskItems.
        var taskWrites = new TaskChangeSignal();
        var entries = TasksTestHost.EntriesFor(TasksTestHost.RepositoryFor(store, taskWrites));

        TasksTestHost.AddToastChannel(context.Services);
        context.Services.AddScoped(sp => new TasksDesktopState(
            TasksTestHost.TaskStoreFor(sp.GetRequiredService<WorkspaceSettingsStore>()),
            entries,
            sp.GetRequiredService<GitHubIntegration>(),
            TasksCopilotCli.Unavailable,
            taskWrites: taskWrites));
        var inbox = InboxTestHost.AddInboxState(context.Services);

        // A task sync worker over a scripted replica that hands back one task,
        // writing through the same repository the pane reads. The real session
        // and the real worker, for the reason SettingsDevicesTests composes
        // them: what is under test is the shell hearing what the exchange did.
        OnePullReplica? replica = null;
        if (pull is not null)
        {
            _ = featureSettings.SetEnabled(SyncFeatures.Sync, true);
            var credentials = new InMemoryDeviceCredentialStore(
                new DeviceCredential(Guid.NewGuid(), Guid.NewGuid(), "Workshop PC", "a-registration-credential"));
            replica = new OnePullReplica(pull);
            var http = new HttpClient(replica) { BaseAddress = new Uri("https://sync.test") };
            var tasks = TasksTestHost.RepositoryFor(store);
            var syncState = new SettingsDevicesTests.ForgetfulTaskSyncStateStore();

            context.Services.AddSingleton<IDeviceCredentialStore>(credentials);
            context.Services.AddSingleton<ITaskSyncStateStore>(syncState);
            context.Services.AddSingleton(_ => new TaskSyncSession(
                new TaskSyncClient(http), new TaskReplicaMerge(tasks), tasks, syncState, credentials, TimeProvider.System));
            context.Services.AddSingleton(sp => new TaskSyncWorker(
                sp, featureSettings, credentials, syncState, new FakeTimeProvider()));
        }

        return new Harness(root, context, shellNavigation, entries, inbox, replica);
    }

    private sealed record Harness(
        string Root,
        BunitContext Context,
        ShellNavigationStore ShellNavigation,
        ITaskItems Entries,
        FakeInboxItems Inbox,
        OnePullReplica? Replica = null) : IDisposable
    {
        public void Dispose()
        {
            Context.Dispose();
            TestDatabases.Release(Root);

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
    /// the first pull hands the task back, on a cursor that then stays put —
    /// until it is told to fail, when every pull is refused.</summary>
    private sealed class OnePullReplica(TaskItem task) : HttpMessageHandler
    {
        public bool Failing { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Failing && request.Method != HttpMethod.Post)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

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
}
