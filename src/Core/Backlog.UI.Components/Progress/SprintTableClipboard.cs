using System.Net;
using System.Text;

namespace Backlog.UI.Components.Progress;

/// <summary>
/// One sprint as a table to paste into PowerPoint or a mail
/// (<c>.devbook/domain/progress/features.md#copying-a-sprint-as-a-table</c>): the HTML a
/// rich editor pastes as a formatted table, and the plain text beside it for whatever
/// takes no HTML. Hand both to <see cref="Buttons.CopyButton"/> — <c>Html</c> and
/// <c>Text</c> — and the clipboard carries <c>text/html</c> and <c>text/plain</c>
/// together.
/// </summary>
/// <param name="Html">A <c>&lt;table&gt;</c> whose look is in inline styles alone:
/// PowerPoint and Outlook drop a style sheet, a class and a custom property on paste,
/// so the colours are literal values fit for a white page.</param>
/// <param name="Text">The same rows, tab-separated, one line each, under a header line.</param>
public sealed record SprintTable(string Html, string Text);

/// <summary>
/// Builds the <see cref="SprintTable"/> a sprint is copied as. Pure, so the sprint view,
/// the daily strip and their tests read the same table. Columns: Plan, Item, Status,
/// Pull requests, Owner, Note; one row per item, grouped by plan as the board groups
/// them — goal plans first, then by name, the items under no plan last — the plan named
/// on its group's first row.
/// </summary>
public static class SprintTableClipboard
{
    /// <summary>The column headings, in order.</summary>
    public static IReadOnlyList<string> Columns { get; } = ["Plan", "Item", "Status", "Pull requests", "Owner", "Note"];

    private const string NoPlan = "No plan";

    /// <summary>Line breaks in the plain text are Windows', where PowerPoint and Outlook
    /// run, whatever the host is.</summary>
    private const string NewLine = "\r\n";

    private const string Border = "border:1px solid #D0D7DE";
    private const string TableStyle = "border-collapse:collapse;font-family:'Segoe UI',Arial,sans-serif;font-size:11pt;color:#1F2328";
    private const string HeadStyle = $"{Border};padding:4px 8px;text-align:left;vertical-align:top;background:#F6F8FA;font-weight:600";
    private const string CellStyle = $"{Border};padding:4px 8px;text-align:left;vertical-align:top";
    private const string LinkStyle = "color:#0969DA;text-decoration:underline";

    /// <summary>Every row of <paramref name="board"/>, whatever the board's filter and
    /// grouping show at the moment.</summary>
    public static SprintTable Build(SprintBoardModel board)
    {
        ArgumentNullException.ThrowIfNull(board);
        return Build(board.Rows, board.GoalPlans);
    }

    /// <summary>The table over <paramref name="rows"/>, the plans an "in these plans" goal
    /// rule names leading.</summary>
    public static SprintTable Build(IEnumerable<SprintBoardRow> rows, IReadOnlyCollection<string> goalPlans)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(goalPlans);

        var groups = SprintBoardRules.Group(rows, goalPlans, byPlan: true);

        var html = new StringBuilder();
        html.Append($"<table style=\"{TableStyle}\"><thead><tr>");
        foreach (var column in Columns) html.Append($"<th style=\"{HeadStyle}\">{column}</th>");
        html.Append("</tr></thead><tbody>");

        var text = new StringBuilder(string.Join('\t', Columns));

        foreach (var group in groups)
        {
            var first = true;
            foreach (var row in group.Rows)
            {
                var plan = first ? PlanName(group.Plan) : string.Empty;
                first = false;

                html.Append("<tr>")
                    .Append(Cell(Encode(plan)))
                    .Append(Cell(Encode(row.Title)))
                    .Append(Cell($"<span style=\"color:{StatusColour(row)};font-weight:600\">{Encode(row.StatusText)}</span>"))
                    .Append(Cell(string.Join(", ", row.PullRequests.Select(PullRequestHtml))))
                    .Append(Cell(Encode(string.Join(", ", row.Owners))))
                    .Append(Cell(Encode(Note(row))))
                    .Append("</tr>");

                text.Append(NewLine).Append(string.Join('\t',
                    Flat(plan),
                    Flat(row.Title),
                    Flat(row.StatusText),
                    Flat(string.Join(", ", row.PullRequests.Select(PullRequestText))),
                    Flat(string.Join(", ", row.Owners)),
                    Flat(Note(row))));
            }
        }

        html.Append("</tbody></table>");
        return new SprintTable(html.ToString(), text.ToString());
    }

    /// <summary>
    /// The colour a status is written in, on a white page: done green, in progress and in
    /// review amber, blocked red, not started grey — the sprint view's status colours,
    /// taken to their light-page values because a pasted table never sees the app's
    /// theme.
    /// </summary>
    public static string StatusColour(SprintBoardRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.State switch
        {
            SprintRowState.Done => "#1A7F37",
            SprintRowState.InProgress => "#9A6700",
            SprintRowState.Blocked => "#CF222E",
            _ => "#6E7781"
        };
    }

    /// <summary>The pull request on github.com, or null when its repository is not an
    /// <c>owner/name</c> to build one from. A GitHub Enterprise host is not linked: the
    /// row carries no host.</summary>
    public static string? PullRequestUrl(SprintBoardPullRequest pull)
    {
        ArgumentNullException.ThrowIfNull(pull);

        var parts = pull.Repository.Trim().Split('/');
        return parts.Length == 2 && parts.All(part => part.Length > 0) && pull.Number.Trim().Length > 0
            ? $"https://github.com/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}/pull/{Uri.EscapeDataString(pull.Number.Trim())}"
            : null;
    }

    private static string PullRequestHtml(SprintBoardPullRequest pull)
    {
        var number = Encode($"#{pull.Number.Trim()}");
        var link = PullRequestUrl(pull) is { } url
            ? $"<a href=\"{Encode(url)}\" style=\"{LinkStyle}\">{number}</a>"
            : number;
        return StateWord(pull.State) is { } word ? $"{link} {word}" : link;
    }

    private static string PullRequestText(SprintBoardPullRequest pull) =>
        StateWord(pull.State) is { } word ? $"#{pull.Number.Trim()} {word}" : $"#{pull.Number.Trim()}";

    /// <summary>A merged or unread pull request is its number alone, as on the board's
    /// chip (<see cref="SprintBoardPullRequest.Label"/>); the rest say what they are. A
    /// draft keeps its number here, where the chip drops it, because a pasted table has
    /// no chip to press.</summary>
    private static string? StateWord(SprintPullRequestState state) => state switch
    {
        SprintPullRequestState.Open => "open",
        SprintPullRequestState.Draft => "draft",
        SprintPullRequestState.Closed => "closed",
        _ => null
    };

    private static string PlanName(string? plan) => plan is null ? NoPlan : $"+{plan}";

    private static string Note(SprintBoardRow row)
    {
        var note = row.Note?.Trim() ?? string.Empty;
        if (!row.OpenQuestion) return note;
        return note.Length == 0 ? "Open question" : $"Open question: {note}";
    }

    private static string Cell(string content) => $"<td style=\"{CellStyle}\">{content}</td>";

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    /// <summary>One cell of the plain text: a tab or a line break inside it would split
    /// the row, so each run of them becomes one space.</summary>
    private static string Flat(string value) =>
        string.Join(' ', value.Split(['\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
