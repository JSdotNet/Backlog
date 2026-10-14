using Backlog.UI.Components.Progress;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The table a sprint is copied as, for PowerPoint and Outlook
/// (<c>.devbook/domain/progress/features.md#copying-a-sprint-as-a-table</c>): an HTML
/// table carrying its whole look in inline styles, and the plain text beside it for
/// whatever cannot take HTML.
/// </summary>
public sealed class SprintTableClipboardTests
{
    [Fact]
    public void The_table_has_the_six_columns_in_order()
    {
        var table = SprintTableClipboard.Build([Row("Ship it", SprintRowState.Done, "rekeningen")], []);

        var header = Between(table.Html, "<thead>", "</thead>");
        var columns = header.Split("<th ").Skip(1).Select(cell => cell[(cell.IndexOf('>') + 1)..cell.IndexOf("</th>", StringComparison.Ordinal)]).ToList();
        Assert.Equal(["Plan", "Item", "Status", "Pull requests", "Owner", "Note"], columns);
        Assert.StartsWith("Plan\tItem\tStatus\tPull requests\tOwner\tNote", table.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_style_is_inline_with_no_class_and_no_style_sheet()
    {
        var table = SprintTableClipboard.Build(
            [Row("Ship it", SprintRowState.Done, "rekeningen", pulls: [Pull("JSdotNet/Backlog", "12", SprintPullRequestState.Merged)])],
            []);

        Assert.DoesNotContain("class=", table.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<style", table.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("var(", table.Html, StringComparison.Ordinal);
        Assert.Contains("<table style=\"", table.Html, StringComparison.Ordinal);
        Assert.Contains("<td style=\"", table.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void One_row_per_item_grouped_by_plan_goal_plans_first_and_no_plan_last()
    {
        var table = SprintTableClipboard.Build(
            [
                Row("Loose end", SprintRowState.NotStarted, null),
                Row("Move specs", SprintRowState.InProgress, "verplaatsen"),
                Row("Accounts", SprintRowState.Done, "rekeningen"),
                Row("More accounts", SprintRowState.Blocked, "rekeningen")
            ],
            ["verplaatsen"]);

        var body = Between(table.Html, "<tbody>", "</tbody>");
        Assert.Equal(4, body.Split("<tr>").Length - 1);

        var order = new[] { "Move specs", "Accounts", "More accounts", "Loose end" }.Select(title => body.IndexOf(title, StringComparison.Ordinal)).ToList();
        Assert.Equal(order.OrderBy(index => index), order);
        Assert.DoesNotContain(-1, order);

        // The plan is named once, on its group's first row.
        Assert.Equal(1, Occurrences(body, "+rekeningen"));
        Assert.Contains("No plan", body, StringComparison.Ordinal);

        var lines = table.Text.Split("\r\n");
        Assert.Equal(5, lines.Length);
        Assert.Equal("+verplaatsen\tMove specs", string.Join('\t', lines[1].Split('\t').Take(2)));
        Assert.Equal("+rekeningen\tAccounts", string.Join('\t', lines[2].Split('\t').Take(2)));
        Assert.Equal("\tMore accounts", string.Join('\t', lines[3].Split('\t').Take(2)));
        Assert.Equal("No plan\tLoose end", string.Join('\t', lines[4].Split('\t').Take(2)));
    }

    [Fact]
    public void Pull_request_numbers_link_to_github_and_say_their_state()
    {
        var table = SprintTableClipboard.Build(
            [Row("Ship it", SprintRowState.InProgress, "p", pulls:
            [
                Pull("JSdotNet/Backlog", "4530", SprintPullRequestState.Merged),
                Pull("JSdotNet/Backlog", "4707", SprintPullRequestState.Open),
                Pull("JSdotNet/Other", "9", SprintPullRequestState.Draft)
            ])],
            []);

        Assert.Contains("<a href=\"https://github.com/JSdotNet/Backlog/pull/4530\" style=\"", table.Html, StringComparison.Ordinal);
        Assert.Contains(">#4530</a>", table.Html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://github.com/JSdotNet/Backlog/pull/4707\"", table.Html, StringComparison.Ordinal);
        Assert.Contains(">#4707</a> open", table.Html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://github.com/JSdotNet/Other/pull/9\"", table.Html, StringComparison.Ordinal);
        Assert.Contains(">#9</a> draft", table.Html, StringComparison.Ordinal);
        Assert.Contains("\t#4530, #4707 open, #9 draft\t", table.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pull_request_with_no_owner_and_name_is_named_without_a_link()
    {
        var table = SprintTableClipboard.Build(
            [Row("Ship it", SprintRowState.Done, "p", pulls: [Pull("backlog", "3", SprintPullRequestState.Merged)])],
            []);

        Assert.DoesNotContain("<a ", table.Html, StringComparison.Ordinal);
        Assert.Contains("#3", table.Html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SprintRowState.Done, false, "Done", "#1A7F37")]
    [InlineData(SprintRowState.InProgress, false, "In progress", "#9A6700")]
    [InlineData(SprintRowState.InProgress, true, "In review", "#9A6700")]
    [InlineData(SprintRowState.Blocked, false, "Blocked", "#CF222E")]
    [InlineData(SprintRowState.NotStarted, false, "Not started", "#6E7781")]
    public void The_status_is_coloured_text(SprintRowState state, bool inReview, string text, string colour)
    {
        IReadOnlyList<SprintBoardPullRequest> pulls = inReview ? [Pull("JSdotNet/Backlog", "1", SprintPullRequestState.Open)] : [];
        var table = SprintTableClipboard.Build([Row("Ship it", state, "p", pulls: pulls)], []);

        Assert.Contains($"color:{colour};font-weight:600\">{text}</span>", table.Html, StringComparison.Ordinal);
        Assert.Equal(text, table.Text.Split("\r\n")[1].Split('\t')[2]);
    }

    [Fact]
    public void Owners_and_the_note_are_carried_and_an_open_question_says_so()
    {
        var table = SprintTableClipboard.Build(
            [
                Row("Ship it", SprintRowState.InProgress, "p", owners: ["ada", "grace"], note: "Waits on design"),
                Row("Ask it", SprintRowState.NotStarted, "p", note: "Which bank?", openQuestion: true)
            ],
            []);

        Assert.Contains(">ada, grace</td>", table.Html, StringComparison.Ordinal);
        Assert.Contains(">Waits on design</td>", table.Html, StringComparison.Ordinal);
        Assert.Contains(">Open question: Which bank?</td>", table.Html, StringComparison.Ordinal);
        Assert.EndsWith("\tOpen question: Which bank?", table.Text.Split("\r\n")[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Text_is_encoded_in_the_html_and_flattened_in_the_plain_text()
    {
        var table = SprintTableClipboard.Build(
            [Row("<script>a & b</script>", SprintRowState.Done, "p", note: "one\ttwo\r\nthree")],
            []);

        Assert.DoesNotContain("<script>", table.Html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;a &amp; b&lt;/script&gt;", table.Html, StringComparison.Ordinal);

        var cells = table.Text.Split("\r\n")[1].Split('\t');
        Assert.Equal(6, cells.Length);
        Assert.Equal("one two three", cells[5]);
    }

    [Fact]
    public void A_sprint_with_no_items_copies_the_header_alone()
    {
        var table = SprintTableClipboard.Build([], []);

        Assert.Contains("<tbody></tbody>", table.Html, StringComparison.Ordinal);
        Assert.Equal("Plan\tItem\tStatus\tPull requests\tOwner\tNote", table.Text);
    }

    [Fact]
    public void A_board_is_copied_whole_with_its_goal_plans_first()
    {
        var board = new SprintBoardModel(
            "Team", "Sprint 42", "Mon 5 Oct – Fri 16 Oct", ["backlog"], false, false, "Ship it",
            SprintBoardCounts.None, "+b", false, SprintBoardCounts.None, "Day 1 of 10", [], 10, string.Empty, ["b"],
            [Row("From a", SprintRowState.Done, "a"), Row("From b", SprintRowState.Done, "b")]);

        var table = SprintTableClipboard.Build(board);

        Assert.True(table.Html.IndexOf("From b", StringComparison.Ordinal) < table.Html.IndexOf("From a", StringComparison.Ordinal));
    }

    private static SprintBoardRow Row(
        string title,
        SprintRowState state,
        string? plan,
        IReadOnlyList<SprintBoardPullRequest>? pulls = null,
        IReadOnlyList<string>? owners = null,
        string? note = null,
        bool openQuestion = false) =>
        new(Guid.NewGuid().ToString(), title, state, plan, "backlog", false, false, pulls ?? [], owners ?? [], note, openQuestion);

    private static SprintBoardPullRequest Pull(string repository, string number, SprintPullRequestState state) =>
        new(repository, number, state);

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal) + start.Length;
        return text[from..text.IndexOf(end, from, StringComparison.Ordinal)];
    }

    private static int Occurrences(string text, string value) =>
        (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;
}
