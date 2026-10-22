using Backlog.Infrastructure.GitHub;
using Backlog.SharedKernel.Devbook;
using Backlog.UI.Components.Devbook;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Clusters view of the Technology page
/// (<c>.devbook/domain/devbook/features.md#technology-view</c>): every layer as a
/// cluster, a layer opened into pills and satellites, a technology's links lit up,
/// and following them — across layers, along the trail and Back.
/// <para>
/// The fixture is five technologies in three layers. Shared holds .NET and Razor
/// on it; Desktop holds Blazor on both and Playwright on Blazor; Testing holds bUnit
/// on Razor and Blazor. So Desktop links to Shared twice, Testing to Shared once and
/// to Desktop once.
/// </para>
/// </summary>
public sealed class TechnologyClustersViewTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public async Task All_layers_draws_one_cluster_per_layer_with_a_dot_per_technology()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();

        Assert.Equal(["shared.md", "desktop.md", "testing.md"], component.FindAll("[data-testid='technology-clusters-cluster']").Select(cluster => cluster.GetAttribute("data-layer")).ToArray());
        Assert.Equal(5, component.FindAll("[data-testid='technology-clusters-dot']").Count);

        // Not adopted is hollow; adopted is filled.
        Assert.Contains("tech-clusters__dot--hollow", Dot(component, "desktop.md#blazor").ClassName, StringComparison.Ordinal);
        Assert.Contains("tech-clusters__dot--hollow", Dot(component, "desktop.md#playwright").ClassName, StringComparison.Ordinal);
        Assert.DoesNotContain("tech-clusters__dot--hollow", Dot(component, "shared.md#net").ClassName, StringComparison.Ordinal);

        // Colour follows the family: two frameworks share one, a tool takes another.
        Assert.Contains("tech-family--framework", Dot(component, "shared.md#razor").ClassName, StringComparison.Ordinal);
        Assert.Contains("tech-family--framework", Dot(component, "desktop.md#blazor").ClassName, StringComparison.Ordinal);
        Assert.Contains("tech-family--library-tool-plugin", Dot(component, "desktop.md#playwright").ClassName, StringComparison.Ordinal);

        // One weighted line per linked pair, each with its count.
        Assert.Equal(3, component.FindAll("[data-testid='technology-clusters-line'][data-tone='link']").Count);
        Assert.Equal(["2", "1", "1"], component.FindAll("[data-testid='technology-clusters-link-count']").Select(badge => badge.TextContent.Trim()).ToArray());

        Assert.True(component.Find("[data-testid='technology-clusters-back']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Opening_a_layer_shows_its_pills_and_the_other_layers_as_satellites()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();

        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();

        // Grouped by family: the framework before the tool.
        Assert.Equal(["Blazor", "Playwright"], PillNames(component));
        Assert.Equal("Desktop", component.Find("[data-testid='technology-clusters-crumb-layer']").TextContent.Trim());
        Assert.Empty(component.FindAll("[data-testid='technology-clusters-crumb-technology']"));

        var satellites = component.FindAll("[data-testid='technology-clusters-satellite']");
        Assert.Equal(["shared.md", "testing.md"], satellites.Select(satellite => satellite.GetAttribute("data-layer")).ToArray());
        Assert.Equal(["2", "1"], component.FindAll("[data-testid='technology-clusters-satellite-links']").Select(badge => badge.TextContent.Trim()).ToArray());

        // Blazor on Playwright's side of the layer is the one quiet line inside it.
        Assert.Single(component.FindAll("[data-testid='technology-clusters-line'][data-tone='quiet']"));

        // A satellite opens its layer.
        component.Find("[data-testid='technology-clusters-satellite'][data-layer='testing.md']").Click();
        Assert.Equal(["bUnit"], PillNames(component));
    }

    [Fact]
    public async Task A_layer_opens_from_the_side_panel_list_too()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();

        component.Find("[data-testid='technology-clusters-layer-row'][data-layer='shared.md']").Click();

        Assert.Equal(["Razor", ".NET"], PillNames(component));
        Assert.Equal(["Razor", ".NET"], component.FindAll("[data-testid='technology-clusters-technology-row'] .tech-clusters__row-name").Select(name => name.TextContent.Trim()).ToArray());
    }

    [Fact]
    public async Task Selecting_a_technology_lights_its_links_and_dims_the_rest()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();
        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();

        Pill(component, "desktop.md#blazor").Click();

        Assert.Equal("Blazor", component.Find("[data-testid='technology-clusters-crumb-technology']").TextContent.Trim());
        Assert.Equal("true", Pill(component, "desktop.md#blazor").GetAttribute("aria-pressed"));
        Assert.Contains("tech-clusters__pill--used-by", Pill(component, "desktop.md#playwright").ClassName, StringComparison.Ordinal);

        // Gold to Shared, which it sits on; blue to Playwright beside it and to Testing, which use it.
        Assert.Contains("tech-clusters__satellite--sits-on", Satellite(component, "shared.md").ClassName, StringComparison.Ordinal);
        Assert.Contains("tech-clusters__satellite--used-by", Satellite(component, "testing.md").ClassName, StringComparison.Ordinal);
        Assert.Single(component.FindAll("[data-testid='technology-clusters-line'][data-tone='sits-on']"));
        Assert.Equal(2, component.FindAll("[data-testid='technology-clusters-line'][data-tone='used-by']").Count);

        // The panel shows its measures, and every link carries its layer.
        Assert.Equal("Blazor", component.Find("[data-testid='technology-clusters-side'] #tech-inspector-title").TextContent.Trim());
        Assert.Contains("Rests on", component.Find("[data-testid='technology-clusters-side'] [data-testid='technology-inspector-facts']").TextContent, StringComparison.Ordinal);
        Assert.Contains("Breaks if it changes", component.Find("[data-testid='technology-clusters-side'] [data-testid='technology-inspector-facts']").TextContent, StringComparison.Ordinal);
        Assert.Contains("Shared", Related(component, "sits-on", "Razor").TextContent, StringComparison.Ordinal);
        Assert.Contains("here", Related(component, "used-by", "Playwright").TextContent, StringComparison.Ordinal);
        Assert.Contains("Testing", Related(component, "used-by", "bUnit").TextContent, StringComparison.Ordinal);

        // Playwright uses only Blazor, so both satellites dim when it is selected.
        Pill(component, "desktop.md#playwright").Click();
        Assert.All(component.FindAll("[data-testid='technology-clusters-satellite']"), satellite =>
            Assert.Contains("tech-clusters__satellite--dimmed", satellite.ClassName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Following_a_link_to_another_layer_opens_that_layer_and_shares_the_selection()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();
        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();
        Pill(component, "desktop.md#blazor").Click();

        Related(component, "sits-on", "Razor").Click();

        Assert.Equal(["Razor", ".NET"], PillNames(component));
        Assert.Equal("Shared", component.Find("[data-testid='technology-clusters-crumb-layer']").TextContent.Trim());
        Assert.Equal("Razor", component.Find("[data-testid='technology-clusters-crumb-technology']").TextContent.Trim());
        Assert.Equal("true", Pill(component, "shared.md#razor").GetAttribute("aria-pressed"));

        // The selection is the panel's: Overview's inspector shows it.
        component.Find("[data-testid='technology-overview-tab']").Click();
        component.WaitForAssertion(() => Assert.Equal("Razor", component.Find("[data-testid='technology-overview-panel'] #tech-inspector-title").TextContent.Trim()));
    }

    [Fact]
    public async Task The_trail_keeps_the_last_six_and_steps_back_to_any_of_them()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();
        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();

        Pill(component, "desktop.md#playwright").Click();
        Related(component, "sits-on", "Blazor").Click();
        Related(component, "sits-on", "Razor").Click();

        // Blazor first: the view opened on the shared selection.
        Assert.Equal(["Blazor", "Playwright", "Blazor", "Razor"], TrailNames(component));

        component.FindAll("[data-testid='technology-clusters-trail-step']")[1].Click();

        Assert.Equal("Playwright", component.Find("[data-testid='technology-clusters-crumb-technology']").TextContent.Trim());
        Assert.Equal(["Blazor", "Playwright"], PillNames(component));
        Assert.Equal(["Blazor", "Playwright"], TrailNames(component));
        Related(component, "sits-on", "Blazor").Click();

        // Back and forth between two: the trail keeps the last six visits.
        for (var visit = 0; visit < 4; visit++)
        {
            Related(component, "sits-on", "Razor").Click();
            Related(component, "used-by", "Blazor").Click();
        }

        Assert.Equal(["Razor", "Blazor", "Razor", "Blazor", "Razor", "Blazor"], TrailNames(component));
    }

    [Fact]
    public async Task Back_retraces_the_trail_then_leaves_the_technology_then_the_layer()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();
        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();
        Pill(component, "desktop.md#playwright").Click();
        Related(component, "sits-on", "Blazor").Click();
        Related(component, "sits-on", "Razor").Click();

        var back = () => component.Find("[data-testid='technology-clusters-back']").Click();

        back();
        Assert.Equal("Blazor", component.Find("[data-testid='technology-clusters-crumb-technology']").TextContent.Trim());
        Assert.Equal(["Blazor", "Playwright"], PillNames(component));

        back();
        Assert.Equal("Playwright", component.Find("[data-testid='technology-clusters-crumb-technology']").TextContent.Trim());

        // The trail began on Blazor, the selection the view opened on.
        back();
        Assert.Equal("Blazor", component.Find("[data-testid='technology-clusters-crumb-technology']").TextContent.Trim());

        back();
        Assert.Empty(component.FindAll("[data-testid='technology-clusters-crumb-technology']"));
        Assert.Equal("Desktop", component.Find("[data-testid='technology-clusters-crumb-layer']").TextContent.Trim());

        back();
        Assert.Empty(component.FindAll("[data-testid='technology-clusters-crumb-layer']"));
        Assert.Equal(3, component.FindAll("[data-testid='technology-clusters-cluster']").Count);
        Assert.True(component.Find("[data-testid='technology-clusters-back']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task The_layer_crumb_leaves_the_technology_and_All_layers_goes_home()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();
        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();
        component.Find("[data-testid='technology-clusters-technology-row'][data-id='.tech/desktop.md#blazor']").Click();

        Assert.Equal("Blazor", component.Find("[data-testid='technology-clusters-crumb-technology']").TextContent.Trim());

        component.Find("[data-testid='technology-clusters-crumb-layer']").Click();
        Assert.Empty(component.FindAll("[data-testid='technology-clusters-crumb-technology']"));
        Assert.All(component.FindAll("[data-testid='technology-clusters-pill']"), pill => Assert.Equal("false", pill.GetAttribute("aria-pressed")));

        component.Find("[data-testid='technology-clusters-crumb-all']").Click();
        Assert.Equal(3, component.FindAll("[data-testid='technology-clusters-cluster']").Count);
    }

    [Fact]
    public async Task The_view_opens_on_the_selection_another_view_made()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();

        component.Find("[data-testid='technology-overview-tab']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-row']")));
        component.FindAll("[data-testid='technology-row']")
            .Single(row => row.QuerySelector(".tech-overview__name")!.TextContent.Trim() == "bUnit")
            .Click();

        component.Find("[data-testid='technology-clusters-tab']").Click();

        component.WaitForAssertion(() => Assert.Equal("bUnit", component.Find("[data-testid='technology-clusters-crumb-technology']").TextContent.Trim()));
        Assert.Equal("Testing", component.Find("[data-testid='technology-clusters-crumb-layer']").TextContent.Trim());
        Assert.Equal("true", Pill(component, "testing.md#bunit").GetAttribute("aria-pressed"));
        Assert.Equal("true", component.Find("[data-testid='technology-clusters-technology-row'][data-id='.tech/testing.md#bunit']").GetAttribute("aria-pressed"));
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static AngleSharp.Dom.IElement Dot(IRenderedComponent<TechnologyDevbookPanel> component, string id) =>
        component.Find($"[data-testid='technology-clusters-dot'][data-id='.tech/{id}']");

    private static AngleSharp.Dom.IElement Pill(IRenderedComponent<TechnologyDevbookPanel> component, string id) =>
        component.Find($"[data-testid='technology-clusters-pill'][data-id='.tech/{id}']");

    private static AngleSharp.Dom.IElement Satellite(IRenderedComponent<TechnologyDevbookPanel> component, string layer) =>
        component.Find($"[data-testid='technology-clusters-satellite'][data-layer='{layer}']");

    private static AngleSharp.Dom.IElement Related(IRenderedComponent<TechnologyDevbookPanel> component, string list, string name) =>
        component.FindAll($"[data-testid='technology-clusters-side'] [data-testid='technology-inspector-{list}'] [data-testid='technology-inspector-related']")
            .Single(button => button.TextContent.Trim().StartsWith(name, StringComparison.Ordinal));

    private static string[] PillNames(IRenderedComponent<TechnologyDevbookPanel> component) =>
        [.. component.FindAll("[data-testid='technology-clusters-pill'] .tech-clusters__pill-label").Select(label => label.TextContent.Trim())];

    private static string[] TrailNames(IRenderedComponent<TechnologyDevbookPanel> component) =>
        [.. component.FindAll("[data-testid='technology-clusters-trail-step']").Select(step => step.TextContent.Trim())];

    private Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-technology-clusters-tests", Guid.NewGuid().ToString("n"));
        var repository = Path.Combine(root, "repo");
        var tech = Path.Combine(repository, ".tech");
        Directory.CreateDirectory(tech);
        _roots.Add(root);

        File.WriteAllText(Path.Combine(tech, "technology-graph.md"), TechnologyGraph);
        File.WriteAllText(Path.Combine(tech, "shared.md"), SharedLayer);
        File.WriteAllText(Path.Combine(tech, "desktop.md"), DesktopLayer);
        File.WriteAllText(Path.Combine(tech, "testing.md"), TestingLayer);

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

        return new Harness(context);
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

    private const string TestingLayer = """
        # Testing
        ```meta
        status: adopted
        type: layer
        ```

        Test choices.

        ## bUnit
        ```meta
        status: adopted
        type: package
        depends-on: [".tech/shared.md#razor", ".tech/desktop.md#blazor"]
        ```

        Component tests.
        """;

    private sealed record Harness(BunitContext Context) : IAsyncDisposable
    {
        /// <summary>Renders the panel, waits for the folder to load, and opens Clusters.</summary>
        public async Task<IRenderedComponent<TechnologyDevbookPanel>> RenderClustersAsync()
        {
            var component = Context.Render<TechnologyDevbookPanel>(parameters => parameters.Add(panel => panel.RepositoryAlias, "backlog"));
            component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-row']")));
            await component.InvokeAsync(() => { });

            component.Find("[data-testid='technology-clusters-tab']").Click();
            component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-clusters']")));

            // Every test starts from All layers; the one about arriving checks where the view opens.
            component.Find("[data-testid='technology-clusters-crumb-all']").Click();
            component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-clusters-cluster']")));
            return component;
        }

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
