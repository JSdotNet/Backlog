namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// The Roadmap's id in the surface catalog and the query keys it accepts (local ADR
/// 0040 §1, §6), for the shell to name the view by without naming its component.
/// </summary>
public static class RoadmapSurfaceQuery
{
    /// <summary>The Roadmap view's surface id.</summary>
    public const string SurfaceId = "roadmap";

    /// <summary><c>plan=&lt;tag&gt;</c>: open the chart with that plan's bar selected.</summary>
    public const string Plan = "plan";

    /// <summary>The keys the Roadmap registers.</summary>
    public static IReadOnlyList<string> Keys { get; } = [Plan];
}
