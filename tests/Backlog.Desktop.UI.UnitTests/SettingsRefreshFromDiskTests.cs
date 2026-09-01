using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Backlog.Abstractions.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Storage tab's two controls for picking up another machine's writes.
/// <para>
/// Asserted against the store rather than against what the controls are showing,
/// because the store is what the list reads: a toggle that lit up and wrote
/// nothing would leave the poll exactly as it was. Committing on change with no
/// save button is the house rule the rest of this screen follows.
/// </para>
/// </summary>
public sealed class SettingsRefreshFromDiskTests
{
    [Fact]
    public void Turning_the_check_off_is_stored_straight_away()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        settings.Component.Find("[data-testid='storage-refresh-enabled'] input").Change(false);

        Assert.False(settings.Refresh.Current.PollingEnabled);
    }

    [Fact]
    public void A_committed_interval_is_stored_straight_away()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        var interval = settings.Component.Find("[data-testid='storage-refresh-interval-input']");
        interval.Input("15");
        interval.Change();

        Assert.Equal(15, settings.Refresh.Current.PollingIntervalSeconds);
        Assert.Contains("every 15 seconds", settings.Component.Find("[data-testid='storage-refresh-status']").TextContent);
    }

    /// <summary>A number the store will not run at is reported beside the field
    /// and the field is put back to what is actually in force — the same shape as
    /// a folder path that could not be used.</summary>
    [Fact]
    public void An_interval_the_store_refuses_says_so_and_changes_nothing()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        var interval = settings.Component.Find("[data-testid='storage-refresh-interval-input']");
        interval.Input("0");
        interval.Change();

        Assert.Equal(
            BacklogRefreshSettings.DefaultPollingIntervalSeconds,
            settings.Refresh.Current.PollingIntervalSeconds);

        var status = settings.Component.Find("[data-testid='storage-refresh-status']");
        Assert.Contains("setting__status--error", status.InnerHtml);
    }

    [Fact]
    public void Something_that_is_not_a_number_is_refused_rather_than_read_as_zero()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        var interval = settings.Component.Find("[data-testid='storage-refresh-interval-input']");
        interval.Input("soon");
        interval.Change();

        Assert.Equal(
            BacklogRefreshSettings.DefaultPollingIntervalSeconds,
            settings.Refresh.Current.PollingIntervalSeconds);
        Assert.Contains(
            "setting__status--error",
            settings.Component.Find("[data-testid='storage-refresh-status']").InnerHtml);
    }

    private static void OpenStorageTab(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "Storage").Click();

    private static SettingsRenderContext RenderSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-refresh-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(BacklogFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);

        var refresh = new BacklogRefreshSettingsStore(Path.Combine(root, "refresh", "refresh.json"));
        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IBacklogRefreshSettings>(refresh);
        context.Services.AddSingleton(new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json")));
        context.Services.AddSingleton(new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json")));
        context.Services.AddSingleton(new GitHubIntegration(githubSettings, new NoGitHub(), new NoProbe()));
        context.Services.AddSingleton<FeedbackReporter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(githubSettings, store));

        return new SettingsRenderContext(root, context, context.Render<Settings>(), refresh);
    }

    private sealed class NoGitHub : IGitHubClient
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
        BacklogRefreshSettingsStore Refresh) : IDisposable
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
