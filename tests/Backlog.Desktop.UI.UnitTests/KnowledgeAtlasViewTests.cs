using Microsoft.AspNetCore.Components;

using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Knowledge.Abstractions;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Rendering the atlas view against a real folder on disk.
///
/// <para>These exist because of what they caught. A wrong parameter name compiles
/// perfectly and only takes the circuit down the first time a reader presses a
/// button, because Blazor resolves parameter names at render time. Every one of
/// these tests is therefore a render, not an assertion about a service.</para>
/// </summary>
public sealed class KnowledgeAtlasViewTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public void The_atlas_renders_a_folder_and_lists_what_is_in_it()
    {
        using var harness = CreateHarness();

        var component = harness.Render(DomainScope);

        component.WaitForAssertion(() =>
            Assert.NotEmpty(component.FindAll("[data-testid=\"graph-atlas-index-option\"]")));

        Assert.Equal("Domain", component.Find(".knowledge-atlas__heading h3").TextContent.Trim());

        // Two documents and the chapter inside one of them.
        Assert.Equal(3, component.FindAll("[data-testid=\"graph-atlas-index-option\"]").Count);
    }

    /// <summary>The scope is the host's to drive — see the section strip's own
    /// "All knowledge" button in <c>KnowledgePane</c> — and this view just follows
    /// whatever it is handed.</summary>
    [Fact]
    public void Changing_the_scope_parameter_reads_that_one()
    {
        using var harness = CreateHarness();

        var component = harness.Render(DomainScope);
        component.WaitForAssertion(() =>
            Assert.Equal("Domain", component.Find(".knowledge-atlas__heading h3").TextContent.Trim()));

        component.Render(parameters => parameters.Add(view => view.Scope, KnowledgeAtlasScope.All));

        component.WaitForAssertion(() =>
            Assert.Equal("All knowledge", component.Find(".knowledge-atlas__heading h3").TextContent.Trim()));
    }

    /// <summary>Selecting a document opens the sheet on it, with the group as the
    /// eyebrow and the path as the line that tells two same-named chapters
    /// apart.</summary>
    [Fact]
    public void Selecting_a_document_opens_the_sheet_on_it()
    {
        using var harness = CreateHarness();

        var component = harness.Render(DomainScope);

        component.WaitForAssertion(() =>
            Assert.NotEmpty(component.FindAll("[data-testid=\"graph-atlas-index-option\"]")));

        Assert.Equal("false", component.Find("[data-testid=\"knowledge-atlas-sheet\"]").GetAttribute("data-open"));

        component.FindAll("[data-testid=\"graph-atlas-index-option\"]")[0].Click();

        var sheet = component.Find("[data-testid=\"knowledge-atlas-sheet\"]");

        Assert.Equal("true", sheet.GetAttribute("data-open"));
        Assert.Equal("Tasks", sheet.QuerySelector(".detail-sheet__kicker")!.TextContent.Trim());
        Assert.Contains(".domain/tasks/domain.md", sheet.QuerySelector(".detail-sheet__lede")!.TextContent);
    }

    /// <summary>The index is generated, so a folder in a checkout that has not run
    /// the generator is a normal state and says which state it is.</summary>
    [Fact]
    public void A_folder_with_no_generated_index_says_which_thing_is_missing()
    {
        using var harness = CreateHarness(writeIndex: false);

        var component = harness.Render(DomainScope);

        component.WaitForAssertion(() =>
            Assert.Contains("has not been written yet", component.Markup));

        Assert.Empty(component.FindAll("[data-testid=\"graph-atlas-index-option\"]"));
    }

    private static readonly KnowledgeAtlasScope DomainScope = new("domain", "Domain", ".domain");

    private Harness CreateHarness(bool writeIndex = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-atlas-tests", Guid.NewGuid().ToString("N"));
        _roots.Add(root);

        var repository = Path.Combine(root, "repo");
        var domain = Path.Combine(repository, ".domain");
        Directory.CreateDirectory(Path.Combine(domain, "_meta"));

        if (writeIndex)
        {
            File.WriteAllText(Path.Combine(domain, "_meta", "graph.json"), Graph);
        }

        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        Assert.Null(gitHubSettings.SetRepositories(repositories));
        gitHubSettings.SetCloneDirectory("backlog", repository);

        var context = new BunitContext();

        // The canvas reaches for interop and bUnit does not run it. What is under
        // test here is everything Blazor renders around it.
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(gitHubSettings));
        context.Services.AddSingleton<KnowledgeAtlasService>();

        return new Harness(context);
    }

    private const string Graph = """
        {
          "elements": {
            "nodes": [
              { "data": { "id": ".domain/tasks/domain.md", "label": "Tasks Domain", "folder": "domain", "type": "file", "status": "active", "path": ".domain/tasks/domain.md" } },
              { "data": { "id": ".domain/tasks/domain.md#entries", "label": "Entries", "folder": "domain", "type": "chapter", "status": "draft", "path": ".domain/tasks/domain.md" } },
              { "data": { "id": ".domain/context-map.md", "label": "Context Map", "folder": "domain", "type": "file", "status": "active", "path": ".domain/context-map.md" } }
            ],
            "edges": [
              { "data": { "id": "contains:1", "source": ".domain/tasks/domain.md", "target": ".domain/tasks/domain.md#entries", "type": "contains" } },
              { "data": { "id": "related:1", "source": ".domain/tasks/domain.md", "target": ".domain/context-map.md", "type": "related" } }
            ]
          }
        }
        """;

    private sealed record Harness(BunitContext Context) : IDisposable
    {
        public IRenderedComponent<KnowledgeAtlasView> Render(KnowledgeAtlasScope scope) =>
            Context.Render<KnowledgeAtlasView>(parameters =>
            {
                parameters.Add(view => view.Scope, scope);
                parameters.Add(view => view.RepositoryAlias, "backlog");
            });

        public void Dispose() => Context.Dispose();
    }

    public void Dispose()
    {
        foreach (var root in _roots)
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A temp folder that outlives the run is not worth failing a test over.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
