using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the Storage tab tells the reader to do with the folder.
/// <para>
/// This screen is the only place the app gives storage advice, and the advice it
/// used to give - every entry is its own markdown file, so point this at a synced
/// folder - stopped being true when local ADR 0003 made the store one SQLite
/// database. Somebody followed it onto OneDrive, which cannot merge a binary
/// file, and lost committed status edits to six conflicted copies. That is R9 in
/// <c>.arc42/11-risks-and-technical-debt.md</c>, and its mitigation says the copy
/// must be corrected regardless of when sync ships.
/// </para>
/// <para>
/// Pinned in a test rather than left to review because the sentence was wrong for
/// three releases without anybody noticing, and because the absence of the old
/// advice is the half that actually protects the reader: new copy can be added
/// alongside a retired warning and read as complete.
/// </para>
/// </summary>
public sealed class SettingsStorageCopyTests
{
    /// <summary>The fact the reader needs first, because it is what makes the
    /// warning below follow: one file, not a folder of them.</summary>
    [Fact]
    public void The_copy_says_the_backlog_is_a_single_database_file()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        var copy = Description(settings.Component);

        Assert.Contains("one SQLite database", copy, StringComparison.Ordinal);
        Assert.Contains("single binary file", copy, StringComparison.Ordinal);
    }

    /// <summary>
    /// The warning names a product rather than only describing a category. "Do not
    /// use a file-sync folder" is not advice somebody recognises their own OneDrive
    /// folder in, and recognising it is the entire point.
    /// </summary>
    [Fact]
    public void The_copy_warns_that_a_file_sync_folder_loses_edits()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        var copy = Description(settings.Component);

        Assert.Contains("OneDrive", copy, StringComparison.Ordinal);
        Assert.Contains("local", copy, StringComparison.Ordinal);
        Assert.Contains("lose edits", copy, StringComparison.Ordinal);
    }

    /// <summary>
    /// The regression that matters. Asserted over the whole tab and not just the
    /// one paragraph, so the retired advice cannot come back somewhere else on the
    /// screen and still pass.
    /// </summary>
    [Fact]
    public void The_tab_no_longer_advises_pointing_the_folder_at_a_synced_one()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        // Tabs names its panels "tabpanel-{id}", and a panel renders its content
        // only while it is the active one - hence opening the tab above.
        var tab = settings.Component.Find("#tabpanel-storage").TextContent;

        Assert.DoesNotContain("markdown file with YAML", tab, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("travels with it", tab, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("synced or version-controlled", tab, StringComparison.OrdinalIgnoreCase);
    }

    private static string Description(IRenderedComponent<Settings> component) =>
        component.Find("[data-testid='storage-path-description']").TextContent;

    private static void OpenStorageTab(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "Storage").Click();

    private static SettingsRenderContext RenderSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-storage-copy-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<ITasksRefreshSettings>(
            new TasksRefreshSettingsStore(Path.Combine(root, "refresh", "refresh.json")));
        context.Services.AddSingleton(new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json")));
        context.Services.AddSingleton(new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json")));
        context.Services.AddSingleton(new GitHubIntegration(githubSettings, new NoGitHub(), new NoProbe()));
        context.Services.AddSingleton<FeedbackReporter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(githubSettings, store));

        return new SettingsRenderContext(root, context, context.Render<Settings>());
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
        IRenderedComponent<Settings> Component) : IDisposable
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
