namespace Backlog.UI.Components.Roadmap;

/// <summary>
/// A span of work on the timeline: what it is, which row it sits on, and the two
/// dates that decide where it starts and how wide it is.
/// <para>
/// <paramref name="End"/> is the last day the work runs, not the day after it.
/// A plan that says "through the 31st" means the 31st, and an exclusive end
/// would draw a one-day item as a bar with no width at all.
/// </para>
/// </summary>
/// <param name="Id">Unique across the plan. Dependencies and every callback
/// name a bar by this.</param>
/// <param name="RowId">The row it belongs on. A bar naming a row that is not in
/// the plan is left out rather than drawn somewhere arbitrary.</param>
/// <param name="Title">The label written along the bar.</param>
/// <param name="Start">First day, inclusive.</param>
/// <param name="End">Last day, inclusive.</param>
/// <param name="Shade">How light the bar is drawn against its group's colour,
/// 0 through 3. The screenshot this component was drawn from varies the shade
/// bar by bar inside one band, and that is a real aid: it separates two bars
/// that abut without giving either a colour that means something else.
/// It is a caller's choice because only the caller knows which bars should read
/// as a set.</param>
/// <param name="Facets">What the bar can be filtered on. See
/// <see cref="RoadmapFacet"/> for why the names are the caller's.</param>
/// <param name="Detail">A second line for the tooltip and the accessible name —
/// a status, an owner, a note. Never drawn on the bar itself, which has room for
/// a title and nothing else.</param>
/// <param name="Locked">Whether the bar refuses to be moved or resized even when
/// the timeline allows it. A dependency that is somebody else's commitment is
/// the usual reason.</param>
public sealed record RoadmapBar(
    string Id,
    string RowId,
    string Title,
    DateOnly Start,
    DateOnly End,
    int Shade = 0,
    IReadOnlyList<RoadmapFacet>? Facets = null,
    string? Detail = null,
    bool Locked = false)
{
    public IReadOnlyList<RoadmapFacet> FacetList => Facets ?? [];

    /// <summary>How many days the bar covers, counting both ends. Never less
    /// than one: a bar whose end precedes its start is a caller's mistake, and
    /// collapsing it to a single day says so more usefully than drawing it
    /// backwards.</summary>
    public int Days => Math.Max(1, End.DayNumber - Start.DayNumber + 1);

    /// <summary>The shade clamped into the range the stylesheet actually
    /// defines, so an out-of-range value falls back to the base tint rather than
    /// producing a class nothing styles.</summary>
    public int ShadeStep => Math.Clamp(Shade, 0, 3);
}

/// <summary>
/// A point in time on a milestones row — a launch, a freeze, a board meeting.
/// <para>
/// Deliberately not a one-day <see cref="RoadmapBar"/>. A milestone has no
/// duration to drag the edge of, it is drawn as a glyph rather than a span, and
/// at a year's zoom a one-day bar is a sliver nobody can hit.
/// </para>
/// </summary>
/// <param name="Id">Unique across the plan; a dependency may name a milestone
/// exactly as it names a bar.</param>
/// <param name="RowId">The row it sits on.</param>
/// <param name="Title">The label beside the glyph.</param>
/// <param name="On">The day it falls on.</param>
/// <param name="Marker">Which glyph. Three shapes rather than three colours,
/// because colour alone must not be what tells two kinds of marker apart.</param>
/// <param name="Detail">A second line for the tooltip and accessible name.</param>
/// <param name="Line">Whether a rule is drawn down the whole chart at this date,
/// through every row rather than only the one the marker sits on.
/// <para>
/// For the dates the whole plan is read against. A release or a freeze is not a fact
/// about one lane, and a reader checking what lands before it should not have to hold
/// a vertical position in their head while their eye travels down the chart. It is
/// the caller's choice because only the caller knows which dates are that kind of
/// date: a chart where every milestone drew a line would be a chart of lines.
/// </para></param>
public sealed record RoadmapMilestone(
    string Id,
    string RowId,
    string Title,
    DateOnly On,
    RoadmapMarker Marker = RoadmapMarker.Diamond,
    string? Detail = null,
    bool Line = false);

/// <summary>Which glyph a milestone is drawn as.</summary>
public enum RoadmapMarker
{
    Diamond,
    Star,
    Square
}

/// <summary>
/// A dependency: <paramref name="FromId"/> has to land before
/// <paramref name="ToId"/> can.
/// <para>
/// Either end may name a bar or a milestone. The timeline resolves both ends to
/// wherever they are drawn <em>right now</em> — including a bar the reader is
/// part-way through dragging — so an arrow is never a stale line to where
/// something used to be. A link whose end is missing or filtered out of view is
/// dropped, because an arrow to nothing is worse than no arrow.
/// </para>
/// </summary>
/// <param name="FromId">The thing that must happen first.</param>
/// <param name="ToId">The thing that waits for it.</param>
public sealed record RoadmapLink(string FromId, string ToId);
