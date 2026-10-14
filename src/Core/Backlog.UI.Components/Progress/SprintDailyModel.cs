using System.Net;
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
/// What the <b>Copy for the stand-up</b> button puts on the clipboard: the strip, then the
/// sprint's table from <see cref="SprintTableClipboard"/>
/// (<c>.devbook/domain/progress/features.md#copying-a-sprint-as-a-table</c>), as HTML with
/// every style inline and as plain text beside it, the way Copy as table carries the table
/// alone.
/// </summary>
public static class SprintDailyCopy
{
    private const string NewLine = "\r\n";
    private const string Wrapper = "font-family:'Segoe UI',Arial,sans-serif;font-size:11pt;color:#1F2328";
    private const string LabelStyle = "margin:0;color:#57606A;font-size:9pt;font-weight:600;text-transform:uppercase;letter-spacing:0.06em";
    private const string HeadlineStyle = "margin:4px 0;font-size:13pt;font-weight:600";
    private const string SummaryStyle = "margin:0 0 8px;color:#57606A";
    private const string HeadingStyle = "margin:10px 0 2px;font-weight:600";
    private const string ListStyle = "margin:0;padding-left:20px";
    private const string RiskStyle = "margin:0;color:#CF222E";
    private const string PaceStyle = "margin:0";
    private const string LinkStyle = "color:#0969DA;text-decoration:underline";

    /// <summary>The stand-up copy: the strip, a blank line, then <paramref name="table"/>
    /// when there is one. Plain text lines end in <c>\r\n</c>, as the table's do.</summary>
    public static SprintTable StandUp(SprintDailyModel model, SprintTable? table)
    {
        ArgumentNullException.ThrowIfNull(model);

        var text = PlainText(model).Replace("\n", NewLine, StringComparison.Ordinal);
        if (table is not null) text += NewLine + table.Text + NewLine;

        var html = new StringBuilder();
        html.Append($"<div style=\"{Wrapper}\">");
        html.Append($"<p style=\"{LabelStyle}\">{Encode(model.Label)}</p>");
        html.Append($"<p style=\"{HeadlineStyle}\">{Encode(model.Headline)}</p>");
        html.Append($"<p style=\"{SummaryStyle}\">{Encode(model.Summary)}</p>");
        HtmlSection(html, "Since yesterday", model.SinceYesterday);
        HtmlSection(html, "On today", model.OnToday);
        HtmlSection(html, "Blocked and open questions", model.Blocked);
        html.Append($"<p style=\"{HeadingStyle}\">Pace</p>");
        html.Append($"<p style=\"{(model.GoalAtRisk ? RiskStyle : PaceStyle)}\">{Encode(model.Pace)}</p>");
        if (table is not null) html.Append("<br>").Append(table.Html);
        html.Append("</div>");

        return new SprintTable(html.ToString(), text);
    }

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

    private static void HtmlSection(StringBuilder html, string heading, IReadOnlyList<SprintDailyLine> lines)
    {
        if (lines.Count == 0) return;

        html.Append($"<p style=\"{HeadingStyle}\">{heading}</p><ul style=\"{ListStyle}\">");
        foreach (var line in lines)
        {
            html.Append("<li>").Append(Encode(line.Text));
            foreach (var pull in line.PullRequests)
            {
                var number = Encode($"#{pull.Number.Trim()}");
                html.Append(' ').Append(SprintTableClipboard.PullRequestUrl(pull) is { } url
                    ? $"<a href=\"{Encode(url)}\" style=\"{LinkStyle}\">{number}</a>"
                    : number);
            }

            html.Append("</li>");
        }

        html.Append("</ul>");
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
