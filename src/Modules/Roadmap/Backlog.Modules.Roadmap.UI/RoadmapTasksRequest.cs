using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// A roadmap item's work, asked to be shown in the task list: its plan tag and the task
/// it links — the two halves of what the roadmap gathers under it
/// (<c>.devbook/domain/roadmap/domain.md#roadmap-item-gathering</c>).
/// <para>
/// Raised by the band and answered by the shell, which owns the view switch and the
/// task list's filters — the roadmap references no Tasks module.
/// </para>
/// </summary>
/// <param name="PlanTag">The plan tag with its <c>+</c>, the way a task carries it. The
/// roadmap holds the bare slug and lifts the <c>+</c> here, at its own boundary
/// (<c>.devbook/domain/roadmap/features.md#tagging-planned-work</c>).</param>
/// <param name="RepositoryAliases">The configured aliases behind the bar the request was
/// raised from, the first the anchor; empty for every repository. Not what the list is
/// scoped to: the roadmap gathers an item's work wherever it is filed, so the shell shows
/// every repository, and a narrower scope could only hide some of what the bar counted.</param>
/// <param name="LinkedTaskId">The task the item links, when it links one — gathered
/// whether or not it carries the tag. A bare id, as the item holds it: a task link is
/// only an id.</param>
public sealed record RoadmapTasksRequest(string PlanTag, IReadOnlyList<string> RepositoryAliases, Guid? LinkedTaskId = null)
{
    /// <summary>
    /// The request for <paramref name="item"/>, opened from the bar <paramref name="barId"/>.
    /// <para>
    /// One repository's part of the item (<c>item@band</c>, with or without its
    /// <c>#phase</c>) names that band's repository alone, because that is the work the
    /// reader picked. A bar that is the whole item names every configured repository the
    /// item names. The no-repository band, and an item that names no configured
    /// repository, name none. None of them scopes the list — see
    /// <see cref="RepositoryAliases"/>. The item's linked task rides along whichever bar
    /// it was opened from.
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
            RoadmapPlanView.RepositoriesBehind(barId, item, repositories),
            item.TaskId);
    }
}
