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
/// <param name="Parts">The same figures per repository, for a plan whose work is
/// filed in more than one. The shelf lists them one line each, because a plan
/// spanning repositories is read as the part each of them has to do.</param>
public sealed record RoadmapShelfPlan(
    string Tag,
    IReadOnlyList<string> Repositories,
    int TaskCount,
    int TotalEffort,
    int UnestimatedCount,
    IReadOnlyList<RoadmapShelfPart>? Parts = null)
{
    public IReadOnlyList<RoadmapShelfPart> PartList => Parts ?? [];

    /// <summary>How much work, and how big — without where.</summary>
    public string Totals => RoadmapShelfPart.Figures(TaskCount, TotalEffort, UnestimatedCount);

    /// <summary>The line under the tag: how much work, how big, and where.</summary>
    public string Summary =>
        Repositories.Count > 0 ? $"{Totals} · {string.Join(", ", Repositories)}" : Totals;
}

/// <summary>One repository's share of a <see cref="RoadmapShelfPlan"/>.</summary>
public sealed record RoadmapShelfPart(
    string Repository,
    int TaskCount,
    int TotalEffort,
    int UnestimatedCount)
{
    public string Totals => Figures(TaskCount, TotalEffort, UnestimatedCount);

    internal static string Figures(int tasks, int effort, int unestimated)
    {
        var parts = new List<string>
        {
            tasks == 1 ? "1 task" : $"{tasks} tasks",
            effort == 1 ? "1 point" : $"{effort} points"
        };

        if (unestimated > 0) parts.Add($"{unestimated} unestimated");

        return string.Join(" · ", parts);
    }
}
