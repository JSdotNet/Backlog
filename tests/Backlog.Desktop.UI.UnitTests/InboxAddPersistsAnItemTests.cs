using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.Extensions;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the Inbox's Add and Capture do inside the rendered shell.
/// <para>
/// Add is the pane's own — the dialog's title and notes go through the Inbox's
/// module as an item with the notes as its body — so what is under test here is
/// that the shell composes the pane over a module that actually receives it, and
/// that nothing else in the shell reacts: no backlog row, no Tasks pane opening.
/// Capture is the pane raising a callback and the shell answering it with a run
/// over the settings, which the pane knows nothing about.
/// <see cref="HomeInboxWiringTests"/> is the same arrangement for routing.
/// </para>
/// </summary>
public sealed class InboxAddPersistsAnItemTests
{
    private const string Title = "Ask about the trial length";
    private const string Notes = "Before Friday, and in writing.";

    // --- Add --------------------------------------------------------------

    [Fact]
    public async Task Adding_from_the_inbox_files_an_item_with_its_title_and_notes()
    {
        using var harness = CreateHarness();

        var component = Render(harness);
        await AddAsync(component, Title, Notes);

        var item = Assert.Single(harness.Inbox.Items);

        Assert.Equal(Title, item.Title);
        Assert.Equal(Notes, item.BodyMd);
        Assert.Equal("manual", item.Channel);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
    }

    /// <summary>An item typed into the Inbox arrives through the manual channel
    /// — which is what the row's badge has to say — and the row is on screen
    /// without anybody reloading.</summary>
    [Fact]
    public async Task The_new_item_appears_in_the_inbox_as_a_manual_item()
    {
        using var harness = CreateHarness();

        var component = Render(harness);
        await AddAsync(component, Title, Notes);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane-list']")));

        Assert.Contains(Title, component.Find("[data-testid='inbox-pane-list']").TextContent, StringComparison.Ordinal);
        Assert.Equal("Manual", component.Find("[data-testid='inbox-pane-item-source']").TextContent.Trim());
    }

    /// <summary>Add is capture, not triage. The reader is filling the queue: no
    /// backlog entry is written, and the Tasks pane stays closed.</summary>
    [Fact]
    public async Task Adding_writes_no_backlog_entry_and_opens_no_backlog_pane()
    {
        using var harness = CreateHarness();

        var component = Render(harness);
        await AddAsync(component, Title, Notes);
        var state = State(harness);

        Assert.Empty(await harness.Entries.ListAsync(TestContext.Current.CancellationToken));
        Assert.Null(state.SelectedRow);
        Assert.Null(state.EditingRow);
        Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
        Assert.Empty(component.FindAll("[data-testid='entry-detail']"));
    }

    [Fact]
    public async Task Notes_are_optional()
    {
        using var harness = CreateHarness();

        var component = Render(harness);
        await AddAsync(component, Title, notes: null);

        var item = Assert.Single(harness.Inbox.Items);
        Assert.Equal(Title, item.Title);
        Assert.Equal(string.Empty, item.BodyMd);
    }

    [Fact]
    public async Task A_blank_title_is_refused_by_the_dialog_and_files_nothing()
    {
        using var harness = CreateHarness();

        var component = Render(harness);
        await WaitForInboxAsync(component);

        await component.Find("[data-testid='inbox-pane-add']").ClickAsync(new());
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane-add-dialog']")));

        Assert.True(component.Find("[data-testid='inbox-pane-add-submit']").HasAttribute("disabled"));
        Assert.Empty(harness.Inbox.Items);
    }

    /// <summary>The dialog has closed by the time the module answers, so a
    /// refusal is a toast — its own, so a driver can tell it from an item act's.
    /// Read from the channel: the toast stack is MainLayout's, below every
    /// route, and this renders Home alone.</summary>
    [Fact]
    public async Task A_refused_add_is_a_toast()
    {
        using var harness = CreateHarness();
        harness.Inbox.NextCaptureError = Error.Unexpected("inbox.store_failed", "The inbox database is locked.");

        var component = Render(harness);
        await AddAsync(component, Title, Notes);

        Assert.Empty(harness.Inbox.Items);
        var toasts = harness.Context.Services.GetRequiredService<ToastChannel>();
        component.WaitForAssertion(() =>
        {
            var toast = Assert.Single(toasts.Visible);
            Assert.Equal("inbox-add-error", toast.TestId);
            Assert.Equal(ToastSeverity.Error, toast.Severity);
        });
    }

    // --- Capture ----------------------------------------------------------

    [Fact]
    public async Task Capture_with_nothing_enabled_points_at_settings()
    {
        using var harness = CreateHarness();

        var component = Render(harness);
        await WaitForInboxAsync(component);

        await component.Find("[data-testid='inbox-pane-capture']").ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            var result = component.Find("[data-testid='inbox-pane-capture-result']");
            Assert.Equal("status", result.GetAttribute("role"));
            Assert.Contains("No capture sources are enabled", result.TextContent, StringComparison.Ordinal);
            Assert.Contains("Settings", result.TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Capture_with_youtube_enabled_reports_no_adapter_yet()
    {
        using var harness = CreateHarness();
        Assert.Null(harness.CaptureSources.SetEnabled(CaptureSourceKind.YouTube, true));

        var component = Render(harness);
        await WaitForInboxAsync(component);

        await component.Find("[data-testid='inbox-pane-capture']").ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            var result = component.Find("[data-testid='inbox-pane-capture-result']").TextContent;
            Assert.Contains("YouTube", result, StringComparison.Ordinal);
            Assert.Contains("no adapter is available yet", result, StringComparison.Ordinal);
            Assert.Contains("0 new items", result, StringComparison.Ordinal);
        });

        // The button comes back once the run is done, and a run that found
        // nothing put nothing in the Inbox.
        Assert.False(component.Find("[data-testid='inbox-pane-capture']").HasAttribute("disabled"));
        Assert.Empty(harness.Inbox.Items);
    }

    // --- Driving it -------------------------------------------------------

    /// <summary>Opens the Inbox the way a reader does — the header option — and
    /// waits for the pane to arrive. The pane may be empty, so what is waited
    /// for is the header's Add rather than a list.</summary>
    private static async Task<IRenderedComponent<InboxPane>> WaitForInboxAsync(IRenderedComponent<Home> component)
    {
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane-option']")));

        if (component.FindAll("[data-testid='inbox-pane-add']").Count == 0)
        {
            await component.Find("[data-testid='inbox-pane-option']").ClickAsync(new());
            component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane-add']")));
        }

        return component.FindComponent<InboxPane>();
    }

    /// <summary>Add, through the dialog, the way a reader does it.</summary>
    private static async Task AddAsync(IRenderedComponent<Home> component, string title, string? notes)
    {
        await WaitForInboxAsync(component);

        await component.Find("[data-testid='inbox-pane-add']").ClickAsync(new());
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane-add-dialog']")));

        await component.Find("[data-testid='inbox-pane-add-title'] input").InputAsync(new() { Value = title });
        if (notes is not null)
        {
            await component.Find("[data-testid='inbox-pane-add-notes'] textarea").InputAsync(new() { Value = notes });
        }

        await component.Find("[data-testid='inbox-pane-add-submit']").ClickAsync(new());

        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='inbox-pane-add-dialog']")));
    }

    private static TasksDesktopState State(Harness harness) =>
        harness.Context.Services.GetRequiredService<TasksDesktopState>();

    private static IRenderedComponent<Home> Render(Harness harness)
    {
        harness.Context.JSInterop.Mode = JSRuntimeMode.Loose;
        return harness.Context.Render<Home>();
    }

    /// <summary>
    /// The shell with the Inbox on, the Capture module composed, the Inbox's
    /// module stood in for by <see cref="FakeInboxItems"/>, and everything that
    /// would put extra chrome or a network call in the way off. The same host
    /// <see cref="HomeInboxWiringTests"/> builds.
    /// </summary>
    private static Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-inbox-add-tests", Guid.NewGuid().ToString("n"));
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        var shellNavigation = new ShellNavigationStore(Path.Combine(root, "shell", "shell-navigation.json"));
        var captureSources = new CaptureSourcesSettingsStore(Path.Combine(root, "capture", "capture-sources.json"));

        // Only the Inbox open on start: adding must not open the Tasks pane,
        // and the assertion is only worth something if it started closed.
        shellNavigation.SetLastPanes(["Inbox"], []);

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

        // The two halves of Capture the shell needs: the module for the run, and
        // the host's choice of where the sources are kept.
        context.Services.AddSingleton<ICaptureSourceSettings>(captureSources);
        context.Services.AddCaptureModule();

        TasksTestHost.AddToastChannel(context.Services);

        context.Services.AddScoped(sp => TasksTestHost.StateFor(
            sp.GetRequiredService<WorkspaceSettingsStore>(),
            sp.GetRequiredService<GitHubIntegration>(),
            TasksCopilotCli.Unavailable));
        var inbox = InboxTestHost.AddInboxState(context.Services);

        return new Harness(root, context, captureSources, TasksTestHost.EntriesFor(store), inbox);
    }

    private sealed record Harness(
        string Root,
        BunitContext Context,
        CaptureSourcesSettingsStore CaptureSources,
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
