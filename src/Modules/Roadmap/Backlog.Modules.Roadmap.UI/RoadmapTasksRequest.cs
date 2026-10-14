using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// A roadmap item's work, asked to be shown in the task list: its plan tag and the
/// repositories to scope the list to.
/// <para>
/// Raised by the band and answered by the shell, which owns the view switch and the
/// task list's filters — the roadmap references no Tasks module, the same arrangement
/// as the In progress view's "open this task".
/// </para>
/// </summary>
/// <param name="PlanTag">The plan tag with its <c>+</c>, the way a task carries it. The
/// roadmap holds the bare slug and lifts the <c>+</c> here, at its own boundary
/// (<c>.devbook/domain/roadmap/features.md#tagging-planned-work</c>).</param>
/// <param name="RepositoryAliases">The configured aliases to scope the list to, the
/// first the anchor; empty for every repository.</param>
public sealed record RoadmapTasksRequest(string PlanTag, IReadOnlyList<string> RepositoryAliases)
{
    /// <summary>
    /// The request for <paramref name="item"/>, opened from the bar <paramref name="barId"/>.
    /// <para>
    /// One repository's part of the item (<c>item@band</c>, with or without its
    /// <c>#phase</c>) scopes to that band's repository alone, because that is the work
    /// the reader picked. A bar that is the whole item scopes to every configured
    /// repository the item names. The no-repository band, and an item that names no
    /// configured repository, scope to every repository: the plan's tasks may be filed
    /// anywhere.
    /// </para>
    /// </summary>
    public static RoadmapTasksRequest For(
        RoadmapItemDto item,
        string? barId,
        IReadOnlyList<PlannedRepository> repositories)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(repositories);

        return new RoadmapTasksRequest(
            "+" + item.Tag,
            RoadmapPlanView.RepositoriesBehind(barId, item, repositories));
    }
}
