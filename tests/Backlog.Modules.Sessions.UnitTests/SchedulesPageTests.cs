using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI;
using Bunit;
using static Backlog.Modules.Sessions.UnitTests.ScheduleFixtures;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// The Schedules page on its own (local ADR 0051 §6;
/// <c>.devbook/domain/sessions/features.md#what-the-schedules-page-shows</c>): the way back,
/// the Show filter and its counts, the cards of the schedules that need the person, the
/// table by repository, and the selected schedule's detail with its recent runs and what
/// holds it. The states come as given; <c>ScheduleReadingTests</c> decides them.
/// </summary>
public sealed class SchedulesPageTests
{
    /// <summary>Friday 6 November 2026, 05:41 UTC: the issue sweep run that holds the schedule.</summary>
    private static readonly DateTimeOffset Friday = new(2026, 11, 6, 5, 41, 0, TimeSpan.Zero);

    private static ScheduleReadingResult IssueSweep() => Schedule(
        "issue-sweep", "Issue sweep", "JSdotNet/Backlog", ScheduledRunState.Held,
        cron: "30 5 * * 1-5",
        nextRunAt: new DateTimeOffset(2026, 11, 12, 5, 30, 0, TimeSpan.Zero),
        runs:
        [
            Run("tue", ScheduledRunState.Held, new DateTimeOffset(2026, 11, 10, 5, 40, 0, TimeSpan.Zero), skipped: true, heldBy: "fri"),
            Run("mon", ScheduledRunState.Held, new DateTimeOffset(2026, 11, 9, 5, 40, 0, TimeSpan.Zero), skipped: true, heldBy: "fri"),
            Run("fri", ScheduledRunState.NeedsYou, Friday, needsYou: ["Answer the proposals in the brief"]),
            Run("thu", ScheduledRunState.Handled, new DateTimeOffset(2026, 11, 5, 5, 40, 0, TimeSpan.Zero), verdict: "Two issues resolved as drafts"),
        ]);

    private static ScheduleReadingResult WeeklyUpdate() => Schedule(
        "weekly-update", "Weekly update", "JSdotNet/Backlog", ScheduledRunState.NeedsYou,
        cron: "0 18 * * 0",
        runs:
        [
            Run("sun", ScheduledRunState.NeedsYou, new DateTimeOffset(2026, 11, 8, 18, 0, 0, TimeSpan.Zero),
                needsYou: ["Review two pull requests", "Look at a flaky test run", "Follow up on two issues", "Close the stale branch"]),
        ]);

    private static ScheduleReadingResult ChangeReport() => Schedule(
        "change-report", "Change report", "JSdotNet/Backlog", ScheduledRunState.ReviewReady,
        cron: "0 17 * * 0",
        runs: [Run("report", ScheduledRunState.ReviewReady, new DateTimeOffset(2026, 11, 8, 17, 0, 0, TimeSpan.Zero), verdict: "Report ready")]);

    private static ScheduleReadingResult PackageUpdate() => Schedule(
        "package-update", "Package update", "finance", ScheduledRunState.Failed,
        cron: "0 5 * * 6",
        runs: [Run("sat", ScheduledRunState.Failed, new DateTimeOffset(2026, 11, 7, 5, 0, 0, TimeSpan.Zero), verdict: "restore failed", failed: true)]);

    private static ScheduleReadingResult DevbookValidate() => Schedule(
        "devbook-validate", "Devbook validate", "JSdotNet/devbook", ScheduledRunState.Missed,
        cron: "0 4 * * *",
        dueAt: new DateTimeOffset(2026, 11, 11, 4, 0, 0, TimeSpan.Zero),
        runs: [Run("validate", ScheduledRunState.Handled, new DateTimeOffset(2026, 11, 10, 4, 8, 0, TimeSpan.Zero))]);

    private static ScheduleReadingResult[] All() =>
        [IssueSweep(), WeeklyUpdate(), ChangeReport(), PackageUpdate(), DevbookValidate()];

    private static IRenderedComponent<SchedulesPage> Render(
        BunitContext context,
        IReadOnlyList<ScheduleReadingResult> schedules,
        Func<string, bool>? openRun = null,
        Action? back = null) =>
        context.Render<SchedulesPage>(parameters => parameters
            .Add(p => p.Schedules, schedules)
            .Add(p => p.Now, Now)
            .Add(p => p.Zone, TimeZoneInfo.Utc)
            .Add(p => p.OpenRun, openRun)
            .Add(p => p.OnBack, () => back?.Invoke()));

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context;
    }

    [Fact]
    public void A_Sessions_breadcrumb_leads_back_and_names_the_page_as_where_the_reader_is()
    {
        using var context = Context();
        var backs = 0;
        var page = Render(context, All(), back: () => backs++);

        var breadcrumb = page.Find("[data-testid='schedules-breadcrumb']");
        Assert.Equal("Schedules", breadcrumb.QuerySelector("[aria-current='page']")!.TextContent.Trim());

        page.Find("[data-testid='schedules-back']").Click();

        Assert.Equal(1, backs);
        Assert.Equal("Sessions", page.Find("[data-testid='schedules-back']").TextContent.Trim());
    }

    [Fact]
    public void The_line_under_the_title_counts_the_schedules_and_their_repositories()
    {
        using var context = Context();
        var page = Render(context, All());

        Assert.StartsWith(
            "5 schedules across 3 repositories",
            page.Find("[data-testid='schedules-subtitle']").TextContent.Trim(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_Show_filter_offers_four_options_each_with_its_count()
    {
        using var context = Context();
        var page = Render(context, All());

        var options = page.FindAll("[data-testid='schedules-show'] [data-testid^='schedules-show-'][aria-pressed]");

        Assert.Equal(
            ["All 5", "Needs you 2", "Review ready 1", "Failed or missed 2"],
            options.Select(option => string.Join(' ', option.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));
        Assert.Equal("true", page.Find("[data-testid='schedules-show-all']").GetAttribute("aria-pressed"));
    }

    [Theory]
    [InlineData("needs-you", new[] { "issue-sweep", "weekly-update" })]
    [InlineData("review-ready", new[] { "change-report" })]
    [InlineData("failed-or-missed", new[] { "package-update", "devbook-validate" })]
    public void A_filter_narrows_the_table_to_its_states(string filter, string[] expected)
    {
        using var context = Context();
        var page = Render(context, All());

        page.Find($"[data-testid='schedules-show-{filter}']").Click();

        Assert.Equal("true", page.Find($"[data-testid='schedules-show-{filter}']").GetAttribute("aria-pressed"));
        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            page.FindAll("[data-testid='schedules-row']").Select(row => row.GetAttribute("data-schedule")!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_schedules_that_need_the_person_lead_the_page_as_cards()
    {
        using var context = Context();
        var page = Render(context, All());

        var cards = page.FindAll("[data-testid='schedules-card']");
        Assert.Equal(["issue-sweep", "weekly-update"], cards.Select(card => card.GetAttribute("data-schedule")));

        var held = cards[0];
        Assert.Equal("Held", held.QuerySelector("[data-testid='schedules-card-chip']")!.TextContent.Trim());
        Assert.Equal("since Fri 6 Nov", held.QuerySelector("[data-testid='schedules-card-since']")!.TextContent.Trim());
        Assert.Equal("JSdotNet/Backlog", held.QuerySelector("[data-testid='schedules-repo']")!.TextContent.Trim());
        Assert.Equal(
            "Answer the Fri 6 Nov run. Until it is archived, every run skips itself (2 skipped so far).",
            held.QuerySelector("[data-testid='schedules-card-ask']")!.TextContent.Trim());
        Assert.Equal("Open the Fri 6 Nov run", held.QuerySelector("[data-testid='schedules-card-open']")!.TextContent.Trim());
        Assert.Equal("All runs", held.QuerySelector("[data-testid='schedules-card-runs']")!.TextContent.Trim());
    }

    [Fact]
    public void A_needs_you_card_lists_what_its_run_asks_three_lines_and_how_many_more()
    {
        using var context = Context();
        var page = Render(context, All());

        var card = page.FindAll("[data-testid='schedules-card']")[1];
        var lines = card.QuerySelectorAll("[data-testid='schedules-card-ask'] li").Select(line => line.TextContent.Trim()).ToList();

        Assert.Equal(["Review two pull requests", "Look at a flaky test run", "Follow up on two issues", "and 1 more"], lines);
        Assert.Equal("Open run", card.QuerySelector("[data-testid='schedules-card-open']")!.TextContent.Trim());
        Assert.Equal("Sun 8 Nov", card.QuerySelector("[data-testid='schedules-card-since']")!.TextContent.Trim());
    }

    [Fact]
    public void Open_run_opens_the_run_that_holds_a_held_schedule_and_the_latest_run_otherwise()
    {
        using var context = Context();
        var opened = new List<string>();
        var page = Render(context, All(), openRun: id =>
        {
            opened.Add(id);
            return true;
        });

        var cards = page.FindAll("[data-testid='schedules-card']");
        cards[0].QuerySelector("[data-testid='schedules-card-open']")!.Click();
        page.FindAll("[data-testid='schedules-card']")[1].QuerySelector("[data-testid='schedules-card-open']")!.Click();

        Assert.Equal(["fri", "sun"], opened);
        Assert.Empty(page.FindAll("[data-testid='schedules-open-missing']"));
    }

    [Fact]
    public void A_run_the_list_holds_no_record_of_is_said_on_the_page()
    {
        using var context = Context();
        var page = Render(context, All(), openRun: _ => false);

        page.FindAll("[data-testid='schedules-card']")[1].QuerySelector("[data-testid='schedules-card-open']")!.Click();

        Assert.Contains("does not show the Sun 8 Nov run's session (sun)", page.Find("[data-testid='schedules-open-missing']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void All_runs_shows_the_schedules_runs_in_the_detail()
    {
        using var context = Context();
        var page = Render(context, All());

        page.FindAll("[data-testid='schedules-card']")[1].QuerySelector("[data-testid='schedules-card-runs']")!.Click();

        var detail = page.Find("[data-testid='schedules-detail']");
        Assert.Equal("weekly-update", detail.GetAttribute("data-schedule"));
        Assert.Equal("Weekly update", detail.QuerySelector("[data-testid='schedules-detail-title']")!.TextContent.Trim());
        Assert.Equal(["sun"], detail.QuerySelectorAll("[data-testid='schedules-detail-run']").Select(run => run.GetAttribute("data-session")));
    }

    [Fact]
    public void The_detail_follows_the_filter_to_a_schedule_the_table_shows()
    {
        using var context = Context();
        var page = Render(context, All());

        page.Find("[data-testid='schedules-show-needs-you']").Click();
        page.Find("[data-testid='schedules-row-name']").Click();
        page.Find("[data-testid='schedules-show-review-ready']").Click();

        // The detail follows the filter: never a schedule the table does not show.
        Assert.Equal("change-report", page.Find("[data-testid='schedules-detail']").GetAttribute("data-schedule"));
    }

    [Fact]
    public void The_table_has_a_section_per_repository_with_cadence_last_run_next_run_and_state()
    {
        using var context = Context();
        var page = Render(context, All());

        var headings = page.FindAll("[data-testid='schedules-table'] th[scope='colgroup']")
            .Select(heading => heading.QuerySelector(".data-table__group-name")!.TextContent.Trim());
        Assert.Equal(["finance", "JSdotNet/Backlog", "JSdotNet/devbook"], headings);

        Assert.Equal(
            ["Schedule", "Cadence", "Last run", "Next run", "State"],
            page.FindAll("[data-testid='schedules-table'] thead th").Select(th => th.TextContent.Trim()));

        var sweep = page.Find("[data-testid='schedules-row'][data-schedule='issue-sweep']");
        Assert.Equal("Issue sweep", sweep.QuerySelector("[data-testid='schedules-row-name']")!.TextContent.Trim());
        Assert.Equal("Mon–Fri 05:30", sweep.QuerySelector("[data-testid='schedules-row-cadence']")!.TextContent.Trim());
        Assert.Equal("Tue 05:40 · skipped", sweep.QuerySelector("[data-testid='schedules-row-last']")!.TextContent.Trim());
        Assert.Equal("Thu 05:30", sweep.QuerySelector("[data-testid='schedules-row-next']")!.TextContent.Trim());
        Assert.Equal("Held", sweep.QuerySelector("[data-testid='schedules-row-chip']")!.TextContent.Trim());

        var update = page.Find("[data-testid='schedules-row'][data-schedule='weekly-update']");
        Assert.Equal("Sun 18:00", update.QuerySelector("[data-testid='schedules-row-cadence']")!.TextContent.Trim());
        Assert.Equal("Sun 18:00", update.QuerySelector("[data-testid='schedules-row-last']")!.TextContent.Trim());
    }

    [Fact]
    public void Choosing_a_schedule_opens_its_detail_and_marks_its_row()
    {
        using var context = Context();
        var page = Render(context, All());

        page.Find("[data-testid='schedules-row'][data-schedule='package-update'] [data-testid='schedules-row-name']").Click();

        var detail = page.Find("[data-testid='schedules-detail']");
        Assert.Equal("package-update", detail.GetAttribute("data-schedule"));
        Assert.Equal("Sat 05:00 · next Thu 07:12", detail.QuerySelector("[data-testid='schedules-detail-cadence']")!.TextContent.Trim());
        Assert.Equal("Failed: restore failed", detail.QuerySelector("[data-testid='schedules-detail-run-note']")!.TextContent.Trim());
        Assert.Empty(detail.QuerySelectorAll("[data-testid='schedules-detail-held']"));

        var row = page.Find("[data-testid='schedules-row'][data-schedule='package-update']");
        Assert.Contains("schedules-page__row--selected", row.ClassName, StringComparison.Ordinal);
        Assert.Equal("true", row.QuerySelector("[data-testid='schedules-row-name']")!.GetAttribute("aria-pressed"));
    }

    [Fact]
    public void A_held_schedules_detail_explains_what_holds_it_and_lists_its_skipped_runs()
    {
        using var context = Context();
        var page = Render(context, All());

        page.Find("[data-testid='schedules-row'][data-schedule='issue-sweep'] [data-testid='schedules-row-name']").Click();

        var detail = page.Find("[data-testid='schedules-detail']");
        var held = detail.QuerySelector("[data-testid='schedules-detail-held']")!;
        Assert.Equal("note", held.GetAttribute("role"));
        Assert.Equal("Held since Fri 6 Nov.", held.QuerySelector("[data-testid='schedules-detail-held-heading']")!.TextContent.Trim());
        Assert.Equal(
            "Each run skips itself while that run is unarchived. Answer its brief, then archive it in Claude Desktop to release the schedule.",
            held.QuerySelector("[data-testid='schedules-detail-held-text']")!.TextContent.Trim());

        var runs = detail.QuerySelectorAll("[data-testid='schedules-detail-run']");
        Assert.Equal(["tue", "mon", "fri", "thu"], runs.Select(run => run.GetAttribute("data-session")));
        Assert.Equal(
            ["Skipped", "Skipped", "Needs you", "Handled"],
            runs.Select(run => run.QuerySelector("[data-testid='schedules-detail-run-chip']")!.TextContent.Trim()));
        Assert.Equal("Skipped: the Fri 6 Nov run is not archived", runs[0].QuerySelector("[data-testid='schedules-detail-run-note']")!.TextContent.Trim());
        Assert.Equal("Open the Fri 6 Nov run", detail.QuerySelector("[data-testid='schedules-detail-open']")!.TextContent.Trim());
    }

    [Fact]
    public void A_missed_schedules_detail_says_when_it_was_due()
    {
        using var context = Context();
        var page = Render(context, All());

        page.Find("[data-testid='schedules-row'][data-schedule='devbook-validate'] [data-testid='schedules-row-name']").Click();

        Assert.Equal(
            "Missed: it was due Today 04:00, and no run has reported its start since.",
            page.Find("[data-testid='schedules-detail-missed']").TextContent.Trim());
    }

    [Fact]
    public void A_register_that_could_not_be_read_says_so_and_claims_no_empty_page()
    {
        using var context = Context();
        var page = context.Render<SchedulesPage>(parameters => parameters
            .Add(p => p.Schedules, [])
            .Add(p => p.Error, "The schedules could not be read: locked."));

        Assert.Contains("locked", page.Find("[data-testid='schedules-error']").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-testid='schedules-empty']"));
    }

    [Fact]
    public void A_repository_reads_by_the_workspace_alias_where_it_has_one()
    {
        using var context = Context();
        var page = context.Render<SchedulesPage>(parameters => parameters
            .Add(p => p.Schedules, All())
            .Add(p => p.Now, Now)
            .Add(p => p.Zone, TimeZoneInfo.Utc)
            .Add(p => p.RepositoryAlias, repository => repository == "JSdotNet/Backlog" ? "backlog" : null));

        Assert.Equal("backlog", page.Find("[data-testid='schedules-card'] [data-testid='schedules-repo']").TextContent.Trim());
        Assert.Contains(
            "backlog",
            page.FindAll("[data-testid='schedules-table'] .data-table__group-name").Select(name => name.TextContent.Trim()));
    }

    [Fact]
    public void No_schedule_reported_yet_reads_as_an_empty_page_that_says_how_one_appears()
    {
        using var context = Context();
        var page = Render(context, []);

        Assert.Contains("No schedule has reported a run yet", page.Find("[data-testid='schedules-empty']").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-testid='schedules-table']"));
    }
}
