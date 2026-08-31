using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Widening the atlas back to the whole knowledge base, from the pane that owns
/// the section strip.
///
/// <para>
/// The scope picker that used to sit inside the atlas view is gone; the section
/// strip already narrows the map to whichever tab is active, so the one thing it
/// was missing was an explicit way back to "all of it" — a button beside the
/// existing "Atlas" toggle, not a second control with its own idea of scope.
/// </para>
/// </summary>
public sealed class KnowledgePaneAtlasTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public async Task The_all_knowledge_button_is_not_offered_before_the_atlas_is_open()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='knowledge-atlas-toggle']")));

        Assert.Empty(component.FindAll("[data-testid='knowledge-atlas-all']"));

        // The picker this button replaces is gone too.
        Assert.Empty(component.FindAll("[data-testid='knowledge-atlas-scope']"));
    }

    [Fact]
    public async Task Opening_the_atlas_scopes_it_to_the_active_section()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='knowledge-atlas-toggle']")));

        component.Find("[data-testid='knowledge-atlas-toggle']").Click();

        component.WaitForAssertion(() =>
            Assert.Equal("Domain", component.Find(".knowledge-atlas__heading h3").TextContent.Trim()));
        Assert.NotNull(component.Find("[data-testid='knowledge-atlas-all']"));
    }

    [Fact]
    public async Task The_all_knowledge_button_widens_the_open_atlas()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='knowledge-atlas-toggle']")));
        component.Find("[data-testid='knowledge-atlas-toggle']").Click();
        component.WaitForAssertion(() =>
            Assert.Equal("Domain", component.Find(".knowledge-atlas__heading h3").TextContent.Trim()));

        component.Find("[data-testid='knowledge-atlas-all']").Click();

        // Widening the scope does not close the map — it is still the map, just
        // showing more of it.
        component.WaitForAssertion(() =>
            Assert.Equal("All knowledge", component.Find(".knowledge-atlas__heading h3").TextContent.Trim()));
        Assert.Equal("Hide atlas", component.Find("[data-testid='knowledge-atlas-toggle']").TextContent.Trim());
    }

    private const string ContextMap = """
        # Context Map

        ```meta
        status: draft
        ```

        The context map.
        """;

    private const string Graph = """
        {
          "elements": {
            "nodes": [
              { "data": { "id": ".domain/context-map.md", "label": "Context Map", "folder": "domain", "type": "file", "status": "active", "path": ".domain/context-map.md" } }
            ],
            "edges": []
          }
        }
        """;

    private Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-knowledge-pane-atlas", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".domain", "_meta"));
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));
        _roots.Add(root);

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), ContextMap);
        File.WriteAllText(Path.Combine(root, ".domain", "_meta", "graph.json"), Graph);
        File.WriteAllText(Path.Combine(root, ".arc42", "03-context-and-scope.md"), "# Context and scope\n\nThe system in its surroundings.\n");

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(KnowledgeFeatures.KnowledgeSections, true);
        _ = features.SetEnabled(KnowledgeFeatures.RepositoryKnowledge, true);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        // Two sections and no more, matching the two folders on disk, so the pane
        // opens on Domain and the atlas has exactly one other scope to widen from.
        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = root,
            KnowledgeFolders = [.. KnowledgeFolderSetting.Defaults().Select(folder => folder with { Enabled = folder.Key is ".domain" or ".arc42" })]
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(gitHub, settings));
        context.Services.AddSingleton(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));
        context.Services.AddSingleton<Arc42KnowledgeStore>();
        context.Services.AddSingleton(new KnowledgeCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<KnowledgeChapterWriter>();
        context.Services.AddSingleton<KnowledgeMenu>();
        context.Services.AddSingleton<KnowledgeScope>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<KnowledgeFolderOpenService>();
        context.Services.AddSingleton<KnowledgeAtlasService>();
        context.Services.AddSingleton<IGitFileHistoryService>(new StubGitFileHistory());

        return new Harness(context, repository.Alias);
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record Harness(BunitContext Context, string RepositoryAlias) : IAsyncDisposable
    {
        public IRenderedComponent<KnowledgePane> Render() =>
            Context.Render<KnowledgePane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
