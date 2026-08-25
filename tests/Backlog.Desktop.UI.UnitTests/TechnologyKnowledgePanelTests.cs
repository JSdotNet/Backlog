using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Technology is the area with no chapter selection to inherit: it is not in the
/// knowledge menu, so nothing hands it a path. Its own layer tabs are the
/// selection instead, and these tests are about that substitution holding — the
/// node grid follows the active tab.
/// <para>
/// The layer file is read into that grid and is not rendered a second time as a
/// document below it, so the panel offers no editing surface of its own; the
/// status selector beside the layer heading is the only thing it writes.
/// </para>
/// </summary>
public sealed class TechnologyKnowledgePanelTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public async Task The_active_layer_renders_the_node_grid_and_no_document_surface()
    {
        await using var harness = CreateHarness();

        var component = harness.RenderLayers();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-node']")));
        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-surface']"));
        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-edit']"));
    }

    [Fact]
    public async Task The_pane_opens_on_the_graph_with_the_layer_detail_behind_the_second_tab()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='technology-graph-tab']")));

        // The header is the title and the action, and nothing between them: the
        // folder path and the root document's summary were both removed from it.
        Assert.Empty(component.FindAll(".knowledge-pane__source"));
        Assert.Empty(component.FindAll(".knowledge-pane__subtitle"));
        Assert.Single(component.FindAll(".knowledge-pane__header [data-testid='technology-open-vscode-button']"));

        // The graph tab is the one the pane opens with, so none of the layer
        // detail is on screen until the second tab is pressed.
        Assert.Equal("true", component.Find("[data-testid='technology-graph-tab']").GetAttribute("aria-selected"));
        Assert.Empty(component.FindAll("[data-testid='technology-node']"));

        component.Find("[data-testid='technology-layers-tab']").Click();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-node']")));
    }

    [Fact]
    public async Task The_node_grid_shows_the_layer_whose_tab_is_pressed()
    {
        await using var harness = CreateHarness();

        var component = harness.RenderLayers();

        component.WaitForAssertion(() => Assert.Contains(
            ".NET",
            component.Find(".tech-node-grid").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task Switching_layer_switches_the_nodes_on_screen()
    {
        await using var harness = CreateHarness();
        var component = harness.RenderLayers();
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll(".tech-layer-tab").Count));

        component.FindAll(".tech-layer-tab")[1].Click();

        component.WaitForAssertion(() => Assert.Contains(
            "Blazor",
            component.Find(".tech-node-grid").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_status_change_reaches_the_layer_file()
    {
        await using var harness = CreateHarness();
        var component = harness.RenderLayers();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".tech-layer__header [data-testid='knowledge-state-select'] select")));

        component.Find(".tech-layer__header [data-testid='knowledge-state-select'] select").Change("adopted");

        component.WaitForAssertion(
            () => Assert.Contains(
                "status: adopted",
                File.ReadAllText(Path.Combine(harness.TechFolder, "shared.md")),
                StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task An_unavailable_technology_folder_offers_no_way_in()
    {
        await using var harness = CreateHarness(withTechFolder: false);

        var component = harness.Render();

        // The panel's own element is on screen from the first render, loading or
        // not, so waiting for it was waiting for nothing: the absences below were
        // read off a panel that had not finished looking for the folder yet, and
        // would have held for one that went on to offer the layer detail. The
        // settings link belongs to the answer "there is no folder here", which is
        // the state they are about.
        component.WaitForAssertion(() => Assert.Contains("Open repository settings", component.Markup, StringComparison.Ordinal));
        Assert.Empty(component.FindAll("[data-testid='technology-layers-tab']"));
        Assert.Empty(component.FindAll("[data-testid='technology-node']"));
    }

    /// <summary>
    /// Deletes the temp folders once every test has awaited its harness away, so
    /// nothing this class rendered can still be writing into one of them. The
    /// catch stays as a courtesy for a lock this class does not own — a scanner
    /// or an indexer holding a file open — rather than as the thing keeping the
    /// suite green.
    /// </summary>
    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private Harness CreateHarness(bool withTechFolder = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-technology-panel-tests", Guid.NewGuid().ToString("n"));
        var repository = Path.Combine(root, "repo");
        var tech = Path.Combine(repository, ".tech");
        Directory.CreateDirectory(withTechFolder ? tech : repository);
        _roots.Add(root);

        if (withTechFolder)
        {
            WriteTechIndex(tech, "shared.md", "desktop.md");
            File.WriteAllText(Path.Combine(tech, "technology-graph.md"), TechnologyGraph);
            File.WriteAllText(Path.Combine(tech, "shared.md"), SharedLayer);
            File.WriteAllText(Path.Combine(tech, "desktop.md"), DesktopLayer);
        }

        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        Assert.Null(gitHubSettings.SetRepositories(repositories));
        gitHubSettings.SetCloneDirectory("backlog", repository);

        var context = new BunitContext();

        // The graph and the diagram both reach for interop. Neither is what these
        // tests are about.
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var folders = new RecordingKnowledgeFolderSource(
            new KnowledgeFolderSource(gitHubSettings),
            Path.Combine(tech, "shared.md"));

        context.Services.AddSingleton<IKnowledgeFolderSource>(folders);
        context.Services.AddSingleton<TechnologyKnowledgeService>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<KnowledgeFolderOpenService>();

        return new Harness(context, tech, folders);
    }

    private const string TechnologyGraph = """
        # Technology graph
        ```meta
        status: draft
        ```

        Repository technology overview.
        """;

    private const string SharedLayer = """
        # Shared Technologies
        ```meta
        status: accepted
        kind: layer
        ```

        Shared platform choices.

        ## .NET
        ```meta
        status: accepted
        kind: runtime
        ```

        Cross-platform runtime.
        """;

    private const string DesktopLayer = """
        # Desktop Stack
        ```meta
        status: proposed
        kind: layer
        ```

        Desktop UI choices.

        ## Blazor
        ```meta
        status: accepted
        kind: ui-framework
        ```

        Component model.
        """;

    private sealed record Harness(BunitContext Context, string TechFolder, RecordingKnowledgeFolderSource Folders) : IAsyncDisposable
    {
        public IRenderedComponent<TechnologyKnowledgePanel> Render() =>
            Context.Render<TechnologyKnowledgePanel>(parameters => parameters
                .Add(panel => panel.RepositoryAlias, "backlog"));

        /// <summary>Renders the panel and opens its Layers tab. The graph is the
        /// tab the panel opens with, and the layer detail these tests are about
        /// — node grid, layer picker — is behind the second one; an inactive tab
        /// panel renders none of it.</summary>
        public IRenderedComponent<TechnologyKnowledgePanel> RenderLayers()
        {
            var component = Render();
            component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='technology-layers-tab']")));
            component.Find("[data-testid='technology-layers-tab']").Click();
            return component;
        }

        /// <summary>
        /// Awaited disposal, because a status change this harness triggers is
        /// still being written when the test returns. A synchronous
        /// <c>Dispose</c> hands that write to the renderer's dispatcher and returns
        /// before it lands, so the folder delete that follows could arrive while
        /// the file was still being replaced — a locked temp file on a slow
        /// machine and a green suite on a fast one.
        /// </summary>
        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }

    /// <summary>
    /// The committed reading order for a <c>.tech</c> fixture. The layer sequence
    /// lives here now rather than in the root document's fence, so a fixture that
    /// cares about order writes the index the reader actually consults.
    /// </summary>
    private static void WriteTechIndex(string techPath, params string[] layers)
    {
        Directory.CreateDirectory(Path.Combine(techPath, "_meta"));

        var entries = new List<string>
        {
            "{ \"type\": \"file\", \"name\": \"technology-graph.md\", \"path\": \".tech/technology-graph.md\", \"title\": \"Technology graph\", \"status\": \"draft\", \"root\": true }"
        };
        entries.AddRange(layers.Select(layer =>
            $"{{ \"type\": \"file\", \"name\": \"{layer}\", \"path\": \".tech/{layer}\", \"title\": \"{layer}\", \"status\": null }}"));

        File.WriteAllText(
            Path.Combine(techPath, "_meta", "index.json"),
            "{ \"schemaVersion\": 1, \"scope\": \".tech\", \"problems\": [], \"entries\": [" + string.Join(", ", entries) + "] }");
    }
}
