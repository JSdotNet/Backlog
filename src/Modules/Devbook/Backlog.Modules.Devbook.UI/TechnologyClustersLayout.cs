using System.Globalization;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>One layer file as the Clusters view draws it: its technologies,
/// boundary nodes left out, grouped by type family and then by name.</summary>
public sealed record ClustersLayer(string FileName, string Title, IReadOnlyList<TechnologyGraphNode> Technologies)
{
    /// <summary>The title without the word every layer title ends in.</summary>
    public string Label => TechnologyViews.LayerLabel(Title);
}

/// <summary>What a line on the map says: nothing in particular, a link between
/// layers, what the selected technology sits on, or what uses it.</summary>
public enum ClustersLineTone
{
    Quiet,
    Link,
    SitsOn,
    UsedBy,
}

/// <summary>A straight line on the map, in the stage's own units.</summary>
public sealed record ClustersLine(double X1, double Y1, double X2, double Y2, ClustersLineTone Tone, double Weight, bool Dashed)
{
    public double Length => Math.Sqrt(((X2 - X1) * (X2 - X1)) + ((Y2 - Y1) * (Y2 - Y1)));

    /// <summary>The angle from the start to the end, in degrees clockwise from the x axis.</summary>
    public double Angle => Math.Atan2(Y2 - Y1, X2 - X1) * 180 / Math.PI;
}

/// <summary>A count drawn on a line, centred on (<see cref="X"/>, <see cref="Y"/>).</summary>
public sealed record ClustersBadge(double X, double Y, int Count);

/// <summary>One technology as a dot in its layer's cluster, centred on (<see cref="X"/>, <see cref="Y"/>).</summary>
public sealed record ClustersDot(TechnologyGraphNode Node, double X, double Y, double Size);

/// <summary>One layer on the All layers map, centred on (<see cref="X"/>, <see cref="Y"/>).</summary>
public sealed record ClustersCluster(ClustersLayer Layer, double X, double Y, double Diameter, IReadOnlyList<ClustersDot> Dots);

public sealed record ClustersOverviewLayout(
    double Width,
    double Height,
    IReadOnlyList<ClustersCluster> Clusters,
    IReadOnlyList<ClustersLine> Lines,
    IReadOnlyList<ClustersBadge> Badges);

/// <summary>How a technology's pill reads against the selection.</summary>
public enum ClustersPillState
{
    Plain,
    Selected,
    SitsOn,
    UsedBy,
    Dimmed,
}

/// <summary>One technology of the open layer, its top-left corner at
/// (<see cref="X"/>, <see cref="Y"/>) and <see cref="TechnologyClustersLayout.PillWidth"/>
/// by <see cref="TechnologyClustersLayout.PillHeight"/> in size.</summary>
public sealed record ClustersPill(TechnologyGraphNode Node, double X, double Y, ClustersPillState State)
{
    public double CentreX => X + (TechnologyClustersLayout.PillWidth / 2);

    public double CentreY => Y + (TechnologyClustersLayout.PillHeight / 2);
}

/// <summary>How a satellite reads against the selection: plain with nothing
/// selected, lit by what links to it, or dimmed when nothing does.</summary>
public enum ClustersSatelliteState
{
    Plain,
    SitsOn,
    UsedBy,
    Both,
    Dimmed,
}

/// <summary>Another layer, beside the open one, centred on (<see cref="X"/>, <see cref="Y"/>).</summary>
public sealed record ClustersSatellite(ClustersLayer Layer, double X, double Y, int Links, ClustersSatelliteState State);

/// <summary>A rectangle, by its top-left corner and size.</summary>
public sealed record ClustersRect(double X, double Y, double Width, double Height);

public sealed record ClustersLayerLayout(
    double Width,
    double Height,
    ClustersLayer Layer,
    ClustersRect Halo,
    IReadOnlyList<ClustersPill> Pills,
    IReadOnlyList<ClustersSatellite> Satellites,
    IReadOnlyList<ClustersLine> Lines);

/// <summary>
/// The geometry of the Clusters view (<c>.devbook/domain/devbook/features.md#technology-view</c>),
/// worked out here from the graph rather than measured in the browser, so the map
/// needs no script: every position is in the stage's own units, and the view turns
/// them into percentages of a stage that keeps the same aspect ratio, so a line
/// keeps its angle at any width.
///
/// <para>Two maps. <see cref="Overview"/> is every layer as a cluster on a ring,
/// sized by how many technologies it holds, with the cross-layer <c>depends-on</c>
/// counts as weighted lines between them. <see cref="Layer"/> is one layer opened:
/// its technologies as a grid of pills, the other layers as satellites on a ring
/// around it, and the lines the selection draws.</para>
/// </summary>
public static class TechnologyClustersLayout
{
    public const double Width = 1000;

    public const double OverviewHeight = 760;

    public const double PillWidth = 140;

    public const double PillHeight = 36;

    public const double SatelliteDiameter = 72;

    /// <summary>The grid a layer's pills are laid on: the step across and down.</summary>
    public const double ColumnStep = 155;

    public const double RowStep = 48;

    public const int MaxColumns = 4;

    private const double RingRadiusX = 340;

    private const double RingRadiusY = 250;

    private const double MinClusterDiameter = 64;

    private const double MaxClusterDiameter = 180;

    private const double SatelliteRingX = 420;

    private const double MinSatelliteRingY = 300;

    private const double SatelliteMargin = 80;

    private const double SelectionWeight = 2.5;

    private const double QuietWeight = 1.5;

    /// <summary>The folder's layers in reading order, each with its own
    /// technologies; a layer file holding none still has its cluster.</summary>
    public static IReadOnlyList<ClustersLayer> Layers(TechnologyDevbookView view) =>
        [.. view.Layers.Select(layer => new ClustersLayer(
            layer.FileName,
            layer.Title,
            [.. view.Graph.Nodes
                .Where(node => !node.IsBoundary && string.Equals(node.LayerFileName, layer.FileName, StringComparison.Ordinal))
                .OrderBy(node => node.Family)
                .ThenBy(node => node.Label, StringComparer.CurrentCultureIgnoreCase)]))];

    /// <summary>A line's weight for the number of links it stands for: thicker with
    /// more, capped so one busy pair cannot drown the map.</summary>
    public static double WeightFor(int links) => Math.Min(7, QuietWeight + (links / 3.0));

    /// <summary>A cluster's diameter for the technologies it holds, by the square
    /// root so area rather than width follows the count.</summary>
    public static double DiameterFor(int technologies, double max = MaxClusterDiameter) =>
        Math.Min(max, Math.Max(MinClusterDiameter, 40 + (25 * Math.Sqrt(technologies))));

    /// <summary>The cross-layer <c>depends-on</c> edges between each pair of the
    /// given layers, either direction, keyed by the pair's file names in reading order.</summary>
    public static IReadOnlyDictionary<(string First, string Second), int> PairCounts(
        IReadOnlyList<ClustersLayer> layers,
        IReadOnlyList<TechnologyLayerLink> links)
    {
        // Two files under one title cannot be told apart in the links, which name a
        // layer by its title; the first in reading order takes them.
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < layers.Count; index++)
        {
            order.TryAdd(layers[index].Title, index);
        }

        var counts = new Dictionary<(string, string), int>();

        foreach (var link in links)
        {
            if (!order.TryGetValue(link.FromLayer, out var from) || !order.TryGetValue(link.ToLayer, out var to) || from == to) continue;

            var key = from < to ? (layers[from].FileName, layers[to].FileName) : (layers[to].FileName, layers[from].FileName);
            counts[key] = counts.GetValueOrDefault(key) + link.Count;
        }

        return counts;
    }

    /// <summary>Every layer as a cluster on a ring, the first at the top and the
    /// rest clockwise in reading order; a folder of one layer sits it in the centre.</summary>
    public static ClustersOverviewLayout Overview(IReadOnlyList<ClustersLayer> layers, IReadOnlyList<TechnologyLayerLink> links)
    {
        const double centreX = Width / 2;
        const double centreY = OverviewHeight / 2;

        // Clusters shrink on a crowded ring, so neighbours never overlap.
        var perimeter = 2 * Math.PI * Math.Sqrt(((RingRadiusX * RingRadiusX) + (RingRadiusY * RingRadiusY)) / 2);
        var maxDiameter = layers.Count <= 1 ? MaxClusterDiameter : Math.Min(MaxClusterDiameter, perimeter / layers.Count * 0.8);

        var clusters = new List<ClustersCluster>(layers.Count);
        for (var index = 0; index < layers.Count; index++)
        {
            var (x, y) = layers.Count == 1
                ? (centreX, centreY)
                : OnEllipse(centreX, centreY, RingRadiusX, RingRadiusY, index, layers.Count);
            var diameter = DiameterFor(layers[index].Technologies.Count, maxDiameter);
            clusters.Add(new ClustersCluster(layers[index], Round(x), Round(y), Round(diameter), Dots(layers[index], x, y, diameter)));
        }

        var byFile = clusters.ToDictionary(cluster => cluster.Layer.FileName, StringComparer.Ordinal);
        var lines = new List<ClustersLine>();
        var badges = new List<ClustersBadge>();
        foreach (var ((first, second), count) in PairCounts(layers, links))
        {
            var a = byFile[first];
            var b = byFile[second];
            lines.Add(new ClustersLine(a.X, a.Y, b.X, b.Y, ClustersLineTone.Link, WeightFor(count), false));
            badges.Add(new ClustersBadge(Round((a.X + b.X) / 2), Round((a.Y + b.Y) / 2), count));
        }

        return new ClustersOverviewLayout(Width, OverviewHeight, clusters, lines, badges);
    }

    /// <summary>One layer opened: its pills in a grid of at most four columns, the
    /// other layers as satellites on a ring around it, and the lines.
    ///
    /// <para>With nothing selected the lines are the layer's own <c>depends-on</c>
    /// edges, quiet, and one weighted line to each satellite it links to. With
    /// <paramref name="selected"/> one of its technologies, the lines are that
    /// technology's: gold to what it sits on, blue to what uses it. A link inside the
    /// layer goes to the pill; a link to another layer goes to its satellite, dashed
    /// when that layer holds both. Everything the selection does not reach dims.</para></summary>
    public static ClustersLayerLayout Layer(
        IReadOnlyList<ClustersLayer> layers,
        ClustersLayer layer,
        TechnologyGraphNode? selected,
        Func<string, TechnologyGraphNode?> nodeById)
    {
        var count = layer.Technologies.Count;
        var columns = Math.Max(1, Math.Min(MaxColumns, count));
        var rows = Math.Max(1, (int)Math.Ceiling(count / (double)columns));
        var gridWidth = (columns * ColumnStep) - (ColumnStep - PillWidth);
        var gridHeight = (rows * RowStep) - (RowStep - PillHeight);

        // The halo is the grid plus a margin and a title line; the satellite ring
        // clears it with a satellite's own width to spare.
        var haloHeight = gridHeight + 76;
        var ringY = Math.Max(MinSatelliteRingY, (haloHeight / 2) + SatelliteDiameter);
        var height = (2 * ringY) + (2 * SatelliteMargin);
        const double centreX = Width / 2;
        var centreY = height / 2;

        var gridLeft = Round(centreX - (gridWidth / 2));
        var gridTop = Round(centreY - (gridHeight / 2) + 14);
        var halo = new ClustersRect(gridLeft - 24, gridTop - 52, gridWidth + 48, haloHeight);

        var focus = selected is { IsBoundary: false } && layer.Technologies.Any(node => SameId(node.Id, selected.Id)) ? selected : null;
        var sitsOn = focus?.DependencyIds.ToHashSet(StringComparer.Ordinal) ?? [];
        var usedBy = focus?.UserIds.ToHashSet(StringComparer.Ordinal) ?? [];

        var pills = new List<ClustersPill>(count);
        for (var index = 0; index < count; index++)
        {
            var node = layer.Technologies[index];
            var state = focus is null ? ClustersPillState.Plain
                : SameId(node.Id, focus.Id) ? ClustersPillState.Selected
                : sitsOn.Contains(node.Id) ? ClustersPillState.SitsOn
                : usedBy.Contains(node.Id) ? ClustersPillState.UsedBy
                : ClustersPillState.Dimmed;
            pills.Add(new ClustersPill(node, gridLeft + (index % columns * ColumnStep), gridTop + (index / columns * RowStep), state));
        }

        var pillById = pills.ToDictionary(pill => pill.Node.Id, StringComparer.Ordinal);
        var layerOf = layers
            .SelectMany(each => each.Technologies.Select(node => (node.Id, Layer: each)))
            .ToDictionary(pair => pair.Id, pair => pair.Layer, StringComparer.Ordinal);

        // Per other layer: how many of the links counted run down into it, and how many come up from it.
        var satelliteLinks = new Dictionary<string, (int SitsOn, int UsedBy)>(StringComparer.Ordinal);
        var lines = new List<ClustersLine>();

        if (focus is not null)
        {
            var from = pillById[focus.Id];
            foreach (var (ids, tone) in new[] { (focus.DependencyIds, ClustersLineTone.SitsOn), (focus.UserIds, ClustersLineTone.UsedBy) })
            {
                foreach (var id in ids)
                {
                    if (pillById.TryGetValue(id, out var to))
                    {
                        // A technology at both ends — a cycle — gets one dashed line, drawn once.
                        var both = sitsOn.Contains(id) && usedBy.Contains(id);
                        if (both && tone == ClustersLineTone.UsedBy) continue;

                        lines.Add(new ClustersLine(from.CentreX, from.CentreY, to.CentreX, to.CentreY, tone, SelectionWeight, both));
                    }
                    else if (layerOf.TryGetValue(id, out var other))
                    {
                        var (down, up) = satelliteLinks.GetValueOrDefault(other.FileName);
                        satelliteLinks[other.FileName] = tone == ClustersLineTone.SitsOn ? (down + 1, up) : (down, up + 1);
                    }
                }
            }
        }
        else
        {
            foreach (var node in layer.Technologies)
            {
                var from = pillById[node.Id];
                foreach (var id in node.DependencyIds)
                {
                    if (pillById.TryGetValue(id, out var to))
                    {
                        lines.Add(new ClustersLine(from.CentreX, from.CentreY, to.CentreX, to.CentreY, ClustersLineTone.Quiet, QuietWeight, false));
                    }
                    else if (layerOf.TryGetValue(id, out var other))
                    {
                        var (down, up) = satelliteLinks.GetValueOrDefault(other.FileName);
                        satelliteLinks[other.FileName] = (down + 1, up);
                    }
                }

                foreach (var id in node.UserIds)
                {
                    if (!pillById.ContainsKey(id) && layerOf.TryGetValue(id, out var other))
                    {
                        var (down, up) = satelliteLinks.GetValueOrDefault(other.FileName);
                        satelliteLinks[other.FileName] = (down, up + 1);
                    }
                }
            }
        }

        var others = layers.Where(each => !string.Equals(each.FileName, layer.FileName, StringComparison.Ordinal)).ToList();
        var satellites = new List<ClustersSatellite>(others.Count);
        var haloCentreY = gridTop + (gridHeight / 2);
        for (var index = 0; index < others.Count; index++)
        {
            var other = others[index];
            var (x, y) = OnRectangle(centreX, centreY, SatelliteRingX, ringY, index, others.Count);
            x = Round(x);
            y = Round(y);
            var (down, up) = satelliteLinks.GetValueOrDefault(other.FileName);
            var links = down + up;

            ClustersSatelliteState state;
            if (focus is null)
            {
                state = ClustersSatelliteState.Plain;
                if (links > 0) lines.Add(new ClustersLine(centreX, haloCentreY, x, y, ClustersLineTone.Link, WeightFor(links), false));
            }
            else if (links == 0)
            {
                state = ClustersSatelliteState.Dimmed;
            }
            else
            {
                var from = pillById[focus.Id];
                state = down > 0 && up > 0 ? ClustersSatelliteState.Both : down > 0 ? ClustersSatelliteState.SitsOn : ClustersSatelliteState.UsedBy;
                lines.Add(new ClustersLine(
                    from.CentreX,
                    from.CentreY,
                    x,
                    y,
                    down > 0 ? ClustersLineTone.SitsOn : ClustersLineTone.UsedBy,
                    SelectionWeight,
                    state == ClustersSatelliteState.Both));
            }

            satellites.Add(new ClustersSatellite(other, x, y, links, state));
        }

        return new ClustersLayerLayout(Width, height, layer, halo, pills, satellites, lines);
    }

    /// <summary>A number as CSS reads it: invariant, at most two decimals.</summary>
    public static string Css(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A stage coordinate as a percentage of the stage's extent.</summary>
    public static string Percent(double value, double extent) => Css(value / extent * 100) + "%";

    private static bool SameId(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

    private static double Round(double value) => Math.Round(value, 2);

    /// <summary>The dots inside a cluster, in rows centred on its middle, small
    /// enough that the block fits inside the circle.</summary>
    private static IReadOnlyList<ClustersDot> Dots(ClustersLayer layer, double centreX, double centreY, double diameter)
    {
        var count = layer.Technologies.Count;
        if (count == 0) return [];

        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (int)Math.Ceiling(count / (double)columns);
        var cell = Math.Min(20, diameter * 0.68 / Math.Max(columns, rows));
        var size = Round(cell * 0.8);
        var top = centreY - (rows * cell / 2);

        var dots = new List<ClustersDot>(count);
        for (var index = 0; index < count; index++)
        {
            var row = index / columns;
            var inRow = Math.Min(columns, count - (row * columns));
            var left = centreX - (inRow * cell / 2);
            dots.Add(new ClustersDot(layer.Technologies[index], Round(left + ((index % columns) + 0.5) * cell), Round(top + (row + 0.5) * cell), size));
        }

        return dots;
    }

    /// <summary>The <paramref name="index"/>th of <paramref name="count"/> points
    /// spaced evenly round an ellipse, the first at the top.</summary>
    private static (double X, double Y) OnEllipse(double centreX, double centreY, double radiusX, double radiusY, int index, int count)
    {
        var angle = (-Math.PI / 2) + (2 * Math.PI * index / count);
        return (centreX + (radiusX * Math.Cos(angle)), centreY + (radiusY * Math.Sin(angle)));
    }

    /// <summary>The <paramref name="index"/>th of <paramref name="count"/> points
    /// spaced evenly round a rectangle's edge, the first at the top centre and the
    /// rest clockwise. A rectangle rather than an ellipse, because the grid it rings
    /// is a rectangle: an ellipse round it cuts its corners.</summary>
    private static (double X, double Y) OnRectangle(double centreX, double centreY, double halfWidth, double halfHeight, int index, int count)
    {
        var perimeter = 4 * (halfWidth + halfHeight);
        var distance = perimeter * index / Math.Max(1, count);

        // From the top centre, clockwise: half the top, the right side, the bottom,
        // the left side, and the other half of the top.
        if (distance <= halfWidth) return (centreX + distance, centreY - halfHeight);
        distance -= halfWidth;
        if (distance <= 2 * halfHeight) return (centreX + halfWidth, centreY - halfHeight + distance);
        distance -= 2 * halfHeight;
        if (distance <= 2 * halfWidth) return (centreX + halfWidth - distance, centreY + halfHeight);
        distance -= 2 * halfWidth;
        if (distance <= 2 * halfHeight) return (centreX - halfWidth, centreY + halfHeight - distance);
        distance -= 2 * halfHeight;
        return (centreX - halfWidth + distance, centreY - halfHeight);
    }
}
