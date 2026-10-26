using Backlog.UI.Components.Progress;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The stand-up as plain text for the clipboard
/// (<c>.devbook/domain/progress/requirements.md#the-stand-up</c>): four blocks in order —
/// Goals, Finished, Planned, Impediments — each heading with its count, Finished grouped
/// by goal, Planned grouped by goal with No goal last, and every line a fixed template.
/// </summary>
public sealed class StandUpClipboardTests
{
    private static DateOnly Oct(int day) => new(2026, 10, day);

    private static GoalsBoardModel Board(StandUpModel standUp, params GoalCardModel[] goals) =>
        new(GoalsIteration.Week, new GoalsWindow(Oct(12), Oct(16)), new GoalsWindow(Oct(19), Oct(23)), goals, [], standUp, []);

    private static readonly GoalCardModel Paid = new(
        "g1", "Customers get paid faster", Oct(30), new DateOnly(2026, 11, 6), true,
        new GoalCounts(9, 15, 3, 1, 2, 3), [], [], []);

    private static readonly GoalCardModel Search = new(
        "g2", "Devbook search ships", Oct(23), null, false,
        new GoalCounts(5, 6, 0, 0, 1, 0), [], [], []);

    private static readonly StandUpModel Week = new(
        Finished:
        [
            new StandUpGroup("Customers get paid faster", [new StandUpEntry("Invoice PDF export"), new StandUpEntry("Reminder emails")])
        ],
        Planned:
        [
            new StandUpGroup("Customers get paid faster", [new StandUpEntry("Customer search by IBAN"), new StandUpEntry("Recurring invoices")]),
            new StandUpGroup(null, [new StandUpEntry("Renew the signing certificate")])
        ],
        PlannedEffort: 21,
        Capacity: 18,
        Impediments:
        [
            new StandUpImpediment("i1", "Bank sample files missing", StandUpImpedimentState.Escalated, [], "Account manager", 3, null, "Customers get paid faster"),
            new StandUpImpediment("i2", "Credit note numbering unclear", StandUpImpedimentState.New, [], null, 1, null, null)
        ],
        ResolvedInLast: 1);

    private static string[] Lines(string text) => text.Split("\r\n");

    /// <summary>Scenario "A week's stand-up": four blocks, in order, each with its count.</summary>
    [Fact]
    public void The_four_blocks_head_with_their_counts_in_order()
    {
        var lines = Lines(StandUpClipboard.Text(Board(Week, Paid, Search)));

        var headings = lines.Where(line => line.StartsWith("Goals ", StringComparison.Ordinal)
                                           || line.StartsWith("Finished ", StringComparison.Ordinal)
                                           || line.StartsWith("Planned ", StringComparison.Ordinal)
                                           || line.StartsWith("Impediments ", StringComparison.Ordinal)).ToList();

        Assert.Equal(["Goals 2", "Finished 2", "Planned 3 · 21 / 18 pts", "Impediments 2"], headings);
    }

    [Fact]
    public void The_whole_text_is_fixed_templates_over_the_reading()
    {
        var expected = string.Join("\r\n",
            "Goals 2",
            "- Customers get paid faster: 9/15 · +3 · due 30 Oct · lands ~6 Nov · At risk",
            "- Devbook search ships: 5/6 · due 23 Oct · On track",
            "",
            "Finished 2",
            "Customers get paid faster",
            "- Invoice PDF export",
            "- Reminder emails",
            "",
            "Planned 3 · 21 / 18 pts",
            "Customers get paid faster",
            "- Customer search by IBAN",
            "- Recurring invoices",
            "No goal",
            "- Renew the signing certificate",
            "",
            "Impediments 2",
            "- Bank sample files missing · Escalated · Account manager · 3 days · Customers get paid faster",
            "- Credit note numbering unclear · New · Not reported yet · 1 day",
            "1 resolved");

        Assert.Equal(expected, StandUpClipboard.Text(Board(Week, Paid, Search)));
    }

    /// <summary>No goal is last under Planned, whatever order the reading handed the groups in.</summary>
    [Fact]
    public void No_goal_is_last_under_Planned()
    {
        var standUp = Week with
        {
            Planned =
            [
                new StandUpGroup(null, [new StandUpEntry("Renew the signing certificate")]),
                new StandUpGroup("Customers get paid faster", [new StandUpEntry("Customer search by IBAN")])
            ]
        };

        var lines = Lines(StandUpClipboard.Text(Board(standUp, Paid))).ToList();

        Assert.True(lines.IndexOf("No goal") > lines.IndexOf("- Customer search by IBAN"));
    }

    /// <summary>An impediment moved on with nobody named copies without "Not reported yet":
    /// only a New one reads that.</summary>
    [Fact]
    public void A_later_impediment_with_nobody_named_copies_without_not_reported_yet()
    {
        var standUp = Week with
        {
            Impediments =
            [
                new StandUpImpediment("i3", "Sandbox keys expired", StandUpImpedimentState.BeingHandled, [], null, 0, null, null),
                new StandUpImpediment("i2", "Credit note numbering unclear", StandUpImpedimentState.New, [], null, 1, null, null)
            ]
        };

        var lines = Lines(StandUpClipboard.Text(Board(standUp, Paid)));

        Assert.Contains("- Sandbox keys expired · Being handled · 0 days", lines);
        Assert.Contains("- Credit note numbering unclear · New · Not reported yet · 1 day", lines);
    }

    [Fact]
    public void An_empty_stand_up_still_has_its_four_headings_and_no_resolved_line()
    {
        var lines = Lines(StandUpClipboard.Text(Board(StandUpModel.Empty)));

        Assert.Equal(["Goals 0", "", "Finished 0", "", "Planned 0 · 0 pts", "", "Impediments 0"], lines);
    }
}
