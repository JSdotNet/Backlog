using Backlog.Infrastructure.AzureFoundry;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.Extensions;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Opening the app has to show the backlog that is already there.
/// <para>
/// The list is a parameterless child of the shell, so Blazor does not re-render
/// it just because the shell finished loading — the state has to say it changed.
/// Reading an empty store finishes before the first render and hides that, which
/// is why this test seeds an entry first: with anything on disk the read is
/// genuinely asynchronous, and a shell that stays quiet leaves a populated
/// backlog showing "Nothing here yet."
/// </para>
/// </summary>
public sealed class HomeInitialLoadTests
{
    [Fact]
    public async Task Entries_already_in_the_store_are_on_screen_at_first_render()
    {
        using var harness = await CreateHarnessAsync("# Seeded entry");
        harness.Context.JSInterop.Mode = JSRuntimeMode.Loose;

        var component = harness.Context.Render<Home>();

        // Nothing else touches the list, so this only ever completes because the
        // load announced itself. Without that there is no second render and this
        // waits until it gives up.
        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='empty-state']"));
            Assert.Contains("Seeded entry", component.Find("[data-testid='entry-list']").TextContent);
        });
    }

    /// <summary>
    /// Once the list is on screen, a Done entry's pull request that was never read
    /// is asked about in the background — but only while the GitHub integration is
    /// switched on. Off, the shell asks GitHub nothing at all.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_done_entrys_unread_pull_request_is_read_after_load_only_with_github_on(bool gitHubOn)
    {
        var client = new CountingGitHubClient();
        using var harness = await CreateHarnessAsync(
            "# Seeded entry\n`task` `!done` `repo:backlog`\n",
            client,
            gitHubOn,
            linkPullRequest: 708);
        harness.Context.JSInterop.Mode = JSRuntimeMode.Loose;

        var component = harness.Context.Render<Home>();

        component.WaitForAssertion(() =>
            Assert.Contains("Seeded entry", component.Find("[data-testid='entry-list']").TextContent));

        // The flag-off case is only meaningful if there was something to read.
        Assert.NotEmpty(Assert.Single(harness.Context.Services.GetRequiredService<TasksDesktopState>().Rows).PullRequestLinks);

        if (gitHubOn)
        {
            component.WaitForAssertion(() => Assert.True(client.StatusReads > 0));
        }
        else
        {
            Assert.Equal(0, client.StatusReads);
            Assert.Equal(0, client.StateReads);
        }
    }

    /// <summary>Switching the GitHub integration on after the list loaded checks
    /// then, rather than waiting for a reload that may not come.</summary>
    [Fact]
    public async Task Turning_github_on_later_reads_the_unread_pull_requests()
    {
        var client = new CountingGitHubClient();
        using var harness = await CreateHarnessAsync(
            "# Seeded entry\n`task` `!done` `repo:backlog`\n",
            client,
            gitHubOn: false,
            linkPullRequest: 708);
        harness.Context.JSInterop.Mode = JSRuntimeMode.Loose;

        var component = harness.Context.Render<Home>();
        component.WaitForAssertion(() =>
            Assert.Contains("Seeded entry", component.Find("[data-testid='entry-list']").TextContent));
        Assert.Equal(0, client.StatusReads);

        var features = Assert.IsType<AppFeatureSettingsStore>(harness.Context.Services.GetRequiredService<IAppFeatureSettings>());
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, true);

        component.WaitForAssertion(() => Assert.True(client.StatusReads > 0));
    }

    private static async Task<Harness> CreateHarnessAsync(
        string entryText,
        IGitHubClient? gitHubClient = null,
        bool gitHubOn = false,
        int? linkPullRequest = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-home-initial-load", Guid.NewGuid().ToString("n"));
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));

        // Written through the real use case, so the store is left exactly as the
        // app leaves it — index and all.
        var saved = await TasksTestHost.EntriesFor(store).SaveFromTextAsync(null, entryText, 0);
        Assert.True(saved.IsSuccess);

        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));

        if (linkPullRequest is { } number)
        {
            var (repositories, _) = GitHubSettings.ParseText("JSdotNet/Backlog");
            gitHubSettings.SetRepositories(repositories);

            var linked = await TasksTestHost.EntriesFor(store).LinkToIssueAsync(
                saved.Value!.Entry.Id,
                "JSdotNet/Backlog",
                number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                EntryProjectionDto.PullRequestTargetType,
                TestContext.Current.CancellationToken);
            Assert.True(linked.IsSuccess);
        }

        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));

        foreach (var feature in new[]
                 {
                     AppFeatures.InboxPane,
                     DevbookFeatures.DevbookSections,
                     DevbookFeatures.RepositoryDevbook,
                     DevPcFeatures.SystemTools,
                     AppFeatures.AiAssistant,
                     AppFeatures.FeedbackReporting,
                     TasksFeatures.GitHubIntegration
                 })
        {
            _ = featureSettings.SetEnabled(feature, false);
        }

        if (gitHubOn) _ = featureSettings.SetEnabled(TasksFeatures.GitHubIntegration, true);

        var gitHub = new GitHubIntegration(gitHubSettings, gitHubClient ?? new StubGitHubClient(), new StubProbe());
        var devbookFolderSource = new DevbookFolderSource(gitHubSettings, store);

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(featureSettings);
        context.Services.AddSingleton(new ShellNavigationStore(Path.Combine(root, "shell", "shell-navigation.json")));
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton(new FeedbackReporter(gitHub));
        context.Services.AddSingleton<IAzureFoundryChatClient, StubAzureFoundryChatClient>();
        context.Services.AddSingleton<IDevToolService, UnsupportedDevToolService>();
        context.Services.AddSingleton<IAppUpdateService, UnsupportedAppUpdateService>();
        context.Services.AddSingleton<IDevbookFolderSource>(devbookFolderSource);
        // The Roadmap module the way a host wires it: a real plan document under the
        // same storage root, so the band draws what was stored rather than a fixture.
        context.Services.AddSingleton<IRoadmapPlanning>(sp =>
            TasksTestHost.PlanningFor(sp.GetRequiredService<WorkspaceSettingsStore>()));
        // The band gathers an item's linked and tagged work through this port before it
        // opens the editor, so a host that composes the band composes the rollup with it.
        context.Services.AddSingleton<IRoadmapItemRollup>(sp =>
            new Backlog.Infrastructure.FileSystem.Roadmap.RoadmapItemRollupService(
                TasksTestHost.EntriesFor(sp.GetRequiredService<WorkspaceSettingsStore>()),
                () => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
        context.Services.AddSingleton<IImportedPlanSource>(sp =>
            TasksTestHost.ImportedPlansFor(sp.GetRequiredService<WorkspaceSettingsStore>()));
        context.Services.AddSingleton<IRoadmapWorkChanges>(TasksTestHost.WorkChanges());
        context.Services.AddSingleton(TasksTestHost.UntouchedPace());
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
        context.Services.AddScoped(sp => new DomainDevbookStore(sp.GetRequiredService<IDevbookFolderSource>()));
        // Over a registry that knows the configured repository, so the start-up
        // reconcile leaves a recorded pull request's owner/name as it was written.
        context.Services.AddScoped(sp => new TasksDesktopState(
            TasksTestHost.TaskStoreFor(sp.GetRequiredService<WorkspaceSettingsStore>()),
            TasksTestHost.EntriesFor(TasksTestHost.RepositoryFor(sp.GetRequiredService<WorkspaceSettingsStore>()), new ConfiguredRepositoryDirectory()),
            sp.GetRequiredService<GitHubIntegration>(),
            TasksCopilotCli.Unavailable,
            toasts: sp.GetRequiredService<IToastChannel>()));
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
        // The Inbox pane the shell now composes: its state over the in-memory
        // module, the same terms as the Tasks state above.
        _ = InboxTestHost.AddInboxState(context.Services);

        return new Harness(root, context);
    }

    /// <summary>Counts the pull request reads and answers each like a GitHub that
    /// cannot find it — the count is the whole question here.</summary>
    private sealed class CountingGitHubClient : IGitHubClient
    {
        private int _statusReads;
        private int _stateReads;

        public int StatusReads => Volatile.Read(ref _statusReads);
        public int StateReads => Volatile.Read(ref _stateReads);

        public Task<GitHubIssue> CreateIssueAsync(GitHubRepositoryRef repository, string title, string? body, IEnumerable<string>? labels = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubPullRequestStatus> GetPullRequestStatusAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _statusReads);
            return Task.FromException<GitHubPullRequestStatus>(new GitHubException("Not Found"));
        }

        public Task<GitHubPullRequest> GetPullRequestAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _stateReads);
            return Task.FromException<GitHubPullRequest>(new GitHubException("Not Found"));
        }

        public Task<GitHubUploadedFile> UploadFileAsync(GitHubRepositoryRef repository, string path, string branch, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>The one repository the harness configures, under its own
    /// owner/name; everything else is unknown and forgotten once registered.</summary>
    private sealed class ConfiguredRepositoryDirectory : IRepositoryDirectory
    {
        private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

        public IReadOnlyList<TasksRepositoryRef> Repositories => [Backlog];

        public TasksRepositoryRef? Resolve(string name) =>
            string.Equals(name, Backlog.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, Backlog.Alias, StringComparison.OrdinalIgnoreCase)
                ? Backlog
                : null;

        public Result<TasksRepositoryRef> Register(string name) => new TasksRepositoryRef(name, name, name);
    }

    private sealed record Harness(string Root, BunitContext Context) : IDisposable
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
}
