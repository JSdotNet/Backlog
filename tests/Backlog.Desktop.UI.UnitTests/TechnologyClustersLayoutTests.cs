namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The geometry behind the Clusters view: worked out in C# from the graph, so these
/// read it without a browser.
/// </summary>
public sealed class TechnologyClustersLayoutTests
{
    [Fact]
    public void A_cluster_grows_with_its_technologies_within_its_bounds()
    {
        Assert.True(TechnologyClustersLayout.DiameterFor(4) < TechnologyClustersLayout.DiameterFor(12));
        Assert.Equal(64, TechnologyClustersLayout.DiameterFor(0));
        Assert.Equal(180, TechnologyClustersLayout.DiameterFor(400));
    }

    [Fact]
    public void A_line_thickens_with_its_links_and_stops_at_seven()
    {
        Assert.Equal(1.5 + (1 / 3.0), TechnologyClustersLayout.WeightFor(1), 3);
        Assert.True(TechnologyClustersLayout.WeightFor(6) > TechnologyClustersLayout.WeightFor(3));
        Assert.Equal(7, TechnologyClustersLayout.WeightFor(100));
    }

    [Fact]
    public void Pair_counts_add_both_directions_between_two_layers()
    {
        var (layers, _) = Stack();
        var links = new[]
        {
            new TechnologyLayerLink("Desktop", "Shared", 2),
            new TechnologyLayerLink("Shared", "Desktop", 1),
            new TechnologyLayerLink("Testing", "Shared", 1),
            new TechnologyLayerLink("Elsewhere", "Shared", 4),
        };

        var counts = TechnologyClustersLayout.PairCounts(layers, links);

        Assert.Equal(3, counts[("shared.md", "desktop.md")]);
        Assert.Equal(1, counts[("shared.md", "testing.md")]);
        Assert.Equal(2, counts.Count);
    }

    [Fact]
    public void The_overview_puts_every_cluster_on_the_stage_without_overlap()
    {
        var (layers, _) = Stack();

        var map = TechnologyClustersLayout.Overview(layers, [new TechnologyLayerLink("Desktop", "Shared", 2)]);

        Assert.Equal(3, map.Clusters.Count);
        foreach (var cluster in map.Clusters)
        {
            Assert.InRange(cluster.X - (cluster.Diameter / 2), 0, map.Width);
            Assert.InRange(cluster.X + (cluster.Diameter / 2), 0, map.Width);
            Assert.InRange(cluster.Y - (cluster.Diameter / 2), 0, map.Height);
            Assert.InRange(cluster.Y + (cluster.Diameter / 2), 0, map.Height);
            Assert.Equal(cluster.Layer.Technologies.Count, cluster.Dots.Count);
        }

        for (var a = 0; a < map.Clusters.Count; a++)
        {
            for (var b = a + 1; b < map.Clusters.Count; b++)
            {
                var (first, second) = (map.Clusters[a], map.Clusters[b]);
                var distance = Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));
                Assert.True(distance > (first.Diameter + second.Diameter) / 2, $"{first.Layer.Label} overlaps {second.Layer.Label}");
            }
        }

        var line = Assert.Single(map.Lines);
        Assert.Equal(TechnologyClustersLayout.WeightFor(2), line.Weight);
        Assert.Equal(2, Assert.Single(map.Badges).Count);
    }

    [Fact]
    public void A_satellite_holding_both_ends_gets_one_dashed_line_from_the_selection()
    {
        var (layers, nodes) = Stack();
        var desktop = layers[1];
        var blazor = nodes["blazor"];

        var map = TechnologyClustersLayout.Layer(layers, desktop, blazor, id => nodes.Values.FirstOrDefault(node => node.Id == id));

        var shared = map.Satellites.Single(satellite => satellite.Layer.FileName == "shared.md");
        Assert.Equal(ClustersSatelliteState.Both, shared.State);
        Assert.Equal(3, shared.Links);

        var testing = map.Satellites.Single(satellite => satellite.Layer.FileName == "testing.md");
        Assert.Equal(ClustersSatelliteState.Dimmed, testing.State);

        var toShared = map.Lines.Single(line => line.X2 == shared.X && line.Y2 == shared.Y);
        Assert.True(toShared.Dashed);
        Assert.Equal(ClustersLineTone.SitsOn, toShared.Tone);

        // Same-layer links go to the pill: Playwright uses Blazor.
        var playwright = map.Pills.Single(pill => pill.Node.Id == "playwright");
        Assert.Equal(ClustersPillState.UsedBy, playwright.State);
        Assert.Contains(map.Lines, line => line.Tone == ClustersLineTone.UsedBy && line.X2 == playwright.CentreX && line.Y2 == playwright.CentreY);
    }

    [Fact]
    public void Satellites_ring_the_grid_without_touching_it_and_stay_on_the_stage()
    {
        var many = Enumerable.Range(0, 30).Select(index => Node($"t{index}", "Big", "big.md")).ToList();
        var layers = new List<ClustersLayer> { new("big.md", "Big", many) };
        layers.AddRange(Enumerable.Range(0, 9).Select(index => new ClustersLayer($"l{index}.md", $"L{index}", [])));

        var map = TechnologyClustersLayout.Layer(layers, layers[0], null, _ => null);

        Assert.Equal(9, map.Satellites.Count);
        var radius = TechnologyClustersLayout.SatelliteDiameter / 2;
        foreach (var satellite in map.Satellites)
        {
            Assert.InRange(satellite.X - radius, 0, map.Width);
            Assert.InRange(satellite.X + radius, 0, map.Width);
            Assert.InRange(satellite.Y - radius, 0, map.Height);
            Assert.InRange(satellite.Y + radius, 0, map.Height);

            var clearsX = satellite.X + radius < map.Halo.X || satellite.X - radius > map.Halo.X + map.Halo.Width;
            var clearsY = satellite.Y + radius < map.Halo.Y || satellite.Y - radius > map.Halo.Y + map.Halo.Height;
            Assert.True(clearsX || clearsY, $"{satellite.Layer.Label} sits on the grid");
        }

        Assert.All(map.Pills, pill => Assert.Equal(ClustersPillState.Plain, pill.State));
        Assert.Equal(4, map.Pills.Select(pill => pill.X).Distinct().Count());
    }

    [Fact]
    public void Positions_are_written_for_css_whatever_the_culture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("nl-NL");
            Assert.Equal("12.5%", TechnologyClustersLayout.Percent(125, 1000));
            Assert.Equal("-33.33", TechnologyClustersLayout.Css(-33.3333));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    /// <summary>Shared holds .NET and Razor; Desktop holds Blazor, on Razor and .NET,
    /// and Playwright on Blazor; Testing holds bUnit. Razor also sits on Blazor here,
    /// so Shared holds both ends of Blazor's links.</summary>
    private static (IReadOnlyList<ClustersLayer> Layers, Dictionary<string, TechnologyGraphNode> Nodes) Stack()
    {
        var nodes = new Dictionary<string, TechnologyGraphNode>
        {
            ["net"] = Node("net", "Shared", "shared.md"),
            ["razor"] = Node("razor", "Shared", "shared.md", dependencies: ["net", "blazor"]),
            ["blazor"] = Node("blazor", "Desktop", "desktop.md", dependencies: ["razor", "net"], users: ["playwright", "razor"]),
            ["playwright"] = Node("playwright", "Desktop", "desktop.md", dependencies: ["blazor"]),
            ["bunit"] = Node("bunit", "Testing", "testing.md"),
        };

        IReadOnlyList<ClustersLayer> layers =
        [
            new("shared.md", "Shared", [nodes["net"], nodes["razor"]]),
            new("desktop.md", "Desktop", [nodes["blazor"], nodes["playwright"]]),
            new("testing.md", "Testing", [nodes["bunit"]]),
        ];

        return (layers, nodes);
    }

    private static TechnologyGraphNode Node(string id, string layer, string file, string[]? dependencies = null, string[]? users = null) =>
        new(id, id, layer, "framework", "adopted", string.Empty, file)
        {
            DependencyIds = dependencies ?? [],
            UserIds = users ?? [],
        };
}
