namespace Backlog.UI.Components.Roadmap;

/// <summary>
/// A band of rows down the left of <see cref="RoadmapTimeline"/>, and the one
/// place a colour is chosen for everything sitting in it.
/// <para>
/// What a group <em>is</em> stays outside. It has been a department, a product
/// area, a person's employer and a set of repositories, and the timeline can
/// tell none of those apart — it holds groups in the order it was handed them
/// and draws the rows under each. A component that knew a group was an "area"
/// would have to be reopened the first time someone grouped by quarter owner.
/// </para>
/// </summary>
/// <param name="Id">Unique within the plan.</param>
/// <param name="Title">The label on the band.</param>
/// <param name="Rows">The rows inside it, in the order they are drawn. A group
/// with no rows takes up no vertical space and is left out.</param>
/// <param name="Color">Any CSS colour. Everything in the group is tinted from
/// it, so one value keeps a band separable from its neighbours.
/// <para>
/// The library declines to pick this. Its palette guide is explicit that product
/// code must not grow a second semantic palette, and six separable hues is
/// exactly that — so the colour arrives from the caller the same way the graph
/// explorer's legend colours do. No colour means a neutral band, which reads as
/// "nobody said", not as a seventh category.
/// </para></param>
public sealed record RoadmapGroup(
    string Id,
    string Title,
    IReadOnlyList<RoadmapRow> Rows,
    string? Color = null)
{
    public IReadOnlyList<RoadmapRow> RowList => Rows ?? [];
}

/// <summary>One line of the timeline: a name on the left, and a track to its
/// right that things are placed on by date.</summary>
/// <param name="Id">Unique across the whole plan, not just within the group —
/// a bar names the row it sits on and nothing else disambiguates it.</param>
/// <param name="Title">The label on the left.</param>
/// <param name="Kind">Whether the row carries bars or point markers. The
/// distinction is not cosmetic: a bar can be dragged onto a row of bars and must
/// not be dragged onto a row of milestones, because a milestone row has no
/// duration to give it.</param>
public sealed record RoadmapRow(
    string Id,
    string Title,
    RoadmapRowKind Kind = RoadmapRowKind.Bars);

/// <summary>What a row holds.</summary>
public enum RoadmapRowKind
{
    /// <summary>Spans with a start and an end.</summary>
    Bars,

    /// <summary>Points in time.</summary>
    Milestones
}

/// <summary>
/// One filterable fact about a bar, as a name and a value.
/// <para>
/// This is how the timeline filters on tag, repository and area without ever
/// learning those three words. The filter bar is built from whatever facet names
/// the bars actually carry, so a caller that files its work by tag, repository
/// and area gets those three controls, and a caller that files it by customer
/// and contract gets those two — with no change here.
/// </para>
/// <para>
/// A bar may carry the same <paramref name="Name"/> more than once; two tags is
/// the ordinary case, and the bar matches a filter when any of its values does.
/// </para>
/// </summary>
/// <param name="Name">What kind of fact it is, exactly as it will be labelled.</param>
/// <param name="Value">The value, exactly as it will be offered as a choice.</param>
public sealed record RoadmapFacet(string Name, string Value);
