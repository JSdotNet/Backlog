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

    /// <summary>
    /// The panel this was reported against. It drew its own header and its own
    /// body, and the relative path was on both — once beside the name and once on
    /// the editing surface's bar directly below it.
    /// </summary>
    [Fact]
    public void The_selected_document_is_shown_through_the_shared_file_view()
    {
        using var harness = CreateCloneHarness();

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog")
            .Add(parameter => parameter.SelectedPath, ".github/copilot-instructions.md"));

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-surface']")));

        // Inside the file view's body rather than merely somewhere on the panel.
        // The header is what keeps the identity on screen while the file scrolls,
        // and a body that landed beside the file view instead of in it would take
        // that away while still looking right in a screenshot.
        Assert.Single(component.FindAll("[data-testid='instructions-document-body'] [data-testid='knowledge-chapter-surface']"));

        // A selection means no file list, so the whole panel is the scope: the
        // path belongs on the file view's header and nowhere else on the screen.
        component.AssertTheFileIsNamedOnce("copilot-instructions.md", "[data-testid='instructions-knowledge-panel']");
    }

    [Fact]
    public void The_file_view_header_carries_the_agent_the_scope_and_the_size()
    {
        using var harness = CreateCloneHarness();

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog")
            .Add(parameter => parameter.SelectedPath, ".github/copilot-instructions.md"));

        // Every fact the panel's own header used to list, on the header that
        // replaced it. Moving onto a shared component is only an alignment if
        // nothing a reader was being told goes missing in the move.
        component.WaitForAssertion(() =>
        {
            var meta = component.Find(".file-view__meta").TextContent;
            Assert.Contains("GitHub Copilot", meta, StringComparison.Ordinal);
            Assert.Contains("Repository-wide instructions", meta, StringComparison.Ordinal);
            Assert.Contains(" B", meta, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_selected_document_is_edited_from_the_file_and_not_from_the_discovery_pass()
    {
        using var harness = CreateCloneHarness();
        var claude = Path.Combine(harness.Root, "clone", "CLAUDE.md");

        var component = harness.Context.Render<InstructionsKnowledgePanel>(parameters => parameters
            .Add(parameter => parameter.RepositoryAlias, "backlog"));
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("[data-testid='instructions-document-button']").Count));

        // Discovery read every instruction file in the clone to build the list on
        // the left, and the file moved after it. Selecting it has to open what the
        // file says now: the editor writes the whole buffer back, so a buffer built
        // from the discovery pass would put the old text back over this.
        File.WriteAllText(claude, "# Claude\n\nChanged on disk after the list was built.\n");
        component.FindAll("[data-testid='instructions-document-button']")[1].Click();

        component.WaitForAssertion(
            () => Assert.Contains("Changed on disk after the list was built.", component.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("As the list found it.", component.Markup, StringComparison.Ordinal);
    }

    private static Harness CreateCloneHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-instructions-clone-tests", Guid.NewGuid().ToString("n"));
        var clone = Path.Combine(root, "clone");
        Directory.CreateDirectory(Path.Combine(clone, ".github"));
        File.WriteAllText(Path.Combine(clone, ".github", "copilot-instructions.md"), "# Copilot\n\nRepository-wide guidance.\n");
        File.WriteAllText(Path.Combine(clone, "CLAUDE.md"), "# Claude\n\nAs the list found it.\n");

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = clone,
            KnowledgeFolders = KnowledgeFolderSetting.Defaults()
        };
        Assert.Null(gitHubSettings.SetRepositories([repository]));

        var context = new BunitContext();

        // The markdown editor watches its textarea through interop for the
        // highlight layer, which is not what this is about.
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(store);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(gitHubSettings, store));
        context.Services.AddSingleton(new InstructionSourceDiscovery());
        context.Services.AddSingleton<KnowledgeChapterWriter>();

        return new Harness(root, context);
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
        // The panel renders the shared editing surface, which writes.
        context.Services.AddSingleton<KnowledgeChapterWriter>();

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
