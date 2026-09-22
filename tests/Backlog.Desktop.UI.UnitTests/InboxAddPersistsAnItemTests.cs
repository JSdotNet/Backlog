using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.Extensions;
using Backlog.Modules.Capture.Ports;
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

    /// <summary>The sources are configured on the pane itself now, so the
    /// line points at the panel under it rather than at a Settings tab.</summary>
    [Fact]
    public async Task Capture_with_nothing_enabled_points_at_the_sources_panel()
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
            Assert.Contains("Sources", result.TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("Settings", result.TextContent, StringComparison.Ordinal);
        });

        // And the panel it points at is on the pane, folded, with its trigger
        // saying nothing is on.
        Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane-sources'] [data-testid='capture-sources-toggle']"));
        Assert.Equal("false", component.Find("[data-testid='capture-sources-toggle']").GetAttribute("aria-expanded"));
        Assert.Equal("none on", component.Find("[data-testid='capture-sources-summary']").TextContent.Trim());
    }

    /// <summary>The feature seen from the pane: a source switched on in the
    /// panel is what the next press runs, and the run's line comes back onto
    /// that source's row as its last capture.</summary>
    [Fact]
    public async Task A_source_switched_on_in_the_panel_is_run_and_its_row_shows_the_last_capture()
    {
        using var harness = CreateHarness();
        harness.Adapter.Entries.Add(new CapturedEntry("yt:video:one", "One", null, null, null));

        var component = Render(harness);
        await WaitForInboxAsync(component);

        await component.Find("[data-testid='capture-sources-toggle']").ClickAsync(new());
        component.WaitForAssertion(() =>
            Assert.Equal("true", component.Find("[data-testid='capture-sources-toggle']").GetAttribute("aria-expanded")));

        component.Find("[data-testid='capture-source-youtube-enabled'] input").Change(true);
        Assert.True(harness.CaptureSources.Current.For(CaptureSourceKind.YouTube).Enabled);
        component.WaitForAssertion(() =>
            Assert.Equal("1 of 3 on", component.Find("[data-testid='capture-sources-summary']").TextContent.Trim()));
        Assert.Equal("Never captured.", component.Find("[data-testid='capture-source-youtube-last-run']").TextContent.Trim());

        await component.Find("[data-testid='inbox-pane-capture']").ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            Assert.Equal("YouTube: 1 new item.", component.Find("[data-testid='inbox-pane-capture-result']").TextContent.Trim());
            var lastRun = component.Find("[data-testid='capture-source-youtube-last-run']").TextContent;
            Assert.StartsWith("Last capture", lastRun.Trim(), StringComparison.Ordinal);
            Assert.Contains("1 new item", lastRun, StringComparison.Ordinal);
        });

        // The log opens on the same run.
        await component.Find("[data-testid='capture-source-youtube-log-toggle']").ClickAsync(new());
        component.WaitForAssertion(() =>
        {
            var log = component.Find("[data-testid='capture-source-youtube-log']");
            Assert.Contains("YouTube: 1 new item.", log.TextContent, StringComparison.Ordinal);
        });
        Assert.Equal("Never captured.", component.Find("[data-testid='capture-source-website-last-run']").TextContent.Trim());
    }

    /// <summary>Email has no adapter in the product yet, and this host
    /// registers none for it either: the run says so on that source's line
    /// rather than failing.</summary>
    [Fact]
    public async Task Capture_with_a_source_that_has_no_adapter_says_so()
    {
        using var harness = CreateHarness();
        Assert.Null(harness.CaptureSources.SetEnabled(CaptureSourceKind.Email, true));

        var component = Render(harness);
        await WaitForInboxAsync(component);

        await component.Find("[data-testid='inbox-pane-capture']").ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            var result = component.Find("[data-testid='inbox-pane-capture-result']").TextContent;
            Assert.Equal("Email: no adapter is available yet.", result.Trim());
        });

        // The button comes back once the run is done, and a run that found
        // nothing put nothing in the Inbox.
        Assert.False(component.Find("[data-testid='inbox-pane-capture']").HasAttribute("disabled"));
        Assert.Empty(harness.Inbox.Items);
    }

    /// <summary>The feature: what is new at an enabled source becomes Inbox
    /// rows, on screen without a restart, and pressing the button again over
    /// the same source adds nothing — the entry arrives under the same id.</summary>
    [Fact]
    public async Task Capture_puts_what_the_source_found_in_the_inbox_and_a_second_run_adds_nothing()
    {
        using var harness = CreateHarness();
        Assert.Null(harness.CaptureSources.SetEnabled(CaptureSourceKind.YouTube, true));
        Assert.Null(harness.CaptureSources.SetTargets(CaptureSourceKind.YouTube, ["@dotnet"]));
        harness.Adapter.Entries.Add(new CapturedEntry(
            "yt:video:abc123DEF45",
            "What is new in Aspire 13",
            "https://www.youtube.com/watch?v=abc123DEF45",
            BodyMd: null,
            PublishedAt: new DateTimeOffset(2026, 9, 10, 15, 0, 0, TimeSpan.Zero)));

        var component = Render(harness);
        await WaitForInboxAsync(component);

        await component.Find("[data-testid='inbox-pane-capture']").ClickAsync(new());

        component.WaitForAssertion(() =>
        {
            Assert.Equal("YouTube: 1 new item.", component.Find("[data-testid='inbox-pane-capture-result']").TextContent.Trim());
            Assert.Contains("What is new in Aspire 13", component.Find("[data-testid='inbox-pane-list']").TextContent, StringComparison.Ordinal);
        });

        var item = Assert.Single(harness.Inbox.Items);
        Assert.Equal("youtube", item.Channel);
        Assert.Equal("https://www.youtube.com/watch?v=abc123DEF45", item.SourceUrl);
        Assert.Equal("YouTube", component.Find("[data-testid='inbox-pane-item-source']").TextContent.Trim());

        await component.Find("[data-testid='inbox-pane-capture']").ClickAsync(new());

        component.WaitForAssertion(() =>
            Assert.Equal("YouTube: 0 new items.", component.Find("[data-testid='inbox-pane-capture-result']").TextContent.Trim()));
        Assert.Single(harness.Inbox.Items);
        Assert.Equal(2, harness.Adapter.Runs);
    }

    /// <summary>Two sources on: each says its own line, and the total follows
    /// so the reader is not left adding up.</summary>
    [Fact]
    public async Task Capture_over_two_sources_reports_each_and_the_total()
    {
        using var harness = CreateHarness();
        Assert.Null(harness.CaptureSources.SetEnabled(CaptureSourceKind.YouTube, true));
        Assert.Null(harness.CaptureSources.SetEnabled(CaptureSourceKind.Email, true));
        harness.Adapter.Entries.Add(new CapturedEntry("yt:video:one", "One", null, null, null));
        harness.Adapter.Entries.Add(new CapturedEntry("yt:video:two", "Two", null, null, null));

        var component = Render(harness);
        await WaitForInboxAsync(component);

        await component.Find("[data-testid='inbox-pane-capture']").ClickAsync(new());

        component.WaitForAssertion(() =>
            Assert.Equal(
                "YouTube: 2 new items. Email: no adapter is available yet. 2 new items in all.",
                component.Find("[data-testid='inbox-pane-capture-result']").TextContent.Trim()));
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
        shellNavigation.SetLastPanes(["Inbox"]);

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

        // The halves of Capture the shell needs: the module for the run, the
        // host's choice of where the sources and the run log are kept, one
        // adapter a test can arm, and the delivery into the fake Inbox below.
        var adapter = new FakeCaptureSourceAdapter(CaptureSourceKind.YouTube);
        context.Services.AddSingleton<ICaptureSourceSettings>(captureSources);
        context.Services.AddSingleton<ICaptureRunLog>(
            new CaptureRunLogStore(Path.Combine(root, "capture", "capture-runs.json")));
        context.Services.AddSingleton<ICaptureSourceAdapter>(adapter);
        context.Services.AddCaptureModule();
        InboxTestHost.AddCaptureDelivery(context.Services);

        TasksTestHost.AddToastChannel(context.Services);

        context.Services.AddScoped(sp => TasksTestHost.StateFor(
            sp.GetRequiredService<WorkspaceSettingsStore>(),
            sp.GetRequiredService<GitHubIntegration>(),
            TasksCopilotCli.Unavailable));
        var inbox = InboxTestHost.AddInboxState(context.Services);

        return new Harness(root, context, captureSources, adapter, TasksTestHost.EntriesFor(store), inbox);
    }

    private sealed record Harness(
        string Root,
        BunitContext Context,
        CaptureSourcesSettingsStore CaptureSources,
        FakeCaptureSourceAdapter Adapter,
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
