using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.Services;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What triaging an inbox item opens.
/// <para>
/// The entry, in the detail pane, on its own fields — and <em>not</em> the
/// raw-markdown escape hatch. <c>.design/content-editing.md#raw-markdown-escape-hatch</c>
/// puts the canonical markdown behind Ctrl+Shift+M precisely so that it is not
/// "the primary surface <c>#editing-model</c> rules out". Triage used to open an
/// editor on the row without ever selecting it, and the hatch is the selected row
/// being the row with an editor open — so the pane never appeared at all and the
/// only observable effect was the filter pinning a row nobody could see.
/// </para>
/// <para>
/// Driven through the rendered shell rather than through the state, because the
/// Inbox raising <c>OnOpen</c> and the shell answering it is what is under test:
/// the Inbox knows nothing about a backlog row, and mapping the item back to one
/// is the shell's job. <see cref="NewEntryOpensOnItsTitleTests"/> is the same
/// three facts on the other path that had this bug.
/// </para>
/// </summary>
public sealed class InboxTriageOpensTheEntryTests
{
    private const string Draft = "# Triage me\n`task` `!draft`\n";
    private const string Settled = "# Deploy SpecManager\n`task` `!ready`\n";

    // --- What triage opens ------------------------------------------------

    /// <summary>The row the Inbox handed back is the row the pane is open on.
    /// Resolved the way the shell resolves it — <see cref="TasksDrafts.Find"/> over
    /// the item the pane actually rendered — so the test names the entry the same
    /// way the handler does.</summary>
    [Fact]
    public async Task Triaging_an_item_selects_the_entry_it_came_from()
    {
        using var harness = CreateHarness();
        await SeedAsync(harness, Draft);

        var component = Render(harness);
        var item = await TriageTheOnlyItemAsync(component);
        var state = State(harness);

        var row = TasksDrafts.Find(state.Rows, item);

        Assert.NotNull(row);
        Assert.Same(row, state.SelectedRow);
    }

    /// <summary>And the pane is on screen for it. This is the half the bug lost
    /// entirely: an editing row that is not the selected row renders nothing, so
    /// triage appeared to do nothing at all.</summary>
    [Fact]
    public async Task Triaging_an_item_puts_the_entry_in_the_detail_pane()
    {
        using var harness = CreateHarness();
        await SeedAsync(harness, Draft);

        var component = Render(harness);
        await TriageTheOnlyItemAsync(component);
        var state = State(harness);

        var detail = Assert.Single(component.FindAll("[data-testid='entry-detail']"));
        Assert.Contains("Triage me", detail.TextContent, StringComparison.Ordinal);

        Assert.NotNull(state.SelectedRow);
        Assert.Contains(state.SelectedRow, state.FilteredRows);
    }

    // --- The hatch is not there -------------------------------------------

    /// <summary>The bug, stated as the two facts it is: the hatch is closed, and
    /// nothing of it is on screen. Not rendered rather than rendered-and-hidden —
    /// a textarea nobody can see is still a textarea the caret can land in.</summary>
    [Fact]
    public async Task Triaging_an_item_does_not_open_the_raw_markdown_hatch()
    {
        using var harness = CreateHarness();
        await SeedAsync(harness, Draft);

        var component = Render(harness);
        await TriageTheOnlyItemAsync(component);
        var state = State(harness);

        Assert.False(state.RawHatchOpen);
        Assert.Null(state.EditingRow);
        Assert.Empty(component.FindAll("[data-testid='entry-raw-input']"));

        // And with it the "reads as" hint, which is the hatch's own line and not
        // the pane's.
        Assert.Empty(component.FindAll("[data-testid='entry-meta-reading']"));
    }

    // --- And is still one keystroke away ----------------------------------

    /// <summary>Not rendered is not unreachable. The shortcut still reaches the
    /// source on a triaged entry, and Escape gives the fields back without closing
    /// the pane on it.</summary>
    [Fact]
    public async Task Ctrl_shift_m_still_opens_the_hatch_on_a_triaged_entry()
    {
        using var harness = CreateHarness();
        await SeedAsync(harness, Draft);

        var component = Render(harness);
        await TriageTheOnlyItemAsync(component);
        var state = State(harness);
        var row = state.SelectedRow;

        await component.Find("[data-testid='entry-detail']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "M", CtrlKey = true, ShiftKey = true });

        Assert.True(state.RawHatchOpen);
        Assert.Single(component.FindAll("[data-testid='entry-raw-input']"));

        await component.Find("[data-testid='entry-detail']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.False(state.RawHatchOpen);
        Assert.Empty(component.FindAll("[data-testid='entry-raw-input']"));

        // Escape with the hatch open is about the hatch, so the entry it was a
        // view of is still open and still in the list.
        Assert.Same(row, state.SelectedRow);
        Assert.Contains(row, state.Rows);
    }

    // --- The Inbox the reader was working through -------------------------

    /// <summary>Triage opens the backlog beside the Inbox rather than instead of
    /// it: this is the shell opening a pane on the reader's behalf, and closing the
    /// list they picked the item from would take away the queue they were working
    /// through. The arrangement is persisted, the same as any other pane
    /// change.</summary>
    [Fact]
    public async Task Triaging_an_item_opens_the_backlog_beside_the_inbox()
    {
        using var harness = CreateHarness();
        await SeedAsync(harness, Draft);

        var component = Render(harness);
        await TriageTheOnlyItemAsync(component);

        Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane']"));
        Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));

        Assert.Contains("Inbox", harness.ShellNavigation.LastEnabledPanes);
        Assert.Contains("Tasks", harness.ShellNavigation.LastEnabledPanes);
    }

    // --- An item whose row has gone ---------------------------------------

    /// <summary>An item the backlog can no longer place is nothing to open. The
    /// Inbox holds the items it was handed, so a row that went underneath one
    /// leaves a key resolving to nothing — and the shell's guard is what keeps that
    /// a no-op rather than a throw. Raised through the pane's own callback, because
    /// the button for an item that has gone is by definition no longer
    /// rendered.</summary>
    [Fact]
    public async Task Triaging_an_item_whose_row_has_gone_does_nothing()
    {
        using var harness = CreateHarness();
        await SeedAsync(harness, Draft);

        var component = Render(harness);
        var inbox = await WaitForInboxAsync(component);
        var state = State(harness);

        await component.InvokeAsync(() =>
            inbox.Instance.OnOpen.InvokeAsync(new InboxItem("not-a-row", "Gone", null)));

        Assert.Null(state.SelectedRow);
        Assert.Null(state.EditingRow);
        Assert.Empty(component.FindAll("[data-testid='entry-detail']"));
    }

    // --- What triage does to the entry it leaves --------------------------

    /// <summary>Triage does not leave a live caret pointed at an entry that is no
    /// longer on screen. Selecting flushes the outgoing editor, so an entry
    /// somebody was writing in is saved on the way into triage rather than 750ms
    /// after they moved on.</summary>
    [Fact]
    public async Task Triaging_an_item_saves_the_entry_that_was_being_edited()
    {
        using var harness = CreateHarness();
        await SeedAsync(harness, Draft);
        await SeedAsync(harness, Settled);

        var component = Render(harness);
        var state = State(harness);

        var settled = state.Rows.Single(row => row.PreviewTitle == "Deploy SpecManager");
        await component.InvokeAsync(async () =>
        {
            await state.SelectAsync(settled);
            await state.ToggleRawHatchAsync();
            state.OnRawTextInput(settled, "# Deploy SpecManager twice\n`task` `!ready`\n");
        });

        await TriageTheOnlyItemAsync(component);

        Assert.False(state.RawHatchOpen);
        Assert.Null(state.EditingRow);

        // On disk rather than only on the row: the flush is the point, and a row
        // still holding the text would prove nothing about the save.
        var stored = await harness.Entries.ListAsync();
        Assert.Contains(stored, entry => entry.Title == "Deploy SpecManager twice");
    }

    /// <summary>A draft nobody wrote in goes when the reader triages something
    /// else, rather than leaving an "Untitled" husk in the list. An unsaved draft
    /// exists nowhere but that list, so leaving it is the moment that decides.</summary>
    [Fact]
    public async Task Triaging_an_item_drops_a_draft_nobody_wrote_in()
    {
        using var harness = CreateHarness();
        await SeedAsync(harness, Draft);

        var component = Render(harness);
        var state = State(harness);

        await component.InvokeAsync(state.NewRow);
        var untouched = state.Rows[^1];

        await TriageTheOnlyItemAsync(component);

        Assert.DoesNotContain(untouched, state.Rows);
        Assert.NotNull(state.SelectedRow);
        Assert.NotSame(untouched, state.SelectedRow);
    }

    // --- Driving it -------------------------------------------------------

    /// <summary>Opens the Inbox the way a reader does — the header option — and
    /// waits for the pane to arrive with its list in it. Idempotent, because
    /// triage opens a second pane beside one that is already there.</summary>
    private static async Task<IRenderedComponent<InboxPane>> WaitForInboxAsync(IRenderedComponent<Home> component)
    {
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane-option']")));

        if (component.FindAll("[data-testid='inbox-pane-list']").Count == 0)
        {
            await component.Find("[data-testid='inbox-pane-option']").ClickAsync(new());
            component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane-list']")));
        }

        return component.FindComponent<InboxPane>();
    }

    /// <summary>Presses "Open in backlog" on the one item in the queue and hands
    /// back the item that was pressed. Through the button rather than through the
    /// callback, because half of what is under test happens in the render the click
    /// causes. The control carries no test id of its own — the Inbox draws one per
    /// item, so the item's own control is the one inside its row.</summary>
    private static async Task<InboxItem> TriageTheOnlyItemAsync(IRenderedComponent<Home> component)
    {
        var inbox = await WaitForInboxAsync(component);
        var item = Assert.Single(inbox.Instance.Items);

        await component.Find("[data-testid='inbox-pane-list'] .inbox-pane__item button").ClickAsync(new());
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']")));

        return item;
    }

    private static TasksDesktopState State(Harness harness) =>
        harness.Context.Services.GetRequiredService<TasksDesktopState>();

    /// <summary>Writes an entry into the workspace the shell reads on start,
    /// through the module's own port. Before the render, because the state loads
    /// its rows once, when Home initializes.</summary>
    private static async Task SeedAsync(Harness harness, string text)
    {
        var order = (await harness.Entries.ListAsync()).Count;
        var saved = await harness.Entries.SaveFromTextAsync(null, text, order);

        Assert.True(saved.IsSuccess);
    }

    private static IRenderedComponent<Home> Render(Harness harness)
    {
        harness.Context.JSInterop.Mode = JSRuntimeMode.Loose;
        return harness.Context.Render<Home>();
    }

    /// <summary>
    /// The shell with the Inbox on, and everything that would put extra chrome or a
    /// network call in the way off.
    /// <para>
    /// Inbox is the feature catalog's <c>Dev</c> feature and ships off, so a test
    /// about triage has to turn it on before the path is reachable at all.
    /// </para>
    /// </summary>
    private static Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-inbox-triage-tests", Guid.NewGuid().ToString("n"));
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        var shellNavigation = new ShellNavigationStore(Path.Combine(root, "shell", "shell-navigation.json"));

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
        context.Services.AddSingleton(new KnowledgeCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        // The dashboard takeover, with no provider behind it — see DashboardTestHost.
        _ = context.Services.AddUnavailableDashboard("backlog", "backlog-ide");
        context.Services.AddScoped(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));

        // Home publishes its transient results on this, and injects it hard rather
        // than resolving it: a screen that silently lost the reader's only feedback
        // is worse than one that refuses to construct. So a host that renders Home
        // has to register it, the same as the other four.
        TasksTestHost.AddToastChannel(context.Services);

        context.Services.AddScoped(sp => TasksTestHost.StateFor(
            sp.GetRequiredService<WorkspaceSettingsStore>(),
            sp.GetRequiredService<GitHubIntegration>(),
            TasksCopilotCli.Unavailable));

        return new Harness(root, context, shellNavigation, TasksTestHost.EntriesFor(store));
    }

    private sealed record Harness(
        string Root,
        BunitContext Context,
        ShellNavigationStore ShellNavigation,
        ITaskItems Entries) : IDisposable
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
