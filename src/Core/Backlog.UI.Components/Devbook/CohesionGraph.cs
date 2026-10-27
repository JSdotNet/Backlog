namespace Backlog.UI.Components.Devbook;

/// <summary>
/// A cohesion graph laid out and ready to draw on <see cref="CohesionBoard"/>:
/// usages as boxes on the stage lanes and the layer bands beneath them, the
/// hand-offs and support links between them as orthogonal polylines, and the
/// platforms the usages are built on. Every coordinate is the caller's — the
/// board places nothing — so a host lays the graph out once and the board only
/// decides what is lit.
/// </summary>
/// <param name="Width">The whole graph's width, in CSS pixels.</param>
/// <param name="Height">The whole graph's height, the last band included.</param>
/// <param name="LaneHeight">How far down the stage lanes run.</param>
/// <param name="LaneHeaderHeight">The height of a lane's header, where its number
/// and name sit and the pull-request marker stands.</param>
/// <param name="PullRequestX">Where the pull-request marker stands, or null for
/// none.</param>
/// <param name="Lanes">The stages, in path order.</param>
/// <param name="Bands">The layers beneath the path, top to bottom.</param>
/// <param name="Nodes">One box per usage, in reading order.</param>
/// <param name="Edges">The hand-offs, then the support links.</param>
/// <param name="Platforms">What the usages are built on, each named once.</param>
public sealed record CohesionGraph(
    double Width,
    double Height,
    double LaneHeight,
    double LaneHeaderHeight,
    double? PullRequestX,
    IReadOnlyList<CohesionLane> Lanes,
    IReadOnlyList<CohesionBand> Bands,
    IReadOnlyList<CohesionNode> Nodes,
    IReadOnlyList<CohesionEdge> Edges,
    IReadOnlyList<CohesionPlatform> Platforms)
{
    public static CohesionGraph Empty { get; } = new(0, 0, 0, 0, null, [], [], [], [], []);

    public CohesionNode? Node(string? id) =>
        id is null ? null : Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal));

    public CohesionPlatform? Platform(string? id) =>
        id is null ? null : Platforms.FirstOrDefault(platform => string.Equals(platform.Id, id, StringComparison.Ordinal));

    /// <summary>A hand-off that moves the work on: both ends on the path, the
    /// target at a later stage.</summary>
    public bool IsForward(CohesionEdge edge)
    {
        if (edge.Kind == CohesionEdgeKind.Support) return false;
        return Node(edge.From) is { OnPath: true } from && Node(edge.To) is { OnPath: true } to && to.Stage > from.Stage;
    }

    /// <summary>A hand-off that comes back: to an earlier stage on the path, or
    /// out of a layer.</summary>
    public bool FeedsBack(CohesionEdge edge)
    {
        if (edge.Kind == CohesionEdgeKind.Support) return false;
        if (Node(edge.From) is not { } from || Node(edge.To) is not { } to) return false;
        return !from.OnPath || (to.OnPath && to.Stage < from.Stage);
    }

    /// <summary>How many usages are built on the platform.</summary>
    public int UsageCount(string platformId) =>
        Nodes.Count(node => node.Platforms.Contains(platformId, StringComparer.Ordinal));
}

/// <summary>One stage's lane: a column of the path.</summary>
/// <param name="Key">The stage's word, used for its test id.</param>
/// <param name="Name">Its display name.</param>
/// <param name="X">The lane's left edge.</param>
/// <param name="Width">The lane's width.</param>
/// <param name="BeforePullRequest">Whether the stage sits before the pull request.</param>
public sealed record CohesionLane(string Key, string Name, double X, double Width, bool BeforePullRequest);

/// <summary>One layer's band beneath the path.</summary>
/// <param name="Key">The layer's word, used for its test id.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Y">The band's top; its name sits at the top.</param>
/// <param name="Height">The band's height.</param>
public sealed record CohesionBand(string Key, string Name, double Y, double Height);

/// <summary>One usage's box.</summary>
/// <param name="Id">What identifies the usage on the board and to the host.</param>
/// <param name="Name">The usage's own name.</param>
/// <param name="Status">Its rung on the <c>.tech</c> ladder — adopted, trial,
/// candidate, hold or retired — which styles the box; null draws a dashed box.</param>
/// <param name="Scheduled">Whether a schedule runs it, drawn as a clock glyph.</param>
/// <param name="Stage">The stage it sits at, from 1, or the column its band
/// places it in.</param>
/// <param name="Where">Where it sits, in words: its stage's name or its layer's.</param>
/// <param name="OnPath">Whether it sits on the path rather than in a band.</param>
/// <param name="Platforms">The ids of the platforms it is built on.</param>
public sealed record CohesionNode(
    string Id,
    string Name,
    string? Status,
    bool Scheduled,
    int Stage,
    string Where,
    bool OnPath,
    double X,
    double Y,
    double Width,
    double Height,
    IReadOnlyList<string> Platforms);

/// <summary>What an edge says.</summary>
public enum CohesionEdgeKind
{
    /// <summary>A hand-off: the source hands work on to the target.</summary>
    Feed,

    /// <summary>The hand-off out of production back into the loop that is still
    /// open — drawn red.</summary>
    OpenLoop,

    /// <summary>A support link: one usage leaning on or steering another. Drawn
    /// only in Everything, or when one of its ends is picked.</summary>
    Support
}

public readonly record struct CohesionPoint(double X, double Y);

/// <param name="From">The id of the node the edge leaves.</param>
/// <param name="To">The id of the node its arrow points at.</param>
/// <param name="Kind">A hand-off, the open loop, or a support link.</param>
/// <param name="IsDashed">For a hand-off: whether it would hand on rather than
/// does — a candidate at either end.</param>
/// <param name="Label">What is handed on, or null.</param>
/// <param name="Points">The polyline, from the edge of the source's box to the
/// edge of the target's; at least two points.</param>
/// <param name="LabelAt">Where the label sits.</param>
public sealed record CohesionEdge(
    string From,
    string To,
    CohesionEdgeKind Kind,
    bool IsDashed,
    string? Label,
    IReadOnlyList<CohesionPoint> Points,
    CohesionPoint LabelAt);

/// <summary>Something the usages are built on.</summary>
/// <param name="Id">What the nodes' <see cref="CohesionNode.Platforms"/> name it by.</param>
/// <param name="Name">Its display name.</param>
public sealed record CohesionPlatform(string Id, string Name);

/// <summary>How the board is read.</summary>
public enum CohesionView
{
    /// <summary>Every hand-off; support links only around a picked usage.</summary>
    HandOffs,

    /// <summary>The hand-offs that move to a later stage, each node badged with
    /// its stage number.</summary>
    Forward,

    /// <summary>The hand-offs to an earlier stage or out of a layer.</summary>
    FeedsBack,

    /// <summary>Every hand-off and every support link.</summary>
    Everything
}
