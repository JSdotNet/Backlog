using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Technology is the area with no chapter selection to inherit: it is not in the
/// knowledge menu, so nothing hands it a path. Its own layer tabs are the
/// selection instead, and these tests are about that substitution holding — the
/// surface follows the active tab, and switching tab switches the file being
/// written.
/// <para>
/// The status selector beside the layer heading is the other reason this panel
/// needed care. It and the body debounce are two read-modify-writes on one file,
/// so a status change has to get the pending body to disk on its way out.
/// </para>
/// </summary>
public sealed class TechnologyKnowledgePanelTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public async Task The_active_layer_renders_the_editing_surface()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-surface']")));
        Assert.Single(component.FindAll("[data-testid='knowledge-chapter-edit']"));
    }

    [Fact]
    public async Task The_editing_surface_sits_below_the_node_grid_rather_than_replacing_it()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();

        // The grid is what the panel was for before it could be written to, so
        // both are asserted together: the surface is an addition, and an addition
        // that took the grid's place would pass either check on its own.
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-node']")));
        Assert.NotEmpty(component.FindAll("[data-testid='knowledge-chapter-surface']"));
        Assert.True(
            component.Markup.IndexOf("technology-node", StringComparison.Ordinal)
            < component.Markup.IndexOf("knowledge-chapter-surface", StringComparison.Ordinal),
            "The editing surface belongs below the node grid.");
    }

    [Fact]
    public async Task The_surface_shows_the_layer_whose_tab_is_pressed()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();

        component.WaitForAssertion(() => Assert.Contains(
            "Shared platform choices.",
            component.Find("[data-testid='knowledge-chapter-surface']").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task Switching_layer_switches_the_chapter_being_edited()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll(".tech-layer-tab").Count));

        component.FindAll(".tech-layer-tab")[1].Click();

        component.WaitForAssertion(() => Assert.Contains(
            "Desktop UI choices.",
            component.Find("[data-testid='knowledge-chapter-surface']").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_typed_layer_chapter_reaches_the_file()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-edit']")));

        component.Find("[data-testid='knowledge-chapter-edit']").Click();
        component.Find("textarea").Input("# Shared Technologies\n\nTyped into the shared layer.\n");
        component.Find("textarea").Blur();

        component.WaitForAssertion(
            () => Assert.Contains(
                "Typed into the shared layer.",
                File.ReadAllText(Path.Combine(harness.TechFolder, "shared.md")),
                StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_status_change_does_not_cost_the_pending_body_edit()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-edit']")));

        component.Find("[data-testid='knowledge-chapter-edit']").Click();
        component.WaitForElement("textarea").Input("# Shared Technologies\n\n```meta\nstatus: accepted\nkind: layer\n```\n\nTyped, then the status changed.\n");

        // No blur, no Done: the body is pending when the selector fires, which is
        // the race. The handler flushes it before writing the status, so the file
        // ends up carrying both rather than whichever wrote last.
        //
        // From here on, the first thing to ask the folder source where .tech is
        // will be the status write, so what the layer file says at that moment is
        // recorded. That is what makes the ordering decidable: the settled file
        // cannot tell the two orders apart, because the merge repairs both.
        harness.Folders.ArmStatusWriteSnapshot();
        component.Find(".tech-layer__header [data-testid='knowledge-state-select'] select").Change("adopted");

        component.WaitForAssertion(
            () =>
            {
                var markdown = File.ReadAllText(Path.Combine(harness.TechFolder, "shared.md"));
                Assert.Contains("Typed, then the status changed.", markdown, StringComparison.Ordinal);
                Assert.Contains("status: adopted", markdown, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));

        Assert.Contains(
            "Typed, then the status changed.",
            harness.Folders.ChapterWhenStatusWasWritten ?? "the status was never written",
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unavailable_technology_folder_offers_no_way_in()
    {
        await using var harness = CreateHarness(withTechFolder: false);

        var component = harness.Render();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-knowledge-panel']")));
        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-surface']"));
        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-edit']"));
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

        // The graph, the diagram and the markdown editor all reach for interop.
        // None of that is what these tests are about.
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var folders = new RecordingKnowledgeFolderSource(
            new KnowledgeFolderSource(gitHubSettings),
            Path.Combine(tech, "shared.md"));

        context.Services.AddSingleton<IKnowledgeFolderSource>(folders);
        context.Services.AddSingleton<TechnologyKnowledgeService>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<KnowledgeFolderOpenService>();
        context.Services.AddSingleton<KnowledgeChapterWriter>();

        return new Harness(context, tech, folders);
    }

    private const string TechnologyGraph = """
        # Technology graph
        ```meta
        status: draft
        order: ["shared.md", "desktop.md"]
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

        /// <summary>
        /// Awaited disposal, because the editing surface this harness renders
        /// writes its last pending save on the way out. A synchronous
        /// <c>Dispose</c> hands that save to the renderer's dispatcher and returns
        /// before it lands, so the folder delete that follows could arrive while
        /// the file was still being replaced — a locked temp file on a slow
        /// machine and a green suite on a fast one.
        /// </summary>
        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
