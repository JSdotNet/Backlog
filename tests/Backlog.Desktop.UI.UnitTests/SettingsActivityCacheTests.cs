using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The way back when a repository's saved pull-request detail is wrong.
/// <para>
/// The detail is kept because a merged pull request cannot change, which is true
/// right up until somebody rewrites the repository's history. There has to be a
/// control that says so and undoes it, and it has to be per repository — a
/// rewritten history is one repository's problem, and re-reading the others would
/// be a long fetch for nothing.
/// </para>
/// </summary>
public sealed class SettingsActivityCacheTests
{
    [Fact]
    public void EachRepositoryOffersToForgetItsSavedPullRequests()
    {
        using var settings = RenderSettings();
        OpenRepositoriesTab(settings.Component);

        var section = settings.Component.Find("[data-testid='repo-activity-cache']");

        var status = settings.Component.Find("[data-testid='repo-activity-cache-status']").TextContent;

        // Said plainly, and the cost said with it: the next dashboard read is a
        // slow one. Named per repository, because forgetting one leaves the others
        // alone and a sentence that did not say which is being forgotten would read
        // as though it emptied everything.
        Assert.Contains("fetches every pull request's detail in", status, StringComparison.Ordinal);
        Assert.Contains("again", status, StringComparison.Ordinal);

        // The resting sentence states the rule, never the state of the cache folder:
        // nothing on this screen reads that folder, so a claim that saved detail "is
        // reused" would be false on a first run and unchecked on every other.
        Assert.Contains("never changes", status, StringComparison.Ordinal);

        Assert.NotNull(section.QuerySelector("[data-testid='repo-activity-cache-forget']"));
    }

    [Fact]
    public void ForgettingDropsOnlyTheRepositoryWhoseCardItIsOn()
    {
        using var settings = RenderSettings();
        OpenRepositoriesTab(settings.Component);

        settings.Component.Find("[data-testid='repo-activity-cache-forget']").Click();

        var forgotten = Assert.Single(settings.Cache.Forgotten);
        Assert.Equal("JSdotNet/Backlog", forgotten);
    }

    /// <summary>One gesture, both caches. A listing kept after its detail was
    /// forgotten would save only the cheaper half of the next fetch, and a person
    /// who pressed "forget" would not know there was a second thing to forget.</summary>
    [Fact]
    public void ForgettingDropsTheListingWithTheDetail()
    {
        using var settings = RenderSettings();
        OpenRepositoriesTab(settings.Component);

        settings.Component.Find("[data-testid='repo-activity-cache-forget']").Click();

        Assert.Equal(["JSdotNet/Backlog"], settings.Listings.Forgotten);
    }

    [Fact]
    public void TheOtherRepositoryIsForgottenOnlyWhenItsOwnCardAsks()
    {
        using var settings = RenderSettings();
        OpenRepositoriesTab(settings.Component);

        // Move to the second repository's subpage and use the control there.
        settings.Component
            .FindAll("[data-testid='repo-subpage-tab']")
            .Single(tab => tab.TextContent.Contains("spec-manager", StringComparison.Ordinal))
            .Click();

        settings.Component.Find("[data-testid='repo-activity-cache-forget']").Click();

        Assert.Equal(["innovadis-dev/spec-manager"], settings.Cache.Forgotten);
    }

    [Fact]
    public void TheSectionSaysSoOnceItHasBeenForgotten()
    {
        using var settings = RenderSettings();
        OpenRepositoriesTab(settings.Component);

        settings.Component.Find("[data-testid='repo-activity-cache-forget']").Click();

        settings.Component.WaitForAssertion(() => Assert.Contains(
            "Forgotten",
            settings.Component.Find("[data-testid='repo-activity-cache-status']").TextContent,
            StringComparison.Ordinal));
    }

    private static void OpenRepositoriesTab(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "Repositories").Click();

    private static SettingsRenderContext RenderSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-activity-cache-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog\ninnovadis-dev/spec-manager");
        Assert.Empty(errors);
        Assert.Null(githubSettings.SetRepositories(repositories));

        var cache = new RecordingDetailCache();

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IWorkingHoursSettings>(
            new WorkingHoursSettingsStore(Path.Combine(root, "working-hours", "working-hours.json")));
        context.Services.AddSingleton<ICaptureSourceSettings>(
            new CaptureSourcesSettingsStore(Path.Combine(root, "capture", "capture-sources.json")));
        context.Services.AddSingleton(new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json")));
        context.Services.AddSingleton(new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json")));
        context.Services.AddSingleton(new GitHubIntegration(githubSettings, new NoGitHub(), new NoProbe()));
        context.Services.AddSingleton<FeedbackReporter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(githubSettings, store));
        context.Services.AddSingleton(new DevbookSourceSelection(githubSettings, new StubBranchCatalog()));
        context.Services.AddSingleton<IPullRequestDetailCache>(cache);
        var listings = new RecordingListingCache();
        context.Services.AddSingleton<IActivityListingCache>(listings);

        return new SettingsRenderContext(root, context, context.Render<Settings>(), cache, listings);
    }

    /// <summary>The listing port beside the detail one, recording the same thing.</summary>
    private sealed class RecordingListingCache : IActivityListingCache
    {
        public List<string> Forgotten { get; } = [];

        public ActivityListing? TryRead(GitHubRepositoryRef repository, string author) => null;

        public void Write(GitHubRepositoryRef repository, string author, ActivityListing listing)
        {
        }

        public void ForgetRepository(GitHubRepositoryRef repository) => Forgotten.Add(repository.FullName);
    }

    /// <summary>The port, recording what it was asked to drop. What is being pinned
    /// is which repository the screen names — whether a file actually disappears is
    /// the file-system project's test.</summary>
    private sealed class RecordingDetailCache : IPullRequestDetailCache
    {
        public List<string> Forgotten { get; } = [];

        public PullRequestDetail? TryRead(GitHubRepositoryRef repository, int number) => null;

        public void Write(GitHubRepositoryRef repository, int number, PullRequestDetail detail)
        {
        }

        public void ForgetRepository(GitHubRepositoryRef repository) => Forgotten.Add(repository.FullName);
    }

    /// <summary>Nothing here reaches GitHub — forgetting a local copy is a local
    /// act.</summary>
    private sealed class NoGitHub : IGitHubClient
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

    private sealed class NoProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not connected."));

        public void Invalidate()
        {
        }
    }

    private sealed record SettingsRenderContext(
        string Root,
        BunitContext TestContext,
        IRenderedComponent<Settings> Component,
        RecordingDetailCache Cache,
        RecordingListingCache Listings) : IDisposable
    {
        public void Dispose()
        {
            TestContext.Dispose();

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
