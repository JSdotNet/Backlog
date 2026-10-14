using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Progress.UI.Planning;
using Backlog.UI.Components.Badges;

namespace Backlog.Desktop.UI.InProgress;

/// <summary>
/// The shell's tasks as Progress UI plans them into a sprint
/// (<c>.devbook/domain/progress/features.md#filling-a-sprint</c>): each saved entry with
/// its title, the repositories its <c>repo:</c> tokens name — by alias, as a series holds
/// them — and its plan tags. The shell builds them because it holds the task list, and
/// Progress UI may not reach Tasks (local ADR 0028). Read on every asking, never kept.
/// </summary>
internal static class SprintCandidates
{
    /// <summary>Every saved entry of <paramref name="state"/>.</summary>
    public static IReadOnlyList<SprintCandidate> Of(TasksDesktopState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return [.. state.Rows.Where(row => row.IsPersisted).Select(row => Of(state, row))];
    }

    /// <summary>One entry.</summary>
    public static SprintCandidate Of(TasksDesktopState state, EntryRow row)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(row);

        return new SprintCandidate(
            row.Id ?? row.Key,
            row.PreviewTitle,
            [.. row.PreviewRepoIds.Select(id => state.RepositoryAliasFor(id) ?? id).Distinct(StringComparer.OrdinalIgnoreCase)],
            [.. row.PreviewTags.Where(tag => TagText.Kind(tag) is TagKind.Plan)],
            row.IsPreviewCompleted);
    }
}
