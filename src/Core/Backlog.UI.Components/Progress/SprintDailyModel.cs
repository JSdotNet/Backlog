using System.Text;

namespace Backlog.UI.Components.Progress;

/// <summary>
/// The sprint view's Since yesterday strip, in plain words
/// (<c>.devbook/domain/progress/features.md#the-daily-strip</c>): what moved since the
/// previous working day, what is on today, what is blocked or open, and the pace. Every
/// sentence is already written by the host from fixed templates over its sprint reading;
/// the strip only lays them out, the <see cref="SprintBoardModel"/> precedent.
/// </summary>
/// <param name="Label">"Daily · Tue 13 Oct · Sprint 42, day 7 of 10".</param>
/// <param name="Headline">One sentence on the day: what landed and what is stuck.</param>
/// <param name="Summary">"Fincent · goal 12 of 17 · all work 13 of 29 · 3 working days left".</param>
/// <param name="SinceYesterday">Done, opened for review, started and added to the sprint
/// since the previous working day; one quiet line when nothing moved.</param>
/// <param name="OnToday">In progress and waiting on review; one quiet line when nothing is.</param>
/// <param name="Blocked">Blocked members and open questions; empty when there are none,
/// and the section is not drawn then.</param>
/// <param name="Pace">The goal's open count and whether it is at risk, then all the work
/// against the even pace.</param>
/// <param name="GoalAtRisk">Whether the goal is at risk, for the strip's tone.</param>
public sealed record SprintDailyModel(
    string Label,
    string Headline,
    string Summary,
    IReadOnlyList<SprintDailyLine> SinceYesterday,
    IReadOnlyList<SprintDailyLine> OnToday,
    IReadOnlyList<SprintDailyLine> Blocked,
    string Pace,
    bool GoalAtRisk);

/// <summary>What one line of the strip says, which sets its marker.</summary>
public enum SprintDailyLineKind
{
    /// <summary>Nothing to report in its section.</summary>
    Quiet,
    Done,
    OpenedForReview,
    Started,
    Added,
    InProgress,
    WaitingOnReview,
    Blocked,
    OpenQuestion
}

/// <summary>One line of the strip: its words and the pull requests it names, drawn as
/// chips after the words.</summary>
public sealed record SprintDailyLine(SprintDailyLineKind Kind, string Text, IReadOnlyList<SprintBoardPullRequest> PullRequests)
{
    public static SprintDailyLine Of(SprintDailyLineKind kind, string text) => new(kind, text, []);
}

/// <summary>
/// The strip as the text the <b>Copy for the stand-up</b> button puts on the clipboard.
/// <para>
/// The one place the strip leaves as text: the copy-as-table step's clipboard helper
/// replaces <see cref="PlainText"/> here when it lands, and nothing else changes.
/// </para>
/// </summary>
public static class SprintDailyCopy
{
    /// <summary>The label, headline and summary, then each section under its heading
    /// with a "- " per line and the pull requests by number. Lines end in <c>\n</c>.</summary>
    public static string PlainText(SprintDailyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var text = new StringBuilder();
        text.Append(model.Label).Append('\n');
        text.Append(model.Headline).Append('\n');
        text.Append(model.Summary).Append('\n');

        Section(text, "Since yesterday", model.SinceYesterday);
        Section(text, "On today", model.OnToday);
        Section(text, "Blocked and open questions", model.Blocked);

        text.Append('\n').Append("Pace").Append('\n').Append(model.Pace).Append('\n');
        return text.ToString();
    }

    /// <summary>A line's words with its pull requests after them: "Done: Categorieën (#4685)".</summary>
    public static string LineText(SprintDailyLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return line.PullRequests.Count == 0
            ? line.Text
            : $"{line.Text} ({string.Join(", ", line.PullRequests.Select(pull => $"#{pull.Number}"))})";
    }

    private static void Section(StringBuilder text, string heading, IReadOnlyList<SprintDailyLine> lines)
    {
        if (lines.Count == 0) return;

        text.Append('\n').Append(heading).Append('\n');
        foreach (var line in lines) text.Append("- ").Append(LineText(line)).Append('\n');
    }
}
