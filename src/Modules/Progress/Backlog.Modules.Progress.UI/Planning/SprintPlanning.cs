using System.Globalization;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.UI.Components.Menus;
using Backlog.UI.Components.Progress;

namespace Backlog.Modules.Progress.UI.Planning;

/// <summary>
/// A task as the host knows it, for planning it into a sprint: the "Add to sprint"
/// picker's choices and a task's Sprint section. The host lists them from its own task
/// list, because Progress UI may not reach Tasks (local ADR 0028) — the shape the sprint
/// view's outside line already takes.
/// </summary>
/// <param name="Repositories">The repositories the task targets, by alias.</param>
/// <param name="PlanTags">The plans it is filed under, with or without the sigil.</param>
/// <param name="Finished">Done or archived: still offered, since a sprint takes any task
/// in its repositories whatever its status, but said so.</param>
public sealed record SprintCandidate(
    Guid TaskId,
    string Title,
    IReadOnlyList<string> Repositories,
    IReadOnlyList<string> PlanTags,
    bool Finished = false);

/// <summary>
/// What planning a sprint offers, worked out from the series as the module answers it
/// (<c>.devbook/domain/progress/features.md#filling-a-sprint</c>) — the picker's choices,
/// a row's menu and a task's Sprint section. Pure, so its tests need no module behind
/// them; every write it leads to is the module's.
/// </summary>
public static class SprintPlanning
{
    /// <summary>Row menu ids, which <see cref="RowMenu"/> hands out and the host's
    /// handler reads back.</summary>
    public const string MarkGoal = "goal";
    public const string UnmarkGoal = "ungoal";
    public const string EditNote = "note";
    public const string MarkQuestion = "question";
    public const string ClearQuestion = "unquestion";
    public const string RemoveTask = "remove";
    public const string UnlinkPlan = "unlink";

    /// <summary>The open sprint of <paramref name="series"/>, the one sprint that takes
    /// writes; null before the first one starts.</summary>
    public static SprintDto? OpenSprint(SprintSeriesDto series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return series.OpenSprint;
    }

    /// <summary>What the sprint records about <paramref name="taskId"/>, or null.</summary>
    public static SprintItemDto? ItemOf(SprintDto sprint, Guid taskId)
    {
        ArgumentNullException.ThrowIfNull(sprint);
        return sprint.Items.FirstOrDefault(item => item.TaskId == taskId);
    }

    public static bool IsLinked(SprintDto sprint, string? planTag)
    {
        ArgumentNullException.ThrowIfNull(sprint);
        var bare = Bare(planTag);
        return bare.Length > 0 && sprint.PlanLinks.Any(link => string.Equals(Bare(link.PlanTag), bare, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The picker's choices: every plan filed in the series' repositories, then every
    /// task in them, each marked when the open sprint already holds it — a task added
    /// directly or brought in by a linked plan, a plan linked.
    /// </summary>
    public static IReadOnlyList<SprintAddChoice> Choices(SprintSeriesDto series, SprintDto sprint, IEnumerable<SprintCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(sprint);
        ArgumentNullException.ThrowIfNull(candidates);

        var inSeries = candidates
            .Select(candidate => candidate with
            {
                Repositories = [.. candidate.Repositories.Where(alias => series.Repositories.Contains(alias, StringComparer.OrdinalIgnoreCase))]
            })
            .Where(candidate => candidate.Repositories.Count > 0)
            .ToList();

        var plans = inSeries
            .SelectMany(candidate => candidate.PlanTags.Select(Bare).Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(tag => (Tag: tag, Candidate: candidate)))
            .GroupBy(pair => pair.Tag, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SprintAddChoice(
                SprintAddKind.Plan,
                group.Key,
                $"+{group.Key}",
                Count(group.Count(), "task", "tasks"),
                [.. group.SelectMany(pair => pair.Candidate.Repositories).Distinct(StringComparer.OrdinalIgnoreCase)],
                IsLinked(sprint, group.Key)));

        var tasks = inSeries
            .OrderBy(candidate => candidate.Finished)
            .ThenBy(candidate => candidate.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(candidate => new SprintAddChoice(
                SprintAddKind.Task,
                candidate.TaskId.ToString(),
                candidate.Title,
                TaskDetail(candidate),
                candidate.Repositories,
                IsMember(sprint, candidate)));

        return [.. plans, .. tasks];
    }

    /// <summary>Whether the open sprint holds <paramref name="candidate"/>: added
    /// directly, or filed under a linked plan in one of the series' repositories.</summary>
    public static bool IsMember(SprintDto sprint, SprintCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(sprint);
        ArgumentNullException.ThrowIfNull(candidate);

        return ItemOf(sprint, candidate.TaskId) is { AddedDirectly: true }
            || candidate.PlanTags.Any(tag => IsLinked(sprint, tag));
    }

    /// <summary>
    /// A row's menu: the goal mark, the note, the open question, and taking the task out
    /// — removing it when it was added directly, unlinking its plan when a plan brought it
    /// in. The mark is what the sprint records, not the goal set the rules pick.
    /// </summary>
    public static IReadOnlyList<MenuItem> RowMenu(SprintDto sprint, SprintMemberDto member)
    {
        ArgumentNullException.ThrowIfNull(sprint);
        ArgumentNullException.ThrowIfNull(member);

        var item = ItemOf(sprint, member.TaskId);
        var items = new List<MenuItem>
        {
            item is { IsGoal: true } ? new(UnmarkGoal, "Unmark goal") : new(MarkGoal, "Mark as goal"),
            new(EditNote, string.IsNullOrWhiteSpace(item?.Note) ? "Add a note…" : "Edit note…"),
            item is { OpenQuestion: true } ? new(ClearQuestion, "Clear open question") : new(MarkQuestion, "Mark as open question")
        };

        var removals = new List<MenuItem>();
        if (item is { AddedDirectly: true }) removals.Add(new(RemoveTask, "Remove from sprint", Destructive: true));
        if (member.PlanTag is { } plan && IsLinked(sprint, plan)) removals.Add(new(UnlinkPlan, $"Unlink +{Bare(plan)}", Destructive: true));

        if (removals.Count > 0)
        {
            items.Add(MenuItem.Divider("remove-divider"));
            items.AddRange(removals);
        }

        return items;
    }

    /// <summary>A task's place in the open sprint of <paramref name="series"/>, for its
    /// side panel. The plan offered is the first of the task's plans that is linked, else
    /// its first plan.</summary>
    public static SprintTaskPanelModel Panel(SprintSeriesDto series, SprintCandidate task)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(task);

        var sprint = series.OpenSprint;
        var plans = task.PlanTags.Select(Bare).Where(tag => tag.Length > 0).ToList();
        if (sprint is null) return new(series.Name, null, null, SprintMembership.Outside, false, null, false, plans.FirstOrDefault(), false);

        var linked = plans.FirstOrDefault(tag => IsLinked(sprint, tag));
        var item = ItemOf(sprint, task.TaskId);
        var direct = item is { AddedDirectly: true };

        var membership = (direct, linked is not null) switch
        {
            (true, true) => SprintMembership.AddedAndThroughPlan,
            (true, false) => SprintMembership.Added,
            (false, true) => SprintMembership.ThroughPlan,
            _ => SprintMembership.Outside
        };

        return new(
            series.Name,
            sprint.Name,
            InProgress.SprintBoardMapper.Dates(sprint.Start, sprint.End),
            membership,
            item?.IsGoal ?? false,
            item?.Note,
            item?.OpenQuestion ?? false,
            linked ?? plans.FirstOrDefault(),
            linked is not null);
    }

    /// <summary>The first series switched on that holds one of <paramref name="repositories"/>.</summary>
    public static SprintSeriesDto? SeriesOf(IEnumerable<SprintSeriesDto> series, IEnumerable<string> repositories)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(repositories);

        var on = series.Where(candidate => candidate.IsOn).ToList();
        return repositories
            .Select(alias => on.FirstOrDefault(candidate => candidate.Repositories.Contains(alias.Trim(), StringComparer.OrdinalIgnoreCase)))
            .FirstOrDefault(found => found is not null);
    }

    /// <summary>A plan tag without its <c>+</c> sigil, as the module compares them.</summary>
    public static string Bare(string? tag)
    {
        var trimmed = tag?.Trim() ?? string.Empty;
        return trimmed.StartsWith('+') ? trimmed[1..].Trim() : trimmed;
    }

    private static string TaskDetail(SprintCandidate candidate)
    {
        var repositories = string.Join(", ", candidate.Repositories);
        var plans = string.Join(" ", candidate.PlanTags.Select(Bare).Where(tag => tag.Length > 0).Select(tag => $"+{tag}"));
        var parts = new[] { repositories, plans, candidate.Finished ? "finished" : string.Empty };
        return string.Join(" · ", parts.Where(part => part.Length > 0));
    }

    private static string Count(int count, string one, string many) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? one : many)}";
}
