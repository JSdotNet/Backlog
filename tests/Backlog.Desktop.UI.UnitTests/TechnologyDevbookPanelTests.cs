using Backlog.Infrastructure.GitHub;
using Backlog.SharedKernel.Devbook;
using Backlog.UI.Components.Devbook;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Technology page (<c>.devbook/domain/devbook/features.md#technology-view</c>):
/// one tab bar over five views, the Overview board — summary tiles, type chips,
/// search, sort, table — and the inspector on the shared selection.
/// <para>
/// The fixture is a four-technology stack whose measures are small enough to
/// work out by hand: .NET under Razor, Razor and .NET under Blazor, Blazor under
/// Playwright. Every figure asserted here is that graph's, computed by the reader;
/// none is the page's own.
/// </para>
/// </summary>
public sealed class TechnologyDevbookPanelTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public async Task One_tab_bar_offers_the_five_views_and_no_atlas_or_layers()
    {
        await using var harness = CreateHarness();

        var component = await harness.RenderOverviewAsync();

        var tabs = component.FindAll("[role='tab']").Select(tab => tab.TextContent.Trim()).ToArray();
        Assert.Equal(["Overview", "Dependencies", "Complexity", "Clusters", "Type lenses"], tabs);
        Assert.Equal("true", component.Find("[data-testid='technology-overview-tab']").GetAttribute("aria-selected"));
        Assert.Empty(component.FindAll("[data-testid='technology-graph-tab']"));
        Assert.Empty(component.FindAll("[data-testid='technology-layers-tab']"));
        Assert.Empty(component.FindAll("[data-testid='technology-atlas']"));
    }

    [Fact]
    public async Task The_summary_tiles_read_the_graphs_measures()
    {
        await using var harness = CreateHarness();

        var component = await harness.RenderOverviewAsync();

        Assert.Equal("4", TileValue(component, "technologies"));
        Assert.Equal("across 2 layers", TileFootnote(component, "technologies"));
        Assert.Equal("3", TileValue(component, "types"));
        Assert.Equal("3hops", TileValue(component, "chain"));
        Assert.Equal("Playwright → .NET", TileFootnote(component, "chain"));
        Assert.Equal("2", TileValue(component, "most-used"));
        Assert.Equal("sit directly on .NET", TileFootnote(component, "most-used"));
        Assert.Equal("2 adopted · 1 trial · 1 candidate", TileFootnote(component, "ladder"));
    }

    [Fact]
    public async Task The_table_lists_every_technology_most_used_first_with_its_measures()
    {
        await using var harness = CreateHarness();

        var component = await harness.RenderOverviewAsync();

        Assert.Equal([".NET", "Blazor", "Razor", "Playwright"], RowNames(component));

        var playwright = component.FindAll("[data-testid='technology-table-table'] tbody tr")
            .Single(row => row.QuerySelector(".tech-overview__name")!.TextContent == "Playwright");
        var cells = playwright.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToArray();

        Assert.Equal("TOOLPlaywright", cells[0].Replace(" ", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal));
        Assert.Equal("Desktop", cells[1]);
        Assert.Contains("candidate", cells[2], StringComparison.Ordinal);
        Assert.Equal(["0", "3", "3"], cells[3..6]);
        Assert.Contains("Medium", cells[6], StringComparison.Ordinal);
        Assert.Contains("6", cells[6], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_type_chip_filters_the_list_and_says_it_is_pressed()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderOverviewAsync();

        var chips = component.FindAll("[data-testid='technology-type-chip']");
        Assert.Equal(["all", "framework", "runtime", "tool"], chips.Select(chip => chip.GetAttribute("data-type")).ToArray());
        Assert.Equal("true", Chip(component, "all").GetAttribute("aria-pressed"));
        Assert.Contains("2", Chip(component, "framework").TextContent, StringComparison.Ordinal);

        Chip(component, "framework").Click();

        Assert.Equal("true", Chip(component, "framework").GetAttribute("aria-pressed"));
        Assert.Equal("false", Chip(component, "all").GetAttribute("aria-pressed"));
        Assert.Equal(["Blazor", "Razor"], RowNames(component));
        Assert.Equal("Framework technologies", component.Find("#tech-overview-list").TextContent.Trim());

        Chip(component, "all").Click();

        Assert.Equal(4, RowNames(component).Length);
    }

    /// <summary>The folder is re-read with the filtered type gone, so the filter
    /// goes too: the list never narrows to a type no chip offers.</summary>
    [Fact]
    public async Task A_filter_on_a_type_the_folder_no_longer_has_is_dropped()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderOverviewAsync();

        Chip(component, "tool").Click();
        Assert.Equal(["Playwright"], RowNames(component));

        var desktop = Path.Combine(harness.TechFolder, "desktop.md");
        var text = File.ReadAllText(desktop);
        File.WriteAllText(desktop, text[..text.IndexOf("## Playwright", StringComparison.Ordinal)]);
        harness.Folders.NotifyContentChanged();

        component.WaitForAssertion(() => Assert.Equal(3, RowNames(component).Length));
        Assert.Empty(component.FindAll("[data-testid='technology-type-chip'][data-type='tool']"));
        Assert.Equal("true", Chip(component, "all").GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task The_search_narrows_the_list_by_name()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderOverviewAsync();

        component.Find("[data-testid='technology-search'] input").Input("pla");

        component.WaitForAssertion(() => Assert.Equal(["Playwright"], RowNames(component)));
    }

    [Theory]
    [InlineData("MostComplex", new[] { "Playwright", "Blazor", "Razor", ".NET" })]
    [InlineData("Deepest", new[] { "Playwright", "Blazor", "Razor", ".NET" })]
    [InlineData("Name", new[] { ".NET", "Blazor", "Playwright", "Razor" })]
    [InlineData("MostUsed", new[] { ".NET", "Blazor", "Razor", "Playwright" })]
    public async Task A_sort_orders_the_list(string sort, string[] expected)
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderOverviewAsync();

        component.Find($"[data-testid='technology-sort'][data-sort='{sort}']").Click();

        Assert.Equal(expected, RowNames(component));
        Assert.Equal("true", component.Find($"[data-testid='technology-sort'][data-sort='{sort}']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task The_page_opens_on_the_most_used_technology_in_the_inspector()
    {
        await using var harness = CreateHarness();

        var component = await harness.RenderOverviewAsync();

        Assert.Equal(".NET", InspectorTitle(component));
        Assert.Contains("Nothing — a foundation", component.Find("[data-testid='technology-inspector-sits-on']").TextContent, StringComparison.Ordinal);
        Assert.Equal("v10.0.11", component.Find("[data-testid='technology-inspector'] code.devbook-value").TextContent);
    }

    [Fact]
    public async Task Selecting_a_row_shows_its_facts_and_neighbours_in_the_inspector()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderOverviewAsync();

        Row(component, "Blazor").Click();

        Assert.Equal("Blazor", InspectorTitle(component));
        Assert.Equal("true", Row(component, "Blazor").GetAttribute("aria-pressed"));

        var facts = component.FindAll("[data-testid='technology-inspector-facts'] div")
            .ToDictionary(fact => fact.QuerySelector("dt")!.TextContent, fact => fact.QuerySelector("dd")!.TextContent);
        Assert.Equal("1", facts["Used by"]);
        Assert.Equal("1", facts["Breaks if it changes"]);
        Assert.Equal("2", facts["Rests on"]);
        Assert.Equal("2", facts["Depth"]);

        Assert.Equal(["Razor", ".NET"], RelatedNames(component, "sits-on"));
        Assert.Equal(["Playwright"], RelatedNames(component, "used-by"));
    }

    [Fact]
    public async Task A_leaf_says_nothing_depends_on_it_and_a_neighbour_moves_the_selection()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderOverviewAsync();

        Row(component, "Playwright").Click();
        Assert.Contains("Nothing depends on it — a leaf", component.Find("[data-testid='technology-inspector-used-by']").TextContent, StringComparison.Ordinal);

        component.Find("[data-testid='technology-inspector-sits-on'] [data-testid='technology-inspector-related']").Click();

        Assert.Equal("Blazor", InspectorTitle(component));
    }

    [Fact]
    public async Task The_selection_survives_switching_views()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderOverviewAsync();

        Row(component, "Razor").Click();
        component.Find("[data-testid='technology-inspector-show-dependencies']").Click();

        Assert.Equal("true", component.Find("[data-testid='technology-dependencies-tab']").GetAttribute("aria-selected"));

        component.Find("[data-testid='technology-overview-tab']").Click();

        component.WaitForAssertion(() => Assert.Equal("Razor", InspectorTitle(component)));
    }

    [Fact]
    public async Task The_lens_link_opens_the_type_lenses_view_named_for_the_type()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderOverviewAsync();

        Row(component, "Razor").Click();
        var lens = component.Find("[data-testid='technology-inspector-open-lens']");
        Assert.Equal("Open framework lens", lens.TextContent.Trim());

        lens.Click();

        Assert.Equal("true", component.Find("[data-testid='technology-type-lenses-tab']").GetAttribute("aria-selected"));
        component.WaitForAssertion(() => Assert.Equal(
            ["framework", "runtime", "tool"],
            component.FindAll("[data-testid='technology-lens-card']").Select(card => card.GetAttribute("data-type")).ToArray()));
    }

    /// <summary>The lens link lands on the technology it was pressed for, not its
    /// type's most-used one: Polly selected in Overview, "Open package lens", and
    /// the Package card names Polly, is marked as the selection and takes focus.</summary>
    [Fact]
    public async Task The_lens_link_lands_on_the_selected_technologys_card()
    {
        await using var harness = CreateHarness(withPackages: true);
        var component = await harness.RenderOverviewAsync();

        Row(component, "Polly").Click();
        var focusesBefore = harness.Context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Count;
        component.Find("[data-testid='technology-inspector-open-lens']").Click();

        component.WaitForAssertion(() => Assert.Equal("Polly", LensCardName(component, "package")));
        var package = LensCard(component, "package");
        Assert.Equal("true", package.GetAttribute("aria-current"));
        Assert.Single(component.FindAll("[data-testid='technology-lens-card'][aria-current]"));
        Assert.Equal(".NET", LensCardName(component, "runtime"));
        Assert.Equal("Agent host", LensCardName(component, "service"));
        component.WaitForAssertion(() => Assert.Equal(focusesBefore + 1, harness.Context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Count));
    }

    /// <summary>Opened from the tab strip the board still shows and marks the
    /// selection, but the focus stays on the tab.</summary>
    [Fact]
    public async Task The_type_lenses_tab_shows_the_selection_without_moving_focus()
    {
        await using var harness = CreateHarness(withPackages: true);
        var component = await harness.RenderOverviewAsync();

        Row(component, "Polly").Click();
        var focusesBefore = harness.Context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Count;
        component.Find("[data-testid='technology-type-lenses-tab']").Click();

        component.WaitForAssertion(() => Assert.Equal("Polly", LensCardName(component, "package")));
        Assert.Equal("true", LensCard(component, "package").GetAttribute("aria-current"));
        Assert.Equal(focusesBefore, harness.Context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Count);
    }

    /// <summary>The inspector draws the section the selection's type chooses below
    /// the common facts: .NET is a runtime, so the stack lens, with what is built
    /// directly on it grouped by layer.</summary>
    [Fact]
    public async Task The_inspector_shows_the_lens_its_type_chooses()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderOverviewAsync();

        var lens = component.Find("[data-testid='technology-inspector'] [data-testid='technology-lens']");
        Assert.Equal("stack", lens.GetAttribute("data-lens"));
        Assert.Equal(["built-on-it"], lens.QuerySelectorAll("[data-testid='technology-lens-row']").Select(row => row.GetAttribute("data-row")).ToArray());
        Assert.Equal(["Shared", "Desktop"], lens.QuerySelectorAll("[data-testid='technology-lens-layer']").Select(group => group.GetAttribute("data-layer")).ToArray());

        Row(component, "Playwright").Click();

        Assert.Empty(component.FindAll("[data-testid='technology-inspector'] [data-testid='technology-lens']"));
    }

    [Fact]
    public async Task Open_chapter_asks_the_host_for_the_technologys_own_chapter()
    {
        await using var harness = CreateHarness();
        DevbookChapterLink? opened = null;
        var component = await harness.RenderOverviewAsync(link => opened = link);

        Row(component, "Blazor").Click();
        component.Find("[data-testid='technology-inspector-open-chapter']").Click();

        Assert.NotNull(opened);
        Assert.Equal("tech", opened.AreaKey);
        Assert.Equal(".tech/desktop.md#blazor", opened.Reference);
    }

    /// <summary>The inspector's select offers the folder's own ladder and writes the
    /// choice back to the technology's chapter; the page re-reads, so the ladder
    /// tile follows.</summary>
    [Fact]
    public async Task A_status_change_in_the_inspector_reaches_the_layer_file()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderOverviewAsync();

        Row(component, "Blazor").Click();
        var select = component.Find("[data-testid='technology-inspector-state-select'] select");
        var offered = select.QuerySelectorAll("option").Select(option => option.TextContent.Trim()).ToArray();
        Assert.Equal(DevbookStatus.Values(DevbookFolder.Tech), offered.Take(5));

        select.Change("hold");

        component.WaitForAssertion(
            () =>
            {
                var desktop = File.ReadAllText(Path.Combine(harness.TechFolder, "desktop.md"));
                var blazor = desktop[desktop.IndexOf("## Blazor", StringComparison.Ordinal)..desktop.IndexOf("## Playwright", StringComparison.Ordinal)];
                Assert.Contains("status: hold", blazor, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
        component.WaitForAssertion(() => Assert.Equal("2 adopted · 1 candidate · 1 hold", TileFootnote(component, "ladder")));
        Assert.Equal("Blazor", InspectorTitle(component));
    }

    /// <summary>
    /// A pull announces the folder change off the renderer's thread, and the
    /// reload it starts can find the folder gone. The view that last read stays
    /// on screen rather than the handler's exception ending the process.
    /// </summary>
    [Fact]
    public async Task A_folder_that_cannot_be_read_after_a_change_leaves_the_last_view_on_screen()
    {
        await using var harness = CreateHarness();
        var folders = UnreadableDevbookFolderSource.Install(harness.Context);

        var component = await harness.RenderOverviewAsync();

        folders.MakeUnreadable();
        folders.NotifyContentChanged();
        await component.InvokeAsync(() => { });

        Assert.Equal(4, RowNames(component).Length);
    }

    [Fact]
    public async Task An_unavailable_technology_folder_offers_no_way_in()
    {
        await using var harness = CreateHarness(withTechFolder: false);

        var component = harness.Render();

        // The settings link belongs to the answer "there is no folder here", so
        // waiting for it waits for the load to have finished.
        component.WaitForAssertion(() => Assert.Contains("Open repository settings", component.Markup, StringComparison.Ordinal));
        Assert.Empty(component.FindAll("[data-testid='technology-tabs']"));
        Assert.Empty(component.FindAll("[data-testid='technology-row']"));
        Assert.Empty(component.FindAll("[data-testid='technology-inspector']"));
    }

    /// <summary>
    /// Deletes the temp folders once every test has awaited its harness away, so
    /// nothing this class rendered can still be writing into one of them.
    /// </summary>
    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string TileValue(IRenderedComponent<TechnologyDevbookPanel> component, string tile) =>
        component.Find($"[data-testid='technology-summary-{tile}'] .metric-tile__value").TextContent
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal);

    private static string TileFootnote(IRenderedComponent<TechnologyDevbookPanel> component, string tile) =>
        component.Find($"[data-testid='technology-summary-{tile}'] .metric-tile__footnote").TextContent.Trim();

    private static AngleSharp.Dom.IElement Chip(IRenderedComponent<TechnologyDevbookPanel> component, string type) =>
        component.Find($"[data-testid='technology-type-chip'][data-type='{type}']");

    private static string[] RowNames(IRenderedComponent<TechnologyDevbookPanel> component) =>
        [.. component.FindAll("[data-testid='technology-row'] .tech-overview__name").Select(name => name.TextContent.Trim())];

    private static AngleSharp.Dom.IElement Row(IRenderedComponent<TechnologyDevbookPanel> component, string name) =>
        component.FindAll("[data-testid='technology-row']")
            .Single(row => row.QuerySelector(".tech-overview__name")!.TextContent.Trim() == name);

    private static AngleSharp.Dom.IElement LensCard(IRenderedComponent<TechnologyDevbookPanel> component, string type) =>
        component.Find($"[data-testid='technology-lens-card'][data-type='{type}']");

    private static string LensCardName(IRenderedComponent<TechnologyDevbookPanel> component, string type) =>
        LensCard(component, type).QuerySelector("[data-testid='technology-lens-card-name']")!.TextContent.Trim();

    private static string InspectorTitle(IRenderedComponent<TechnologyDevbookPanel> component) =>
        component.Find(".tech-inspector__title").TextContent.Trim();

    private static string[] RelatedNames(IRenderedComponent<TechnologyDevbookPanel> component, string list) =>
        [.. component.FindAll($"[data-testid='technology-inspector-{list}'] [data-testid='technology-inspector-related']").Select(button => button.TextContent.Trim())];

    /// <summary>The inspector's stated status sits behind the devbook status
    /// switch, like every other chapter status; on, it is drawn as before.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_inspector_status_follows_the_devbook_status_switch(bool shown)
    {
        await using var harness = CreateHarness(status: shown);
        var component = await harness.RenderOverviewAsync();

        Row(component, "Blazor").Click();

        Assert.Equal("Blazor", InspectorTitle(component));
        Assert.Equal(shown, component.FindAll("[data-testid='technology-inspector-state-select']").Count > 0);
        Assert.Empty(component.FindAll("[data-testid='technology-inspector-state-badge']"));
    }

    [Fact]
    public async Task Switching_status_on_shows_it_without_reopening_the_panel()
    {
        await using var harness = CreateHarness(status: false);
        var component = await harness.RenderOverviewAsync();
        Assert.Empty(component.FindAll("[data-testid='technology-inspector-state-select']"));

        harness.Context.Services.GetRequiredService<IAppFeatureSettings>().SetEnabled(DevbookFeatures.Status, true);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-inspector-state-select']")));
    }

    /// <param name="status">The devbook status switch, with a <see cref="DevbookScope"/>
    /// composed to answer it; null composes none, which shows the status.</param>
    private Harness CreateHarness(bool withTechFolder = true, bool withPackages = false, bool? status = null)
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
            if (withPackages) File.WriteAllText(Path.Combine(tech, "packages.md"), PackagesLayer);
        }

        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        Assert.Null(gitHubSettings.SetRepositories(repositories));
        gitHubSettings.SetCloneDirectory("backlog", repository);

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var folders = new RecordingDevbookFolderSource(
            new DevbookFolderSource(gitHubSettings),
            Path.Combine(tech, "shared.md"));

        context.Services.AddSingleton<IDevbookFolderSource>(folders);
        context.Services.AddSingleton<TechnologyDevbookService>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<DevbookFolderOpenService>();

        if (status is { } shown)
        {
            var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
            Assert.Null(features.SetEnabled(DevbookFeatures.Status, shown));
            context.Services.AddSingleton<IAppFeatureSettings>(features);
            context.Services.AddSingleton(gitHubSettings);
            context.Services.AddSingleton<DevbookScope>();
        }

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
        status: adopted
        type: layer
        ```

        Shared platform choices.

        ## .NET
        ```meta
        status: adopted
        type: runtime
        version: "10.0.11"
        ```

        Cross-platform runtime.

        ## Razor
        ```meta
        status: adopted
        type: framework
        depends-on: [".tech/shared.md#net"]
        ```

        Component syntax.
        """;

    private const string DesktopLayer = """
        # Desktop Stack
        ```meta
        status: adopted
        type: layer
        ```

        Desktop UI choices.

        ## Blazor
        ```meta
        status: trial
        type: framework
        depends-on: [".tech/shared.md#razor", ".tech/shared.md#net"]
        ```

        Component model.

        ## Playwright
        ```meta
        status: candidate
        type: tool
        depends-on: [".tech/desktop.md#blazor"]
        ```

        Browser tests.
        """;

    /// <summary>Two packages, one of them used: the Package lens card's most-used
    /// technology is ModelContextProtocol, so Polly is the one it does not show
    /// until it is selected.</summary>
    private const string PackagesLayer = """
        # Package Technologies
        ```meta
        status: adopted
        type: layer
        ```

        Packages.

        ## ModelContextProtocol
        ```meta
        status: adopted
        type: package
        version: "1.1.0"
        depends-on: [".tech/shared.md#net"]
        ```

        The MCP C# SDK.

        ## Polly
        ```meta
        status: adopted
        type: package
        version: "8.4.0"
        depends-on: [".tech/shared.md#net"]
        ```

        Resilience.

        ## Agent host
        ```meta
        status: trial
        type: service
        depends-on: [".tech/packages.md#modelcontextprotocol"]
        ```

        Hosts the agents.
        """;

    private sealed record Harness(BunitContext Context, string TechFolder, RecordingDevbookFolderSource Folders) : IAsyncDisposable
    {
        public IRenderedComponent<TechnologyDevbookPanel> Render(Action<DevbookChapterLink>? onNavigate = null) =>
            Context.Render<TechnologyDevbookPanel>(parameters =>
            {
                parameters.Add(panel => panel.RepositoryAlias, "backlog");
                if (onNavigate is not null) parameters.Add(panel => panel.OnNavigateToChapter, onNavigate);
            });

        /// <summary>
        /// Renders the panel and waits for the Overview's rows. The store reads the
        /// folder on the thread pool, so the rows land on a later pass; the empty
        /// dispatch drains that pass before a test clicks, so the click is handled
        /// inline rather than queued behind it.
        /// </summary>
        public async Task<IRenderedComponent<TechnologyDevbookPanel>> RenderOverviewAsync(Action<DevbookChapterLink>? onNavigate = null)
        {
            var component = Render(onNavigate);
            component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-row']")));
            await component.InvokeAsync(() => { });
            return component;
        }

        /// <summary>
        /// Awaited disposal, because a status change this harness triggers is
        /// still being written when the test returns.
        /// </summary>
        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
