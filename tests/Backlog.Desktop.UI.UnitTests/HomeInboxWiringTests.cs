using Backlog.Desktop.UI.Inbox;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.Services;
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
    private static Harness CreateHarness(bool inboxOpenOnStart = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-inbox-wiring-tests", Guid.NewGuid().ToString("n"));
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        var shellNavigation = new ShellNavigationStore(Path.Combine(root, "shell", "shell-navigation.json"));

        // Only the Inbox open on start, unless the test is about opening it:
        // the routed entry has to open the Tasks pane itself for the first test
        // to prove anything.
        if (inboxOpenOnStart) shellNavigation.SetLastPanes(["Inbox"], []);

        _ = featureSettings.SetEnabled(AppFeatures.InboxPane, true);
        _ = featureSettings.SetEnabled(RoadmapFeatures.Roadmap, false);
        _ = featureSettings.SetEnabled(DashboardFeatures.Dashboard, false);
        _ = featureSettings.SetEnabled(DevPcFeatures.SystemTools, false);
        _ = featureSettings.SetEnabled(SessionFeatures.Sessions, false);
        _ = featureSettings.SetEnabled(KnowledgeFeatures.KnowledgeSections, false);
        _ = featureSettings.SetEnabled(KnowledgeFeatures.RepositoryKnowledge, false);
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
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(gitHubSettings, store));
        context.Services.AddSingleton<IRoadmapPlanning>(sp =>
            TasksTestHost.PlanningFor(sp.GetRequiredService<WorkspaceSettingsStore>()));
        context.Services.AddSingleton<IRoadmapItemRollup>(sp =>
            new Backlog.Infrastructure.FileSystem.Roadmap.RoadmapItemRollupService(
                TasksTestHost.EntriesFor(sp.GetRequiredService<WorkspaceSettingsStore>()),
                () => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
        context.Services.AddSingleton<DesignKnowledgeProvider>();
        context.Services.AddSingleton<TechnologyKnowledgeService>();
        context.Services.AddSingleton<InstructionSourceDiscovery>();
        context.Services.AddSingleton<KnowledgeMenu>();
        context.Services.AddSingleton<Arc42KnowledgeStore>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<KnowledgeFolderOpenService>();
        context.Services.AddSingleton<KnowledgeScope>();
        context.Services.AddSingleton<KnowledgeUpdateService>();
        context.Services.AddSingleton<IGitHubBranchCatalog>(new StubBranchCatalog());
        context.Services.AddSingleton<KnowledgeSourceSelection>();
        context.Services.AddSingleton(new KnowledgeCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        _ = context.Services.AddUnavailableDashboard("backlog", "backlog-ide");
        context.Services.AddScoped(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));
        TasksTestHost.AddToastChannel(context.Services);
        context.Services.AddScoped(sp => TasksTestHost.StateFor(
            sp.GetRequiredService<WorkspaceSettingsStore>(),
            sp.GetRequiredService<GitHubIntegration>(),
            TasksCopilotCli.Unavailable));
        var inbox = InboxTestHost.AddInboxState(context.Services);

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

    private sealed class EmptySessionSource : IAgentSessionSource
    {
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
