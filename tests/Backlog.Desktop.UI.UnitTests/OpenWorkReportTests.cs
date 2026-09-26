using Backlog.Desktop.UI.Tasks;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The arithmetic behind the Tasks pane's open-work summary and the report it opens.
/// <para>
/// Pure on purpose: the rows, today, readiness and the repository resolver are all
/// handed in, so nothing here needs a clock, a store or a pane. The pane tests in
/// <c>OpenWorkSummaryTests</c> prove it is fed the right rows; these prove what it
/// makes of them.
/// </para>
/// </summary>
public sealed class OpenWorkReportTests
{
    private static readonly DateOnly Today = new(2026, 9, 24);

    private static EntryRow Row(string meta, string title = "A task") =>
        new() { RawText = $"# {title}\n{meta}\n" };

    private static OpenWorkReport Build(
        IEnumerable<EntryRow> rows,
        Func<EntryRow, bool>? isReady = null,
        Func<EntryRow, IReadOnlyList<string>>? repositories = null) =>
        OpenWorkReport.Build(
            [.. rows],
            Today,
            isReady ?? (_ => true),
            repositories ?? (_ => []));

    [Fact]
    public void Totals_count_open_rows_and_leave_ticked_off_ones_out()
    {
        var totals = OpenWorkTotals.Of(
        [
            Row("`task` `effort:3`"),
            Row("`task` `effort:5`"),
            Row("`task`"),
            Row("`task` `effort:8` `completed:2026-09-20`")
        ]);

        Assert.Equal(3, totals.Open);
        Assert.Equal(8, totals.Points);
        Assert.Equal(1, totals.Unestimated);
    }

    [Fact]
    public void Zero_is_an_estimate_and_not_unestimated()
    {
        var totals = OpenWorkTotals.Of([Row("`task` `effort:0`"), Row("`task`")]);

        Assert.Equal(0, totals.Points);
        Assert.Equal(1, totals.Unestimated);
    }

    [Theory]
    [InlineData(new[] { "`task` `effort:3`", "`task` `effort:5`", "`task`" }, "3 open · 8 pts · 1 unest.")]
    [InlineData(new[] { "`task` `effort:1`" }, "1 open · 1 pt")]
    [InlineData(new[] { "`task` `effort:2`", "`task`", "`task`" }, "3 open · 2 pts · 2 unest.")]
    [InlineData(new string[0], "0 open · 0 pts")]
    public void The_summary_says_the_unestimated_count_only_when_there_is_one(string[] metas, string expected)
    {
        var totals = OpenWorkTotals.Of([.. metas.Select(meta => Row(meta))]);

        Assert.Equal(expected, totals.Summary);
    }

    [Fact]
    public void The_headline_counts_what_was_ticked_off_in_the_last_seven_days_including_today()
    {
        var report = Build(
        [
            Row("`task` `completed:2026-09-24`"),
            Row("`task` `completed:2026-09-18`"),
            Row("`task` `completed:2026-09-17`"),
            Row("`task` `completed:2026-09-25`"),
            Row("`task`")
        ]);

        // today-6 .. today: the 24th and the 18th. The 17th is a day too old, and a
        // completion dated tomorrow is not "in the last seven days" however it got there.
        Assert.Equal(2, report.DoneLastSevenDays);
        Assert.Equal(1, report.Totals.Open);
    }

    [Fact]
    public void Each_plan_gets_a_row_and_a_row_under_two_plans_counts_under_both()
    {
        var report = Build(
        [
            Row("`task` `+alpha` `effort:3`"),
            Row("`task` `+alpha` `+beta` `effort:2`"),
            Row("`task` `+beta`"),
            Row("`task` `+alpha` `effort:5` `completed:2026-09-01`"),
            Row("`task` `#release` `effort:1`")
        ]);

        var alpha = Assert.Single(report.Plans, plan => plan.Tag == "+alpha");
        Assert.Equal(2, alpha.OpenCount);
        Assert.Equal(5, alpha.OpenPoints);
        Assert.Equal(0, alpha.UnestimatedCount);
        Assert.Equal(5, alpha.DonePoints);
        Assert.Equal(10, alpha.TotalPoints);
        Assert.Equal("5 of 10 points done", alpha.Progress);
        Assert.Equal(50, alpha.DonePercent);

        var beta = Assert.Single(report.Plans, plan => plan.Tag == "+beta");
        Assert.Equal(2, beta.OpenCount);
        Assert.Equal(2, beta.OpenPoints);
        Assert.Equal(1, beta.UnestimatedCount);
        Assert.Equal("0 of 2 points done", beta.Progress);
        Assert.Equal(0, beta.DonePercent);

        // A general tag is not a plan: the row carrying only `#release` is No plan.
        var none = Assert.Single(report.Plans, plan => plan.IsNoPlan);
        Assert.Equal("No plan", none.Label);
        Assert.Equal(1, none.OpenCount);
        Assert.Equal(1, none.OpenPoints);
        Assert.Null(none.Progress);
        Assert.Null(none.DonePercent);
    }

    [Fact]
    public void A_fully_done_plan_is_still_listed_but_last()
    {
        var report = Build(
        [
            Row("`task` `+aardvark` `effort:3` `completed:2026-09-01`"),
            Row("`task` `+zebra` `effort:2`"),
            Row("`task`")
        ]);

        Assert.Equal(["+zebra", null, "+aardvark"], report.Plans.Select(plan => plan.Tag));

        var done = report.Plans[^1];
        Assert.Equal(0, done.OpenCount);
        Assert.Equal("3 of 3 points done", done.Progress);
        Assert.Equal(100, done.DonePercent);
    }

    [Fact]
    public void A_plan_with_no_points_has_no_fill_and_a_part_point_rounds_down()
    {
        var report = Build(
        [
            Row("`task` `+empty`"),
            Row("`task` `+third` `effort:1` `completed:2026-09-01`"),
            Row("`task` `+third` `effort:2`")
        ]);

        Assert.Null(Assert.Single(report.Plans, plan => plan.Tag == "+empty").DonePercent);
        Assert.Equal(33, Assert.Single(report.Plans, plan => plan.Tag == "+third").DonePercent);
    }

    [Fact]
    public void No_plan_is_absent_when_every_open_row_has_a_plan()
    {
        var report = Build([Row("`task` `+alpha`")]);

        Assert.DoesNotContain(report.Plans, plan => plan.IsNoPlan);
    }

    [Fact]
    public void Breakdowns_count_open_rows_by_status_priority_and_type()
    {
        var report = Build(
        [
            Row("`task` `!ready` `*high`"),
            Row("`task` `!ready` `*low`"),
            Row("`prompt` `!in-progress` `*high`"),
            Row("`idea` `!ready` `*high` `completed:2026-09-20`")
        ]);

        Assert.Equal([new OpenWorkCount("Ready", 2), new OpenWorkCount("In progress", 1)], report.ByStatus);
        Assert.Equal([new OpenWorkCount("High", 2), new OpenWorkCount("Low", 1)], report.ByPriority);
        Assert.Equal([new OpenWorkCount("Prompt", 1), new OpenWorkCount("Task", 2)], report.ByType);
    }

    [Fact]
    public void A_row_counts_under_every_repository_it_resolves_to_and_no_repo_otherwise()
    {
        var both = Row("`task`", "Both");
        var docs = Row("`task`", "Docs");
        var nowhere = Row("`task`", "Nowhere");

        var report = Build(
            [both, docs, nowhere],
            repositories: row => row.PreviewTitle switch
            {
                "Both" => ["backlog", "docs"],
                "Docs" => ["docs"],
                _ => []
            });

        Assert.Equal(
            [new OpenWorkCount("docs", 2), new OpenWorkCount("backlog", 1), new OpenWorkCount("No repo", 1)],
            report.ByRepository);
    }

    [Fact]
    public void Overdue_is_before_today_and_due_soon_is_today_through_six_days_on()
    {
        var report = Build(
        [
            Row("`task` `due:2026-09-23`", "Yesterday"),
            Row("`task` `due:2026-09-10`", "Long ago"),
            Row("`task` `due:2026-09-24`", "Today"),
            Row("`task` `due:2026-09-30`", "Sixth day"),
            Row("`task` `due:2026-10-01`", "Seventh day"),
            Row("`task` `due:2026-09-01` `completed:2026-09-02`", "Done late")
        ]);

        Assert.Equal(["Long ago", "Yesterday"], report.Overdue.Select(task => task.Title));
        Assert.Equal(["Today", "Sixth day"], report.DueSoon.Select(task => task.Title));
    }

    [Fact]
    public void Waiting_work_is_counted_and_weighed_not_listed()
    {
        var publish = Row("`task` `effort:3`", "Publish it");
        var announce = Row("`task` `effort:2`", "Announce it");
        var unsized = Row("`task`", "Tidy up after");
        var free = Row("`task` `effort:8`", "Renew the certificate");
        var finished = Row("`task` `effort:5` `completed:2026-09-20`", "Old wait");
        var waiting = new[] { publish, announce, unsized, finished };

        var report = Build(
            [publish, announce, unsized, free, finished],
            isReady: row => !waiting.Contains(row));

        Assert.Equal(new OpenWorkTotals(3, 5, 1), report.Waiting);
        Assert.Equal("3 tasks · 5 points · 1 unestimated", report.Waiting.WaitingSummary);
    }

    [Fact]
    public void One_waiting_task_reads_in_the_singular()
    {
        var report = Build([Row("`task` `effort:1`")], isReady: _ => false);

        Assert.Equal("1 task · 1 point", report.Waiting.WaitingSummary);
    }

    [Fact]
    public void An_untitled_task_is_called_untitled()
    {
        var report = Build([new EntryRow { RawText = "# \n`task` `due:2026-09-01`\n" }]);

        Assert.Equal("Untitled", Assert.Single(report.Overdue).Title);
    }

    [Fact]
    public void Nothing_open_is_an_empty_report()
    {
        var report = Build([Row("`task` `effort:3` `completed:2026-09-22`")]);

        Assert.True(report.IsEmpty);
        Assert.Equal(1, report.DoneLastSevenDays);
        Assert.Empty(report.ByStatus);
        Assert.Empty(report.Overdue);
        Assert.Equal(0, report.Waiting.Open);
    }
}
