using Bunit;
using Backlog.Desktop.UI.Components;
using Backlog.Desktop.UI.Services;
using Backlog.Infrastructure.GitHub;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class InstructionsKnowledgePanelTests
{
    [Fact]
    public void Instructions_panel_renders_repository_instructions_without_throwing()
    {
        using var harness = CreateHarness();

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog"));

        Assert.Contains("Instructions", component.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(component.FindAll("[data-testid='instructions-document']"));
    }

    private static Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-instructions-tests", Guid.NewGuid().ToString("n"));
        var store = new BacklogStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories);
        var configuredRepository = repository with
        {
            CloneDirectory = FindRepositoryRoot(),
            KnowledgeFolders = KnowledgeFolderSetting.Defaults()
        };

        Assert.Null(gitHubSettings.SetRepositories([configuredRepository]));

        var integration = new GitHubIntegration(gitHubSettings, new StubGitHubClient(), new StubProbe());

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton(integration);
        context.Services.AddSingleton(new InstructionSourceDiscovery());

        return new Harness(root, context);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, ".github", "copilot-instructions.md");
            if (File.Exists(candidate))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
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
