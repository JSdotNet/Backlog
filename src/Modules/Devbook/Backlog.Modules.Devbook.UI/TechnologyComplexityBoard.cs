using System.Globalization;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The four corners of the Complexity scatter
/// (<c>.devbook/domain/devbook/features.md#technology-view</c>): how many technologies
/// use one against how much sits under it.
/// </summary>
public enum TechnologyQuadrant
{
    /// <summary>Rests on a lot, nothing much rests on it.</summary>
    DeepLeaves,

    /// <summary>Many users and a deep stack.</summary>
    Hubs,

    /// <summary>Many users, little underneath.</summary>
    Foundations,

    /// <summary>Few users, little underneath.</summary>
    Leaves,
}

public static class TechnologyQuadrants
{
    /// <summary>Used by at or above this is the right-hand half.</summary>
    public const double UsedBySplit = 3.5;

    /// <summary>Rests on at or above this is the upper half.</summary>
    public const double RestsOnSplit = 4.5;

    /// <summary>The order the quadrants are listed in: the board's reading order,
    /// top left, top right, bottom right, bottom left.</summary>
    public static IReadOnlyList<TechnologyQuadrant> All { get; } =
        [TechnologyQuadrant.DeepLeaves, TechnologyQuadrant.Hubs, TechnologyQuadrant.Foundations, TechnologyQuadrant.Leaves];

    public static TechnologyQuadrant Of(int usedBy, int restsOn) => restsOn >= RestsOnSplit
        ? usedBy >= UsedBySplit ? TechnologyQuadrant.Hubs : TechnologyQuadrant.DeepLeaves
        : usedBy >= UsedBySplit ? TechnologyQuadrant.Foundations : TechnologyQuadrant.Leaves;

    public static TechnologyQuadrant Of(TechnologyGraphNode node) => Of(node.UsedBy, node.RestsOn);

    public static string Label(TechnologyQuadrant quadrant) => quadrant switch
    {
        TechnologyQuadrant.DeepLeaves => "Deep leaves",
        TechnologyQuadrant.Hubs => "Hubs",
        TechnologyQuadrant.Foundations => "Foundations",
        _ => "Leaves",
    };

    /// <summary>The quadrant's one line, as the design words it.</summary>
    public static string Meaning(TechnologyQuadrant quadrant) => quadrant switch
    {
        TechnologyQuadrant.DeepLeaves => "Rest on a lot, nothing rests on them. They break when anything under them moves.",
        TechnologyQuadrant.Hubs => "Many users and a deep stack.",
        TechnologyQuadrant.Foundations => "Load-bearing. Upgrade on purpose, never in passing.",
        _ => "Cheap to change or replace.",
    };

    /// <summary>The class suffix a quadrant is coloured by.</summary>
    public static string Slug(TechnologyQuadrant quadrant) => quadrant switch
    {
        TechnologyQuadrant.DeepLeaves => "deep-leaves",
        TechnologyQuadrant.Hubs => "hubs",
        TechnologyQuadrant.Foundations => "foundations",
        _ => "leaves",
    };
}

/// <summary>
/// One point on the scatter: every technology at the same <em>used by</em> and
/// <em>rests on</em>, in graph order. Two technologies at one place would hide
/// each other, so they are drawn as one point labelled with both names.
/// </summary>
public sealed record TechnologyComplexityPoint(int UsedBy, int RestsOn, IReadOnlyList<TechnologyGraphNode> Members)
{
    public TechnologyQuadrant Quadrant => TechnologyQuadrants.Of(UsedBy, RestsOn);

    /// <summary>How many names a point prints before it says how many more it holds.</summary>
    public const int NamesShown = 3;

    /// <summary>Every member's name, joined the way the design joins them.</summary>
    public string Label => string.Join(" · ", Members.Select(member => member.Label));

    /// <summary>The label the plot prints: the first <see cref="NamesShown"/> names
    /// and a count of the rest, so a crowded place — the many technologies nothing
    /// uses — does not run one line across every other point.</summary>
    public string ShortLabel => Members.Count <= NamesShown
        ? Label
        : $"{string.Join(" · ", Members.Take(NamesShown).Select(member => member.Label))} +{Members.Count - NamesShown} more";

    /// <summary>Filled while any member is adopted: a merged point is hollow only
    /// when every technology on it is still a candidate or a trial.</summary>
    public bool IsAdopted => Members.Any(member => member.IsAdopted);

    public bool Contains(string? id) => id is not null && Members.Any(member => string.Equals(member.Id, id, StringComparison.Ordinal));

    /// <summary>The member a press selects: the first, or — when a member is
    /// already selected — the one after it, so pressing a merged point again walks
    /// through every technology on it.</summary>
    public string NextId(string? selectedId)
    {
        var index = -1;
        for (var position = 0; position < Members.Count; position++)
        {
            if (string.Equals(Members[position].Id, selectedId, StringComparison.Ordinal)) index = position;
        }

        return Members[(index + 1) % Members.Count].Id;
    }
}

/// <summary>A tick under the horizontal axis: the value and how far along it sits.</summary>
public sealed record TechnologyComplexityTick(int Value, double Percent);

/// <summary>
/// What the Complexity board draws, worked out from the measures the graph already
/// carries: the points, where each sits, the hotspot lists and the axis.
/// Nothing here counts the stack again.
/// </summary>
public static class TechnologyComplexityBoard
{
    /// <summary>The most complex list's length.</summary>
    public const int MostComplexCount = 6;

    /// <summary>The widest blast radius list's length.</summary>
    public const int WidestBlastRadiusCount = 5;

    /// <summary>Technologies at identical coordinates merged into one point, the
    /// points ordered top to bottom, then left to right.</summary>
    public static IReadOnlyList<TechnologyComplexityPoint> Points(IEnumerable<TechnologyGraphNode> technologies) =>
        [.. technologies
            .GroupBy(node => (node.UsedBy, node.RestsOn))
            .Select(group => new TechnologyComplexityPoint(group.Key.UsedBy, group.Key.RestsOn, [.. group]))
            .OrderByDescending(point => point.RestsOn)
            .ThenBy(point => point.UsedBy)];

    /// <summary>The technologies that rest on the most, most first; a tie reads by name.</summary>
    public static IReadOnlyList<TechnologyGraphNode> MostComplex(IEnumerable<TechnologyGraphNode> technologies) =>
        [.. technologies
            .OrderByDescending(node => node.RestsOn)
            .ThenBy(node => node.Label, StringComparer.CurrentCultureIgnoreCase)
            .Take(MostComplexCount)];

    /// <summary>The technologies the most others use directly, most first; a tie reads by name.</summary>
    public static IReadOnlyList<TechnologyGraphNode> WidestBlastRadius(IEnumerable<TechnologyGraphNode> technologies) =>
        [.. technologies
            .OrderByDescending(node => node.UsedBy)
            .ThenBy(node => node.Label, StringComparer.CurrentCultureIgnoreCase)
            .Take(WidestBlastRadiusCount)];

    /// <summary>The right end of the horizontal axis: the largest used by, but never
    /// so short that the quadrant split falls off it.</summary>
    public static int UsedByMax(IEnumerable<TechnologyGraphNode> technologies) =>
        Math.Max(technologies.Select(node => node.UsedBy).DefaultIfEmpty(0).Max(), (int)Math.Ceiling(TechnologyQuadrants.UsedBySplit));

    /// <summary>The top of the vertical axis: one above the largest rests on, so the
    /// highest point is not drawn on the frame, and never below the quadrant split.</summary>
    public static int RestsOnMax(IEnumerable<TechnologyGraphNode> technologies) =>
        Math.Max(technologies.Select(node => node.RestsOn).DefaultIfEmpty(0).Max() + 1, (int)Math.Ceiling(TechnologyQuadrants.RestsOnSplit));

    /// <summary>How far across a used by sits, square-root scaled so the long tail of
    /// single-user technologies stays apart.</summary>
    public static double X(double usedBy, int usedByMax) => usedByMax <= 0 ? 0 : Math.Sqrt(Math.Max(usedBy, 0)) / Math.Sqrt(usedByMax) * 100;

    /// <summary>How far up a rests on sits, linearly.</summary>
    public static double Y(double restsOn, int restsOnMax) => restsOnMax <= 0 ? 0 : Math.Max(restsOn, 0) / restsOnMax * 100;

    /// <summary>Past this far across, a point hangs its label to its left.</summary>
    public const double LabelStartFrom = 70;

    /// <summary>
    /// How much of the plot's width each point's label may take before it would
    /// reach the next point on its row: up to the next point to the right, or —
    /// for a label hung to the left — back to the one before it, else to the plot's
    /// edge. Points on one row sit as close as one used by apart, so an unbounded
    /// label would print over its neighbour; the view truncates it to this room and
    /// shows it whole while the point is hovered, focused or selected.
    /// </summary>
    public static IReadOnlyDictionary<TechnologyComplexityPoint, double> LabelRoom(IReadOnlyList<TechnologyComplexityPoint> points, int usedByMax)
    {
        var room = new Dictionary<TechnologyComplexityPoint, double>();
        foreach (var row in points.GroupBy(point => point.RestsOn))
        {
            var across = row.Select(point => (Point: point, X: X(point.UsedBy, usedByMax))).OrderBy(entry => entry.X).ToList();
            for (var index = 0; index < across.Count; index++)
            {
                var x = across[index].X;
                var space = x > LabelStartFrom
                    ? x - (index > 0 ? across[index - 1].X : 0)
                    : (index < across.Count - 1 ? across[index + 1].X : 100) - x;
                room[across[index].Point] = Math.Max(space - 1, 3);
            }
        }

        return room;
    }

    /// <summary>0, the powers of two, and the end of the axis; a power too close to
    /// the end to print beside it is left out.</summary>
    public static IReadOnlyList<TechnologyComplexityTick> Ticks(int usedByMax)
    {
        var ticks = new List<TechnologyComplexityTick> { new(0, 0) };
        for (var value = 1; value < usedByMax && X(value, usedByMax) <= 85; value *= 2)
        {
            ticks.Add(new(value, X(value, usedByMax)));
        }

        ticks.Add(new(usedByMax, 100));
        return ticks;
    }

    /// <summary>A percentage as CSS writes it, whatever the reader's culture.</summary>
    public static string Css(double percent) => percent.ToString("0.##", CultureInfo.InvariantCulture) + "%";
}
