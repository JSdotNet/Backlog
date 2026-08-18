using Bunit;
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
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
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

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(gitHubSettings, store));
        context.Services.AddSingleton(new InstructionSourceDiscovery());

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
}
