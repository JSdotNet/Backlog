using Backlog.Infrastructure.Copilot;
using Bunit;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.GitHub;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class HomeKnowledgePaneTests
{
    [Fact]
    public void Clicking_knowledge_pane_renders_without_throwing()
    {
        using var harness = CreateHarness();
        harness.Context.JSInterop.Mode = JSRuntimeMode.Loose;

        var component = harness.Context.Render<Home>();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='knowledge-pane-option']")));

        component.Find("[data-testid='repository-filter-option']").Click();
        var knowledgeButton = component.Find("[data-testid='knowledge-pane-option']");
        knowledgeButton.Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='knowledge-stack']"));
            Assert.NotEmpty(component.FindAll(".knowledge-menu__item"));
        });
    }

    private static Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-home-tests", Guid.NewGuid().ToString("n"));
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));

        _ = featureSettings.SetEnabled(AppFeatures.InboxPane, true);
        _ = featureSettings.SetEnabled(KnowledgeFeatures.KnowledgeSections, true);
        _ = featureSettings.SetEnabled(KnowledgeFeatures.RepositoryKnowledge, true);
        _ = featureSettings.SetEnabled(DevPcFeatures.SystemTools, false);
        _ = featureSettings.SetEnabled(AppFeatures.AiAssistant, false);
        _ = featureSettings.SetEnabled(AppFeatures.FeedbackReporting, false);
        _ = featureSettings.SetEnabled(BacklogFeatures.GitHubIntegration, false);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories);
        var configuredRepository = repository with
        {
            CloneDirectory = FindRepositoryRoot(),
            KnowledgeFolders = KnowledgeFolderSetting.Defaults()
        };
        Assert.Null(gitHubSettings.SetRepositories([configuredRepository]));

        var gitHub = new GitHubIntegration(gitHubSettings, new StubGitHubClient(), new StubProbe());
        var knowledgeFolderSource = new KnowledgeFolderSource(gitHubSettings, store);
        var repositoryBacklog = new RepositoryBacklogSource(
            BacklogTestHost.BacklogStoreFor(store, knowledgeFolderSource));

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(featureSettings);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton(new FeedbackReporter(gitHub));
        context.Services.AddSingleton<IAzureFoundryChatClient, StubAzureFoundryChatClient>();
        context.Services.AddSingleton<ICopilotToolService, UnsupportedCopilotToolService>();
        context.Services.AddSingleton<IAppUpdateService, UnsupportedAppUpdateService>();
        context.Services.AddSingleton<IKnowledgeFolderSource>(knowledgeFolderSource);
        context.Services.AddSingleton(repositoryBacklog);
        context.Services.AddSingleton<DesignKnowledgeProvider>();
        context.Services.AddSingleton<TechnologyKnowledgeService>();
        context.Services.AddSingleton<InstructionSourceDiscovery>();
        context.Services.AddSingleton<KnowledgeMenu>();
        context.Services.AddSingleton<Arc42KnowledgeStore>();
        // Every knowledge panel now renders its selected chapter through the
        // shared editing surface, and that surface writes. A host that composes
        // the pane composes the writer with it.
        context.Services.AddSingleton<KnowledgeChapterWriter>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<KnowledgeFolderOpenService>();
        context.Services.AddSingleton<KnowledgeScope>();
        context.Services.AddSingleton(new KnowledgeCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddScoped(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));
        context.Services.AddScoped(sp => BacklogTestHost.StateFor(
            sp.GetRequiredService<WorkspaceSettingsStore>(),
            sp.GetRequiredService<GitHubIntegration>(),
            BacklogCopilotCli.Unavailable,
            sp.GetRequiredService<RepositoryBacklogSource>()));

        return new Harness(root, context);
    }

    private static string FindRepositoryRoot() => RepositoryRoot.Root.FullName;

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
