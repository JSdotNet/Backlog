using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Complexity board of the Technology page
/// (<c>.devbook/domain/devbook/features.md#technology-view</c>): the scatter split
/// into four quadrants, points merged where technologies share a place, the two
/// hotspot lists, the longest chain, and the table companion — all on the shared
/// selection.
/// <para>
/// The fixture is a stack built so each quadrant holds something and two places
/// hold several technologies. A chain N0 ← N1 ← … ← N5; four technologies sit on
/// N5 alone (each rests on 6, nothing uses them); three sit on N0 alone (each
/// rests on 1). So N0 is used by 4 and rests on nothing (a foundation), N5 is used
/// by 4 and rests on 5 (a hub), the four on N5 are deep leaves at one place, and
/// the rest are leaves. The measures are the graph's own, computed by
/// <see cref="TechnologyGraphMeasures"/>.
/// </para>
/// </summary>
public sealed class TechnologyComplexityViewTests : IDisposable
{
    private readonly BunitContext _context = new();
    private string? _selected;
    private readonly List<string?> _picked = [];

    public TechnologyComplexityViewTests() => _context.JSInterop.Mode = JSRuntimeMode.Loose;

    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData(0, 0, TechnologyQuadrant.Leaves)]
    [InlineData(3, 4, TechnologyQuadrant.Leaves)]
    [InlineData(4, 4, TechnologyQuadrant.Foundations)]
    [InlineData(3, 5, TechnologyQuadrant.DeepLeaves)]
    [InlineData(4, 5, TechnologyQuadrant.Hubs)]
    [InlineData(20, 0, TechnologyQuadrant.Foundations)]
    [InlineData(0, 9, TechnologyQuadrant.DeepLeaves)]
    public void A_technology_falls_in_the_quadrant_its_two_splits_put_it(int usedBy, int restsOn, TechnologyQuadrant expected) =>
        Assert.Equal(expected, TechnologyQuadrants.Of(usedBy, restsOn));

    [Fact]
    public void The_table_companion_names_each_technologys_coordinates_and_quadrant()
    {
        var component = Render();

        var rows = component.FindAll("[data-testid='technology-complexity-table-table'] tbody tr")
            .ToDictionary(
                row => row.QuerySelector(".tech-overview__name")!.TextContent.Trim(),
                row => row.QuerySelectorAll("td").Skip(1).Select(cell => cell.TextContent.Trim()).ToArray());

        Assert.Equal(13, rows.Count);
        Assert.Equal(["4", "0", "Foundations"], rows["N0"]);
        Assert.Equal(["4", "5", "Hubs"], rows["N5"]);
        Assert.Equal(["0", "6", "Deep leaves"], rows["Alpha"]);
        Assert.Equal(["1", "4", "Leaves"], rows["N4"]);
        Assert.Equal(["0", "1", "Leaves"], rows["X1"]);
    }

    [Fact]
    public void The_quadrants_are_named_with_their_meaning()
    {
        var component = Render();

        var quadrants = component.FindAll("[data-testid='technology-complexity-quadrant']");

        Assert.Equal(["deep-leaves", "hubs", "leaves", "foundations"], quadrants.Select(quadrant => quadrant.GetAttribute("data-quadrant")).ToArray());
        Assert.Equal(["Deep leaves", "Hubs", "Leaves", "Foundations"], quadrants.Select(quadrant => quadrant.QuerySelector(".tech-complexity__quadrant-name")!.TextContent).ToArray());
        Assert.Equal("Load-bearing. Upgrade on purpose, never in passing.", quadrants[3].QuerySelector(".tech-complexity__quadrant-meaning")!.TextContent);
    }

    [Fact]
    public void An_empty_quadrant_says_none_today()
    {
        // Without the four on N5, nothing is a hub: N5 keeps its depth but loses its users.
        var component = Render(withHubUsers: false);

        var hubs = component.Find("[data-testid='technology-complexity-quadrant'][data-quadrant='hubs']");
        Assert.Equal("Many users and a deep stack. None today.", hubs.QuerySelector(".tech-complexity__quadrant-meaning")!.TextContent);
    }

    [Fact]
    public void Technologies_at_one_place_merge_into_one_point_labelled_with_their_names()
    {
        var component = Render();

        var points = Points(component);

        Assert.Equal(8, points.Count);
        Assert.Equal("Delta · Alpha · Charlie +1 more", Point(component, 0, 6).QuerySelector(".tech-complexity__label")!.TextContent);
        Assert.Equal("X1 · X2 · X3", Point(component, 0, 1).QuerySelector(".tech-complexity__label")!.TextContent);
        Assert.Equal("N5", Point(component, 4, 5).QuerySelector(".tech-complexity__label")!.TextContent);
        Assert.Equal("Delta · Alpha · Charlie · Bravo: used by 0, rests on 6, Deep leaves", Point(component, 0, 6).GetAttribute("aria-label"));
    }

    [Fact]
    public void Used_by_is_square_root_scaled_across_and_rests_on_linear_up()
    {
        var component = Render();

        // The axis runs to 4 used by and to 7 rests on (one above the highest).
        Assert.Contains("--x: 50%", Point(component, 1, 4).GetAttribute("style"), StringComparison.Ordinal);
        Assert.Contains("--y: 57.14%", Point(component, 1, 4).GetAttribute("style"), StringComparison.Ordinal);
        Assert.Contains("--x: 100%", Point(component, 4, 0).GetAttribute("style"), StringComparison.Ordinal);
        Assert.Contains("--y: 0%", Point(component, 4, 0).GetAttribute("style"), StringComparison.Ordinal);
    }

    [Fact]
    public void Most_complex_lists_the_six_that_rest_on_most_ties_by_name()
    {
        var component = Render();

        Assert.Equal(["Alpha", "Bravo", "Charlie", "Delta", "N5", "N4"], HotspotNames(component, "most-complex"));
        Assert.Equal(["6", "6", "6", "6", "5", "4"], HotspotValues(component, "most-complex"));
    }

    [Fact]
    public void Widest_blast_radius_lists_the_five_most_used_ties_by_name()
    {
        var component = Render();

        Assert.Equal(["N0", "N5", "N1", "N2", "N3"], HotspotNames(component, "blast-radius"));
        Assert.Equal(["4", "4", "1", "1", "1"], HotspotValues(component, "blast-radius"));
    }

    [Fact]
    public void The_longest_chain_runs_from_the_deepest_down_to_a_foundation()
    {
        var component = Render();

        Assert.Equal("Longest chain · 6 hops", component.Find("#tech-complexity-chain").TextContent.Trim());
        Assert.Equal(
            ["Delta", "N5", "N4", "N3", "N2", "N1", "N0"],
            component.FindAll("[data-testid='technology-complexity-chain-row']").Select(row => row.TextContent.Trim()).ToArray());
    }

    [Fact]
    public void A_point_a_hotspot_a_chain_step_and_a_table_row_each_set_the_shared_selection()
    {
        var component = Render();

        Point(component, 4, 5).Click();
        component.FindAll("[data-testid='technology-complexity-hotspot']").First(row => row.TextContent.Contains("N0", StringComparison.Ordinal)).Click();
        component.FindAll("[data-testid='technology-complexity-chain-row']").Single(row => row.TextContent.Trim() == "N3").Click();
        component.FindAll("[data-testid='technology-complexity-row']").Single(row => row.TextContent.Contains("X2", StringComparison.Ordinal)).Click();

        Assert.Equal(["n5", "n0", "n3", "x2"], _picked);
    }

    [Fact]
    public void Pressing_a_merged_point_again_walks_through_its_technologies()
    {
        var component = Render();

        Point(component, 0, 6).Click();
        Assert.Equal("u1", _selected);

        component.Render(parameters => parameters.Add(view => view.SelectedId, _selected));
        Assert.Equal("true", Point(component, 0, 6).GetAttribute("aria-pressed"));
        Point(component, 0, 6).Click();
        Assert.Equal("u2", _selected);

        component.Render(parameters => parameters.Add(view => view.SelectedId, "u4"));
        Point(component, 0, 6).Click();
        Assert.Equal("u1", _selected);
    }

    [Fact]
    public void The_selection_shows_on_the_point_and_on_the_companion_row()
    {
        var component = Render(selected: "n0");

        Assert.Equal("true", Point(component, 4, 0).GetAttribute("aria-pressed"));
        Assert.Equal("false", Point(component, 4, 5).GetAttribute("aria-pressed"));
        var row = component.FindAll("[data-testid='technology-complexity-row']").Single(button => button.TextContent.Contains("N0", StringComparison.Ordinal));
        Assert.Equal("true", row.GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Colour_by_switches_the_points_hue_and_a_candidate_is_hollow()
    {
        var component = Render();

        Assert.Contains("tech-complexity__point--foundations", Point(component, 4, 0).ClassName, StringComparison.Ordinal);
        Assert.DoesNotContain("tech-complexity__point--hollow", Point(component, 4, 0).ClassName, StringComparison.Ordinal);
        Assert.Contains("tech-complexity__point--hollow", Point(component, 0, 1).ClassName, StringComparison.Ordinal);

        ColourButton(component, "type").Click();

        Assert.Equal("true", ColourButton(component, "type").GetAttribute("aria-pressed"));
        Assert.Contains("tech-complexity__point--family-runtime", Point(component, 4, 0).ClassName, StringComparison.Ordinal);
        Assert.Contains("tech-complexity__point--family-librarytoolandplugin", Point(component, 0, 1).ClassName, StringComparison.Ordinal);

        ColourButton(component, "layer").Click();

        Assert.Contains("tech-complexity__point--layer-1", Point(component, 4, 0).ClassName, StringComparison.Ordinal);
        Assert.Contains("tech-complexity__point--layer-2", Point(component, 0, 6).ClassName, StringComparison.Ordinal);
    }

    private IRenderedComponent<TechnologyComplexityView> Render(bool withHubUsers = true, string? selected = null)
    {
        _selected = selected;
        return _context.Render<TechnologyComplexityView>(parameters => parameters
            .Add(view => view.View, View(withHubUsers))
            .Add(view => view.SelectedId, selected)
            .Add(view => view.SelectedIdChanged, id =>
            {
                _selected = id;
                _picked.Add(id);
            }));
    }

    private static TechnologyDevbookView View(bool withHubUsers)
    {
        var nodes = new List<TechnologyGraphNode>();
        var edges = new List<TechnologyGraphEdge>();

        void Add(string id, string label, string type, string status, int layer, params string[] dependsOn)
        {
            nodes.Add(new TechnologyGraphNode(id, label, layer == 0 ? "Shared Technologies" : "Desktop Stack", type, status, string.Empty, LayerIndex: layer));
            edges.AddRange(dependsOn.Select(target => new TechnologyGraphEdge($"{id}->{target}", id, target, "depends on")));
        }

        Add("n0", "N0", "runtime", "adopted", 0);
        for (var index = 1; index <= 5; index++)
        {
            Add($"n{index}", $"N{index}", "framework", "adopted", 0, $"n{index - 1}");
        }

        if (withHubUsers)
        {
            Add("u1", "Delta", "framework", "adopted", 1, "n5");
            Add("u2", "Alpha", "framework", "adopted", 1, "n5");
            Add("u3", "Charlie", "framework", "adopted", 1, "n5");
            Add("u4", "Bravo", "framework", "adopted", 1, "n5");
        }

        Add("x1", "X1", "tool", "candidate", 1, "n0");
        Add("x2", "X2", "tool", "trial", 1, "n0");
        Add("x3", "X3", "tool", "candidate", 1, "n0");

        var location = new DevbookFolderLocation(".tech", true, null, "JSdotNet/Backlog", null, null);
        return TechnologyDevbookView.Unavailable(location) with { Graph = TechnologyGraphMeasures.Apply(nodes, edges) };
    }

    private static IReadOnlyList<AngleSharp.Dom.IElement> Points(IRenderedComponent<TechnologyComplexityView> component) =>
        component.FindAll("[data-testid='technology-complexity-point']");

    private static AngleSharp.Dom.IElement Point(IRenderedComponent<TechnologyComplexityView> component, int usedBy, int restsOn) =>
        component.Find($"[data-testid='technology-complexity-point'][data-used-by='{usedBy}'][data-rests-on='{restsOn}']");

    private static AngleSharp.Dom.IElement ColourButton(IRenderedComponent<TechnologyComplexityView> component, string colour) =>
        component.Find($"[data-testid='technology-complexity-colour'][data-colour='{colour}']");

    private static string[] HotspotNames(IRenderedComponent<TechnologyComplexityView> component, string list) =>
        [.. component.FindAll($"[data-testid='technology-complexity-{list}'] .tech-complexity__hotspot-name").Select(name => name.TextContent.Trim())];

    private static string[] HotspotValues(IRenderedComponent<TechnologyComplexityView> component, string list) =>
        [.. component.FindAll($"[data-testid='technology-complexity-{list}'] .tech-complexity__hotspot-value").Select(value => value.TextContent.Trim())];
}
