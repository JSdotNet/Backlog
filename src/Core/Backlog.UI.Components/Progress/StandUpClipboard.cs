using System.Text;

namespace Backlog.UI.Components.Progress;

/// <summary>
/// The stand-up as plain text for the clipboard
/// (<c>.devbook/domain/progress/features.md#copying-the-stand-up</c>): Goals, Finished,
/// Planned and Impediments, each heading with its count, Finished grouped by goal and
/// Planned grouped by goal with No goal last. Every line is a fixed template over the reading, through
/// <see cref="GoalsText"/>; none is written by a model. Pure, so the view and its tests
/// read the same text. Hand it to <see cref="Buttons.CopyButton"/> as <c>Text</c>.
/// </summary>
public static class StandUpClipboard
{
    /// <summary>Line breaks are Windows', where Teams and Outlook run, whatever the host is;
    /// so the text pastes as separate lines there.</summary>
    private const string NewLine = "\r\n";

    private const string NoGoal = "No goal";

    /// <summary>The four blocks of <paramref name="board"/>, a blank line between each.</summary>
    public static string Text(GoalsBoardModel board)
    {
        ArgumentNullException.ThrowIfNull(board);

        var standUp = board.StandUp;
        var blocks = new List<IEnumerable<string>>
        {
            GoalsBlock(board.Goals),
            Grouped($"Finished {StandUpGroups.ItemCount(standUp.Finished)}", standUp.Finished),
            Grouped($"Planned {StandUpGroups.ItemCount(standUp.Planned)} · {GoalsText.PointsTag(standUp.PlannedEffort, standUp.Capacity)}", standUp.Planned),
            ImpedimentsBlock(standUp)
        };

        var text = new StringBuilder();
        foreach (var block in blocks)
        {
            if (text.Length > 0) text.Append(NewLine).Append(NewLine);
            text.AppendJoin(NewLine, block);
        }

        return text.ToString();
    }

    private static IEnumerable<string> GoalsBlock(IReadOnlyList<GoalCardModel> goals)
    {
        yield return $"Goals {goals.Count}";
        foreach (var goal in goals)
            yield return $"- {goal.Title}: {GoalsText.GoalLine(goal)} · {GoalsText.Forecast(goal.AtRisk)}";
    }

    private static IEnumerable<string> Grouped(string heading, IReadOnlyList<StandUpGroup> groups)
    {
        yield return heading;

        // No goal is last whatever order the reading handed the groups in.
        foreach (var group in StandUpGroups.InOrder(groups))
        {
            if (group.Items.Count == 0) continue;

            yield return group.GoalTitle ?? NoGoal;
            foreach (var item in group.Items) yield return "- " + item.Title;
        }
    }

    private static IEnumerable<string> ImpedimentsBlock(StandUpModel standUp)
    {
        yield return $"Impediments {standUp.Impediments.Count}";
        foreach (var impediment in standUp.Impediments)
            yield return $"- {impediment.Title} · {GoalsText.State(impediment.State)} · {GoalsText.ImpedimentLine(impediment)}";

        if (standUp.ResolvedInLast > 0) yield return $"{standUp.ResolvedInLast} resolved";
    }

}
