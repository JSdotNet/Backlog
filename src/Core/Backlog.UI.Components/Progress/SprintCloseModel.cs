using System.Globalization;
using Backlog.UI.Components.Selects;

namespace Backlog.UI.Components.Progress;

/// <summary>What closing a sprint does with one unfinished item
/// (<c>.devbook/domain/progress/domain.md#close-outcome</c>). None of the three changes
/// the task.</summary>
public enum SprintCloseChoice
{
    /// <summary>The task joins the next sprint.</summary>
    Carry,

    /// <summary>The task leaves the sprint and joins no other.</summary>
    BackToBacklog,

    /// <summary>The task is recorded as dropped in the closed sprint only.</summary>
    Drop
}

/// <summary>One unfinished item of the sprint being closed, as the dialog lists it.</summary>
/// <param name="TaskId">The task's id, as text: the key its choice is handed back under.</param>
/// <param name="Detail">One quiet line under the title — "Goal · +specs · blocked 6 days".</param>
public sealed record SprintCloseItem(string TaskId, string Title, string Detail);

/// <summary>
/// The open sprint as the Close dialog draws it, in plain values
/// (<c>.devbook/domain/progress/features.md#closing-a-sprint</c>): the library carries no
/// domain, so the host turns its sprint reading into this and the dialog's
/// <see cref="SprintCloseDecision"/> back into its own close call.
/// </summary>
/// <param name="SprintName">The sprint being closed, "Sprint 42".</param>
/// <param name="NextSprintName">The sprint closing starts, "Sprint 43".</param>
/// <param name="Subtitle">The series, the dates and the goal in words, on one line.</param>
/// <param name="GoalMet">Whether the share of done goal tasks reaches the series' threshold.</param>
/// <param name="Unfinished">Every member that is not done, in the board's order.</param>
/// <param name="Rows">Every member as the board draws it: the review table's rows.</param>
/// <param name="GoalPlans">The plans an "in these plans" goal rule names, for the table's order.</param>
public sealed record SprintCloseModel(
    string SprintName,
    string NextSprintName,
    string Subtitle,
    bool GoalMet,
    int GoalDone,
    int GoalTotal,
    int AllDone,
    int AllTotal,
    int AddedAfterStart,
    IReadOnlyList<SprintCloseItem> Unfinished,
    IReadOnlyList<SprintBoardRow> Rows,
    IReadOnlyList<string> GoalPlans);

/// <summary>What the person chose: an outcome per unfinished item, by
/// <see cref="SprintCloseItem.TaskId"/>, and the next sprint's goal, trimmed.</summary>
public sealed record SprintCloseDecision(IReadOnlyDictionary<string, SprintCloseChoice> Choices, string NextGoal);

/// <summary>
/// The Close dialog's words and its review table, apart from the markup, so the dialog,
/// the host and their tests read the same answer.
/// </summary>
public static class SprintCloseRules
{
    /// <summary>Every unfinished item set to Carry — the default, and what "Set all to the
    /// next sprint" puts back.</summary>
    public static Dictionary<string, SprintCloseChoice> CarryAll(IEnumerable<SprintCloseItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.DistinctBy(item => item.TaskId).ToDictionary(item => item.TaskId, _ => SprintCloseChoice.Carry);
    }

    /// <summary>What a choice reads in the list: "Carry to Sprint 43", "Back to the
    /// backlog", "Drop".</summary>
    public static string ChoiceLabel(SprintCloseChoice choice, string nextSprintName) => choice switch
    {
        SprintCloseChoice.BackToBacklog => "Back to the backlog",
        SprintCloseChoice.Drop => "Drop",
        _ => $"Carry to {nextSprintName}"
    };

    /// <summary>The three choices, Carry first.</summary>
    public static IReadOnlyList<SelectorOption> Options(string nextSprintName) =>
    [
        .. Enum.GetValues<SprintCloseChoice>().Select(choice => new SelectorOption(choice.ToString(), ChoiceLabel(choice, nextSprintName)))
    ];

    /// <summary>"Goal met" or "Goal not met".</summary>
    public static string GoalVerdict(SprintCloseModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.GoalMet ? "Goal met" : "Goal not met";
    }

    /// <summary>"14 of 17 goal tasks done".</summary>
    public static string GoalLine(SprintCloseModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return $"{Number(model.GoalDone)} of {Number(model.GoalTotal)} goal {(model.GoalTotal == 1 ? "task" : "tasks")} done";
    }

    /// <summary>"19 of 29".</summary>
    public static string AllWorkFigure(SprintCloseModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return $"{Number(model.AllDone)} of {Number(model.AllTotal)}";
    }

    /// <summary>"10 unfinished", or "Every item is done" when none is.</summary>
    public static string UnfinishedHeading(SprintCloseModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Unfinished.Count == 0 ? "Every item is done" : $"{Number(model.Unfinished.Count)} unfinished";
    }

    /// <summary>What became of one row at closing: "Done" for a done item, else its
    /// choice — "Carried to Sprint 43", "Back to the backlog" or "Dropped".</summary>
    public static string Result(SprintBoardRow row, IReadOnlyDictionary<string, SprintCloseChoice> choices, string nextSprintName)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(choices);

        if (row.State is SprintRowState.Done) return "Done";

        return (choices.TryGetValue(row.TaskId, out var choice) ? choice : SprintCloseChoice.Carry) switch
        {
            SprintCloseChoice.BackToBacklog => "Back to the backlog",
            SprintCloseChoice.Drop => "Dropped",
            _ => $"Carried to {nextSprintName}"
        };
    }

    /// <summary>The sprint review: the sprint's table
    /// (<see cref="SprintTableClipboard"/>) with a Result column read off
    /// <paramref name="choices"/>.</summary>
    public static SprintTable ReviewTable(SprintCloseModel model, IReadOnlyDictionary<string, SprintCloseChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(choices);

        return SprintTableClipboard.Build(model.Rows, model.GoalPlans, row => Result(row, choices, model.NextSprintName));
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
