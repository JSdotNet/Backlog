using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the Storage tab tells the reader the folder is, and where to keep it.
/// <para>
/// Copy is normally nobody's test, and this is the exception: the paragraph used
/// to invite the reader to point the workspace at a synced folder, and doing that
/// is what produced the conflicted database copies and the silently reverted
/// status edits recorded as R9 in
/// <c>.arc42/11-risks-and-technical-debt.md</c>. Since local ADR 0003 the folder
/// holds one binary SQLite file, which no file-sync product can merge — so the
/// screen was not merely describing the pre-0003 world, it was the instruction
/// that lost the data.
/// </para>
/// <para>
/// Pinned on the two claims that matter rather than on the whole sentence: that
/// the store is named for what it is, and that the invitation is gone. Wording
/// stays free to change; the promise does not.
/// </para>
/// </summary>
public sealed class SettingsStorageGuidanceTests
{
    [Fact]
    public void The_storage_tab_says_the_folder_holds_a_database_rather_than_a_file_per_entry()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        Assert.Contains("SQLite", Guidance(settings.Component), StringComparison.Ordinal);
    }

    /// <summary>
    /// The sentence that has to stay gone. Asked as "does it name a file-sync
    /// product as somewhere to put this" rather than by matching the old wording,
    /// because the defect is the advice and not the phrasing that carried it.
    /// </summary>
    [Fact]
    public void The_storage_tab_warns_against_a_file_synced_folder_instead_of_inviting_one()
    {
        using var settings = RenderSettings();
        OpenStorageTab(settings.Component);

        var guidance = Guidance(settings.Component);

        Assert.Contains("OneDrive", guidance, StringComparison.Ordinal);
        Assert.DoesNotContain("Point this at a synced", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("travels with it", guidance, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every paragraph of the block, joined, so a claim moving between
    /// them does not break the assertion.</summary>
    private static string Guidance(IRenderedComponent<Settings> component) =>
        string.Join(
            ' ',
            component.FindAll("[data-testid='storage-path-guidance']").Select(paragraph => paragraph.TextContent.Trim()));

    private static void OpenStorageTab(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "Storage").Click();

    private static SettingsRenderContext RenderSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-storage-guidance-tests", Guid.NewGuid().ToString("n"));

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
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
