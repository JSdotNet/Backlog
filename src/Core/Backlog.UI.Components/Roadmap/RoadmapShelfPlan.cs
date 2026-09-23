namespace Backlog.UI.Components.Roadmap;

/// <summary>
/// One plan offered on <see cref="RoadmapPlanShelf"/>: work that exists and has no
/// place on the timeline yet.
/// <para>
/// Numbers rather than a caller-written sentence, so every host says the same
/// thing about the same plan and the storybook shows exactly what a screen shows.
/// The shelf writes the words.
/// </para>
/// </summary>
/// <param name="Tag">The plan's tag, bare. Drawn with the plan sigil, and the key a
/// row is found by.</param>
/// <param name="Repositories">Where its work is filed, by the name a reader knows
/// the repository by.</param>
/// <param name="TaskCount">How many pieces of work it holds.</param>
/// <param name="TotalEffort">The effort they registered, summed.</param>
/// <param name="UnestimatedCount">How many registered none. Said separately, because
/// a total that silently left them out would read as the whole plan's size.</param>
public sealed record RoadmapShelfPlan(
    string Tag,
    IReadOnlyList<string> Repositories,
    int TaskCount,
    int TotalEffort,
    int UnestimatedCount)
{
    /// <summary>The line under the tag: how much work, how big, and where.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>
            {
                TaskCount == 1 ? "1 task" : $"{TaskCount} tasks",
                TotalEffort == 1 ? "1 point" : $"{TotalEffort} points"
            };

            if (UnestimatedCount > 0) parts.Add($"{UnestimatedCount} unestimated");
            if (Repositories.Count > 0) parts.Add(string.Join(", ", Repositories));

            return string.Join(" · ", parts);
        }
    }
}
