using System.Globalization;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.UI.Components.Progress;

namespace Backlog.Modules.Progress.UI.InProgress;

/// <summary>
/// Turns the open sprint's reading into what the Close dialog draws
/// (<c>.devbook/domain/progress/features.md#closing-a-sprint</c>), and the dialog's
/// choices back into the module's close outcomes. Pure, so its tests need no module
/// behind them.
/// </summary>
public static class SprintCloseMapper
{
    /// <summary>The dialog for <paramref name="progress"/>, the open sprint of
    /// <paramref name="series"/>, drawn from the same <paramref name="board"/> the sprint
    /// view shows so the review table matches it row for row.</summary>
    public static SprintCloseModel ToModel(SprintProgressDto progress, SprintSeriesDto series, SprintBoardModel board)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(board);

        var goal = progress.GoalTaskIds.ToHashSet();
        var today = progress.ReadOn ?? DateOnly.FromDateTime(DateTime.Today);
        var unfinished = SprintBoardRules.Group(board.Rows, board.GoalPlans, byPlan: true)
            .SelectMany(group => group.Rows)
            .Where(row => row.State is not SprintRowState.Done)
            .Select(row => progress.Members.FirstOrDefault(member => member.TaskId.ToString() == row.TaskId) is { } member
                ? new SprintCloseItem(row.TaskId, row.Title, Detail(member, row, goal.Contains(member.TaskId), today))
                : new SprintCloseItem(row.TaskId, row.Title, string.Empty))
            .ToList();

        return new SprintCloseModel(
            progress.Name,
            NextName(series, progress.Number),
            Subtitle(progress),
            progress.GoalMet,
            progress.Goal.Done,
            progress.Goal.Total,
            progress.AllWork.Done,
            progress.AllWork.Total,
            progress.AllWork.AddedAfterStart,
            unfinished,
            board.Rows,
            board.GoalPlans);
    }

    /// <summary>The name the sprint after <paramref name="number"/> will carry: the
    /// series' pattern with the next number in it.</summary>
    public static string NextName(SprintSeriesDto series, int number)
    {
        ArgumentNullException.ThrowIfNull(series);
        return series.NamePattern.Replace("{n}", (number + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>The dialog's choices as the module's outcomes, by task id. A key that is
    /// not a task id is left out, and the module refuses the close for it.</summary>
    public static IReadOnlyDictionary<Guid, CloseOutcome> Outcomes(SprintCloseDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var outcomes = new Dictionary<Guid, CloseOutcome>();
        foreach (var (taskId, choice) in decision.Choices)
        {
            if (!Guid.TryParse(taskId, out var id)) continue;

            outcomes[id] = choice switch
            {
                SprintCloseChoice.BackToBacklog => CloseOutcome.BackToBacklog,
                SprintCloseChoice.Drop => CloseOutcome.Drop,
                _ => CloseOutcome.Carry
            };
        }

        return outcomes;
    }

    /// <summary>"Fincent team · Mon 5 Oct – Sun 18 Oct · goal: Rekeningen specs shipped".</summary>
    private static string Subtitle(SprintProgressDto progress)
    {
        var parts = new List<string> { progress.SeriesName, SprintBoardMapper.Dates(progress.Start, progress.End) };
        if (!string.IsNullOrWhiteSpace(progress.SprintGoal)) parts.Add($"goal: {progress.SprintGoal.Trim()}");
        return string.Join(" · ", parts);
    }

    /// <summary>"Goal · +verplaatsen-specs · Martijn · blocked 6 days".</summary>
    private static string Detail(SprintMemberDto member, SprintBoardRow row, bool isGoal, DateOnly today)
    {
        var parts = new List<string>();
        if (isGoal) parts.Add("Goal");
        if (!string.IsNullOrWhiteSpace(member.PlanTag)) parts.Add($"+{member.PlanTag.Trim().TrimStart('+')}");
        if (member.Owners.Count > 0) parts.Add(string.Join(", ", member.Owners));

        parts.Add(row.State switch
        {
            SprintRowState.Blocked when member.BlockedSince is { } since && today.DayNumber - since.DayNumber > 0 =>
                $"blocked {SprintBoardRules.Count(today.DayNumber - since.DayNumber, "day", "days")}",
            SprintRowState.Blocked => "blocked",
            SprintRowState.InProgress => row.InReview ? "in review" : "in progress",
            _ => "not started"
        });

        return string.Join(" · ", parts);
    }
}
