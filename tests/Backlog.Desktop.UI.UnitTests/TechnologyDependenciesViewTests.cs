using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Dependencies view of the Technology area
/// (<c>.devbook/domain/devbook/features.md#technology-view</c>): the columns by hop
/// on each side of the focus, the direction and hop filters, grouping by layer,
/// the change impact by layer, and the paths down to a foundation.
/// <para>
/// The fixture is the design's stack around ASP.NET Core, small enough to work out
/// by hand. ASP.NET Core sits on C# and .NET, and on .NET again through C#. Razor,
/// Blazor and Minimal APIs use it; Blazor also through Razor; Playwright uses
/// Blazor and Container Apps uses Minimal APIs. Its measures are computed by
/// <see cref="TechnologyGraphMeasures"/>, as the reader computes them.
/// </para>
/// </summary>
public sealed class TechnologyDependenciesViewTests : IDisposable
{
    private const string Asp = ".tech/shared.md#asp";

    private readonly BunitContext _context = new();
    private string? _selected = Asp;

    public TechnologyDependenciesViewTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Columns_group_each_side_by_hop_with_what_it_sits_on_furthest_left()
    {
        var component = Render();

        Assert.Equal(["SITS ON · 1 HOP", "USED BY · 1 HOP", "USED BY · 2 HOPS"], ColumnHeads(component));
        Assert.Equal([".NET", "C#"], CardNames(component, 0));
        Assert.Equal(["Razor", "Blazor", "Minimal APIs"], CardNames(component, 1));
        Assert.Equal(["Container Apps", "Playwright"], CardNames(component, 2));
    }

    [Fact]
    public void All_hops_puts_the_furthest_column_of_each_side_at_the_edge()
    {
        _selected = ".tech/testing.md#playwright";
        var component = Render();

        Press(component, "hops", "All");

        Assert.Equal(["SITS ON · 3 HOPS", "SITS ON · 2 HOPS", "SITS ON · 1 HOP", "USED BY · NOTHING"], ColumnHeads(component));
        Assert.Equal([".NET", "C#"], CardNames(component, 0));
        Assert.Equal(["Blazor"], CardNames(component, 2));
    }

    [Fact]
    public void A_technology_reached_at_several_hops_shows_once_at_its_nearest_marked_also()
    {
        var component = Render();

        Assert.Single(component.FindAll("[data-testid='technology-dependencies-card'][data-id='.tech/shared.md#net']"));
        Assert.Equal("Shared · foundation · also 2 hops", CardDetail(component, ".tech/shared.md#net"));
        Assert.Equal("Shared · trial · also 2 hops", CardDetail(component, ".tech/shared.md#blazor"));
    }

    [Fact]
    public void A_technology_two_hops_away_says_which_it_is_reached_through()
    {
        var component = Render();

        Assert.Equal("Testing · candidate · via Blazor · also 3 hops", CardDetail(component, ".tech/testing.md#playwright"));
        Assert.Equal("Cloud · trial · via Minimal APIs", CardDetail(component, ".tech/cloud.md#aca"));
        Assert.DoesNotContain("via", CardDetail(component, ".tech/shared.md#razor"), StringComparison.Ordinal);
    }

    [Fact]
    public void One_hop_drops_the_further_columns()
    {
        var component = Render();

        Press(component, "hops", "1");

        Assert.Equal(["SITS ON · 1 HOP", "USED BY · 1 HOP"], ColumnHeads(component));
        Assert.Empty(component.FindAll("[data-testid='technology-dependencies-card'][data-id='.tech/testing.md#playwright']"));
    }

    [Theory]
    [InlineData("SitsOn", new[] { "SITS ON · 1 HOP" })]
    [InlineData("UsedBy", new[] { "USED BY · 1 HOP", "USED BY · 2 HOPS" })]
    [InlineData("Both", new[] { "SITS ON · 1 HOP", "USED BY · 1 HOP", "USED BY · 2 HOPS" })]
    public void The_direction_filter_shows_one_side_or_both(string direction, string[] heads)
    {
        var component = Render();

        Press(component, "direction", direction);

        Assert.Equal(heads, ColumnHeads(component));
        Assert.Single(component.FindAll("[data-testid='technology-dependencies-selected']"));
        Assert.Equal("true", component.Find($"[data-testid='technology-dependencies-direction-option'][data-direction='{direction}']").GetAttribute("aria-checked"));
    }

    [Fact]
    public void Group_by_layer_makes_a_column_per_layer_and_names_the_hop_on_each_card()
    {
        var component = Render();

        Press(component, "group", "layer");

        Assert.Equal(["SITS ON · SHARED", "USED BY · SHARED", "USED BY · CLOUD", "USED BY · TESTING"], ColumnHeads(component));
        Assert.Equal(["Minimal APIs", "Container Apps"], CardNames(component, 2));
        Assert.Equal("Cloud · 2 hops · trial · via Minimal APIs", CardDetail(component, ".tech/cloud.md#aca"));
    }

    [Fact]
    public void Clicking_a_card_makes_it_the_focus()
    {
        var component = Render();

        component.Find("[data-testid='technology-dependencies-card'][data-id='.tech/shared.md#razor']").Click();

        Assert.Equal(".tech/shared.md#razor", _selected);
        component.Render(parameters => parameters.Add(view => view.SelectedId, _selected));
        Assert.Contains("Razor", component.Find("[data-testid='technology-dependencies-selected']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_focus_picker_offers_every_technology_and_changes_the_selection()
    {
        var component = Render();
        var select = component.Find("[data-testid='technology-dependencies-focus'] select");

        Assert.Equal(8, select.QuerySelectorAll("option").Length);
        Assert.Equal(Asp, select.GetAttribute("value"));

        select.Change(".tech/shared.md#net");

        Assert.Equal(".tech/shared.md#net", _selected);
    }

    [Fact]
    public void The_change_impact_totals_breaks_if_it_changes_by_layer()
    {
        var component = Render();

        var total = component.Find("[data-testid='technology-dependencies-impact-total']").TextContent;
        Assert.Contains("reaches 5 technologies", Normalize(total), StringComparison.Ordinal);
        Assert.Contains("in 3 layers", Normalize(total), StringComparison.Ordinal);

        var rows = component.FindAll("[data-testid='technology-dependencies-impact-layers'] li")
            .Select(row => (Name: row.QuerySelector(".metric-ranking__name")!.TextContent.Trim(), Value: row.QuerySelector(".metric-ranking__value")!.TextContent.Trim()))
            .ToArray();
        Assert.Equal([("Shared", "2"), ("Cloud", "2"), ("Testing", "1")], rows);
        Assert.Equal(5, rows.Sum(row => int.Parse(row.Value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void A_leaf_breaks_nothing_and_the_figures_agree_with_the_measure()
    {
        _selected = ".tech/testing.md#playwright";
        var component = Render();

        Assert.Contains("reaches no other technology", component.Find("[data-testid='technology-dependencies-impact-total']").TextContent, StringComparison.Ordinal);
        Assert.Empty(component.FindAll("[data-testid='technology-dependencies-impact-layers']"));

        var figures = component.FindAll("[data-testid='technology-dependencies-figures'] dd").Select(dd => dd.TextContent).ToArray();
        Assert.Equal(["0", "0", "5"], figures);
    }

    [Fact]
    public void Paths_to_a_foundation_list_each_simple_path_and_the_longest_through_it()
    {
        var component = Render();

        var paths = component.FindAll("[data-testid='technology-dependencies-path']").Select(path => path.TextContent).ToArray();
        Assert.Equal(["ASP.NET Core → C# → .NET", "ASP.NET Core → .NET"], paths);
        Assert.Equal("Playwright → Blazor → Razor → ASP.NET Core → C# → .NET", Normalize(component.Find("[data-testid='technology-dependencies-longest']").TextContent));
        Assert.Equal("ASP.NET Core", component.Find(".tech-deps__path-focus").TextContent);
    }

    [Fact]
    public void Paths_stop_at_ten()
    {
        // A ladder of six rungs, each with two ways down to the next: 2^6 = 64 paths.
        var nodes = new List<TechnologyGraphNode>();
        var edges = new List<TechnologyGraphEdge>();
        for (var rung = 0; rung <= 6; rung++)
        {
            nodes.Add(Node($"a{rung}", $"A{rung}", "Shared Technologies", "framework", "adopted", 0, rung * 2));
            nodes.Add(Node($"b{rung}", $"B{rung}", "Shared Technologies", "framework", "adopted", 0, (rung * 2) + 1));
            if (rung == 0) continue;
            foreach (var from in new[] { $"a{rung - 1}", $"b{rung - 1}" })
            {
                edges.Add(Edge(from, $"a{rung}"));
                edges.Add(Edge(from, $"b{rung}"));
            }
        }

        var map = TechnologyDependencyMap.For(TechnologyGraphMeasures.Apply(nodes, edges), Id("a0"))!;

        Assert.Equal(TechnologyDependencyMap.MaxPaths, map.PathsToFoundation.Count);
        Assert.True(map.HasMorePaths);
    }

    [Fact]
    public void A_cycle_ends_every_walk()
    {
        TechnologyGraphNode[] nodes =
        [
            Node("x", "X", "Shared Technologies", "framework", "adopted", 0, 0),
            Node("y", "Y", "Shared Technologies", "framework", "adopted", 0, 1),
            Node("z", "Z", "Shared Technologies", "framework", "adopted", 0, 2),
        ];
        TechnologyGraphEdge[] edges = [Edge("x", "y"), Edge("y", "x"), Edge("y", "z")];

        var map = TechnologyDependencyMap.For(TechnologyGraphMeasures.Apply(nodes, edges), Id("x"))!;

        Assert.Equal(["Y", "Z"], map.SitsOn.Select(reach => reach.Node.Label));
        Assert.Equal(["Y"], map.UsedBy.Select(reach => reach.Node.Label));
        Assert.Equal(["X → Y → Z"], map.PathsToFoundation.Select(path => string.Join(" → ", path.Select(node => node.Label))));
        Assert.Equal(map.LongestPathThrough.Count, map.LongestPathThrough.Distinct().Count());
    }

    [Fact]
    public void A_focus_whose_dependencies_only_loop_back_is_not_called_a_foundation()
    {
        TechnologyGraphNode[] nodes =
        [
            Node("x", "X", "Shared Technologies", "framework", "adopted", 0, 0),
            Node("y", "Y", "Shared Technologies", "framework", "adopted", 0, 1),
        ];
        TechnologyGraphEdge[] edges = [Edge("x", "y"), Edge("y", "x")];
        var location = new DevbookFolderLocation("tech", true, null, "JSdotNet/Backlog", null, null);
        var view = new TechnologyDevbookView(location, "Technology", string.Empty, [], [], [], TechnologyGraphMeasures.Apply(nodes, edges), TechnologyGraphStats.Empty);

        var component = _context.Render<TechnologyDependenciesView>(parameters => parameters
            .Add(dependencies => dependencies.View, view)
            .Add(dependencies => dependencies.SelectedId, Id("x")));

        var paths = component.Find("[data-testid='technology-dependencies-paths']").TextContent;
        Assert.Contains("loops back", paths, StringComparison.Ordinal);
        Assert.DoesNotContain("it is a foundation", paths, StringComparison.Ordinal);
        Assert.Equal("Y → X", Normalize(component.Find("[data-testid='technology-dependencies-longest']").TextContent));
    }

    public void Dispose() => _context.Dispose();

    private IRenderedComponent<TechnologyDependenciesView> Render() =>
        _context.Render<TechnologyDependenciesView>(parameters => parameters
            .Add(view => view.View, Fixture())
            .Add(view => view.SelectedId, _selected)
            .Add(view => view.SelectedIdChanged, id => _selected = id));

    private static void Press(IRenderedComponent<TechnologyDependenciesView> component, string control, string value) =>
        component.Find($"[data-testid='technology-dependencies-{control}-option'][data-{control}='{value}']").Click();

    private static string[] ColumnHeads(IRenderedComponent<TechnologyDependenciesView> component) =>
        [.. component.FindAll("[data-testid='technology-dependencies-column'] .tech-deps__column-head > span:first-child")
            .Select(head => Normalize(head.TextContent).ToUpperInvariant())];

    private static string[] CardNames(IRenderedComponent<TechnologyDependenciesView> component, int column) =>
        [.. component.FindAll("[data-testid='technology-dependencies-column']")[column]
            .QuerySelectorAll(".tech-deps__card-name")
            .Select(name => name.TextContent.Trim())];

    private static string CardDetail(IRenderedComponent<TechnologyDependenciesView> component, string id) =>
        component.Find($"[data-testid='technology-dependencies-card'][data-id='{id}'] .tech-deps__card-detail").TextContent.Trim();

    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static TechnologyDevbookView Fixture()
    {
        TechnologyGraphNode[] nodes =
        [
            Node("shared.md#net", ".NET", "Shared Technologies", "runtime", "adopted", 0, 0),
            Node("shared.md#csharp", "C#", "Shared Technologies", "language", "adopted", 0, 1),
            Node("shared.md#asp", "ASP.NET Core", "Shared Technologies", "framework", "adopted", 0, 2),
            Node("shared.md#razor", "Razor", "Shared Technologies", "framework", "adopted", 0, 3),
            Node("shared.md#blazor", "Blazor", "Shared Technologies", "framework", "trial", 0, 4),
            Node("cloud.md#minimal", "Minimal APIs", "Cloud Stack", "framework", "adopted", 1, 0),
            Node("cloud.md#aca", "Container Apps", "Cloud Stack", "service", "trial", 1, 1),
            Node("testing.md#playwright", "Playwright", "Testing Technologies", "tool", "candidate", 2, 0),
        ];

        TechnologyGraphEdge[] edges =
        [
            Edge("shared.md#csharp", "shared.md#net"),
            Edge("shared.md#asp", "shared.md#csharp"),
            Edge("shared.md#asp", "shared.md#net"),
            Edge("shared.md#razor", "shared.md#asp"),
            Edge("shared.md#blazor", "shared.md#razor"),
            Edge("shared.md#blazor", "shared.md#asp"),
            Edge("cloud.md#minimal", "shared.md#asp"),
            Edge("cloud.md#aca", "cloud.md#minimal"),
            Edge("testing.md#playwright", "shared.md#blazor"),
        ];

        var location = new DevbookFolderLocation("tech", true, null, "JSdotNet/Backlog", null, null);
        return new TechnologyDevbookView(location, "Technology", string.Empty, [], [], [], TechnologyGraphMeasures.Apply(nodes, edges), TechnologyGraphStats.Empty);
    }

    private static string Id(string key) => ".tech/" + key;

    private static TechnologyGraphNode Node(string key, string label, string layer, string kind, string status, int layerIndex, int ordinal) =>
        new(Id(key), label, layer, kind, status, string.Empty, key.Split('#')[0], layerIndex, ordinal);

    private static TechnologyGraphEdge Edge(string from, string to) =>
        new($"{from}->{to}", Id(from), Id(to), "depends on");
}
