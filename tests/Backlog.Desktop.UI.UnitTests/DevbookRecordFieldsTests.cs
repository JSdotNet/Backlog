namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The record fields devbook 1.28.0 defines, as the chapter parser reads them: a debt
/// record's <c>severity</c> and its place on the debt ladder, the C4 fields any arc42
/// or domain block may carry, and a decision record's <c>## History</c> table — the
/// one place a concern's superseded choices are written down.
/// </summary>
public sealed class DevbookRecordFieldsTests
{
    [Fact]
    public void A_debt_record_reads_its_severity_and_status_on_the_debt_ladder()
    {
        var document = DevbookMarkdownParser.Parse(
            ".devbook/arc42/tdr/0001-renders.md",
            "# TDR 0001: Renders\n\n```meta\ndate: 2026-09-16\nseverity: critical\nstatus: in-progress\n```\n\nProse.\n");

        Assert.Equal("critical", document.Metadata.Severity);
        Assert.Equal(DevbookDebtSeverity.Critical, document.Severity);
        Assert.Equal(DevbookDebtLifecycle.InProgress, document.DebtLifecycle);
    }

    [Fact]
    public void A_debt_record_with_no_status_rests_at_identified()
    {
        var document = DevbookMarkdownParser.Parse(
            ".devbook/arc42/tdr/0002-x.md",
            "# TDR 0002\n\n```meta\nseverity: low\n```\n");

        Assert.Equal(DevbookDebtLifecycle.Identified, document.DebtLifecycle);
        Assert.Equal(DevbookDebtSeverity.Low, document.Severity);
    }

    [Theory]
    [InlineData("identified", DevbookDebtLifecycle.Identified)]
    [InlineData("planned", DevbookDebtLifecycle.Planned)]
    [InlineData("in-progress", DevbookDebtLifecycle.InProgress)]
    [InlineData("Resolved", DevbookDebtLifecycle.Resolved)]
    public void Every_rung_of_the_debt_ladder_is_read(string status, DevbookDebtLifecycle expected) =>
        Assert.Equal(expected, new DevbookMeta(status, []).DebtLifecycle);

    [Fact]
    public void A_status_off_the_debt_ladder_is_not_guessed_at()
    {
        // A statuses.json rule may replace the ladder; a value off it is that rule's, not a rung.
        Assert.Null(new DevbookMeta("blocked", []).DebtLifecycle);
        Assert.Null(new DevbookMeta(null, []) { Severity = "enormous" }.DebtSeverity);
    }

    [Fact]
    public void A_record_outside_tdr_has_no_debt_lifecycle_or_severity()
    {
        var document = DevbookMarkdownParser.Parse(
            ".devbook/arc42/adr/0001-desktop.md",
            "# ADR 0001\n\n```meta\nstatus: proposed\n```\n");

        Assert.Null(document.DebtLifecycle);
        Assert.Null(document.Severity);
    }

    [Fact]
    public void The_C4_fields_and_code_are_read_in_both_list_forms()
    {
        var document = DevbookMarkdownParser.Parse(
            ".devbook/arc42/building-blocks/devbook.md",
            "# Devbook\n\n```meta\nc4: [backlog.desktop.devbook]\nc4-touches:\n  - backlog.store\n  - \"github\"\ncode: [src/Modules/Devbook, 'tools/devbook']\n```\n");

        Assert.Equal(["backlog.desktop.devbook"], document.Metadata.C4);
        Assert.Equal(["backlog.store", "github"], document.Metadata.C4Touches);
        Assert.Equal(["src/Modules/Devbook", "tools/devbook"], document.Metadata.Code);
    }

    [Fact]
    public void A_field_that_only_starts_like_c4_is_not_read_as_it()
    {
        // `c4-touches:` begins with `c4`; the parser must not take it for `c4:`.
        var meta = DevbookMarkdownParser.Parse(".arc42/x.md", "# X\n\n```meta\nc4-touches: [a]\n```\n").Metadata;

        Assert.Empty(meta.C4);
        Assert.Equal(["a"], meta.C4Touches);
    }

    [Fact]
    public void A_decision_records_history_is_read_newest_first_with_the_standing_choice_on_top()
    {
        var document = DevbookMarkdownParser.Parse(
            ".devbook/arc42/adr/0001-desktop.md",
            """
            # ADR 0001: Desktop

            ```meta
            ```

            MAUI.

            ## History

            ```meta
            ```

            | Date | Change | Why |
            | --- | --- | --- |
            | 2026-08-05 | MAUI replaces `WinUI 3`. | Aspire must start it. |
            | 2026-08-03 | Plain WinUI 3. | The first strategy preferred it. |
            """.Replace("\r\n", "\n", StringComparison.Ordinal));

        Assert.Equal(2, document.History.Count);
        Assert.Equal(new DevbookHistoryRow("2026-08-05", "MAUI replaces `WinUI 3`.", "Aspire must start it."), document.History[0]);
        Assert.Equal(document.History[0], document.StandingChoice);
        Assert.Equal(["2026-08-03"], document.Superseded.Select(row => row.Date));

        // The table is still drawn as a table.
        Assert.Contains(document.Blocks, block => block is DevbookTableBlock);
    }

    [Fact]
    public void An_escaped_pipe_stays_inside_its_history_cell()
    {
        var document = DevbookMarkdownParser.Parse(
            ".arc42/adr/a.md",
            "# A\n\n## History\n\n| Date | Change | Why |\n| --- | --- | --- |\n| 2026-01-01 | `a \\| b` replaces c. | Because. |\n");

        Assert.Equal("`a | b` replaces c.", Assert.Single(document.History).Change);
    }

    [Fact]
    public void A_table_with_another_header_or_under_another_heading_is_not_history()
    {
        var otherHeader = DevbookMarkdownParser.Parse(
            ".arc42/adr/a.md",
            "# A\n\n## History\n\n| When | What |\n| --- | --- |\n| 2026 | x |\n");
        var otherHeading = DevbookMarkdownParser.Parse(
            ".arc42/adr/b.md",
            "# B\n\n## Rejected\n\n| Date | Change | Why |\n| --- | --- | --- |\n| 2026 | x | y |\n");

        Assert.Empty(otherHeader.History);
        Assert.Empty(otherHeading.History);
        Assert.Null(otherHeading.StandingChoice);
    }
}
