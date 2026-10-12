using Backlog.Modules.Roadmap.Abstractions;

namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// Derives a tag slug from a title, for the editor to pre-fill the Tag field with.
/// <para>
/// The rule is <see cref="RoadmapShelf.SlugOf"/>, the abstractions' restatement of the
/// module's own <c>PlanningTag.From</c>: the UI references the plan's Abstractions, not its
/// domain, so it cannot reach the value object. The module remains the authority: whatever
/// this pre-fills, the save re-normalizes through <c>PlanningTag.Of</c>, so the two only
/// ever differ for the instant before a save. The rule is kept identical so that instant
/// shows the person the value they are about to store rather than one that will change
/// under them.
/// </para>
/// </summary>
internal static class RoadmapTagSlug
{
    public static string From(string? title) => RoadmapShelf.SlugOf(title);
}
